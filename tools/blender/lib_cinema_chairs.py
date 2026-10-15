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


def oriented_mesh(g,mat,vs,fs,uv=None,smooth=True):
    """Outward, closed cast/wood surfaces; optional planar ornament UVs."""
    mesh=bpy.data.meshes.new('ChairPart');mesh.from_pydata(vs,[],fs)
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(mesh);bm.free()
    vs=[tuple(v.co) for v in mesh.vertices];fs=[tuple(p.vertices) for p in mesh.polygons]
    uvs=[[uv(vs[i]) for i in f] for f in fs] if uv else None
    g.add(mat,vs,fs,smooth,uvs)
    count=sum(len(f)-2 for f in fs);bpy.data.meshes.remove(mesh)
    return count


def armrest(g,mat,far=False):
    # One capsule per shared standard. Twelve half-circle intervals at arm's
    # length, with a rolled edge, instead of two angular arms at every joint.
    count=4 if far else 7;outline=[]
    for offset,start in [(.242,0),(-.242,math.pi)]:
        for j in range(count):
            a=start+math.pi*j/(count-1)
            outline.append((.056*math.cos(a),offset+.056*math.sin(a)))
    n=len(outline);vs=[];fs=[]
    for scale,z in [(.84,.027),(1,0),(.84,-.025)]:
        vs += [place(u*scale,v*scale,z,'arm') for u,v in outline]
    for r in range(2):
        for j in range(n):fs.append((r*n+j,r*n+(j+1)%n,(r+1)*n+(j+1)%n,(r+1)*n+j))
    fs += [tuple(reversed(range(n))),tuple(range(2*n,3*n))]
    return oriented_mesh(g,mat,vs,fs)


def standard(g,bronze,gold,far=False,aisle=False):
    # A solid cast side panel: broad under the arm, concave front, swept rear,
    # an integrated flared sole. There are no separate posts or block feet.
    outline=[(-.66,0),(-.14,0),(-.12,.055),(-.16,.13),(-.20,.23),
             (-.22,.34),(-.205,.44),(-.16,.53),(-.10,.60),(-.075,.665),
             (-.09,.692),(-.19,.700),(-.34,.707),(-.50,.712),
             (-.59,.710),(-.65,.65),(-.64,.36),(-.65,.13)]
    if far:outline=[outline[i] for i in (0,1,2,4,6,8,9,10,12,14,15,17)]
    else:outline=[point for i,point in enumerate(outline) if i not in (3,16)]
    n=len(outline);vs=[];fs=[]
    rings=[(-1,True),(-1,False),(1,False),(1,True)] if not far else [(-1,False),(1,False)]
    for side,inset in rings:
        for y,z in outline:
            if inset:
                # Flat decorated faces with a softly rolled cast perimeter.
                vs.append((side*.032,-.38+(y+.38)*.93,.355+(z-.355)*.96))
            else:vs.append((side*(.046+.024*max(0,1-z/.15)),y,z))
    for r in range(len(rings)-1):
        for j in range(n):
            # The flat sole is hidden in the carpet and intentionally omitted.
            if j==0 and (far or r==1):continue
            fs.append((r*n+j,r*n+(j+1)%n,(r+1)*n+(j+1)%n,(r+1)*n+j))
    fs += [tuple(reversed(range(n))),tuple(range((len(rings)-1)*n,len(rings)*n))]
    # Two texture panels: restrained inside the row, gilt fan at aisle ends.
    uv=lambda p: ((.5 if aisle else 0)+.04+.42*(p[1]+.69)/.64,.035+.93*p[2]/.725)
    detail={'panel':oriented_mesh(g,bronze,vs,fs,uv)}
    # Flush bolt-head geometry at the flared foot. The broad cast sole is one
    # continuous panel, not a separate rectangular chair foot.
    if aisle and not far:
        for side in (-1,1):
            for y in (-.52,-.27):
                vs=[(side*.0327,y+.015*math.cos(math.tau*j/5),.085+.015*math.sin(math.tau*j/5)) for j in range(5)]
                face=tuple(range(5)) if side>0 else tuple(reversed(range(5)))
                g.add(gold,vs,[face])
        detail['bolts']=12
    return detail


