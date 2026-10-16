"""2D lightmap intersection audit, including coplanar containment at shared UV vertices."""
import numpy as np


def uv_overlaps(triangles):
    triangles=np.asarray(triangles,np.float64)
    lower=triangles.min(axis=1);upper=triangles.max(axis=1)
    order=np.argsort(lower[:,0],kind='stable');active=np.empty(0,np.int32);pairs=[]
    # A sweep over bounding rectangles catches containment as well as crossing
    # edges. A triangle-surface BVH can miss coplanar triangles sharing a vertex.
    for i in order:
        active=active[upper[active,0]>lower[i,0]+1e-7]
        near=active[(upper[active,1]>lower[i,1]+1e-7)&(lower[active,1]<upper[i,1]-1e-7)]
        pairs.extend((int(j),int(i)) for j in near)
        active=np.append(active,i)
    pairs=np.asarray(pairs,np.int32).reshape(-1,2);bad=[]
    for start in range(0,len(pairs),20000):
        batch=pairs[start:start+20000];a=triangles[batch[:,0]];b=triangles[batch[:,1]]
        edges=np.concatenate((np.roll(a,-1,axis=1)-a,np.roll(b,-1,axis=1)-b),axis=1)
        axes=np.stack((-edges[:,:,1],edges[:,:,0]),axis=2)
        axes/=np.maximum(np.linalg.norm(axes,axis=2)[:,:,None],1e-20)
        pa=np.einsum('bvi,bai->bva',a,axes);pb=np.einsum('bvi,bai->bva',b,axes)
        widths=np.minimum(pa.max(axis=1),pb.max(axis=1))-np.maximum(pa.min(axis=1),pb.min(axis=1))
        bad.extend(batch[np.all(widths>1e-7,axis=1)].tolist())
    return bad


def audit_padding(triangles, resolution=2048, minimum_px=4):
    """Audit real UV island boundaries with exact segment distances (not pack settings).

    Islands are connected across identical UV edges, including FBX vertex splits.
    A sweep limits distance tests to distinct islands within the requested gutter.
    """
    triangles=np.asarray(triangles,np.float64)
    parent=list(range(len(triangles)))
    def root(i):
        while parent[i]!=i:
            parent[i]=parent[parent[i]];i=parent[i]
        return i
    edges={}
    for i,tri in enumerate(triangles):
        for a,b in zip(tri,np.roll(tri,-1,axis=0)):
            key=tuple(sorted((tuple(a),tuple(b))))
            if key in edges:
                previous=edges[key][0];parent[root(i)]=root(previous);edges[key][1]+=1
            else:edges[key]=[i,1]
    boundary=[];owners=[]
    for key,(i,count) in edges.items():
        if count==1:boundary.append(key);owners.append(root(i))
    seg=np.asarray(boundary)*resolution;owners=np.asarray(owners)
    lower=seg.min(axis=1);upper=seg.max(axis=1);active=np.empty(0,np.int32);pairs=[]
    radius=minimum_px-1e-3
    for i in np.argsort(lower[:,0],kind='stable'):
        active=active[upper[active,0]+radius>lower[i,0]]
        near=active[(upper[active,1]+radius>lower[i,1])&(lower[active,1]-radius<upper[i,1])&(owners[active]!=owners[i])]
        pairs.extend((int(j),int(i)) for j in near);active=np.append(active,i)
    def point_segment(p,a,b):
        edge=b-a;delta=p-a
        t=np.sum(delta*edge,axis=1)/np.maximum(np.sum(edge*edge,axis=1),1e-25)
        return np.linalg.norm(delta-np.clip(t,0,1)[:,None]*edge,axis=1)
    closest=float('inf');violations=0
    for start in range(0,len(pairs),20000):
        p=np.asarray(pairs[start:start+20000]);a=seg[p[:,0]];b=seg[p[:,1]]
        distance=np.minimum.reduce([point_segment(a[:,0],b[:,0],b[:,1]),point_segment(a[:,1],b[:,0],b[:,1]),
                                    point_segment(b[:,0],a[:,0],a[:,1]),point_segment(b[:,1],a[:,0],a[:,1])])
        closest=min(closest,float(distance.min()));violations+=int((distance<radius).sum())
    edge_margin=float(min(triangles.min(),1-triangles.max())*resolution)
    return {'islands':len({root(i) for i in range(len(triangles))}),'boundary_segments':len(seg),
            'required_gap_px':minimum_px,'gap_violations':violations,
            'minimum_candidate_gap_px':closest if np.isfinite(closest) else None,
            'atlas_border_px':edge_margin,'pass':violations==0 and edge_margin>=minimum_px-1e-3}
