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
    # Shared standards cross the old per-chair bounds. Compare complete row
    # envelopes, identical for baseline and revised export. Box bounds include
    # both old front/back chairs and the new two-place row; no balustrades/floors.
    spans=[(-8.55,-6.3),(-4.125,4.125),(6.3,8.55)]
    bounds=[]
    for y,z in [(-r*1.65,r*.45) for r in range(-2,4)]+[(-6.08,4.825)]:
        bounds += [(lo-.46,hi+.46,y-.9,y+.055,z-.0001,z+1.24) for lo,hi in spans]
    for side in (-1,1):
        for y in [-2.7,3.15,8.2]:
            bounds.append((side*10.6-.84,side*10.6+.84,y-1.45,y+.61,3.6199,4.86))
    mesh=obj.data;mesh.calc_loop_triangles()
    coords=np.array([list(obj.matrix_world@v.co) for v in mesh.vertices])
    vi=np.array([tuple(t.vertices) for t in mesh.loop_triangles])
    mask=np.zeros(len(vi),bool)
    for x0,x1,y0,y1,z0,z1 in bounds:
        inside=(coords[:,0]>x0)&(coords[:,0]<x1)&(coords[:,1]>y0)&(coords[:,1]<y1)&(coords[:,2]>z0)&(coords[:,2]<z1)
        mask |= np.all(inside[vi],axis=1)
    return mask


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
