"""Mediterranean rooftop; metres, Z up, seats face +Y. All plants are opaque geometry."""
import math
import numpy as np
from mathutils import Vector
from lib_rooftop_geometry import Geometry

SEATS=[(-1.125,0,0),(-.375,0,0),(.375,0,0),(1.125,0,0),
       (-3.15,.10,0),(-4.35,.10,0),(-.70,-2.8,0),(.70,-2.8,0),(3.15,-2.65,0),(4.25,-2.65,0)]
SCREEN=(0,8.5,1.1)
PANORAMA_CENTER=(0,0,1.25)
PANORAMA_ROTATION=.31*math.tau
RADIUS=150.


def build(m,light):
    g=Geometry();rng=np.random.default_rng(574)
    def box(k,c,s,b=0,omit=()):g.box(m[k],c,s,b,omit)
    def tube(k,p,r=.02,n=8):g.tube(m[k],p,r,n)
    def sphere(k,c,s,seg=10,rings=6):g.sphere(m[k],c,s,seg,rings)
    def lathe(k,c,p,n=14):g.lathe(m[k],c,p,n)
    # Continuous structural floor beneath the narrow joints of individual teak planks.
    box('Teak',(0,2.2,-.13),(13.6,14,.24),omit=(0,))
    for ix in range(62):
        x=-6.69+ix*.218
        for iy in range(5):
            y=-4.8+iy*2.8
            box('Teak',(x,y+1.398,-.017),(.208,2.79,.034),omit=(0,1,2,3,4))
    # Stucco parapet at 72 cm, limestone coping at 79 cm: open views at 1.25 m.
    for x in [-6.8,6.8]:
        box('Plaster',(x,2.2,.35),(.25,14,.70),.025,('bottom',))
        box('Stone',(x,2.2,.745),(.34,14.08,.09),.018,('bottom',))
    for y in [-4.8,9.2]:
        box('Plaster',(0,y,.35),(13.6,.25,.70),.025,('bottom',))
        box('Stone',(0,y,.745),(13.8,.34,.09),.018,('bottom',))
    # Dark plaster wall behind the free app-projection area, no screen plane.
    box('City',(0,8.85,2.55),(10.25,.32,5.6),.04,('bottom',))
    for x in [-4.97,4.97]:box('Teak',(x,8.58,3.07),(.18,.20,4.36),.02)
    box('Teak',(0,8.58,5.24),(10.12,.20,.18),.02)
    box('Teak',(0,8.58,.94),(10.12,.20,.14),.02)
    # Four standard places: two properly proportioned low lounge loveseats.
    for cx in [-.75,.75]:
        box('Teak',(cx,.1,.29),(1.49,.95,.16),.035,('bottom',))
        for x in [cx-.63,cx+.63]:
            for y in [-.3,.46]:box('Teak',(x,y,.15),(.09,.09,.3),.014,('bottom',))
        box('Teak',(cx,-.29,.73),(1.48,.11,.62),.025)
        for x in [cx-.70,cx+.70]:box('Teak',(x,.15,.64),(.10,.86,.085),.022)
        for x in [cx-.375,cx+.375]:
            box('Linen',(x,.20,.435),(.72,.78,.15),.065,('bottom',))
            # Finished front exactly y=-.12 at shoulder height, behind each marker.
            box('Linen',(x,-.225,.79),(.715,.21,.53),.055,('bottom',))
        # Small decorative pillow is outside the measured shoulder centre.
        sphere('Textile',(cx+.59,.03,.71),(.17,.13,.20),12,8)
    # Original striped throws with visible soft folds, draped over outside arms.
    for cx in [-1.43,1.43]:
        vs=[];fs=[];uv=[];nx=8;ny=18
        for j in range(ny+1):
            t=j/ny;yy=-.23+t*.98
            for i in range(nx+1):
                u=i/nx;xx=cx+(u-.5)*.43
                zz=.535+.025*math.sin(u*math.tau*3+t*4)
                if t>.68:zz-=.34*((t-.68)/.32)
                vs.append((xx,yy,zz))
        for j in range(ny):
            for i in range(nx):
                a=j*(nx+1)+i;fs.append((a,a+1,a+nx+2,a+nx+1));uv.append([(i/nx,j/ny),((i+1)/nx,j/ny),((i+1)/nx,(j+1)/ny),(i/nx,(j+1)/ny)])
        g.add(m['Textile'],vs,fs,True,uv)
    # Two timber lounge chairs, extra bench and low backed counter stools.
    for idx,(x,y,z) in enumerate(SEATS[4:],4):
        stool=idx>=8
        for dx in [-.28,.28]:
            for dy in [-.28,.36]:
                tube('Teak',[(x+dx*1.15,y+dy*1.1,.02),(x+dx,y+dy,.44)],.035)
        box('Teak',(x,y+.12,.38),(.72,.82,.10),.025,('bottom',))
        box('Linen',(x,y+.13,.465),(.65,.75,.09),.035,('bottom',))
        box('Teak',(x,y-.26,.73),(.72,.09,.58),.02)
        box('Linen',(x,y-.205,.80),(.64,.17,.40),.035)
        if not stool:
            for dx in [-.37,.37]:
                tube('Teak',[(x+dx,y-.27,.45),(x+dx,y-.27,.69),(x+dx,y+.51,.69),(x+dx,y+.51,.43)],.028)
        else:tube('Metal',[(x-.28,y+.36,.23),(x+.28,y+.36,.23)],.018)
    # Join the two rear places into a shared bench frame.
    box('Teak',(0,-3.10,.63),(2.24,.09,.55),.025)
    # Woven outdoor rug: top-only geometric plane uses the whole original texture.
    g.add(m['Rug'],[(-2.45,-.75,.009),(2.45,-.75,.009),(2.45,2.25,.009),(-2.45,2.25,.009)],[(0,1,2,3)],uvs=[[(0,0),(1,0),(1,1),(0,1)]])
    # Low coffee table, side tables; preserve the complete lower screen sightline.
    def table(x,y,r=.36,h=.48):
        lathe('Teak',(x,y,0),[(r,h-.05),(r,h),(0,h)],24)
        for a in [0,2.1,4.2]:tube('Metal',[(x+math.cos(a)*r*.7,y+math.sin(a)*r*.7,.02),(x+math.cos(a)*r*.5,y+math.sin(a)*r*.5,h-.05)],.022)
    table(0,1.46,.62,.36)
    for x,y in [(-2.15,.1),(2.1,.10),(-3.75,1.28)]:table(x,y)
    # Small bar on right, offset from the central axis, with ribbed teak face.
    box('Plaster',(3.8,-1.35,.38),(2.35,.55,.76),.04)
    box('Stone',(3.8,-1.35,.795),(2.52,.73,.08),.03)
    for x in np.arange(2.72,4.95,.12):box('Teak',(float(x),-1.645,.39),(.048,.038,.67),.008)
    # Rear console/cart, bottles, opaque ceramic cups and towels.
    for h in [.22,.70]:box('Teak',(5.75,-3.55,h),(1.25,.58,.055),.02)
    for x in [5.2,6.3]:
        for y in [-3.79,-3.31]:
            tube('Metal',[(x,y,.10),(x,y,.91)],.022)
            sphere('Metal',(x,y,.10),(.07,.035,.07),10,6)
    tube('Metal',[(5.2,-3.79,.91),(5.2,-3.31,.91)],.025)
    for x,y,z in [(5.42,-3.55,.73),(5.68,-3.55,.73),(3.0,-1.35,.84),(3.28,-1.35,.84)]:
        lathe('Foliage',(x,y,z),[(.055,0),(.058,.19),(.028,.23),(.024,.32),(0,.32)],12)
        lathe('Clay',(x+.14,y+.13,z),[(.045,0),(.052,.10),(.043,.11),(.035,.015)],12)
    # Lanterns: opaque frames and a warm solid candle, no pretend glass surfaces.
    def lantern(x,y,z=0,scale=1):
        s=scale
        for zz in [.025,.38]:box('Metal',(x,y,z+zz*s),(.25*s,.25*s,.035*s),.009*s)
        for dx in [-.105,.105]:
            for dy in [-.105,.105]:tube('Metal',[(x+dx*s,y+dy*s,z+.04*s),(x+dx*s,y+dy*s,z+.38*s)],.009*s,6)
        lathe('bulb',(x,y,z+.04*s),[(.055*s,0),(.055*s,.19*s),(0,.19*s)],12)
        tube('Metal',[(x-.06*s,y,z+.4*s),(x-.06*s,y,z+.49*s),(x+.06*s,y,z+.49*s),(x+.06*s,y,z+.4*s)],.012*s)
        light('Lantern',(x,y,z+.22*s),13*s,(1,.57,.24),.13)
    for args in [(-2.15,.1,.50,.65),(2.1,.1,.50,.65),(0,1.46,.38,.65),(-5.5,2.2,0,1.4),(5.6,2.2,0,1.4),(-2.4,-3.75,0,1.3),(2.35,-3.6,0,1.2),(3.8,-1.35,.84,.65)]:lantern(*args)
    # Planters, modeled olive branches and individual lanceolate solid leaves.
    def pot(x,y,r=.43,h=.60):
        lathe('Clay',(x,y,0),[(r*.64,0),(r*.68,.05),(r,h-.06),(r*1.04,h-.06),(r*1.04,h),(r*.9,h),(r*.88,h-.13)],20)
        lathe('City',(x,y,h-.12),[(0,0),(r*.88,0)],18)
    def leaf(center,axis,length,width,k='Foliage',rounded=False):
        c=Vector(center);a=Vector(axis).normalized()*length
        b=a.cross(Vector((0,0,1)))
        if b.length<.0001:b=Vector((1,0,0))
        b=b.normalized()*width;ridge=Vector((0,0,width*.22))
        if rounded:
            # Low-poly solid lanceolate olive leaf; curved normals avoid the
            # near-zero-width UV slivers of a flattened tetrahedron.
            aa=a.normalized();bb=b.normalized();cc=aa.cross(bb).normalized()
            verts=[];faces=[];seg=6;rings=4
            for j in range(rings+1):
                phi=math.pi*j/rings
                for i in range(seg):
                    theta=math.tau*i/seg
                    verts.append(tuple(c+aa*(length*math.cos(phi))+bb*(width*math.sin(phi)*math.cos(theta))+cc*(.014*math.sin(phi)*math.sin(theta))))
            for j in range(rings):
                for i in range(seg):
                    a0=j*seg+i;b0=j*seg+(i+1)%seg;c0=(j+1)*seg+(i+1)%seg;d0=(j+1)*seg+i
                    faces.append((a0,c0,d0) if j==0 else (a0,b0,c0) if j==rings-1 else (a0,b0,c0,d0))
            g.add(m[k],verts,[f[::-1] for f in faces],True)
            return
        vs=[tuple(c-a),tuple(c+b+ridge),tuple(c+a),tuple(c-b+ridge)]
        g.add(m[k],vs,[(0,1,2),(0,2,3),(1,0,3),(1,3,2)],True)
    for x,y,h in [(-5.65,-3.55,2.20),(-6.15,5.6,2.6),(6.15,5.8,2.5),(5.7,-.15,2.0)]:
        pot(x,y)
        # Small shielded uplight inside the pot illuminates the leaf undersides.
        lathe('Metal',(x+.22,y,.48),[(.055,0),(.055,.09),(0,.09)],10)
        light('Olive uplight',(x+.22,y,.60),22,(1,.76,.48),.28,target=(x,y,h))
        tube('Teak',[(x,y,.46),(x+.08,y,1.25),(x-.03,y+.05,h)],.06)
        for a in np.linspace(0,math.tau,7,endpoint=False):
            end=Vector((x+math.cos(a)*.63,y+math.sin(a)*.63,h+rng.uniform(-.15,.35)))
            start=Vector((x,y,h-.75));tube('Teak',[start,(start+end)*.5+Vector((0,0,.15)),end],.021,6)
            for twig in range(6):
                tip=end+Vector(rng.normal(0,[.26,.24,.23]))
                tube('Teak',[end,tip],.005,5)
                for j in range(3):
                    t=(j+1)/3;c=end.lerp(tip,t)
                    for sign in [-1,1]:
                        direction=Vector((math.cos(a+sign*1.1+j*.13),math.sin(a+sign*1.1+j*.13),.18))
                        leaf(c+direction*.10,direction,.145,.047,rounded=True)
    # Lavender clusters in rectangular terra-cotta troughs, never alpha cards.
    for x,y in [(-6.28,-.9),(-6.28,2.6),(6.28,3.6),(2.8,-4.25),(-3.4,-4.25)]:
        box('Clay',(x,y,.21),(.64,1.12,.42),.055,('bottom',))
        for j in range(26):
            xx=x+rng.uniform(-.23,.23);yy=y+rng.uniform(-.44,.44);h=rng.uniform(.65,.93)
            tube('Foliage',[(xx,yy,.40),(xx+.05,yy+.03,h)],.008,5)

            sphere('Lavender',(xx+.05,yy+.03,h),(.026,.026,.07),6,6)
            leaf((xx,yy,.51),(1,.4,.5),.12,.017)
    # Two small fan palms at the rear corners, slender cut solid leaflets.
    for x,y in [(-4.65,-3.85),(4.55,-4.05)]:
        pot(x,y,.34,.48);tube('Teak',[(x,y,.3),(x+.08,y,1.9)],.065)
        for a in np.linspace(0,math.tau,9,endpoint=False):
            start=Vector((x+.08,y,1.9));end=start+Vector((math.cos(a)*.95,math.sin(a)*.95,.25))
            tube('Foliage',[start,(start+end)*.5+Vector((0,0,.23)),end],.012,6)
            for j in range(3,10):
                c=start.lerp(end,j/10)+Vector((0,0,.23*math.sin(j/10*math.pi)))
                for s in [-1,1]:leaf(c,(math.cos(a+.8*s),math.sin(a+.8*s),-.2),.23*(1-j/14),.025)
    # Blackened steel light poles and supported catenary-like festoons.
    for x in [-5.0,5.0]:
        for y,h in [(-3.8,3.4),(2.8,4.35),(7.4,5.9)]:
            lathe('Metal',(x,y,0),[(.12,0),(.12,.04),(.045,.04),(.045,h),(0,h)],12)
    def festoon(a,b,sag,bulbs):
        a=Vector(a);b=Vector(b)
        def at(t):return a.lerp(b,t)-Vector((0,0,sag*4*t*(1-t)))
        tube('Metal',[at(i/40) for i in range(41)],.009,6)
        for t in np.linspace(.035,.965,bulbs):
            p=at(float(t));tube('Metal',[p,p-Vector((0,0,.11))],.018,8)
            sphere('bulb',p-Vector((0,0,.15)),(.043,.043,.065),10,6)
            light('Festoon',p-Vector((0,0,.19)),5,(1,.64,.32),.13)
    for y,h,s in [(-3.8,3.4,.38),(2.8,4.35,.35),(7.4,5.9,.28)]:festoon((-5,y,h),(5,y,h),s,14)
    for x in [-5,5]:
        festoon((x,-3.8,3.4),(x,2.8,4.35),.3,9)
        festoon((x,2.8,4.35),(x,7.4,5.9),.22,7)
    # Nearby real rooftops are below the seated horizon, 10-40m away.
    # Uneven roof levels, cornices, chimneys, rooftop tanks, antennae and lit windows.
    for i,(x,y,w,d,top) in enumerate([(-14,5,8,10,.35),(-16,-10,10,9,.1),(14,-5,8,8,.3),(16,10,9,9,.45),(-4,-16,8,8,.20),(6,-21,9,8,-2.0),(-22,21,10,9,-1.8),(23,21,11,11,-2),(27,-18,10,9,-3),(-24,-21,11,10,-2),(0,27,10,9,-1.8),(9,32,10,8,-2)]):
        box('City',(x,y,top-4),(w,d,8),.035,('bottom',))
        box('Stone',(x,y,top+.04),(w+.18,d+.18,.16),.03,('bottom',))
        # Roof parapet silhouettes do not form one uniform wall.
        for xx in [x-w/2,x+w/2]:box('City',(xx,y,top+.30),(.14,d,.46))
        box('City',(x,y+d/2,top+.30),(w,.14,.46))
        for j in range(2):
            xx=x-w*.25+j*w*.45;yy=y+d*.20
            box('Clay',(xx,yy,top+.60),(.52,.60,1.0),.02)
            box('Stone',(xx,yy,top+1.14),(.69,.75,.12),.02)
        if i%2==0:
            lathe('Metal',(x+w*.22,y-d*.20,top+.12),[(.72,0),(.72,1.1),(.60,1.3),(0,1.3)],16)
            tube('Metal',[(x-w*.32,y,top+.1),(x-w*.32,y,top+2.35)],.018,6)
            for z in [1.7,2.1]:tube('Metal',[(x-w*.32-.55,y,top+z),(x-w*.32+.55,y,top+z)],.012,6)
        for xx in np.arange(x-w/2+.8,x+w/2-.3,1.6):
            for zz in [top-1.4,top-3.1,top-4.8]:
                if rng.random()<.48:box('Windows',(float(xx),y-d/2-.006,zz),(.42,.012,.65))
        for yy in np.arange(y-d/2+.8,y+d/2-.3,1.8):
            for zz in [top-1.4,top-3.1]:
                if rng.random()<.45:box('Windows',(x-w/2-.006,float(yy),zz),(.012,.45,.65))
    # The terrace belongs to a real building, above a dark city ground plane.
    box('City',(0,2.2,-5.1),(13.6,14,10),omit=(0,5))
    box('City',(0,0,-10.2),(115,115,.2),omit=(0,1,2,3,4))
    light('Cool sky',(0,1,14),2200,(.39,.56,1),18,target=(0,1,0))
    light('Warm terrace bounce',(0,-.5,5),230,(1,.78,.55),7,target=(0,0,0))
    return g.objects()


