"""Geometry/normal evidence shared by the exporter and independent FBX audit."""
import numpy as np
from lib_cinema_markers import BOX_FLOORS


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


def chair_masks(obj,shift_y=0):
    # Shared standards cross the old per-chair bounds. Compare complete row
    # envelopes, identical for baseline and revised export. Box bounds include
    # both old front/back chairs and the new two-place row; no balustrades/floors.
    spans=[(-8.55,-6.3),(-4.125,4.125),(6.3,8.55)]
    bounds=[]
    for y,z in [(-r*1.65,r*.45) for r in range(-2,4)]+[(-6.08,4.825)]:
        bounds += [(lo-.46,hi+.46,y-.9,y+.055,z-.0001,z+1.24) for lo,hi in spans]
    for side in (-1,1):
        for y in [-2.7,3.15,8.2]:
            bounds.append((side*10.6-.84,side*10.6+.84,y-1.45,y+.61,BOX_FLOORS[y]-.0001,BOX_FLOORS[y]+1.24))
    mesh=obj.data;mesh.calc_loop_triangles()
    coords=np.array([list(obj.matrix_world@v.co) for v in mesh.vertices])
    coords[:,1]-=shift_y
    vi=np.array([tuple(t.vertices) for t in mesh.loop_triangles])
    mask=np.zeros(len(vi),bool)
    for x0,x1,y0,y1,z0,z1 in bounds:
        inside=(coords[:,0]>x0)&(coords[:,0]<x1)&(coords[:,1]>y0)&(coords[:,1]<y1)&(coords[:,2]>z0)&(coords[:,2]<z1)
        mask |= np.all(inside[vi],axis=1)
    return mask


def architecture_signature(objects,shift_y=0):
    # Disregard new UV2 packing and the rebake, but compare every non-chair triangle
    # including its original flat/smooth flag and material assignment.
    import hashlib
    result={}
    for o in objects:
        mask=chair_masks(o,shift_y);rows=[];mesh=o.data
        for is_seat,t in zip(mask,mesh.loop_triangles):
            if is_seat:continue
            p=sorted(tuple(round(c-(shift_y if axis==1 else 0),5) for axis,c in enumerate(o.matrix_world@mesh.vertices[i].co)) for i in t.vertices)
            rows.append((tuple(p),mesh.polygons[t.polygon_index].use_smooth))
        if rows:result[o.name]={'triangles':len(rows),'sha256':hashlib.sha256(repr(sorted(rows)).encode()).hexdigest()}
    return result


def protected_surface_signature(objects,shift_y):
    """Geometry, UV0 and shading outside the gallery front and stepped side boxes.

    UV2 changes globally on a rebake. Bounds cover old/new box floors and fronts;
    all stalls geometry, rear gallery seats and the remaining hall are compared.
    """
    import hashlib
    result={}
    for obj in objects:
        mesh=obj.data;mesh.calc_loop_triangles()
        coords=np.array([list(obj.matrix_world@v.co) for v in mesh.vertices])
        coords[:,1]-=shift_y
        vi=np.array([tuple(t.vertices) for t in mesh.loop_triangles]);pts=coords[vi]
        x,y,z=pts[:,:,0],pts[:,:,1],pts[:,:,2]
        changed=np.all((np.abs(x)<11.91)&(y> -5.731)&(y< -5.479)&(z>4.729)&(z<6.051),axis=1)
        for side in (-1,1):
            for yc in [-2.7,3.15,8.2]:
                xx=x*side;yy=y-yc
                box=np.all((xx>9.49)&(xx<11.97)&(np.abs(yy)<1.691)&(z>BOX_FLOORS[yc]-.421)&(z<4.931),axis=1)
                changed |= box
        rows=[];uv=mesh.uv_layers['UVMap']
        for skip,t,points in zip(changed,mesh.loop_triangles,pts):
            if skip:continue
            corners=sorted(tuple(round(float(c),4) for c in point)+tuple(round(float(c),5) for c in uv.data[li].uv) for point,li in zip(points,t.loops))
            rows.append((tuple(corners),mesh.polygons[t.polygon_index].use_smooth))
        if rows:result[obj.name]={'triangles':len(rows),'sha256':hashlib.sha256(repr(sorted(rows)).encode()).hexdigest()}
    return result
