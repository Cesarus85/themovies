"""Build, bake, export and preview Jet; Blender 5.2. No Unity or add-ons.
Blender -b --factory-startup -P tools/blender/jet.py -- Assets/TheMovies/Art/Jet
--geometry-only: ignored draft checkpoint; --preview-only: reuse baked checkpoint.
--motion-only: update cloud scroll naming in the shipped FBX without rebuilding/baking.
--motion-preview: render 72 frames at 24 Hz from Seat2 toward a side window.
"""
import sys
sys.dont_write_bytecode=True
import json
import math
import time
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_jet_textures import texture,rendered_scenery
from lib_jet_motion import update_export,CLOUD_NAME,SKY_NAME
from lib_jet_room import build,surroundings,SEATS,SCREEN
from lib_jet_uvs import uv_overlaps
from lib_jet_preview import preview,aim
from lib_seated_head_clearance import audit as audit_heads

ROOT=Path(__file__).resolve().parents[2]
ARGS=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
OUT=(ROOT/(ARGS[0] if ARGS else 'Assets/TheMovies/Art/Jet')).resolve()
assert OUT==ROOT/'Assets/TheMovies/Art/Jet','Output must be Jet in this worktree'
TEX=OUT/'Textures';PREVIEW=ROOT/'Logs/art-preview';BLEND=ROOT/'art/blender'
for p in [TEX,PREVIEW,BLEND]:p.mkdir(parents=True,exist_ok=True)
START=time.time()
def log(s):print('[Jet] '+s,flush=True)
def light(name,loc,power,color,size,target):
    d=bpy.data.lights.new(name,'AREA');d.energy=power*.72;d.color=color;d.shape='DISK';d.size=size
    o=bpy.data.objects.new(name,d);bpy.context.scene.collection.objects.link(o);o.location=loc;aim(o,target)
def configure_cycles(scene):
    scene.render.engine='CYCLES';scene.cycles.samples=256;scene.cycles.use_denoising=True
    scene.cycles.max_bounces=8;scene.cycles.diffuse_bounces=4
    prefs=bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type='METAL';prefs.get_devices()
        available=[d for d in prefs.devices if d.type=='METAL']
        for d in prefs.devices:d.use=d.type=='METAL'
        scene.cycles.device='GPU' if available else 'CPU'
    except Exception:scene.cycles.device='CPU'

def marker(name,loc):
    o=bpy.data.objects.new(name,None);bpy.context.scene.collection.objects.link(o);o.location=loc;o.empty_display_type='ARROWS';return o

if '--motion-only' in ARGS:
    update_export(ROOT,OUT);sys.exit(0)
if '--motion-preview' in ARGS:
    bpy.ops.wm.open_mainfile(filepath=str(BLEND/'Jet-baked.blend'));preview(ROOT,TEX,configure_cycles,True,motion=True);sys.exit(0)
if '--preview-only' in ARGS:
    bpy.ops.wm.open_mainfile(filepath=str(BLEND/'Jet-baked.blend'));preview(ROOT,TEX,configure_cycles,'--quick' in ARGS);sys.exit(0)
def material(key,name,color,kind='',emission=0,img=None):
    m=bpy.data.materials.new(name);m.use_nodes=True;m['jet']=True;m.diffuse_color=(*color,1)
    bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.75
    image=img or texture(TEX/f'{name}_BaseColor.png',color,kind)
    tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
    uv=m.node_tree.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';m.node_tree.links.new(uv.outputs[0],tex.inputs[0]);m.node_tree.links.new(tex.outputs[0],bs.inputs['Base Color'])
    if emission:m.node_tree.links.new(tex.outputs[0],bs.inputs['Emission Color']);bs.inputs['Emission Strength'].default_value=emission
    materials[key]=m
