"""Original orbital observation lounge. Metres; Z up; +Y screen direction."""
import math
import bpy
import numpy as np
from mathutils import Vector
from lib_space_geometry import Geometry

SEATS=[(-1.125,0,0),(-.375,0,0),(.375,0,0),(1.125,0,0),(-4.1,-.6,0),(-3.2,-.6,0),(3.2,-.6,0),(4.1,-.6,0)]
SCREEN=(0,8.3,1.35)
EARTH_CENTER=(280,1000,-1080)
EARTH_RADIUS=1350.
SUN=(-900,160,-80)

def perimeter(inset=0,z=0):
    a=8-inset;b=8-inset;r=max(.25,2-inset*.22)
    pts=[]
    for x,y,start in [(a-r,3+b-r,0),(-a+r,3+b-r,90),(-a+r,3-b+r,180),(a-r,3-b+r,270)]:
        for ang in np.linspace(start,start+90,17,endpoint=False):
            t=math.radians(ang);pts.append((x+r*math.cos(t),y+r*math.sin(t),z))
    return pts


def build(m,light):
    g=Geometry()
    def box(k,c,s,b=0,omit=()):g.box(m[k],c,s,b,omit)
    def tube(k,p,r=.02,n=8,closed=False):g.tube(m[k],p,r,n,closed)
    def lathe(k,c,p,n=32):g.lathe(m[k],c,p,n)
    def band(k,sections):
        rings=[perimeter(d,z) for d,z in sections];n=len(rings[0])
        g.add(m[k],[p for ring in rings for p in ring],[(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(rings)-1) for i in range(n)],True)
    # Single-sided deck and ceiling; no invisible undersides or external wall skins.
    pts=perimeter(0,0);g.add(m['Floor'],pts,[tuple(range(len(pts)))])
    pts=perimeter(.9,6.25);g.add(m['Ivory'],pts,[tuple(reversed(range(len(pts))))])
    band('Graphite',[(0,0),(0,.23),(.16,.29),(.32,.23),(.32,.04)])
    band('Ivory',[(0,5.94),(0,6.36),(.95,6.36),(.95,6.25),(.72,6.05),(0,5.94)])
    band('Wood',[(.03,5.97),(.38,5.97),(.58,6.05)])
    tube('Glow',perimeter(.45,5.945),.022,8,True)
    tube('Glow',perimeter(.38,.12),.018,6,True)
    # The ceiling is detailed with nested flush acoustical panels and inset light coves.
    for inset in [1.5,2.15]:tube('Graphite',perimeter(inset,6.235),.012,6,True)
    tube('Glow',perimeter(2.20,6.22),.022,8,True)
    for x in [-5.8,-4.8,4.8,5.8]:
        box('Wood',(x,1.7,6.21),(.055,8.7,.065),.018)
    # Warm wood border with honest plank joints, away from the standard seats.
    for side in [-1,1]:
        for ix in range(8):
            x=side*(5.15+ix*.30)
            for y in [-2,1,4,7]:box('Wood',(x,y,.014),(.287,2.985,.026),omit=(0,1,2,3,4))
    # Quiet elliptical woven island, embedded nearly flush into the deck.
    verts=[(3.05*math.cos(t),.65+3.05*math.sin(t),.009) for t in np.linspace(0,math.tau,96,endpoint=False)]
    g.add(m['Fabric'],verts,[tuple(range(len(verts)))])
    # Rear bulkhead with curved ends; front and sides remain entirely open windows.
    p=perimeter(0)
    for i,a in enumerate(p):
        b=p[(i+1)%len(p)]
        if a[1]<-3.05 and b[1]<-3.05:
            g.add(m['Ivory'],[(a[0],a[1],.25),(b[0],b[1],.25),(b[0],b[1],6),(a[0],a[1],6)],[(3,2,1,0)])
    # Walnut rear wall with recessed seams and a central pressure door.
    for x in np.linspace(-5.9,5.9,43):
        if abs(x)>1.35:box('Wood',(float(x),-4.91,2.4),(.245,.08,4.55),.016)
    box('Graphite',(0,-4.86,2.1),(2.42,.12,4.16),.16)
    for x in [-.59,.59]:box('Ivory',(x,-4.77,2.08),(1.14,.09,4.03),.12)
    for x in [-1.31,1.31]:tube('Glow',[(x,-4.7,.30),(x,-4.7,3.8),(x*.90,-4.7,4.32)],.025)
    box('Metal',(0,-4.68,2.12),(.035,.035,3.7),.01)
    # Slender angled station ribs; all are outside the projection sight volume.
    for side in [-1,1]:
        for y in [-1.8,4.0]:
            tube('Ivory',[(side*7.81,y,.23),(side*7.85,y,1.4),(side*7.8,y,4.9),(side*7.46,y,6.03)],.105,12)
            tube('Metal',[(side*7.68,y,.32),(side*7.72,y,5.75)],.022)
        tube('Ivory',[(side*6.9,10.34,.23),(side*7.03,10.23,3.0),(side*6.9,10.2,5.99)],.12,12)
    # A fine suspended projector frame, with full 9.5 x 4 m aperture plus reserve.
    for x in [-4.84,4.84]:
        box('Metal',(x,8.34,3.35),(.075,.10,4.16),.025)
        tube('Metal',[(x,8.34,5.43),(x,8.34,6.22)],.018,8)
    for z in [1.27,5.43]:box('Metal',(0,8.34,z),(9.76,.10,.075),.025)
    # Eight compact luxury recliners; head envelope is intentionally in front of the back.
    for i,(x,y,z) in enumerate(SEATS):
        lathe('Metal',(x,y+.34,0),[(0,.025),(.25,.025),(.25,.055),(.12,.10),(.065,.29)],24)
        g.softbox(m['Ivory'],(x,y+.08,.35),(.69,.93,.20),.4)
        g.softbox(m['Leather'],(x,y+.10,.49),(.575,.78,.18),.36)
        g.softbox(m['Ivory'],(x,y-.405,.79),(.70,.22,.77),.40,lean=.11)
        g.softbox(m['Leather'],(x,y-.30,.82),(.585,.16,.63),.36,lean=.10)
        # Head top at 1.13; nearest padded face y=-.22, before bevel curvature.
        for dx in [-.324,.324]:
            box('Wood',(x+dx,y+.09,.64),(.085,.77,.055),.023)
            g.softbox(m['Leather'],(x+dx,y+.09,.685),(.10,.76,.085),.48,seg=24,rings=12)
            tube('Metal',[(x+dx,y-.23,.38),(x+dx,y-.23,.61),(x+dx,y+.38,.61),(x+dx,y+.38,.39)],.018,8)
        # Subtle stitched seams lie well behind the seated-head clearance envelope.
        for dx in [-.235,.235]:tube('Fabric',[(x+dx,y-.227,.68),(x+dx,y-.264,1.05)],.0025,5)
        # Leather lumbar roll below shoulders.
        box('Leather',(x,y-.22,.65),(.49,.12,.14),.059,('bottom',))
    def table(x,y,r=.4,h=.48):
        lathe('Metal',(x,y,0),[(0,.02),(.23,.02),(.23,.05),(.045,.08),(.045,h-.06)],24)
        lathe('Wood',(x,y,0),[(0,h-.06),(r*.95,h-.06),(r,h-.035),(r,h),(0,h)],40)
        # A ceramic cup, with actual open rim and dark tea inside.
        lathe('Ivory',(x+.10,y,h),[(.045,0),(.055,.085),(.049,.092),(.040,.018)],16)
        lathe('Graphite',(x+.10,y,h+.065),[(0,0),(.042,0)],16)
        tube('Ivory',[(x+.15,y,h+.07),(x+.19,y,h+.07),(x+.19,y,h+.025),(x+.15,y,h+.025)],.009,6)
    for x,y in [(-2.15,.15),(2.15,.15),(-4.95,-.4),(4.95,-.4)]:table(x,y)
    table(0,1.7,.74,.37)
    # Functional side consoles: recessed display tile + raised switches, no transparent glass.
    for side in [-1,1]:
        x=side*6.65;y=1.6
        box('Ivory',(x,y,.45),(1.0,2.35,.9),.16,('bottom',))
        box('Metal',(x,y,.96),(.98,2.15,.13),.055)
        # Flat instrument faces carry custom texture; kept below eye sightlines.
        for yy in [y-.55,y+.55]:
            g.add(m['Display'],[(x-.38,yy-.39,1.03),(x+.38,yy-.39,1.03),(x+.38,yy+.39,1.03),(x-.38,yy+.39,1.03)],[(0,1,2,3)],uvs=[[(0,0),(1,0),(1,1),(0,1)]])
        for yy in np.linspace(y-.8,y+.8,7):lathe('Metal',(x+side*.44,float(yy),1.03),[(.018,0),(.018,.025),(0,.025)],8)
    # Rear hospitality console and deliberately restrained greenery.
    for side in [-1,1]:
        x=side*3.55
        box('Ivory',(x,-3.94,.51),(2.5,.64,1.02),.09,('bottom',))
        box('Wood',(x,-3.91,1.055),(2.60,.73,.09),.035)
        for j in range(9):box('Wood',(x-.98+j*.245,-3.59,.5),(.035,.025,.8),.008)
        lathe('Metal',(x,-3.94,1.1),[(.08,0),(.08,.25),(.045,.32),(.04,.42),(0,.42)],20)
        table(x,-2.5,.29,.53)
    rng=np.random.default_rng(94)
    for x in [-6.1,6.1]:
        y=-3.35
        lathe('Ivory',(x,y,0),[(.25,0),(.32,.06),(.38,.55),(.36,.60),(.30,.60),(.29,.48)],32)
        lathe('Graphite',(x,y,.50),[(0,0),(.30,0)],24)
        for k in range(12):
            a=k*2.4;h=float(rng.uniform(1.25,2.35));dx=.45*math.cos(a);dy=.45*math.sin(a)
            tube('Foliage',[(x,y,.5),(x+dx*.4,y+dy*.4,h*.8),(x+dx,y+dy,h)],.011,6)
            for j in range(3):
                c=Vector((x+dx*(.5+j*.2),y+dy*(.5+j*.2),h-.35+j*.13))
                v=Vector((math.cos(a+j)*.28,math.sin(a+j)*.28,.10));w=Vector((-v.y,v.x,0))*.32
                g.add(m['Foliage'],[tuple(c),tuple(c+v*.5+w),tuple(c+v),tuple(c+v*.5-w),tuple(c+v*.5+Vector((0,0,.035)))],[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(3,2,1,0)],True)
    # Original station wayfinding; converted to geometry, same graphite material.
    for body,z,size in [('O R B I T',4.78,.43),('OBSERVATION LOUNGE   /   04',4.46,.115)]:
        d=bpy.data.curves.new('Station lettering','FONT');d.body=body;d.align_x='CENTER';d.size=size
        d.extrude=0;d.bevel_depth=0;d.bevel_resolution=0;d.resolution_u=3
        o=bpy.data.objects.new('Station lettering',d);bpy.context.scene.collection.objects.link(o)
        o.location=(0,-4.86,z);o.rotation_euler=(math.pi/2,0,math.pi)
        bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
        bpy.ops.object.convert(target='MESH');o=bpy.context.object;bpy.context.view_layer.update()
        g.add(m['Graphite'],[tuple(o.matrix_world@v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons])
        bpy.data.objects.remove(o,do_unlink=True)
    # Warm cove lighting and cool, broad earthlight. Baking only.
    for x in [-6,6]:
        for y in [-2,2.5,7]:light('Cove',(x,y,5.72),110,(1,.69,.42),3.0,(x*.70,y,1))
    for x in [-3,0,3]:light('Rear cove',(x,-4.3,4.7),50,(1,.60,.32),2.5,(x,-3.6,1))
    light('Ceiling bounce',(0,1.5,5.8),240,(1,.83,.64),5,(0,1,0))
    light('Earthlight',(0,10,4.7),470,(.30,.57,1),7,(0,0,.7))
    for x in [-7,7]:light('Side earthlight',(x,4,3.5),115,(.26,.49,1),4,(0,1,1))
    return g.objects()


def sphere(name,material,radius,inward=False,seg=192,rings=96):
    # Longitude 0 lies on +X; UV u increases westwards to match NASA equirectangles.
    vs=[];fs=[];uv=[]
    for i in range(rings+1):
        lat=-math.pi/2+math.pi*i/rings
        for j in range(seg):
            lon=-math.tau*j/seg
            vs.append((radius*math.cos(lat)*math.cos(lon),radius*math.cos(lat)*math.sin(lon),radius*math.sin(lat)))
    for i in range(rings):
        for j in range(seg):
            a=i*seg+j;b=i*seg+(j+1)%seg;c=(i+1)*seg+(j+1)%seg;d=(i+1)*seg+j
            f=[a,b,c,d];coords=[(j/seg,i/rings),((j+1)/seg,i/rings),((j+1)/seg,(i+1)/rings),(j/seg,(i+1)/rings)]
            if i==0:f=f[1:];coords=coords[1:]
            if i==rings-1:f=f[:3];coords=coords[:3]
            # With negative longitude step, this ordering gives inward normals.
            if not inward:f.reverse();coords.reverse()
            fs.append(f);uv.append(coords)
    g=Geometry();g.add(material,vs,fs,True,uv);o=g.objects()[0];o.name=name;return o


def surroundings(m):
    parent=bpy.data.objects.new('Earth_rotate5520',None);bpy.context.scene.collection.objects.link(parent)
    parent.location=EARTH_CENTER;parent.rotation_euler=(math.radians(-10.465),math.radians(16.7312),math.radians(101.263))
    earth=sphere('Earth',m['Earth'],EARTH_RADIUS);earth.parent=parent
    stars=sphere('Space_Panorama',m['Stars'],2500,True,128,64);stars.location=(0,0,1.25)
    return earth,stars
