"""Seat-only, subdivision-shaped theatre furniture; existing hall materials unchanged."""
import math
import bpy
import bmesh
from mathutils import Vector
from lib_cinema import Geometry


def outline(angle,kind):
    c,s=math.cos(angle),math.sin(angle)
    exponent=.62 if kind=='back' else .48
    half=(.285,.31) if kind=='back' else (.28,.29) if kind=='seat' else (.048,.29)
    return (math.copysign(abs(c)**exponent,c)*half[0],math.copysign(abs(s)**exponent,s)*half[1])


def place(u,v,w,kind,side=0):
    if kind=='back':return (u,-.63-v*math.tan(math.radians(12))+w,.89+v)
    if kind=='seat':return (u,-.30+v,.48+w+.06*v)
    return (side*.32+u,-.32+v,.725+w-.035*v)


def pillow(g,mat,kind,target,side=0):
    """Closed padded control cage -> Catmull-Clark -> symmetry-aware reduction."""
    n=16;vs=[];fs=[]
    depth=.075 if kind=='back' else .07 if kind=='seat' else .033
    rings=[(.40,depth*.84),(.85,depth), (1.0,0),(.87,-depth*.72),(.40,-depth*.82)]
    for radius,w in rings:
        for j in range(n):
            u,v=outline(math.tau*j/n,kind);u*=radius;v*=radius
            # Gentle transverse bucket: the wings sit forward of the central spine.
            if kind=='back' and w>0:w1=w+.032*(u/.285)**2-.020*(1-(u/.285)**2)
            elif kind=='seat' and w>0:w1=w-.018*(1-(u/.28)**2)*(1-(v/.29)**2)
            else:w1=w
            vs.append((u,v,w1))
    for r in range(len(rings)-1):
        for j in range(n):fs.append((r*n+j,r*n+(j+1)%n,(r+1)*n+(j+1)%n,(r+1)*n+j))
    vs.extend([(0,0,depth*.84-(.020 if kind=='back' else .018 if kind=='seat' else 0)),(0,0,-depth*.82)])
    for j in range(n):
        fs.append((len(vs)-2,(j+1)%n,j));fs.append((len(vs)-1,4*n+j,4*n+(j+1)%n))
    mesh=bpy.data.meshes.new('SeatCage');mesh.from_pydata(vs,[],fs);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new('SeatCage',mesh);bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    sub=obj.modifiers.new('Padded subdivision','SUBSURF');sub.levels=1;sub.render_levels=1
    bpy.ops.object.modifier_apply(modifier=sub.name)
    count=sum(len(p.vertices)-2 for p in obj.data.polygons)
    normalizer=Vector((.285,.31,.075) if kind=='back' else (.28,.29,.07))
    for v in obj.data.vertices:v.co=Vector(tuple(v.co[i]/normalizer[i] for i in range(3)))
    dec=obj.modifiers.new('Seat budget','DECIMATE');dec.ratio=target/count;dec.use_collapse_triangulate=True;dec.use_symmetry=True;dec.symmetry_axis='X'
    bpy.ops.object.modifier_apply(modifier=dec.name)
    local=[Vector(tuple(v.co[i]*normalizer[i] for i in range(3))) for v in obj.data.vertices]
    vs=[place(*v,kind,side) for v in local];fs=[tuple(p.vertices) for p in obj.data.polygons]
    if kind=='back':fs=[f[::-1] for f in fs]  # (u,v,w)->(x,z,y) changes handedness.
    half=(.285,.31) if kind=='back' else (.28,.29)
    uvs=[[(.5+local[i].x/(2*half[0]),.5+local[i].y/(2*half[1])) for i in f] for f in fs]
    g.add(mat,vs,fs,True,uvs)
    bpy.data.objects.remove(obj,do_unlink=True)
    return sum(len(f)-2 for f in fs)


