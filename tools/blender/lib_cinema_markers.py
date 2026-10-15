"""Measure seated head anchors on the final padded meshes, then translate the hall."""
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

EYE_HEIGHT=1.30
# Side boxes step down toward the screen so their floors/seats cannot mask it.
BOX_FLOORS={-2.7:2.90,3.15:1.30,8.2:0.00}
SHOULDER_HEIGHT=.85
HEAD_CLEARANCE=.12


def chair_layout():
    """Stable Spot ordering: 120 stalls, 20 rear gallery, 12 side-box places."""
    xs=[(i-5.5)*.75 for i in range(12)]+[s*x for s in (-1,1) for x in [6.3,7.05,7.8,8.55]]
    return ([(x,-row*1.65,row*.45) for row in range(-2,4) for x in xs]
            +[(x,-6.08,4.825) for x in xs]
            +[(side*10.6+dx,y+.2,BOX_FLOORS[y]) for side in (-1,1) for y in [-2.7,3.15,8.2] for dx in [-.375,.375]])


def mesh_bvh(objects):
    vertices=[];faces=[]
    for obj in objects:
        mesh=obj.data;mesh.calc_loop_triangles();offset=len(vertices)
        vertices.extend(obj.matrix_world@v.co for v in mesh.vertices)
        faces.extend(tuple(offset+i for i in t.vertices) for t in mesh.loop_triangles)
    return BVHTree.FromPolygons(vertices,faces,all_triangles=True)


def anchor_seated_heads(scene):
    """Idempotent: move every scene object together, retaining meshes, UVs and bake.

    Each ray measures the actual reduced upholstery at its horizontal center,
    0.85 m above the local floor. +Y faces the screen. Markers are floor projections
    of the eyes, 0.12 m in front of that padded front surface, not on the seat edge.
    """
    bpy.context.view_layer.update()
    previous=float(scene.get('hall_shift_y',0))
    upholstery=mesh_bvh([o for o in scene.objects if o.type=='MESH' and o.name=='Cinema_Velvet_Seat'])
    positions=chair_layout();heads=[];surfaces=[]
    for x,y,z in positions:
        origin=Vector((x,y+previous+1,z+SHOULDER_HEIGHT))
        hit,normal,_,_=upholstery.ray_cast(origin,Vector((0,-1,0)),2)
        assert hit is not None and normal.y>.8,'Missing forward-facing padded backrest'
        surfaces.append(float(hit.y-y-previous))
        heads.append(Vector((x,hit.y+HEAD_CLEARANCE,z)))
    participants=[next(i for i,p in enumerate(positions) if abs(p[0]-x)<1e-6 and abs(p[1])<1e-6 and abs(p[2])<1e-6)
                  for x in (-1.125,-.375,.375,1.125)]
    delta=-sum(heads[i].y for i in participants)/4
    if abs(delta)<1e-6:delta=0.0  # Do not accumulate float noise on checkpoint re-exports.
    assert max(abs(heads[i].y+delta) for i in participants)<1e-5
    for o in list(scene.objects):
        if o.type=='EMPTY' and (o.name.startswith('Seat') or o.name.startswith('Spot_')):
            bpy.data.objects.remove(o,do_unlink=True)
    for i,point in enumerate(heads):
        marker=bpy.data.objects.new(f'Spot_{i:03d}',None);scene.collection.objects.link(marker)
        marker.location=point;marker.empty_display_type='ARROWS';marker.empty_display_size=.15
    # All baked geometry and both portal markers move as one rigid translation.
    # Root objects also move any parented descendants exactly once.
    for o in scene.objects:
        if o.parent is None:o.location.y+=delta
    for n,i in enumerate(participants,1):
        marker=bpy.data.objects.new(f'Seat{n}',None);scene.collection.objects.link(marker)
        marker.location=((n-2.5)*.75,0,0);marker.empty_display_type='ARROWS'
        scene.objects[f'Spot_{i:03d}'].location=marker.location
    scene['hall_shift_y']=previous+delta
    bpy.context.view_layer.update()
    return {'hall_shift_y':previous+delta,'this_export_shift_y':delta,
            'shoulder_height':SHOULDER_HEIGHT,'head_clearance':HEAD_CLEARANCE,
            'backrest_front_local_y':{'stalls':surfaces[participants[0]],'far':surfaces[120]},
            'spot_counts':{'stalls':120,'rear_gallery':20,'boxes':12},
            'spot_ranges':{'stalls':'Spot_000..Spot_119','rear_gallery':'Spot_120..Spot_139','boxes':'Spot_140..Spot_151'},
            'participants':{f'Seat{n}':f'Spot_{i:03d}' for n,i in enumerate(participants,1)}}
