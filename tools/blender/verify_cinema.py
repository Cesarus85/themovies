"""Independent FBX roundtrip / budget / marker / sightline / atlas raster audit.
Run with Blender -b --factory-startup -P tools/blender/verify_cinema.py.
"""
from pathlib import Path
import json
import sys
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_cinema_validation import normal_rows,chair_masks,architecture_signature
import math
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/TheMovies/Art/Cinema'
baseline_signature=None
if '--baseline' in sys.argv:
    baseline=Path(sys.argv[sys.argv.index('--baseline')+1]).resolve()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(baseline))
    baseline_signature=architecture_signature([o for o in bpy.context.scene.objects if o.type=='MESH'])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(OUT/'Cinema.fbx'))
scene=bpy.context.scene
objs=[o for o in scene.objects if o.type=='MESH']
assert len(objs)<=16
assert all(len(o.data.materials)==1 for o in objs)
assert len({o.data.materials[0].name for o in objs})==len(objs)
expected={f'Seat{i+1}':((i-1.5)*.75,0,0) for i in range(4)}
expected.update(ScreenAnchor=(0,11.9,.4),ScreenTop=(0,11.9,8.6))
assert set(o.name for o in scene.objects if o.type=='EMPTY')==set(expected)
for name,loc in expected.items():assert (scene.objects[name].matrix_world.translation-Vector(loc)).length<.0001,(name,list(scene.objects[name].location))
vertices=[];faces=[];stats={};uvtri=[]
for o in objs:
    mesh=o.data;mesh.calc_loop_triangles()
    assert [u.name for u in mesh.uv_layers]==['UVMap','Lightmap']
    stats[o.name]=len(mesh.loop_triangles)
    offset=len(vertices);vertices.extend(o.matrix_world@v.co for v in mesh.vertices)
    faces.extend(tuple(offset+i for i in t.vertices) for t in mesh.loop_triangles)
    layer=mesh.uv_layers['Lightmap']
    for tri in mesh.loop_triangles:uvtri.append([list(layer.data[i].uv) for i in tri.loops])
assert sum(stats.values())<=250000
architecture=architecture_signature(objs)
if baseline_signature is not None:
    assert architecture==baseline_signature,'Non-chair architecture changed'
normal_evidence={}
reference_path=ROOT/'Logs/art-preview/normals-before-export.npz'
reference=np.load(reference_path) if reference_path.exists() else None
for o in objs:
    mesh=o.data;mask=chair_masks(o)
    smooth=np.array([mesh.polygons[t.polygon_index].use_smooth for t in mesh.loop_triangles])
    count=int(mask.sum());smooth_count=int(smooth[mask].sum())
    if count and ('Velvet' in o.name or 'Walnut' in o.name):
        assert count==smooth_count,(o.name,count,smooth_count)
    assert mesh.has_custom_normals,o.name+' lost explicit normals in FBX'
    evidence={'chair_triangles':count,'smooth_chair_triangles':smooth_count,'has_custom_normals':mesh.has_custom_normals}
    if reference is not None:
        actual=normal_rows(o);expected_rows=reference[o.name]
        assert actual.shape==expected_rows.shape
        assert np.allclose(actual[:,:9],expected_rows[:,:9],atol=.00002)
        a=actual[:,9:].reshape(-1,3);b=expected_rows[:,9:].reshape(-1,3)
        angle=np.degrees(np.arccos(np.clip(np.sum(a*b,axis=1),-1,1)))
        evidence['maximum_normal_error_degrees']=float(angle.max())
        assert angle.max()<.1,(o.name,angle.max())
    normal_evidence[o.name]=evidence
# Count physically separate wooden armrests in the imported mesh. Welding by
# position handles FBX UV/normal splits without relying on exporter metadata.
wood=next(o for o in objs if o.name=='Cinema_Walnut_Panelling')
wood_mask=chair_masks(wood);adj={};arm_faces=[]
for included,tri in zip(wood_mask,wood.data.loop_triangles):
    if not included:continue
    keys=[tuple(round(c,5) for c in wood.matrix_world@wood.data.vertices[i].co) for i in tri.vertices]
    arm_faces.append(keys)
    for a,b in zip(keys,keys[1:]+keys[:1]):
        adj.setdefault(a,set()).add(b);adj.setdefault(b,set()).add(a)
unvisited=set(adj);components=[]
while unvisited:
    first=unvisited.pop();pending=[first];component={first}
    while pending:
        for key in adj[pending.pop()]:
            if key in unvisited:unvisited.remove(key);component.add(key);pending.append(key)
    components.append(component)
expected_arms=[]
spans=[(-8.55,4),(-4.125,12),(6.3,4)]
for y,z in [(-r*1.65,r*.45) for r in range(-2,4)]+[(-6.08,4.825)]:
    for first,n in spans:
        expected_arms += [(first-.375+i*.75,y-.32,z+.726) for i in range(n+1)]
for side in (-1,1):
    for y in [-2.7,3.15,8.2]:
        expected_arms += [(side*10.6+dx,y+.2-.32,3.62+.726) for dx in [-.75,0,.75]]
assert len(components)==len(expected_arms)==179,len(components)
centers=np.array([(np.min(list(c),axis=0)+np.max(list(c),axis=0))/2 for c in components])
for expected_arm in expected_arms:
    assert np.sum(np.linalg.norm(centers-np.array(expected_arm),axis=1)<.003)==1,expected_arm
