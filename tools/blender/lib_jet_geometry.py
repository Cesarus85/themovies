"""Jet mesh toolkit, adapted from the accepted Rooftop primitives without modifying them."""
import math
from mathutils import Vector
import bpy
import bmesh
import numpy as np

class Geometry:
    def __init__(self):
        self.parts = {}
        self.bevel_cache = {}
    def add(self, mat, verts, faces, smooth=False, uvs=None):
        vs, fs, ss, tex = self.parts.setdefault(mat, ([], [], [], []))
        start = len(vs)
        vs.extend(verts)
        for i, face in enumerate(faces):
            fs.append(tuple(start+j for j in face)); ss.append(smooth)
            if uvs is not None:
                tex.append(uvs[i]); continue
            pts = [Vector(verts[j]) for j in face]
            n = (pts[1]-pts[0]).cross(pts[2]-pts[0])
            axis = max(range(3), key=lambda k:abs(n[k]))
            axes = [(1,2),(0,2),(0,1)][axis]
            tex.append([(p[axes[0]],p[axes[1]]) for p in pts])
    def box(self, mat, c, size, bevel=0, omit=()):
        # Sub-11 cm strips use clean planes: microscopic bevel islands cannot
        # receive reliable texels in a 2048 atlas and are imperceptible at seats.
        bevel=0 if min(size)<.11 else min(bevel,min(size)*.45)
        key = (*size,bevel)
        if bevel:
            if key not in self.bevel_cache:
                bm=bmesh.new(); bmesh.ops.create_cube(bm,size=1)
                for v in bm.verts:
                    v.co=Vector([v.co[k]*size[k] for k in range(3)])
                bmesh.ops.bevel(bm,geom=list(bm.edges),offset=bevel,segments=2,affect='EDGES')
                bm.verts.ensure_lookup_table(); bm.verts.index_update()
                self.bevel_cache[key]=([tuple(v.co) for v in bm.verts], [tuple(v.index for v in f.verts) for f in bm.faces])
                bm.free()
            vs,fs=self.bevel_cache[key]
            # Remove bottom polygons from cushions, capitals and plinths if requested.
            if 'bottom' in omit: fs=[f for f in fs if not all(vs[i][2]<-size[2]/2+0.0001 for i in f)]
        else:
            x,y,z=[s/2 for s in size]
            vs=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
            fs=[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)]
            fs=[f for i,f in enumerate(fs) if i not in omit]
        self.add(mat,[(v[0]+c[0],v[1]+c[1],v[2]+c[2]) for v in vs],fs)
    def tube(self,mat,points,radius,segments=8,closed=False):
        vs=[]; fs=[]
        pts=[]
        for p in points:
            v=Vector(p)
            if not pts or (v-pts[-1]).length>1e-6:pts.append(v)
        if closed and (pts[-1]-pts[0]).length<1e-6:pts.pop()
        previous=None; frame=None
        for i,p in enumerate(pts):
            tangent=pts[(i+1)%len(pts)]-pts[i-1] if closed else pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]
            tangent.normalize()
            if previous is None:
                reference=min((Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1))),key=lambda v:abs(tangent.dot(v)))
                frame=tangent.cross(reference).normalized()
            else:
                frame=previous.rotation_difference(tangent)@frame
                frame=(frame-tangent*frame.dot(tangent)).normalized()
            a=frame; b=tangent.cross(a).normalized();previous=tangent
            for j in range(segments):
                t=j*math.tau/segments; vs.append(tuple(p+radius*(a*math.cos(t)+b*math.sin(t))))
        for i in range(len(pts) if closed else len(pts)-1):
            for j in range(segments): fs.append((i*segments+j,i*segments+(j+1)%segments,((i+1)%len(pts))*segments+(j+1)%segments,((i+1)%len(pts))*segments+j))
        self.add(mat,vs,fs,True)
    def sphere(self,mat,c,size,seg=10,rings=6):
        vs=[];fs=[]
        for i in range(rings+1):
            a=math.pi*i/rings
            for j in range(seg):
                t=math.tau*j/seg
                vs.append((c[0]+size[0]*math.sin(a)*math.cos(t),c[1]+size[1]*math.sin(a)*math.sin(t),c[2]+size[2]*math.cos(a)))
        for i in range(rings):
            for j in range(seg):
                a=i*seg+j;b=i*seg+(j+1)%seg;c1=(i+1)*seg+(j+1)%seg;d=(i+1)*seg+j
                fs.append((a,c1,d) if i==0 else (a,b,c1) if i==rings-1 else (a,b,c1,d))
        self.add(mat,vs,[f[::-1] for f in fs],True)
    def softbox(self,mat,c,size,power=.4,lean=0,seg=28,rings=14):
        # Smooth superellipsoid: padded rectangular silhouette with soft corners.
        def sp(v):return 0.0 if abs(v)<1e-10 else math.copysign(abs(v)**power,v)
        vs=[];fs=[]
        for i in range(rings+1):
            lat=-math.pi/2+math.pi*i/rings
            for j in range(seg):
                lon=math.tau*j/seg
                z=size[2]/2*sp(math.sin(lat))
                vs.append((c[0]+size[0]/2*sp(math.cos(lat))*sp(math.cos(lon)),
                           c[1]+size[1]/2*sp(math.cos(lat))*sp(math.sin(lon))-lean*z,c[2]+z))
        for i in range(rings):
            for j in range(seg):
                a=i*seg+j;b=i*seg+(j+1)%seg;cc=(i+1)*seg+(j+1)%seg;d=(i+1)*seg+j
                f=(b,cc,d) if i==0 else (a,b,cc) if i==rings-1 else (a,b,cc,d)
                fs.append(f)
        self.add(mat,vs,fs,True)
    def lathe(self,mat,c,profile,seg=10):
        vs=[(c[0]+r*math.cos(math.tau*j/seg),c[1]+r*math.sin(math.tau*j/seg),c[2]+z) for r,z in profile for j in range(seg)]
        fs=[(i*seg+j,i*seg+(j+1)%seg,(i+1)*seg+(j+1)%seg,(i+1)*seg+j) for i in range(len(profile)-1) for j in range(seg)]
        self.add(mat,vs,fs,True)
    def objects(self):
        result=[]
        for mat,(vs,fs,ss,uvs) in self.parts.items():
            mesh=bpy.data.meshes.new(mat.name);mesh.from_pydata(vs,[],fs);mesh.update()
            uv=mesh.uv_layers.new(name='UVMap')
            for poly,s,coords in zip(mesh.polygons,ss,uvs):
                poly.use_smooth=s
                for li,xy in zip(poly.loop_indices,coords): uv.data[li].uv=xy
            obj=bpy.data.objects.new(mat.name,mesh);bpy.context.scene.collection.objects.link(obj);mesh.materials.append(mat);result.append(obj)
        return result
