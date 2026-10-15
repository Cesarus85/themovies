"""Optional comparison to a saved accepted FBX; ignores lightmap UVs after rebaking."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import bpy
import numpy as np


def capture():
    result={}
    for o in bpy.context.scene.objects:
        if o.type!='MESH' or o.data.materials[0].name in ('Home_Night_unlit','Home_Garden_Night'):continue
        mesh=o.data;mesh.calc_loop_triangles();uv=mesh.uv_layers['UVMap'];triangles=[]
        for triangle in mesh.loop_triangles:
            corners=[]
            for li in triangle.loops:
                point=o.matrix_world@mesh.vertices[mesh.loops[li].vertex_index].co
                corners.append(tuple(round(float(v),5) for v in (*point,*uv.data[li].uv)))
            triangles.append(tuple(sorted(corners)))
        result[o.data.materials[0].name]=Counter(triangles)
    return result


def same_surface(triangle,others):
    """Allow only a changed diagonal on the SAME plane with identical UV0 mapping."""
    xyz=np.array([t[:3] for t in triangle]);uv=np.array([t[3:] for t in triangle])
    for weights in ((1,0,0),(0,1,0),(0,0,1),(.5,.5,0),(0,.5,.5),(.5,0,.5),(1/3,1/3,1/3)):
        point=np.array(weights)@xyz;wanted=np.array(weights)@uv;found=False
        for other in others:
            q=np.array([t[:3] for t in other]);a=q[1]-q[0];b=q[2]-q[0]
            if np.linalg.norm(np.cross(a,b))<1e-12:continue
            factors=np.linalg.lstsq(np.array([a,b]).T,point-q[0],rcond=None)[0]
            v,w=factors
            if min(v,w,1-v-w)<-2e-4 or np.linalg.norm(q[0]+v*a+w*b-point)>2e-5:continue
            mapped=np.array([1-v-w,v,w])@np.array([t[3:] for t in other])
            if np.max(np.abs(mapped-wanted))<3e-5:found=True;break
        if not found:return False
    return True


def compare(before,after,baseline_folder,textures):
    report={}
    for name,original in before.items():
        missing=original-after[name];added=after[name]-original
        for triangle in missing:
            assert same_surface(triangle,list(added)),(name,'changed accepted surface or UV0',triangle)
        window_additions=0
        for triangle in added:
            if same_surface(triangle,list(missing)):continue
            assert all(p[0]>=4.189 and 1.2<p[1]<4.4 and .9<p[2]<2.5 for p in triangle),(name,'addition outside window reveal')
            window_additions+=added[triangle]
        report[name]={'original_triangles':sum(original.values()),'changed_surface_or_uv0':0,
                      'equivalent_retriangulations':sum(missing.values()),'window_reveal_additions':window_additions}
    old=json.loads((Path(baseline_folder)/'textures.json').read_text());unchanged=0
    for name,checksum in old.items():
        if name in ('Home_Light.png','Home_Night_unlit_BaseColor.png'):continue
        assert hashlib.sha256((Path(textures)/name).read_bytes()).hexdigest()==checksum,(name,'changed accepted base map')
        unchanged+=1
    return {'materials':report,'unchanged_original_base_maps':unchanged,'validation':'PASS'}