arm_evidence={'continuous_rows':27,'seats':152,'shared_standards_and_armrests':179,
              'one_armrest_per_boundary':'PASS: imported connected meshes and all 179 positions',
              'upholstery_triangles':stats['Cinema_Velvet_Seat']}
assert arm_evidence['upholstery_triangles']==52800
triangles=np.array(uvtri,np.float64)
assert triangles.min()>=-1e-5 and triangles.max()<=1.00001
# Exact positive-area intersection audit in UV space, in addition to the texel audit.
# SAT tests all six triangle edge normals; touching edges/vertices remain legal.
uvverts=[(float(u),float(v),0.0) for u,v in triangles.reshape(-1,2)]
uvfaces=[(3*i,3*i+1,3*i+2) for i in range(len(triangles))]
uvtree=BVHTree.FromPolygons(uvverts,uvfaces,all_triangles=True,epsilon=1e-10)
pairs=np.array([(i,j) for i,j in uvtree.overlap(uvtree) if i<j],dtype=np.int32)
exact_overlaps=0
for start in range(0,len(pairs),20000):
    batch=pairs[start:start+20000];a=triangles[batch[:,0]];b=triangles[batch[:,1]]
    edges=np.concatenate((np.roll(a,-1,axis=1)-a,np.roll(b,-1,axis=1)-b),axis=1)
    axes=np.stack((-edges[:,:,1],edges[:,:,0]),axis=2)
    lens=np.linalg.norm(axes,axis=2);axes/=np.maximum(lens[:,:,None],1e-20)
    pa=np.einsum('bvi,bai->bva',a,axes);pb=np.einsum('bvi,bai->bva',b,axes)
    widths=np.minimum(pa.max(axis=1),pb.max(axis=1))-np.maximum(pa.min(axis=1),pb.min(axis=1))
    exact_overlaps+=int(np.all(widths>1e-7,axis=1).sum())
assert exact_overlaps==0,f'{exact_overlaps} positive-area UV triangle intersections'
# Interior samples, strictly away from triangle edges. Shared UV edges are legal.
# Full atlas resolution catches overlaps with area at least one texel.
N=4096;count=np.zeros((N,N),np.uint16)
for pts in triangles*N:
    mn=np.maximum(np.floor(pts.min(axis=0)-.5).astype(int),0)
    mx=np.minimum(np.ceil(pts.max(axis=0)-.5).astype(int),N-1)
    if np.any(mx<mn):continue
    a,b,c=pts;ab=b-a;ac=c-a;det=ab[0]*ac[1]-ab[1]*ac[0]
    if abs(det)<1e-10:continue
    yy,xx=np.mgrid[mn[1]:mx[1]+1,mn[0]:mx[0]+1];px=xx+.5-a[0];py=yy+.5-a[1]
    u=(px*ac[1]-py*ac[0])/det;v=(ab[0]*py-ab[1]*px)/det
    inside=(u>1e-6)&(v>1e-6)&(u+v<1-1e-6)
    count[mn[1]:mx[1]+1,mn[0]:mx[0]+1]+=inside.astype(np.uint16)
overlap=int((count>1).sum());assert overlap==0,f'{overlap} overlapping lightmap pixels'
bvh=BVHTree.FromPolygons(vertices,faces,all_triangles=True)
# 4 seats x 9 screen samples, including every maximum-size edge and corner.
sightlines=[]
for name in ['Seat1','Seat2','Seat3','Seat4']:
    origin=Vector(expected[name])+Vector((0,0,1.2))
    for x in [-9.5,0,9.5]:
        for z in [.4,4.5,8.6]:
            target=Vector((x,11.9,z));delta=target-origin
            hit=bvh.ray_cast(origin,delta.normalized(),delta.length-.001)
            assert hit[0] is None,(name,x,z,tuple(hit[0]))
            sightlines.append([name,x,z,'clear'])
textures={}
for p in sorted((OUT/'Textures').glob('*.png')):
    im=bpy.data.images.load(str(p));size=list(im.size)
    assert size==([4096,4096] if p.name=='Cinema_Light.png' else [1024,1024])
    textures[p.name]={'size':size,'bytes':p.stat().st_size}
    if p.name=='Cinema_Light.png':
        pix=np.empty(N*N*4,np.float32);im.pixels.foreach_get(pix);rgb=pix.reshape(N,N,4)[:,:,:3]
        # 8-bit PNG image pixels are normalized stored sRGB sample values.
        atlas_stats={'covered_texels':int((count>0).sum()),'overlapping_texels':overlap,'positive_area_triangle_overlaps':exact_overlaps,'surface_fraction':float((count>0).mean()),'covered_rgb_percentiles_srgb':np.percentile(rgb[count>0],[1,50,95,99,100],axis=0).tolist(),'covered_clipped_fraction':float(np.any(rgb[count>0]>=1.0,axis=1).mean())}
        assert atlas_stats['covered_clipped_fraction']<.01
result={'fbx_roundtrip':'PASS','triangles':stats,'total_triangles':sum(stats.values()),'mesh_objects':len(objs),'materials':len(objs),'textures':textures,'markers':'6 exact; positions within 0.1 mm','sightlines':f'{len(sightlines)} / {len(sightlines)} clear','atlas':atlas_stats,'normals':normal_evidence,'row_construction':arm_evidence,'architecture_triangles':sum(v['triangles'] for v in architecture.values()),'architecture_unchanged':architecture==baseline_signature if baseline_signature is not None else None}
(ROOT/'Logs/art-preview/verification.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))