def stamp(g,source,position):
    x,y,z=position
    for mat,(vs,fs,ss,uvs) in source.parts.items():
        start=len(g.parts.get(mat,([],[],[],[]))[1])
        g.add(mat,[(a+x,b+y,c+z) for a,b,c in vs],fs,True,uvs)
        g.parts[mat][2][start:]=ss


def triangle_count(g):
    return sum(sum(len(f)-2 for f in part[1]) for part in g.parts.values())


def seat_template(velvet,bronze,far=False):
    g=Geometry()
    detail={'back':pillow(g,velvet,'back',92 if far else 224),
            'seat':pillow(g,velvet,'seat',60 if far else 176)}
    # Continuous transverse hinge and back bearer terminate inside both shared
    # cast panels. The padding visibly hangs between them, with no private legs.
    g.tube(bronze,[(-.375,-.47,.445),(.375,-.47,.445)],.031,6 if far else 8)
    g.tube(bronze,[(-.375,-.625,.642),(.375,-.625,.642)],.026,6 if far else 8)
    detail['mounts']=24 if far else 32
    # Rails use a quiet bronze swatch; never wrap the side-panel fan onto them.
    vs,fs,ss,uvs=g.parts[bronze]
    uvs[:]=[[(.02,.97)]*len(face) for face in fs]
    detail['total']=triangle_count(g)
    return g,detail


def add_chairs(g,positions,velvet,walnut,gold,bronze):
    templates={};stats={}
    for far in (False,True):
        seat,detail=seat_template(velvet,bronze,far)
        standards={}
        for aisle in (False,True):
            panel=Geometry();parts=standard(panel,bronze,gold,far,aisle)
            parts['armrest']=armrest(panel,walnut,far)
            parts['total']=triangle_count(panel);standards[aisle]=(panel,parts)
        templates[far]=(seat,standards)
        stats['gallery_boxes' if far else 'stalls']={'seat':detail,'shared_standard':standards[False][1],'aisle_standard':standards[True][1]}
    # Sort into actual contiguous seat blocks, not one complete chair per place.
    levels={}
    for i,(x,y,z) in enumerate(positions):levels.setdefault((y,z,i>=120),[]).append(x)
    rows=[]
    for (y,z,far),xs in levels.items():
        blocks=[]
        for x in sorted(xs):
            if not blocks or x-blocks[-1][-1]>.751:blocks.append([])
            blocks[-1].append(x)
        for xs in blocks:
            assert all(abs(b-a-.75)<1e-5 for a,b in zip(xs,xs[1:])),xs
            seat,standards=templates[far]
            for x in xs:stamp(g,seat,(x,y,z))
            boundaries=[xs[0]-.375]+[(a+b)/2 for a,b in zip(xs,xs[1:])]+[xs[-1]+.375]
            total=len(xs)*triangle_count(seat)
            for i,x in enumerate(boundaries):
                panel,_=standards[i in (0,len(boundaries)-1)]
                stamp(g,panel,(x,y,z));total+=triangle_count(panel)
            average=total/len(xs)
            # Charge the full outside standard to the end seat, half of each
            # shared standard to each neighbour; no averaging away aisle costs.
            shared=standards[False][1]['total'];end=standards[True][1]['total']
            worst=triangle_count(seat)+end+shared/2
            assert len(xs)>=2 and worst<=(350 if far else 750),(xs,worst)
            rows.append({'centers':[[x,y,z] for x in xs],'standard_x':boundaries,'far':far,'triangles':total,'triangles_per_seat':average,'maximum_seat_triangles':worst})
    stats.update(stalls_count=120,far_count=len(positions)-120,rows=rows,
                 standards=sum(len(row['standard_x']) for row in rows),
                 total_triangles=sum(row['triangles'] for row in rows))
    return stats
