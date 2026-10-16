"""Independent FBX roundtrip / budget / marker / sightline / atlas raster audit.
Run with Blender -b --factory-startup -P tools/blender/verify_cinema.py.
"""
from pathlib import Path
import json
import sys
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_cinema_validation import normal_rows,chair_masks,architecture_signature,protected_surface_signature
from lib_cinema_markers import chair_layout,mesh_bvh,EYE_HEIGHT,BOX_FLOORS
from lib_cinema_sightlines import elevated_sightlines
from lib_cinema_uvs import uv_overlaps
import math
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/TheMovies/Art/Cinema'
def mesh_evidence(obj):
    mesh=obj.data;mesh.calc_loop_triangles()
    nm=obj.matrix_world.to_3x3().inverted().transposed()
    return {'positions':np.array([list(obj.matrix_world@v.co) for v in mesh.vertices]),
            'triangles':np.array([tuple(t.vertices) for t in mesh.loop_triangles]),
            'normals':np.array([list((nm@n.vector).normalized()) for n in mesh.corner_normals]),
            'smooth':np.array([p.use_smooth for p in mesh.polygons]),
            'materials':[m.name for m in mesh.materials],
            'uvs':{uv.name:np.array([tuple(loop.uv) for loop in uv.data]) for uv in mesh.uv_layers}}

baseline_evidence=None
baseline_shift=0
front_baseline=None
baseline_markers=None
if '--baseline' in sys.argv or '--balcony-baseline' in sys.argv:
    mode='--balcony-baseline' if '--balcony-baseline' in sys.argv else '--baseline'
    baseline=Path(sys.argv[sys.argv.index(mode)+1]).resolve()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(baseline))
    baseline_evidence={o.name:mesh_evidence(o) for o in bpy.context.scene.objects if o.type=='MESH'}
    baseline_shift=bpy.context.scene.objects['ScreenAnchor'].matrix_world.translation.y-11.9
    if mode=='--balcony-baseline':
        front_baseline=protected_surface_signature([o for o in bpy.context.scene.objects if o.type=='MESH'],baseline_shift)
        baseline_markers={o.name:tuple(o.matrix_world.translation) for o in bpy.context.scene.objects if o.type=='EMPTY'}
        baseline_evidence=None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(OUT/'Cinema.fbx'))
scene=bpy.context.scene
from lib_verify_room_motion import audit as audit_effects
if '--draft' not in sys.argv:motion_evidence=audit_effects(scene,'Cinema',ROOT)
objs=[o for o in scene.objects if o.type=='MESH']
assert len(objs)<=16
assert all(len(o.data.materials)==1 for o in objs)
assert len({o.data.materials[0].name for o in objs})==len(objs)
expected={f'Seat{i+1}':((i-1.5)*.75,0,0) for i in range(4)}
hall_shift=scene.objects['ScreenAnchor'].matrix_world.translation.y-11.9
assert .45<hall_shift<.47,hall_shift
expected.update(ScreenAnchor=(0,11.9+hall_shift,.4),ScreenTop=(0,11.9+hall_shift,8.6))
spot_names={f'Spot_{i:03d}' for i in range(152)}
assert set(o.name for o in scene.objects if o.type=='EMPTY')==set(expected)|spot_names
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
architecture=architecture_signature(objs,hall_shift)
rigid_evidence=None
if baseline_evidence is not None:
    assert set(baseline_evidence)=={o.name for o in objs}
    shift=np.array((0,hall_shift-baseline_shift,0));maximum_error=0
    for o in objs:
        before=baseline_evidence[o.name];after=mesh_evidence(o)
        assert np.array_equal(before['triangles'],after['triangles']),o.name+' topology changed'
        assert before['materials']==after['materials'],o.name+' materials changed'
        assert np.array_equal(before['smooth'],after['smooth']),o.name+' shading changed'
        assert before['positions'].shape==after['positions'].shape
        error=float(np.max(np.abs(after['positions']-before['positions']-shift)))
        maximum_error=max(maximum_error,error)
        assert error<.00002,(o.name,'non-rigid geometry change',error)
        assert np.allclose(before['normals'],after['normals'],atol=1e-5),o.name+' normals changed'
        for name,uv in before['uvs'].items():
            assert np.array_equal(uv,after['uvs'][name]),(o.name,name,'UVs changed')
    rigid_evidence={'pass':True,'translation':list(shift),'maximum_vertex_error_m':maximum_error,
                    'all_triangles_materials_normals_uvs_unchanged':True}
