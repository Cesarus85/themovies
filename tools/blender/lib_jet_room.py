"""Original executive wide-body cabin. Metres, Z up, forward +Y."""
import math
from mathutils import Vector
from lib_jet_geometry import Geometry

SEATS=[(-1.125,0,0),(-.375,0,0),(.375,0,0),(1.125,0,0),
       (-2.35,1.7,0),(-2.35,2.85,0),(2.35,1.7,0),(2.35,2.85,0),
       (-1.72,-2.7,0),(-1.72,-4.0,0),(1.72,-2.7,0),(1.72,-4.0,0)]
SCREEN=(0,5.48,.9)
WINDOWS=[-5.2+i*.8 for i in range(14)]

def stamp(g,part,loc,angle=0):
    c,s=math.cos(angle),math.sin(angle)
    for m,(vs,fs,smooth,uv) in part.parts.items():
        points=[(loc[0]+c*x-s*y,loc[1]+s*x+c*y,loc[2]+z) for x,y,z in vs]
        # Preserve each original polygon's smooth flag.
        for flag in (False,True):
            ids=[i for i,v in enumerate(smooth) if v==flag]
            if ids:g.add(m,points,[fs[i] for i in ids],flag,[uv[i] for i in ids])

def rounded(w,h,r,n=6):
    pts=[]
    for cx,cy,start in [(w/2-r,h/2-r,0),(-w/2+r,h/2-r,90),(-w/2+r,-h/2+r,180),(w/2-r,-h/2+r,270)]:
        for j in range(n):
            a=math.radians(start+j*90/(n-1));pts.append((cx+r*math.cos(a),cy+r*math.sin(a)))
    return pts

def chair(m):
    g=Geometry();le=m['Leather'];wood=m['Wood'];gold=m['Brass']
    # Eye projection is at (0,0); back is well behind the swept head sphere.
    g.softbox(le,(0,.035,.48),(.58,.64,.16),seg=24,rings=10)
    g.softbox(le,(0,-.315,.77),(.58,.17,.52),lean=.14,seg=24,rings=12)
    g.softbox(wood,(0,-.385,.74),(.60,.065,.53),seg=20,rings=10)
    # Articulated lumbar pad and restrained vertical channel stitching.
    g.softbox(le,(0,-.205,.65),(.45,.09,.15),seg=20,rings=8)
    for x in [-.16,0,.16]:
        g.tube(m['Piping'],[(x,-.217,.76),(x,-.233,.9),(x,-.25,.96)],.0025,4)
    for sign in [-1,1]:
        g.softbox(wood,(sign*.318,.015,.645),(.075,.54,.10),seg=16,rings=8)
        g.softbox(le,(sign*.318,.02,.702),(.082,.48,.065),seg=16,rings=8)
        for y in [-.19,.19]:g.tube(gold,[(sign*.318,y,.35),(sign*.318,y,.63)],.017,8)
    g.lathe(gold,(0,0,0),[(.12,.09),(.095,.12),(.045,.17),(.045,.37),(.115,.39)],16)
    for j in range(5):
        a=j*math.tau/5
        g.tube(gold,[(0,0,.12),(.17*math.cos(a),.17*math.sin(a),.07),(.28*math.cos(a),.28*math.sin(a),.055)],.022,6)
        g.sphere(m['Dark'],(.27*math.cos(a),.27*math.sin(a),.035),(.035,.035,.025),8,4)
    return g

