"""Independent audit of the delivered FBX, textures and actual seated-head surfaces.
Blender -b --factory-startup -P tools/blender/verify_home.py
Reports only under ignored Logs/art-preview. Fails on any broken requirement.
"""
import sys
sys.dont_write_bytecode=True
import json
import hashlib
from pathlib import Path
import struct
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_home_uvs import uv_overlaps,audit_padding
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Assets/TheMovies/Art/Home';PREVIEW=ROOT/'Logs/art-preview'
PREVIEW.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(OUT/'Home.fbx'))
scene=bpy.context.scene
objects=[o for o in scene.objects if o.type=='MESH']
assert set(o.type for o in scene.objects)<={'MESH','EMPTY'},'Export contains helpers'
assert len(objects)<=12 and all(len(o.data.materials)==1 for o in objects)
materials={o.data.materials[0].name for o in objects};assert len(materials)==len(objects)
required={'Home_Velvet_Forest','Home_Brass','Home_Linen_emit','Home_Embers_glow','Home_Night_unlit'}
assert required<=materials
expected={f'Seat{i+1}':((i-1.5)*.75,0,0) for i in range(4)}
expected.update({f'Spot_{i:03d}':((i-1.5)*.75,0,0) for i in range(4)})
expected.update(ScreenAnchor=(0,5.2,.78),ScreenTop=(0,5.2,2.63))
assert {o.name for o in scene.objects if o.type=='EMPTY'}==set(expected)
for name,loc in expected.items():assert (scene.objects[name].matrix_world.translation-Vector(loc)).length<.00001,(name,'coordinates')

def bvh_for(objs):
    verts=[];faces=[]
    for o in objs:
        m=o.data;m.calc_loop_triangles();offset=len(verts)
        verts.extend(o.matrix_world@v.co for v in m.vertices)
        faces.extend(tuple(offset+i for i in t.vertices) for t in m.loop_triangles)
    return BVHTree.FromPolygons(verts,faces,all_triangles=True)

stats={};uvtris=[];normal_evidence={}
for o in objects:
    m=o.data;m.calc_loop_triangles();stats[o.name]=len(m.loop_triangles)
    assert [u.name for u in m.uv_layers]==['UVMap','Lightmap'],o.name
    assert m.has_custom_normals,(o.name,'lost explicit normals')
    normal_evidence[o.name]=True
    uv=m.uv_layers['Lightmap']
    uvtris.extend([[tuple(uv.data[l].uv) for l in t.loops] for t in m.loop_triangles])
assert sum(stats.values())<=120000
tris=np.asarray(uvtris,np.float64)
assert tris.min()>=0 and tris.max()<=1
ab=tris[:,1]-tris[:,0];ac=tris[:,2]-tris[:,0]
areas=np.abs(ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0])/2
assert np.min(areas)>1e-14,('degenerate UV triangle',float(areas.min()))
overlaps=uv_overlaps(tris);assert not overlaps,('UV intersections',len(overlaps))
padding=audit_padding(tris);assert padding['pass'],padding
# Raster audit at the actual atlas resolution, independent from the continuous SAT audit.
N=2048;count=np.zeros((N,N),np.uint16)
for pts in tris*N:
    mn=np.maximum(np.floor(pts.min(axis=0)-.5).astype(int),0);mx=np.minimum(np.ceil(pts.max(axis=0)-.5).astype(int),N-1)
    if np.any(mx<mn):continue
    a,b,c=pts;ab=b-a;ac=c-a;det=ab[0]*ac[1]-ab[1]*ac[0]
    if abs(det)<1e-12:continue
    yy,xx=np.mgrid[mn[1]:mx[1]+1,mn[0]:mx[0]+1];px=xx+.5-a[0];py=yy+.5-a[1]
    u=(px*ac[1]-py*ac[0])/det;v=(ab[0]*py-ab[1]*px)/det
    mask=(u>1e-6)&(v>1e-6)&(u+v<1-1e-6)
    count[mn[1]:mx[1]+1,mn[0]:mx[0]+1]+=mask.astype(np.uint16)
assert not np.any(count>1),'Overlapping atlas texels'
assert (count>0).mean()>.10,('Insufficient atlas surface coverage',float((count>0).mean()))
# Every actual seat must have a cushion underneath, a finished padded back at 12 cm,
# a structural floor at the marker, and a free 1.25 m eye position.
bvh=bvh_for(objects)
upholstery=bvh_for([o for o in objects if o.name=='Home_Velvet_Forest'])
floor=bvh_for([o for o in objects if o.name in ('Home_Parquet','Home_Persian_Rug')])
spots=[];sightlines=[]
for i in range(4):
    name=f'Spot_{i:03d}';p=scene.objects[name].matrix_world.translation
    hit,n,face,distance=upholstery.ray_cast(p+Vector((0,0,.85)),Vector((0,-1,0)),.3)
    assert hit is not None and n.y>.8 and abs(distance-.12)<.0001,(name,'padded back',distance)
    ground,normal,_,_=floor.ray_cast(p+Vector((0,0,.05)),Vector((0,0,-1)),.1)
    assert ground is not None and normal.z>.95 and abs(ground.z-p.z)<.00001,(name,'floor')
    cushion,n,_,_=upholstery.ray_cast(p+Vector((0,.15,.78)),Vector((0,0,-1)),.4)
    assert cushion is not None and .44<cushion.z<.52,(name,'missing seat cushion')
    spots.append({'name':name,'head_floor':list(p),'back_clearance_m':distance,'cushion_top_m':cushion.z})
    origin=p+Vector((0,0,1.25))
    for x in np.linspace(-3,3,39):
        for z in np.linspace(.78,2.63,17):
            target=Vector((float(x),5.2,float(z)));delta=target-origin
            hit=bvh.ray_cast(origin,delta.normalized(),delta.length-.001)
            assert hit[0] is None,(name,'blocked screen',x,z,list(hit[0]) if hit[0] else None)
            sightlines.append([name,float(x),float(z)])
