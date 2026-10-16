"""Compare final exported surfaces to the task-local accepted baseline.
Blender -b --factory-startup --python-exit-code 1 -P tools/blender/verify_room_motion_preservation.py
Baseline lives only in ignored Logs/art-preview/motion-baseline; no app/Unity use.
"""
import sys
sys.dont_write_bytecode=True
import hashlib
import json
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_seated_head_clearance import audit as audit_heads
from lib_verify_room_motion import audit as audit_effects
ROOT=Path(__file__).resolve().parents[2];LOG=ROOT/'Logs/art-preview';BASE=LOG/'motion-baseline'
SURFACES={}


def capture(path,heads=False):
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(path));bpy.context.view_layer.update()
    vs=[];fs=[];tris=[]
    objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
    for o in objects:
        me=o.data;me.calc_loop_triangles();offset=len(vs)
        vs.extend(o.matrix_world@v.co for v in me.vertices)
        fs.extend(tuple(offset+i for i in t.vertices) for t in me.loop_triangles)
    xyz=np.asarray(vs,dtype=np.float64);triangles=xyz[np.asarray(fs)]
    markers={o.name:np.asarray(o.matrix_world) for o in bpy.context.scene.objects if o.type=='EMPTY' and not o.name.startswith('FireLight_')}
    head=audit_heads(objects,bpy.context.scene.objects) if heads else None
    tree=BVHTree.FromPolygons(vs,fs,all_triangles=True)
    SURFACES[id(tree)]=triangles
    return tree,triangles,markers,head


def distances(triangles,tree):
    samples=np.concatenate((triangles.mean(axis=1),triangles[:,0],triangles[:,1],triangles[:,2]))
    result=[];target=SURFACES[id(tree)];lower=target.min(axis=1);upper=target.max(axis=1)
    refine_threshold=.001 if np.abs(target).max()>1000 else 1e-5
    for point in samples:
        distance=tree.find_nearest(Vector(point))[3]
        if distance>refine_threshold:
            # Float32 BVH closest-point tests lose precision on very long, thin
            # molding triangles. Refine candidates in float64 before reporting
            # a surface change; do not loosen the physical comparison tolerance.
            radius=distance+.001
            candidates=np.all((lower<=point+radius)&(upper>=point-radius),axis=1)
            tri=target[candidates]
            a,b,c=tri[:,0],tri[:,1],tri[:,2];e=b-a;f=c-a;q=point-a
            ee=np.sum(e*e,axis=1);ff=np.sum(f*f,axis=1);ef=np.sum(e*f,axis=1)
            pe=np.sum(q*e,axis=1);pf=np.sum(q*f,axis=1);det=ee*ff-ef*ef
            safe=np.where(abs(det)>1e-25,det,1)
            v=(ff*pe-ef*pf)/safe;w=(ee*pf-ef*pe)/safe
            inside=(abs(det)>1e-25)&(v>=-1e-7)&(w>=-1e-7)&(v+w<=1+1e-7)
            d=np.where(inside,np.linalg.norm(q-v[:,None]*e-w[:,None]*f,axis=1),np.inf)
            for start,end in ((a,b),(b,c),(c,a)):
                edge=end-start;den=np.maximum(np.sum(edge*edge,axis=1),1e-30)
                factor=np.clip(np.sum((point-start)*edge,axis=1)/den,0,1)
                d=np.minimum(d,np.linalg.norm(point-start-factor[:,None]*edge,axis=1))
            distance=float(d.min())
        result.append(distance)
    return np.asarray(result),samples


report={}
for room in (sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ('Cinema','Home','Rooftop','Space','OrientExpress','Jet')):
    old=BASE/room;new=ROOT/'Assets/TheMovies/Art'/room
    a,ta,ma,ha=capture(old/f'{room}.fbx',room in ('Cinema','Home'))
    b,tb,mb,hb=capture(new/f'{room}.fbx',room in ('Cinema','Home'))
    audit_effects(bpy.context.scene,room,ROOT)
    assert ma.keys()==mb.keys()
    for name in ma:assert np.allclose(ma[name],mb[name],rtol=0,atol=2e-5),(room,name,'marker transform changed')
    if ta.shape==tb.shape and np.array_equal(ta,tb):
        forward=np.zeros(1);reverse=np.zeros(1);points=np.empty((0,3))
    else:
        forward,points=distances(ta,b)
        self_distance,_=distances(ta,a)
        forward=np.maximum(0,forward-self_distance)
        reverse,points=distances(tb,a)
        self_distance,_=distances(tb,b)
        reverse=np.maximum(0,reverse-self_distance)
    # FBX float32 sphere transforms at kilometre scale have millimetre precision.
    tolerance=.003 if room=='Space' else .00003
    assert forward.max()<tolerance,(room,'accepted surface moved/removed',float(forward.max()))
    added=points[reverse>=tolerance] if len(points) else points
    if room=='Home':
        assert len(added)>0
        assert np.all((added[:,0]>-4.05)&(added[:,0]<-3.65)&(added[:,1]>.85)&(added[:,1]<2.55)&(added[:,2]>.24)&(added[:,2]<1.90)),('Home','addition outside fire/candle bounds')
    else:assert not len(added),(room,'new or displaced surface',float(reverse.max()))
    before=(old/'Textures'/f'{room}_Light.png').read_bytes();after=(new/'Textures'/f'{room}_Light.png').read_bytes()
    assert (before!=after)==(room=='Home'),(room,'unexpected bake change')
    if room=='Jet':
        assert (old/'Jet.fbx').read_bytes()==(new/'Jet.fbx').read_bytes()
        assert {p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (old/'Textures').glob('*.png')}=={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (new/'Textures').glob('*.png')}
    result={'accepted_triangles':len(ta),'delivered_triangles':len(tb),'max_original_surface_error_m':float(forward.max()),'added_surface_samples':len(added),'rebaked':before!=after,'accepted_markers_unchanged':True}
    if ha:
        assert ha['failed_markers']==hb['failed_markers'],(room,'new head-clearance failure')
        assert abs(ha['minimum_clearance_lower_bound_m']-hb['minimum_clearance_lower_bound_m'])<1e-5
        result['preexisting_head_clearance']={'baseline_failures':ha['failed_markers'],'current_failures':hb['failed_markers'],'baseline_minimum_m':ha['minimum_clearance_lower_bound_m'],'current_minimum_m':hb['minimum_clearance_lower_bound_m'],'unchanged':True}
    report[room]=result;print('PRESERVATION PASS',room,flush=True)
    (LOG/'room-motion-preservation.json').write_text(json.dumps(report,indent=2)+'\n')
(LOG/'room-motion-preservation.json').write_text(json.dumps(report,indent=2)+'\n')