materials={}
if '--finish-only' in ARGS:
    bpy.ops.wm.open_mainfile(filepath=str(BLEND/'Jet-interior-baked.blend'))
    scene=bpy.context.scene;materials={m.name:m for m in bpy.data.materials if m.get('jet')}
    repaired=scene['jet_uv_repairs'];ring_cuts=scene['jet_ring_cuts']
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    material('Ivory','Jet_Ivory',(.79,.75,.65))
    material('Wood','Jet_Walnut',(.31,.15,.065),'wood')
    material('Burl','Jet_Burl',(.40,.23,.105),'burl')
    material('Leather','Jet_Cream_Leather_Fabric',(.76,.65,.47),'leather')
    material('Navy','Jet_Navy_Leather_Fabric',(.095,.155,.21),'leather')
    material('Carpet','Jet_Woven_Carpet',(.105,.145,.18),'carpet')
    material('Brass','Jet_Polished_Brass',(.55,.39,.17))
    material('Piping','Jet_Leather_Piping',(.36,.28,.17))
    material('Dark','Jet_Dark',(.035,.046,.057))
    material('Glow','Jet_Warm_glow',(.98,.79,.50),emission=.6)
    material('Porcelain','Jet_Porcelain',(.84,.82,.73))
    meshes=build(materials,light)
    for i,loc in enumerate(SEATS):
        marker(f'Spot_{i:03d}',loc)
        if i<4:marker(f'Seat{i+1}',loc)
    marker('ScreenAnchor',SCREEN);marker('ScreenTop',(SCREEN[0],SCREEN[1],SCREEN[2]+1.8))
    bpy.context.view_layer.update()
    heads=audit_heads(meshes,scene.objects)
    (PREVIEW/'jet-build-heads.json').write_text(json.dumps(heads,indent=2)+'\n')
    assert heads['pass'],heads['failed_markers']
    scene.world=bpy.data.worlds.new('Jet bake ambient');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.45,.53,.68,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.20
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join();room=bpy.context.object;room.name='Jet interior'
    bm=bmesh.new();bm.from_mesh(room.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001)
    bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS');bm.to_mesh(room.data);bm.free()
    room.data.update();room.data.normals_split_custom_set([tuple(n.vector) for n in room.data.corner_normals])
    log(f'Interior: {len(room.data.polygons)} triangles; heads PASS')
    if '--geometry-only' in ARGS:
        bpy.ops.wm.save_as_mainfile(filepath=str(BLEND/'Jet-draft.blend'));sys.exit(0)
    room.data.uv_layers[0].name='UVMap';lm=room.data.uv_layers.new(name='Lightmap');room.data.uv_layers.active=lm
    # Furniture is allocated extra lightmap density; unwrap temporary component scale,
    # then restore exact geometry before baking. Reference-pipeline algorithm.
    parent=list(range(len(room.data.vertices)))
    def root(i):
        while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
        return i
    for edge in room.data.edges:
        a,b=edge.vertices;parent[root(a)]=root(b)
    components={}
    for v in room.data.vertices:components.setdefault(root(v.index),[]).append(v.index)
    original={}
    for ids in components.values():
        coords=np.array([room.data.vertices[i].co[:] for i in ids]);center=coords.mean(axis=0)
        furniture=abs(center[0])<2.8 and -5.2<center[1]<4.9 and center[2]>.04 and np.ptp(coords,axis=0).max()<2
        thin=np.ptp(coords,axis=0).min()<.065 and np.ptp(coords,axis=0).max()>.2
        factor=2.4 if thin else 1.8 if furniture else 1
        if factor!=1:
            for i in ids:
                v=room.data.vertices[i];original[i]=v.co.copy();v.co=Vector(center)+(v.co-Vector(center))*factor
    room.data.update()
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.25,island_margin=0,area_weight=.5,scale_to_bounds=True);bpy.ops.object.mode_set(mode='OBJECT')
    for i,co in original.items():room.data.vertices[i].co=co
    room.data.update()
    # Planar closed coves otherwise become giant hollow rectangular UV islands.
    # Cut only low-fill, roughly square islands into eight connected sectors before
    # packing; keep every triangle's shape and area. This recovers the empty centers
    # without wasting the 2048 atlas or sacrificing the mandatory 4px gutters.
    room.data.calc_loop_triangles();layer=room.data.uv_layers['Lightmap']
    uv_faces=[np.array([tuple(layer.data[li].uv) for li in p.loop_indices]) for p in room.data.polygons]
    uparent=list(range(len(uv_faces)))
    def uroot(i):
        while uparent[i]!=i:uparent[i]=uparent[uparent[i]];i=uparent[i]
        return i
    edges={}
    for i,pts in enumerate(uv_faces):
        for a,b in zip(pts,np.roll(pts,-1,axis=0)):
            key=tuple(sorted((tuple(a),tuple(b))))
            if key in edges:uparent[uroot(i)]=uroot(edges[key])
            else:edges[key]=i
    islands={}
    for i in range(len(uv_faces)):islands.setdefault(uroot(i),[]).append(i)
    ring_cuts=0
    for ids in islands.values():
        pts=np.concatenate([uv_faces[i] for i in ids]);lo=pts.min(axis=0);hi=pts.max(axis=0);size=hi-lo
        if min(size)<1e-7 or len(ids)<24:continue
        area=sum(abs(np.cross(uv_faces[i][1]-uv_faces[i][0],uv_faces[i][2]-uv_faces[i][0]))/2 for i in ids)
        if area/(size[0]*size[1])>.42 or not .35<size[0]/size[1]<2.85:continue
        center=(hi+lo)/2
        for i in ids:
            d=(uv_faces[i].mean(axis=0)-center)/size;sector=int((math.atan2(d[1],d[0])+math.pi)/(math.tau)*8)%8
            offset=Vector((2+(ring_cuts%4)*2+sector*.2,2+(ring_cuts//4)*2))
            for li in room.data.polygons[i].loop_indices:layer.data[li].uv+=offset
        ring_cuts+=1
    log(f'Cut {ring_cuts} hollow UV islands into compact sectors')
    def pack():
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.pack_islands(rotate=True,margin_method='FRACTION',margin=5.2/2048,shape_method='CONCAVE')
        bpy.ops.object.mode_set(mode='OBJECT')
    pack();repaired=0
    for repair in range(6):
        room.data.calc_loop_triangles();layer=room.data.uv_layers['Lightmap']
        tris=np.array([[tuple(layer.data[i].uv) for i in t.loops] for t in room.data.loop_triangles])
        overlaps=uv_overlaps(tris);ab=tris[:,1]-tris[:,0];ac=tris[:,2]-tris[:,0]
        degenerate=np.flatnonzero(abs(ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0])<1e-13).tolist()
        if not overlaps and not degenerate:break
        assert repair<5,('UV overlaps',len(overlaps),len(degenerate))
        split=sorted({room.data.loop_triangles[j].polygon_index for i,j in overlaps}|{room.data.loop_triangles[j].polygon_index for j in degenerate})
        scale=math.sqrt(float(np.abs(ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0]).sum())/2/sum(p.area for p in room.data.polygons))
        for k,index in enumerate(split):
            poly=room.data.polygons[index];pts=[room.data.vertices[v].co for v in poly.vertices];axis=(pts[1]-pts[0]).normalized();other=poly.normal.cross(axis).normalized()
            for li,pt in zip(poly.loop_indices,pts):
                delta=pt-pts[0];layer.data[li].uv=(2+(k%32)*.1+delta.dot(axis)*scale,2+(k//32)*.1+delta.dot(other)*scale)
        repaired+=len(split);log(f'Repairing {len(split)} folded faces');pack()
    # Reserve two non-overlapping atlas strips for special-shader meshes. They are
    # never baked or read by unlit materials, but have valid global UV2 too.
    for d in room.data.uv_layers['Lightmap'].data:d.uv.y*=.95
    log('UV audit passed; starting Cycles 256-sample diffuse-only bake')
    image=bpy.data.images.new('Jet_Light',2048,2048,float_buffer=True)
    for m in room.data.materials:
        n=m.node_tree.nodes.new('ShaderNodeTexImage');n.name='BakeTarget';n.image=image;m.node_tree.nodes.active=n
    configure_cycles(scene);scene.render.bake.margin=8
    bpy.ops.object.bake(type='DIFFUSE',pass_filter={'DIRECT','INDIRECT'},use_clear=True,margin=8)
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=-1;scene.view_settings.gamma=1
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
    ng=bpy.data.node_groups.new('Jet Atlas Denoise','CompositorNodeTree');ng.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor');scene.compositing_node_group=ng
    node=ng.nodes.new('CompositorNodeImage');node.image=image;denoise=ng.nodes.new('CompositorNodeDenoise');out=ng.nodes.new('NodeGroupOutput');ng.links.new(node.outputs['Image'],denoise.inputs['Image']);ng.links.new(denoise.outputs['Image'],out.inputs['Image'])
    data=bpy.data.cameras.new('Jet atlas');cam=bpy.data.objects.new('Jet atlas',data);scene.collection.objects.link(cam);scene.camera=cam
    room.hide_render=True;scene.render.resolution_x=2048;scene.render.resolution_y=2048;scene.render.resolution_percentage=100
    scene.cycles.samples=1;scene.render.filepath=str(TEX/'Jet_Light.png');bpy.ops.render.render(write_still=True)
    room.hide_render=False;scene.compositing_node_group=None;bpy.data.node_groups.remove(ng)
    for m in room.data.materials:m.node_tree.nodes.remove(m.node_tree.nodes['BakeTarget'])
    room.data.uv_layers.active=room.data.uv_layers['UVMap']
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.separate(type='MATERIAL');bpy.ops.object.mode_set(mode='OBJECT')
    for o in list(scene.objects):
        if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
    for o in scene.objects:
        if o.type=='MESH':o.name=next(m.name for m in o.data.materials if m)
    scene['jet_uv_repairs']=repaired;scene['jet_ring_cuts']=ring_cuts
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND/'Jet-interior-baked.blend'))
log('Rendering original sunset and volumetric cloud scenery')
sky,cloud=rendered_scenery(ROOT,TEX,configure_cycles)
material('Sky',SKY_NAME,(1,1,1),img=sky)
material('Cloud',CLOUD_NAME,(1,1,1),img=cloud)
scenery=surroundings(materials)
for oi,o in enumerate(scenery):
    # One unique rectangle per disconnected scenery face, in the reserved strip.
    lm=o.data.uv_layers.new(name='Lightmap')
    for pi,p in enumerate(o.data.polygons):
        u0=.01+pi*.49;v0=.966+oi*.016
        for li,uv in zip(p.loop_indices,[(u0,v0),(u0+.46,v0),(u0+.46,v0+.009),(u0,v0+.009)]):lm.data[li].uv=uv
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free();o.data.update()
    o.data.normals_split_custom_set([tuple(n.vector) for n in o.data.corner_normals]);o.data.uv_layers.active=o.data.uv_layers['UVMap']
objects=[o for o in scene.objects if o.type=='MESH']
stats={o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects}
assert sum(stats.values())<=120000 and len(materials)<=14
scene.cycles.samples=256;bpy.context.view_layer.update()
report={'triangles':stats,'total_triangles':sum(stats.values()),'materials':sorted(m.name for m in materials.values()),'mesh_objects':len(objects),'seats':len(SEATS),'bake_samples':256,'passes':['DIRECT','INDIRECT'],'bake_margin_px':8,'denoise':'OpenImageDenoise, linear irradiance','atlas':[2048,2048],'view':'Standard/None/-1 EV/RGB8','scenery_baked':False,'repaired_uv_faces':repaired,'compact_ring_islands':ring_cuts,'device':scene.cycles.device,'elapsed_seconds':round(time.time()-START),'blender':bpy.app.version_string}
scene['jet_build_metrics']=json.dumps(report)
bpy.ops.export_scene.fbx(filepath=str(OUT/'Jet.fbx'),use_selection=False,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
(PREVIEW/'jet-metrics.json').write_text(json.dumps(report,indent=2)+'\n');bpy.ops.wm.save_as_mainfile(filepath=str(BLEND/'Jet-baked.blend'))
log(f'Exported {sum(stats.values())} triangles / {len(materials)} materials')
preview(ROOT,TEX,configure_cycles,'--quick' in ARGS)
log('Complete')
