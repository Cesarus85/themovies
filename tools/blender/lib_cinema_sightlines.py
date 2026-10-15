"""Dense full-screen visibility probes, including a lower-eye clearance check."""
import numpy as np
from mathutils import Vector
from lib_cinema_markers import EYE_HEIGHT,mesh_bvh


def elevated_sightlines(scene):
    bvh=mesh_bvh([o for o in scene.objects if o.type=='MESH'])
    anchor=scene.objects['ScreenAnchor'].matrix_world.translation
    top=scene.objects['ScreenTop'].matrix_world.translation
    checks=[];failures=[]
    # Include all edges, corners and the center; 0.5 m horizontal spacing.
    for i in range(120,152):
        name=f'Spot_{i:03d}'
        for reserve in (0,.05):
            origin=scene.objects[name].matrix_world.translation+Vector((0,0,EYE_HEIGHT-reserve))
            for x in np.linspace(anchor.x-9.5,anchor.x+9.5,39):
                for z in np.linspace(anchor.z,top.z,17):
                    target=Vector((x,anchor.y,z));delta=target-origin
                    hit,normal,face,distance=bvh.ray_cast(origin,delta.normalized(),delta.length-.001)
                    if hit is not None:
                        failures.append({'spot':name,'eye_reserve_m':reserve,'screen_xz':[float(x),float(z)],'hit':list(hit)})
            checks.append({'spot':name,'eye_height':EYE_HEIGHT-reserve,'rays':39*17})
    total=sum(check['rays'] for check in checks)
    return {'eye_height':EYE_HEIGHT,'lower_eye_clearance_probe_m':.05,
            'gallery_spots':20,'box_spots':12,'screen_grid':[39,17],
            'rays':total,'clear':total-len(failures),'blocked':len(failures),
            'failures':failures}