def surroundings(m):
    g=Geometry();vs=[];fs=[];uv=[];nx=128;ny=48
    # Full 360 x 178 degrees; the tiny omitted polar caps are closed separately.
    # Equirectangular UV0 uses true longitude/latitude, no vertical remapping.
    for j in range(ny+1):
        v=.005+.99*j/ny;phi=(v-.5)*math.pi
        for i in range(nx+1):
            u=i/nx;theta=-u*math.tau+PANORAMA_ROTATION
            vs.append((RADIUS*math.cos(phi)*math.cos(theta),RADIUS*math.cos(phi)*math.sin(theta),1.25+RADIUS*math.sin(phi)))
    for j in range(ny):
        for i in range(nx):
            a=j*(nx+1)+i;fs.append((a,a+nx+1,a+nx+2,a+1))
            uv.append([(i/nx,.005+.99*j/ny),(i/nx,.005+.99*(j+1)/ny),((i+1)/nx,.005+.99*(j+1)/ny),((i+1)/nx,.005+.99*j/ny)])
    for j,z,v in [(0,1.25-RADIUS,0),(ny,1.25+RADIUS,1)]:
        pole=len(vs);vs.append((0,0,z))
        for i in range(nx):
            face=(j*(nx+1)+i,j*(nx+1)+i+1,pole)
            coords=[(i/nx,.005+.99*j/ny),((i+1)/nx,.005+.99*j/ny),((i+.5)/nx,v)]
            if j==ny:face=face[::-1];coords=coords[::-1]
            fs.append(face);uv.append(coords)
    g.add(m['Panorama'],vs,[f[::-1] for f in fs],True,[f[::-1] for f in uv])
    obs=g.objects()
    for o in obs:o.visible_shadow=False
    return obs