# Check every source texture reference and PNG header, not only Blender's decoded dimensions.
textures={};atlas_stats={}
expected_files={f'{m}_BaseColor.png' for m in materials}|{'Home_Light.png'}
assert {p.name for p in (OUT/'Textures').glob('*.png')}==expected_files
for path in sorted((OUT/'Textures').glob('*.png')):
    header=path.read_bytes()[:33];assert header[:8]==b'\x89PNG\r\n\x1a\n'
    width,height,depth,mode=struct.unpack('>IIBB',header[16:26]);assert depth==8 and mode in (2,6),(path.name,'not RGB/RGBA8')
    assert width<=2048 and height<=2048
    if path.name=='Home_Light.png':assert (width,height,mode)==(2048,2048,2)
    if path.name not in ('Home_Light.png','Home_Persian_Rug_BaseColor.png'):assert (width,height)==(1024,1024)
    textures[path.name]={'size':[width,height],'bits':depth,'bytes':path.stat().st_size}
    if path.name=='Home_Light.png':
        im=bpy.data.images.load(str(path));p=np.empty(N*N*4,np.float32);im.pixels.foreach_get(p);rgb=p.reshape(N,N,4)[:,:,:3]
        covered=rgb[count>0]
        atlas_stats={'surface_fraction':float((count>0).mean()),'positive_area_intersections':0,'overlapping_texels':0,
                     'covered_srgb_percentiles':np.percentile(covered,[1,50,95,99,100],axis=0).tolist(),
                     'clipped_fraction':float(np.any(covered>=1,axis=1).mean()),
                     'black_fraction':float(np.all(covered<1/255,axis=1).mean())}
        assert atlas_stats['clipped_fraction']<.01,atlas_stats
        # Hidden contacts can have black irradiance; visible surfaces are checked below.
# Sample first-hit surfaces over a full sphere from each actual seated eye.
# This distinguishes visible black bake failures from occluded furniture contacts.
world_tris=[]
for o in objects:
    world_tris.extend([[tuple(o.matrix_world@o.data.vertices[i].co) for i in t.vertices] for t in o.data.loop_triangles])
world_tris=np.asarray(world_tris)
image=bpy.data.images.load(str(OUT/'Textures/Home_Light.png'),check_existing=True)
pixels=np.empty(N*N*4,np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(N,N,4)
visible=[];missing=[]
for i in range(4):
    origin=scene.objects[f'Spot_{i:03d}'].matrix_world.translation+Vector((0,0,1.25))
    for elevation in np.linspace(-1.45,1.45,25):
        for azimuth in np.linspace(0,2*np.pi,120,endpoint=False):
            direction=Vector((np.cos(elevation)*np.cos(azimuth),np.cos(elevation)*np.sin(azimuth),np.sin(elevation)))
            hit,normal,index,distance=bvh.ray_cast(origin,direction,30)
            if hit is None:missing.append([i,float(elevation),float(azimuth)]);continue
            a,b,c=world_tris[index];e=b-a;f=c-a;p=np.asarray(hit)-a
            ee=e@e;ff=f@f;ef=e@f;pe=p@e;pf=p@f;det=ee*ff-ef*ef
            v=(ff*pe-ef*pf)/det;w=(ee*pf-ef*pe)/det
            uv=tris[index,0]*(1-v-w)+tris[index,1]*v+tris[index,2]*w
            ix,iy=np.clip((uv*N).astype(int),0,N-1);visible.append(pixels[iy,ix,:3])
visible=np.asarray(visible);black=float(np.all(visible<1/255,axis=1).mean())
print('Visible surface audit:',len(visible),'rays;',len(missing),'misses;',black,'black fraction')
assert not missing,('Room shell gaps visible from sofa',len(missing),missing[:5])
assert black<.01,('Visible black lightmap failures',black)
visible_evidence={'rays':len(visible),'shell_misses':len(missing),'black_fraction':black,
                  'srgb_percentiles':np.percentile(visible,[1,50,99],axis=0).tolist()}
for m in bpy.data.materials:
    if m.name not in materials:continue
    assert m.use_nodes
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    assert bs.inputs['Base Color'].is_linked,(m.name,'missing base texture')
    # Blender's FBX importer inserts a neutral Normal Map node even with no FBX map.
    # Reject actual normal inputs, not that empty importer placeholder.
    if bs.inputs['Normal'].is_linked:
        normal=bs.inputs['Normal'].links[0].from_node
        assert normal.type=='NORMAL_MAP' and not normal.inputs['Color'].is_linked,'Normal texture prohibited'
    assert all(Path(n.image.filepath).name==f'{m.name}_BaseColor.png' for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image)
    image=bs.inputs['Base Color'].links[0].from_node.image
    assert Path(image.filepath).name==f'{m.name}_BaseColor.png'
result={'validation':'PASS','fbx_reimport':'PASS','fbx_sha256':hashlib.sha256((OUT/'Home.fbx').read_bytes()).hexdigest(),'triangles':stats,'total_triangles':sum(stats.values()),
        'materials':len(materials),'mesh_objects':len(objects),'markers':expected,'seated_head_surface_checks':spots,
        'sightlines':{'eye_height_m':1.25,'screen_size_m':[6,1.85],'grid':[39,17],'clear':len(sightlines),'blocked':0},
        'atlas':atlas_stats,'visible_surface_light':visible_evidence,'atlas_padding':padding,'textures':textures,'custom_normals':normal_evidence}
(PREVIEW/'home-verification.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))
