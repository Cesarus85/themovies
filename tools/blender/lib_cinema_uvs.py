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
