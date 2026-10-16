"""Jet scenery calibration and material-only FBX updates; no light bake."""
import hashlib
import json
import math
import re
import bpy
import numpy as np
from mathutils import Vector

CLOUD_NAME='Jet_Clouds_Panorama_unlit_scroll1500'
SKY_NAME='Jet_Sky_Panorama_unlit_scroll2'

def stable_name(name):
    return re.sub(r'scroll\d+', 'scrollN', name)

def snapshot():
    result={}
    for o in bpy.context.scene.objects:
        if o.type=='EMPTY':result[o.name]=np.asarray(o.matrix_world,dtype=float);continue
        if o.type!='MESH':continue
        me=o.data;me.calc_loop_triangles();rows=[]
        for t in me.loop_triangles:
            for li in t.loops:
                co=o.matrix_world@me.vertices[me.loops[li].vertex_index].co
                normal=(o.matrix_world.to_3x3()@me.corner_normals[li].vector).normalized()
                rows.append((*co,*normal,*me.uv_layers[0].data[li].uv,*me.uv_layers[1].data[li].uv))
        rows=np.asarray(rows);keys=np.round(rows,4)
        order=np.lexsort(tuple(keys[:,i] for i in reversed(range(10))))
        result[stable_name(o.name)]=rows[order]
    return result

def update_export(root,out):
    """Re-serialize the shipped FBX with only cloud name/path strings changed.

    Blender's FBX encoder preserves parsed numeric properties exactly. A second
    import also checks geometry, explicit normals, UVs and markers bit for bit.
    No ignored checkpoint is needed; texture payloads and the bake are untouched.
    """
    from io_scene_fbx import parse_fbx,encode_bin,data_types
    tex=out/'Textures';log=root/'Logs/art-preview';log.mkdir(parents=True,exist_ok=True)
    texture_hashes={stable_name(p.name):hashlib.sha256(p.read_bytes()).hexdigest() for p in tex.glob('*.png')}
    fbx=out/'Jet.fbx';before_hash=hashlib.sha256(fbx.read_bytes()).hexdigest()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx));bpy.context.view_layer.update();before=snapshot()
    cloud=next(o for o in bpy.context.scene.objects if o.type=='MESH' and 'Clouds_Panorama' in o.name)
    old_name=cloud.data.materials[0].name
    old=tex/f'{old_name}_BaseColor.png';new=tex/f'{CLOUD_NAME}_BaseColor.png'
    if old!=new:
        assert old.exists() and not new.exists(), 'Expected one cloud texture, not competing versions'
    original,version=parse_fbx.parse(str(fbx));changed=[]
    methods={getattr(data_types,n.upper()):'add_'+n for n in (
        'bool','char','int8','int16','int32','int64','float32','float64','string','bytes',
        'bool_array','byte_array','int32_array','int64_array','float32_array','float64_array')}
    def replacement(value):
        return value.replace(old_name.encode(),CLOUD_NAME.encode()) if isinstance(value,bytes) else value
    def clone(elem):
        dest=encode_bin.FBXElem(elem.id)
        for kind,value in zip(elem.props_type,elem.props):
            updated=replacement(value)
            if isinstance(value,bytes) and updated!=value:changed.append(elem.id.decode())
            getattr(dest,methods[kind])(updated)
        dest.elems=[clone(e) for e in elem.elems]
        return dest
    temporary=out/'Jet-motion.tmp.fbx'
    encode_bin.write(str(temporary),clone(original),version)
    encoded,encoded_version=parse_fbx.parse(str(temporary));assert encoded_version==version
    def compare(a,b):
        assert a.id==b.id and a.props_type==b.props_type and len(a.elems)==len(b.elems)
        for av,bv in zip(a.props,b.props):
            assert np.array_equal(replacement(av),bv),(a.id,'FBX property changed')
        for ac,bc in zip(a.elems,b.elems):compare(ac,bc)
    compare(original,encoded)
    if old!=new:old.rename(new)
    temporary.replace(fbx)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx));bpy.context.view_layer.update();after=snapshot()
    assert before.keys()==after.keys()
    for name,a in before.items():
        assert np.array_equal(a,after[name]),(name,'geometry, normals, UVs or marker changed')
    assert texture_hashes=={stable_name(p.name):hashlib.sha256(p.read_bytes()).hexdigest() for p in tex.glob('*.png')}
    for m in bpy.data.materials:m['jet']=True
    scene=bpy.context.scene
    scene['jet_motion_update']=json.dumps({'old_material':old_name,'new_material':CLOUD_NAME,'rebaked':False})
    bpy.ops.wm.save_as_mainfile(filepath=str(root/'art/blender/Jet-baked.blend'))
    report={'pass':True,'previous_fbx_sha256':before_hash,'fbx_sha256':hashlib.sha256(fbx.read_bytes()).hexdigest(),
        'old_material':old_name,'new_material':CLOUD_NAME,'rebaked':False,'texture_payloads_unchanged':texture_hashes,
        'fbx_numeric_properties_exactly_preserved':True,'renamed_string_properties':changed,
        'geometry_normals_uvs_markers_preserved':True,'maximum_position_roundtrip_error_m':0}
    (log/'jet-motion-preservation.json').write_text(json.dumps(report,indent=2)+'\n')
    print('JET MOTION-ONLY EXPORT PASS',json.dumps(report),flush=True)


