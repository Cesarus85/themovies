"""Geometry/normal evidence shared by the exporter and independent FBX audit."""
import numpy as np


def normal_rows(obj):
    mesh=obj.data;mesh.calc_loop_triangles()
    normal_matrix=obj.matrix_world.to_3x3().inverted().transposed()
    coords=np.array([list(obj.matrix_world@v.co) for v in mesh.vertices],np.float64)
    normals=np.array([list(normal_matrix@n.vector) for n in mesh.corner_normals],np.float64)
    normals/=np.maximum(np.linalg.norm(normals,axis=1)[:,None],1e-20)
    vi=np.array([tuple(t.vertices) for t in mesh.loop_triangles])
    li=np.array([tuple(t.loops) for t in mesh.loop_triangles])
    pts=coords[vi];ns=normals[li]
    rounded=np.round(pts,5)
    order=np.lexsort((rounded[:,:,2],rounded[:,:,1],rounded[:,:,0]),axis=1)
    pts=np.take_along_axis(pts,order[:,:,None],axis=1)
    ns=np.take_along_axis(ns,order[:,:,None],axis=1)
    flat=np.round(pts.reshape(-1,9),5)
    face_order=np.lexsort(tuple(flat[:,i] for i in reversed(range(9))))
    return np.concatenate((pts.reshape(-1,9),ns.reshape(-1,9)),axis=1)[face_order]


def chair_masks(obj):
    xs=[(i-5.5)*.75 for i in range(12)]+[s*x for s in (-1,1) for x in [6.3,7.05,7.8,8.55]]
    positions=[(x,-r*1.65,r*.45) for r in range(-2,4) for x in xs]
    positions += [(x,-6.08,4.825) for x in xs]
    positions += [(s*10.6,y+d,3.62) for s in (-1,1) for y in [-2.7,3.15,8.2] for d in [-.55,.55]]
    mesh=obj.data;mesh.calc_loop_triangles()
    coords=np.array([list(obj.matrix_world@v.co) for v in mesh.vertices])
    seat_id=np.full(len(coords),-1,np.int32)
    for i,position in enumerate(positions):
        v=coords-np.array(position)
        inside=(np.abs(v[:,0])<.38)&(v[:,1]>-.9)&(v[:,1]<.055)&(v[:,2]>-.0001)&(v[:,2]<1.24)
        seat_id[inside]=i
    vi=np.array([tuple(t.vertices) for t in mesh.loop_triangles]);ids=seat_id[vi]
    return (ids[:,0]>=0)&(ids[:,0]==ids[:,1])&(ids[:,0]==ids[:,2])


def architecture_signature(objects):
    # Disregard new UV2 packing and the rebake, but compare every non-chair triangle
    # including its original flat/smooth flag and material assignment.
    import hashlib
    result={}
    for o in objects:
        mask=chair_masks(o);rows=[];mesh=o.data
        for is_seat,t in zip(mask,mesh.loop_triangles):
            if is_seat:continue
            p=sorted(tuple(round(c,5) for c in o.matrix_world@mesh.vertices[i].co) for i in t.vertices)
            rows.append((tuple(p),mesh.polygons[t.polygon_index].use_smooth))
        if rows:result[o.name]={'triangles':len(rows),'sha256':hashlib.sha256(repr(sorted(rows)).encode()).hexdigest()}
    return result