def armrest(g,mat,side,far=False):
    # Rounded capsule ends with a broad top and rolled edge; no decimation of
    # the thin wooden rim, which otherwise produces long needle triangles.
    count=3 if far else 5;outline=[]
    for offset,start in [(.242,0),(-.242,math.pi)]:
        for j in range(count):
            a=start+math.pi*j/(count-1)
            outline.append((.048*math.cos(a),offset+.048*math.sin(a)))
    n=len(outline);vs=[];fs=[]
    for scale,z in [(.84,.027),(1,0),(.84,-.025)]:
        vs += [place(u*scale,v*scale,z,'arm',side) for u,v in outline]
    for r in range(2):
        for j in range(n):fs.append((r*n+j,r*n+(j+1)%n,(r+1)*n+(j+1)%n,(r+1)*n+j))
    fs += [tuple(reversed(range(n))),tuple(range(2*n,3*n))]
    # Consistent outward orientation for this capsule cage.
    mesh=bpy.data.meshes.new('ArmCage');mesh.from_pydata(vs,[],fs)
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    g.add(mat,[tuple(v.co) for v in mesh.vertices],[tuple(p.vertices) for p in mesh.polygons],True)
    triangles=sum(len(p.vertices)-2 for p in mesh.polygons);bpy.data.meshes.remove(mesh)
    return triangles


def pivot(g,mat,side,n):
    vs=[(side*(.30+dx),-.49+.033*math.cos(math.tau*j/n),.435+.033*math.sin(math.tau*j/n)) for dx in [-.04,.04] for j in range(n)]
    fs=[(j,(j+1)%n,n+(j+1)%n,n+j) for j in range(n)]
    fs.extend([tuple(reversed(range(n))),tuple(range(n,2*n))])
    if side<0:fs=[f[::-1] for f in fs]
    g.add(mat,vs,fs,True)


def template(velvet,walnut,gold,far=False):
    g=Geometry();detail={}
    detail['back']=pillow(g,velvet,'back',92 if far else 224)
    detail['seat']=pillow(g,velvet,'seat',60 if far else 176)
    detail['arms']=sum(armrest(g,walnut,side,far) for side in (-1,1))
    for side in (-1,1):
        # Put the hinge directly on the upright; concentrate silhouette vertices
        # on the ornamental C scroll, not on the straight cast pedestal.
        count=4 if far else 10
        points=[(-.49,.055)]
        points += [(-.49+.32*math.sin(math.pi*j/(count-1)),.55-.115*math.cos(math.pi*j/(count-1))) for j in range(count)]
        points += [(-.60,.60)]
        # Continuous S scroll: an open pedestal instead of the old solid box base.
        g.tube(gold,[(side*.32,y,z) for y,z in points],.024,3)
        pivot(g,gold,side,4 if far else 5)
        g.box(gold,(side*.32,-.49,.025),(.095,.20,.05),omit=(0,))
    volumes=[]
    for vs,fs,_,_ in g.parts.values():
        mesh=bpy.data.meshes.new('SeatWindingAudit');mesh.from_pydata(vs,[],fs)
        bm=bmesh.new();bm.from_mesh(mesh);unvisited=set(bm.faces)
        while unvisited:
            pending=[unvisited.pop()];component=set(pending)
            while pending:
                for edge in pending.pop().edges:
                    for face in edge.link_faces:
                        if face in unvisited:unvisited.remove(face);component.add(face);pending.append(face)
            if all(len(edge.link_faces)==2 for face in component for edge in face.edges):
                volume=0
                for face in component:
                    a=face.verts[0].co
                    for j in range(1,len(face.verts)-1):volume+=a.dot(face.verts[j].co.cross(face.verts[j+1].co))/6
                assert volume>0,'Inward-facing closed chair component'
                volumes.append(volume)
        bm.free();bpy.data.meshes.remove(mesh)
    detail['closed_components_outward']=len(volumes)
    detail['total']=sum(sum(len(f)-2 for f in part[1]) for part in g.parts.values())
    assert detail['total']<=(350 if far else 700),detail
    return g,detail


def add_chairs(g,positions,velvet,walnut,gold):
    near,near_stats=template(velvet,walnut,gold)
    far,far_stats=template(velvet,walnut,gold,True)
    for index,(x,y,z) in enumerate(positions):
        source=near if index<120 else far
        for mat,(vs,fs,ss,uvs) in source.parts.items():
            shifted=[(a+x,b+y,c+z) for a,b,c in vs]
            # Feet have deliberate flat creases; upholstery and rounded wood are smooth.
            start=len(g.parts.get(mat,([],[],[],[]))[1])
            g.add(mat,shifted,fs,True,uvs)
            g.parts[mat][2][start:]=ss
    return {'stalls':near_stats,'gallery_boxes':far_stats,'stalls_count':120,'far_count':len(positions)-120}