def audit_motion(tree,owners,seats,cloud_velocity,sky_velocity,cloud_image):
    """Angular optical flow of actual visible FBX triangles, not guessed distance.

    For r = surface point - eye, v = texture-feature velocity:
      omega = |r cross v| / |r|^2 radians/second.
    U repeats every 4600/9 m, so omega/(N/10000) is the LOCAL angular
    texture width. A finite full-repeat angle is also reported, separately.
    """
    rows=[];all_rates=[];L=4600/9
    for i,p in enumerate(seats):
        eye=Vector(p)+Vector((0,0,1.25));rates=[];local_width=[];finite_width=[]
        for side in [-1,1]:
            for y in [-5.2+j*.8 for j in range(14)]:
                for dy in [-.07,0,.07]:
                    for z in np.linspace(1.06,1.245,40):
                        target=Vector((side*3.15,y+dy,float(z)));direction=(target-eye).normalized()
                        point,normal,index,distance=tree.ray_cast(eye,direction,2990)
                        if point is None or 'Clouds' not in owners[index]:continue
                        r=point-eye;speed=math.degrees(r.cross(cloud_velocity).length/r.length_squared)
                        rates.append(speed);local_width.append(speed/.15)
                        finite_width.append(math.degrees((r+Vector((0,L/2,0))).angle(r-Vector((0,L/2,0)))))
        assert rates,(i,'No visible clouds')
        percentiles=np.percentile(rates,[0,10,50,90,100]).tolist()
        if i<4:assert 4<=percentiles[2]<=6,(i,'Standard-seat median outside 4–6 deg/s',percentiles)
        rows.append({'spot':f'Spot_{i:03d}','visible_cloud_samples':len(rates),'angular_speed_deg_s_min_p10_p50_p90_max':percentiles,
                     'median_local_angular_texture_width_deg':float(np.median(local_width)),
                     'median_finite_full_repeat_angle_deg':float(np.median(finite_width))})
        all_rates.extend(rates)
    # Maximum broadside sky flow; oblique views and higher sky are slower.
    sky_max=math.degrees(sky_velocity.length/(900-max(abs(p[0]) for p in seats)))
    assert sky_max<.03
    w,h=cloud_image.size;pixels=np.empty(w*h*4,np.float32);cloud_image.pixels.foreach_get(pixels)
    luminance=pixels.reshape(h,w,4)[:,:,:3]@np.array((.2126,.7152,.0722))
    power=(abs(np.fft.rfft(luminance-luminance.mean(axis=1,keepdims=True),axis=1))**2).sum(axis=0)
    frequencies=np.fft.rfftfreq(w);temporal={}
    for hz in [72,90]:
        step=w*.15/hz;alias=float(power[frequencies>1/(2*step)].sum()/max(power.sum(),1e-20))
        assert alias<.01,('Excess fine texture energy for scroll speed',hz,alias)
        temporal[str(hz)]={'source_texels_per_frame':step,'maximum_sampled_motion_deg_per_frame':max(all_rates)/hz,
                           'horizontal_contrast_energy_above_temporal_nyquist_fraction':alias}
    return {'cloud_scroll':1500,'cloud_uv_per_second':.15,'cloud_repeat_world_m':L,'cloud_velocity_mps':list(cloud_velocity),
            'sky_scroll':2,'sky_uv_per_second':.0002,'sky_repeat_world_m':4600/3,'sky_max_deg_s':sky_max,
            'seats':rows,'temporal_sampling':temporal,'note':'4–6 deg/s targets Seat1–4 medians. Closer sofas move faster; horizon/oblique rays slower. Runtime headset motion acceptance remains open.'}