def build(m,light):
    g=Geometry();iv=m['Ivory'];wood=m['Wood'];gold=m['Brass'];dark=m['Dark'];glow=m['Glow']
    # Entire floor is the marker floor: z=0, 6m clear width, 12m length.
    g.box(m['Carpet'],(0,0,-.06),(6,12,.12),omit=(0,))
    # Woven aisle borders, geometric brass inlays are flush and unobtrusive.
    for x in [-2.8,2.8]:g.box(m['Piping'],(x,0,.002),(.025,11.9,.004),omit=(0,))
    for side in [-1,1]:
        # Wainscot, continuous ledge and sculpted ceiling shoulders.
        g.box(wood,(side*3.025,0,.47),(.15,12,.94),omit=(0,))
        g.box(wood,(side*2.94,0,.94),(.17,12,.075))
        g.box(gold,(side*2.846,0,.925),(.018,11.96,.025))
        for y in [-5.6+i*.8 for i in range(15)]:
            g.box(gold,(side*2.947,y,.46),(.012,.012,.79))
        # One closed panel around every REAL opening. Outer panel -> rounded
        # cabin bezel -> deep reveal -> 28x40 cm exterior aperture. No glass.
        for y in WINDOWS:
            inner=rounded(.42,.56,.12)
            # Fan each curved corner to its exact rectangular panel corner.
            # Radial projection would chamfer the outer corners, leaking tiny
            # triangles of scenery between neighboring panels.
            outer=[corner for corner in [(.4,.39),(-.4,.39),(-.4,-.39),(.4,-.39)] for _ in range(6)]
            loops=[[(side*2.96,y+u,1.25+v) for u,v in outer],
                   [(side*2.945,y+u,1.25+v) for u,v in inner],
                   [(side*3.15,y+u,1.25+v) for u,v in rounded(.28,.40,.075)]]
            for k in range(2):
                vs=loops[k]+loops[k+1];n=len(inner)
                fs=[(j,(j+1)%n,n+(j+1)%n,n+j) for j in range(n)]
                if side>0:fs=[f[::-1] for f in fs]
                g.add(iv,vs,fs,k==1)
            g.tube(gold,[(side*2.933,y+u,1.25+v) for u,v in rounded(.445,.585,.13)],.008,6,True)
            # Stowed shade lip, lower curved sill; neither spans the aperture.
            g.box(iv,(side*2.93,y,1.558),(.07,.32,.035))
        # Patch the spaces above/below window-panel zone and end margins.
        g.box(iv,(side*3.01,0,.95),(.10,12,.03))
        g.box(iv,(side*3.01,0,1.78),(.10,12,.28))
        for y in [-5.8,5.8]:g.box(iv,(side*3.01,y,1.25),(.10,.4,.78))
        profile=[(3.0,1.92),(2.96,2.08),(2.82,2.24),(2.6,2.36),(2.28,2.43),(1.94,2.45)]
        for a,b in zip(profile,profile[1:]):
            vs=[(side*a[0],-6,a[1]),(side*a[0],6,a[1]),(side*b[0],6,b[1]),(side*b[0],-6,b[1])]
            g.add(iv,vs,[(3,2,1,0) if side>0 else (0,1,2,3)],True)
        # Bin/cove face, individual join lines, flush pulls.
        g.box(wood,(side*2.40,0,2.30),(.19,11.96,.18))
        g.box(gold,(side*2.29,0,2.22),(.018,11.94,.018))
        g.box(glow,(side*2.22,0,2.41),(.055,11.8,.025))
        for y in [-5.2+i*1.3 for i in range(9)]:
            g.box(dark,(side*2.296,y,2.30),(.012,.012,.135))
            g.box(gold,(side*2.285,y+.30,2.28),(.02,.12,.015))
        for y in [-4.8,-2.4,0,2.4,4.8]:
            light('Warm cove',(side*2.15,y,2.34),40,(1,.78,.52),1.4,(side*.6,y,1))
        # Warm sunset enters actual window openings, without exporting lights.
        for y in WINDOWS:
            if side<0:light('Sunset window',(side*3.30,y,1.40),8,(1,.62,.34),.35,(0,y+1,.30))
    # Cabin crown 2.45m, gently rises ahead to 2.80m for the 1.8m screen height.
    for ya,yb,za,zb in [(-6,2.8,2.45,2.45),(2.8,4.6,2.45,2.80),(4.6,6,2.80,2.80)]:
        g.add(iv,[(-1.94,ya,za),(1.94,ya,za),(1.94,yb,zb),(-1.94,yb,zb)],[(3,2,1,0)])
        for side in [-1,1]:g.add(iv,[(side*1.94,ya,za),(side*1.94,yb,zb),(side*2.28,yb,2.43),(side*2.28,ya,2.43)],[(3,2,1,0) if side<0 else (0,1,2,3)])
    for y in [-5,-3,-1,1]:
        g.box(wood,(0,y,2.43),(3.85,.025,.025))
        for x in [-1.55,1.55]:
            g.lathe(gold,(x,y,2.40),[(.065,0),(.065,.025)],12)
            g.lathe(glow,(x,y,2.393),[(0,0),(.043,0),(.043,.008)],12)
    # Bulkheads, front display recess; the dark back wall is not a screen.
    g.box(wood,(0,5.95,1.4),(6,.10,2.8),omit=(0,))
    g.box(dark,(0,5.86,1.79),(3.52,.035,1.91))
    for x in [-1.77,1.77]:g.box(gold,(x,5.61,1.8),(.035,.22,1.88))
    for z in [.86,2.74]:g.box(gold,(0,5.61,z),(3.575,.22,.035))
    # Slender fluted walnut piers, brass-banded sconces outside screen clearance.
    for side in [-1,1]:
        for i in range(9):g.box(wood,(side*(1.91+i*.055),5.82,1.70),(.027,.09,1.77))
        x=side*2.45
        g.box(gold,(x,5.76,1.75),(.15,.035,.53))
        g.softbox(glow,(x,5.68,1.75),(.12,.105,.38),seg=16,rings=8)
        for z in [1.55,1.95]:g.box(gold,(x,5.67,z),(.17,.14,.035))
        light('Front sconce',(x,5.52,1.75),14,(1,.72,.43),.45,(x,4.8,1.2))
    g.box(wood,(0,5.62,.38),(3.5,.56,.70),.04,omit=('bottom',))
    g.box(m['Burl'],(0,5.57,.76),(3.6,.66,.06))
    for x in [-1.22,-.42,.42,1.22]:
        g.box(m['Burl'],(x,5.324,.38),(.77,.025,.55))
        g.box(gold,(x,5.30,.55),(.18,.02,.018))
    # Four individual swivel chairs (low executive backs keep every sightline open).
    c=chair(m)
    for p in SEATS[:4]:stamp(g,c,p)
    # Two two-place wall sofas, inward-facing, generously upholstered.
    for side in [-1,1]:
        g.box(wood,(side*2.48,2.275,.24),(.79,2.60,.30),.07,omit=('bottom',))
        for y in [1.7,2.85]:
            part=Geometry()
            part.softbox(m['Navy'],(0,.01,.49),(1.10,.76,.19),seg=24,rings=10)
            part.softbox(m['Navy'],(0,-.35,.77),(1.10,.20,.51),seg=24,rings=10)
            part.tube(m['Piping'],[(-.48,-.235,.94),(0,-.235,.94),(.48,-.235,.94)],.004,5)
            stamp(g,part,(side*2.35,y,0),side*math.pi/2)
        for y in [.97,3.58]:g.softbox(m['Navy'],(side*2.43,y,.65),(.78,.17,.35),seg=20,rings=10)
        for y in [1.05,3.5]:
            g.box(gold,(side*2.48,y,.12),(.63,.035,.20))
        # End tables with miniature reading lamps, fixed opaque shades.
        g.box(wood,(side*2.40,4.15,.39),(.72,.67,.76),.055,omit=('bottom',))
        g.box(m['Burl'],(side*2.40,4.15,.79),(.76,.71,.045))
        g.lathe(gold,(side*2.40,4.15,.81),[(.09,0),(.09,.02),(.018,.04),(.018,.29)],16)
        g.lathe(glow,(side*2.40,4.15,1.08),[(.13,0),(.10,.17),(.04,.19)],20)
        light('Reading lamp',(side*2.40,4.15,1.15),9,(1,.73,.43),.22,(side*2.4,4,.5))
    # Aft conference suite: four swivel chairs and a rounded inlaid table.
    for i,p in enumerate(SEATS[8:]):stamp(g,c,p,-math.pi/2 if p[0]<0 else math.pi/2)
    g.box(wood,(0,-3.35,.40),(1.16,1.75,.69),.12,omit=('bottom',))
    g.box(gold,(0,-3.35,.735),(2.18,2.56,.045))
    g.box(wood,(0,-3.35,.78),(2.23,2.61,.07))
    g.softbox(m['Burl'],(0,-3.35,.825),(2.14,2.52,.028),seg=32,rings=8)
    for y in [-2.74,-3.97]:
        for x in [-.68,.68]:
            g.box(m['Navy'],(x,y,.851),(.45,.42,.015))
            g.box(m['Ivory'],(x,y,.862),(.27,.32,.009))
            g.tube(gold,[(x+.17,y-.12,.87),(x+.17,y+.12,.87)],.006,6)
            g.lathe(m['Porcelain'],(x*.5,y,.85),[(0,0),(.046,0),(.05,.065),(.044,.075),(.038,.067),(.034,.018),(0,.018)],14)
    # Rear bulkhead, central door, refreshment cabinet and original compass motif.
    g.box(wood,(0,-5.97,1.25),(6,.08,2.50),omit=(0,))
    g.box(m['Burl'],(0,-5.91,1.13),(.92,.025,2.20))
    for x in [-.49,.49]:g.box(gold,(x,-5.87,1.15),(.022,.04,2.27))
    g.box(gold,(.35,-5.84,1.02),(.025,.025,.15))
    for side in [-1,1]:
        g.box(wood,(side*1.87,-5.62,.43),(1.85,.60,.85),.04,omit=('bottom',))
        g.box(m['Burl'],(side*1.87,-5.60,.89),(1.93,.67,.055))
        for x in [side*1.43,side*2.30]:
            g.box(m['Burl'],(x,-5.30,.43),(.80,.028,.68))
            g.box(gold,(x,-5.27,.68),(.16,.025,.02))
        g.box(iv,(side*1.87,-5.89,1.40),(1.72,.025,.73))
        for z in [1.02,1.79]:g.box(gold,(side*1.87,-5.85,z),(1.78,.025,.025))
    # Opaque ceramic coffee service, no glass/transparency.
    for x in [1.35,1.62,1.89]:
        g.lathe(m['Porcelain'],(x,-5.52,.93),[(0,0),(.065,0),(.06,.08),(.05,.09),(.042,.075),(.037,.02),(0,.02)],18)
    g.lathe(gold,(2.35,-5.60,.92),[(0,0),(.10,0),(.11,.05),(.10,.24),(.07,.28),(.07,.30),(0,.30)],20)
    for k in range(8):
        a=k*math.tau/8
        g.add(gold,[(0,-5.855,1.85),(.12*math.cos(a-.12),-5.855,1.85+.12*math.sin(a-.12)),(.23*math.cos(a),-5.855,1.85+.23*math.sin(a)),(.12*math.cos(a+.12),-5.855,1.85+.12*math.sin(a+.12))],[(0,1,2,3)])
    light('Rear hospitality',(0,-5.1,2.26),45,(1,.78,.57),2,(0,-4,.5))
    return g.objects()

def surroundings(m):
    g=Geometry()
    # Both sides have du/dY > 0: runtime +U texture offset moves features -Y,
    # toward the REAR, independently of which window the viewer uses.
    for side in [-1,1]:
        vs=[(side*900,-2300,-101.25),(side*900,2300,-101.25),(side*900,2300,1575),(side*900,-2300,1575)]
        f=(0,1,2,3) if side<0 else (3,2,1,0)
        uv=[(0,0),(3,0),(3,1),(0,1)]
        g.add(m['Sky'],vs,[f],uvs=[[uv[i] for i in f]])
    # Continuous opaque cloud deck, 45m below the floor. UVs repeat in flight
    # direction (U); the depth is actual geometry, no transparent foreground.
    vs=[(-1000,-2300,-45),(1000,-2300,-45),(1000,2300,-45),(-1000,2300,-45)]
    g.add(m['Cloud'],vs,[(0,1,2,3)],uvs=[[(0,0),(0,8),(9,8),(9,0)]])
    return g.objects()