front_scope_evidence=None
if front_baseline is not None:
    protected=protected_surface_signature(objs,hall_shift)
    assert protected==front_baseline,'Geometry/UV0/shading outside gallery front and side boxes changed'
    for name,position in baseline_markers.items():
        target=Vector(position)
        if name.startswith('Spot_') and int(name[-3:])>=140:
            target.z+=list(BOX_FLOORS.values())[((int(name[-3:])-140)//2)%3]-3.62
        assert (scene.objects[name].matrix_world.translation-target).length<.00001,(name,'unexpected marker change')
    front_scope_evidence={'pass':True,'protected_triangles':sum(v['triangles'] for v in protected.values()),
                          'stalls_gallery_screen_markers_unchanged':True,'box_markers_follow_their_floors':True}
normal_evidence={}
reference_path=ROOT/'Logs/art-preview/normals-before-export.npz'
reference=np.load(reference_path) if reference_path.exists() else None
for o in objs:
    mesh=o.data;mask=chair_masks(o,hall_shift)
    smooth=np.array([mesh.polygons[t.polygon_index].use_smooth for t in mesh.loop_triangles])
    count=int(mask.sum());smooth_count=int(smooth[mask].sum())
    if count and ('Velvet' in o.name or 'Walnut' in o.name):
        assert count==smooth_count,(o.name,count,smooth_count)
    assert mesh.has_custom_normals,o.name+' lost explicit normals in FBX'
    evidence={'chair_triangles':count,'smooth_chair_triangles':smooth_count,'has_custom_normals':mesh.has_custom_normals}
    if reference is not None:
        actual=normal_rows(o);expected_rows=reference[o.name if o.name in reference else o.name.removesuffix('_flicker2')]
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
wood_mask=chair_masks(wood,hall_shift);adj={};arm_faces=[]
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
        expected_arms += [(side*10.6+dx,y+.2-.32,BOX_FLOORS[y]+.726) for dx in [-.75,0,.75]]
assert len(components)==len(expected_arms)==179,len(components)
centers=np.array([(np.min(list(c),axis=0)+np.max(list(c),axis=0))/2 for c in components])
for expected_arm in expected_arms:
    expected_arm=(expected_arm[0],expected_arm[1]+hall_shift,expected_arm[2])
    assert np.sum(np.linalg.norm(centers-np.array(expected_arm),axis=1)<.003)==1,expected_arm
arm_evidence={'continuous_rows':27,'seats':152,'shared_standards_and_armrests':179,
              'one_armrest_per_boundary':'PASS: imported connected meshes and all 179 positions',
              'upholstery_triangles':stats['Cinema_Velvet_Seat']}
assert arm_evidence['upholstery_triangles']==52800
triangles=np.array(uvtri,np.float64)
assert triangles.min()>=-1e-5 and triangles.max()<=1.00001
# Audit real 2D overlap, including containment missed by coplanar BVH tests.
exact_overlaps=len(uv_overlaps(triangles))
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
# Check every exported Spot against real upholstery and floor triangles; marker
# names or authoring metadata alone are not sufficient placement evidence.
upholstery=mesh_bvh([o for o in objs if o.name=='Cinema_Velvet_Seat'])
points=np.array([list(scene.objects[f'Spot_{i:03d}'].matrix_world.translation) for i in range(152)])
distances=np.linalg.norm(points[:,None,:]-points[None,:,:],axis=2)
np.fill_diagonal(distances,np.inf)
assert distances.min()>=.4,float(distances.min())
spot_evidence=[]
for i,(x,y,z) in enumerate(chair_layout()):
    point=Vector(points[i]);name=f'Spot_{i:03d}'
    assert abs(point.x-x)<.0001 and abs(point.z-z)<.0001,(name,'wrong chair center/floor height')
    assert -.5<point.y-y-hall_shift<-.4,(name,'wrong row')
    hit,normal,face,distance=upholstery.ray_cast(point+Vector((0,0,.85)),Vector((0,-1,0)),.3)
    assert hit is not None and normal.y>.8 and abs(distance-.12)<.0001,(name,'not 12 cm before padded front',distance)
    floor,n,_,_=bvh.ray_cast(point+Vector((0,0,.05)),Vector((0,0,-1)),.1)
    assert floor is not None and n.z>.95 and abs(floor.z-point.z)<.0001,(name,'missing floor beneath head',floor)
    spot_evidence.append({'name':name,'area':'stalls' if i<120 else 'rear_gallery' if i<140 else 'boxes',
                          'backrest_clearance_m':float(distance),'floor_error_m':float(abs(floor.z-point.z))})
for i in range(4):
    assert (scene.objects[f'Seat{i+1}'].matrix_world.translation-scene.objects[f'Spot_{44+i:03d}'].matrix_world.translation).length<.0001
marker_evidence={'standard_seats':'4 exact, also Spot_044..Spot_047',
                 'spots':{'stalls':120,'rear_gallery':20,'boxes':12,'total':152},
                 'minimum_spot_distance_m':float(distances.min()),'hall_shift_y':hall_shift,
                 'shoulder_height_m':.85,'surface_checks':spot_evidence}

# 4 seats x 9 screen samples, including every maximum-size edge and corner.
sightlines=[]
for name in ['Seat1','Seat2','Seat3','Seat4']:
    origin=Vector(expected[name])+Vector((0,0,EYE_HEIGHT))
    for x in [-9.5,0,9.5]:
        for z in [.4,4.5,8.6]:
            target=Vector((x,expected['ScreenAnchor'][1],z));delta=target-origin
            hit=bvh.ray_cast(origin,delta.normalized(),delta.length-.001)
            assert hit[0] is None,(name,x,z,tuple(hit[0]))
            sightlines.append([name,x,z,'clear'])
elevated=elevated_sightlines(scene)
(ROOT/'Logs/art-preview/elevated-sightlines.json').write_text(json.dumps(elevated,indent=2)+'\n')
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
result={'fbx_roundtrip':'PASS','triangles':stats,'total_triangles':sum(stats.values()),'mesh_objects':len(objs),'materials':len(objs),'textures':textures,'markers':marker_evidence,'sightlines':f'{len(sightlines)} / {len(sightlines)} clear','elevated_sightlines':elevated,'atlas':atlas_stats,'normals':normal_evidence,'row_construction':arm_evidence,'architecture_triangles':sum(v['triangles'] for v in architecture.values()),'architecture_unchanged':rigid_evidence['pass'] if rigid_evidence else None,'rigid_shift':rigid_evidence,'balcony_front_scope':front_scope_evidence}
result['validation']='PASS' if elevated['blocked']==0 else 'FAIL_ELEVATED_SIGHTLINES'
(ROOT/'Logs/art-preview/verification.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))

assert elevated['blocked']==0,('Elevated sightlines blocked',elevated['blocked'],elevated['failures'][:8])
