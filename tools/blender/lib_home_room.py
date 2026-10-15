"""Original, meter-scale living room. Four real sofa places; no other seating."""
import math
import numpy as np
from mathutils import Vector
from lib_home_geometry import Geometry


def build(materials, light):
    g=Geometry()
    wood,parquet,plaster,velvet,rug,brass,stone,dark,books,linen,embers,night=[materials[k] for k in
        ['Walnut','Parquet','Plaster','Velvet','Rug','Brass','Stone','Charcoal','Books','Linen','Embers','Night']]
    # World shell has inward normals and no inaccessible exterior faces.
    # The rug replaces the covered parquet surface; no invisible floor beneath it.
    for x0,x1,y0,y1 in [(-4.2,-2.05,-2.35,5.45),(2.05,4.2,-2.35,5.45),(-2.05,2.05,-2.35,-.58),(-2.05,2.05,3.36,5.45)]:
        vs=[(x0,y0,0),(x1,y0,0),(x1,y1,0),(x0,y1,0)]
        g.add(parquet,vs,[(0,1,2,3)],uvs=[[(x/3.4,y/3.4) for x,y,z in vs]])
    g.add(plaster,[(-4.2,-2.35,2.75),(-4.2,5.45,2.75),(4.2,5.45,2.75),(4.2,-2.35,2.75)],[(0,1,2,3)])
    # Local wall coordinates (u along wall, depth into room, z up).
    def transform(p, wall):
        u,d,z=p
        if wall=='back':return (u,-2.35+d,z)
        if wall=='front':return (-u,5.45-d,z)
        if wall=='left':return (-4.2+d,-u+1.55,z)
        return (4.2-d,u+1.55,z)
    def wallpart(wall, fn, *args, **kw):
        temp=Geometry();getattr(temp,fn)(*args,**kw)
        for mat,(vs,fs,ss,uv) in temp.parts.items():
            for face,s,coords in zip(fs,ss,uv):
                g.add(mat,[transform(vs[i],wall) for i in face],[tuple(range(len(face)))],s,[coords])
    def wb(wall,mat,c,size,bevel=0,omit=()):wallpart(wall,'box',mat,c,size,bevel,omit)
    def wt(wall,mat,pts,r,seg=8,closed=False):wallpart(wall,'tube',mat,pts,r,seg,closed)
    # Walls, raised bolection wainscot and stepped cornice.
    for wall,width in [('back',8.4),('front',8.4),('left',7.8),('right',7.8)]:
        if wall!='right':
            # Shell stops at the wainscot and at the opaque library/screen backs.
            ranges={'back':[(-4.2,-3.76),(-1.34,1.34),(3.76,4.2)],
                    'front':[(-4.2,-3.09),(3.09,4.2)],
                    'left':[(-3.9,-3.41),(-1.79,1.44),(3.26,3.9)]}[wall]
            top=2.62 if wall in ('back','left') else 2.75
            for lo,hi in ranges:
                wallpart(wall,'add',plaster,[(lo,0,.95),(hi,0,.95),(hi,0,top),(lo,0,top)],[(0,3,2,1)])
            if wall in ('back','left'):
                wallpart(wall,'add',plaster,[(-width/2,0,2.62),(width/2,0,2.62),(width/2,0,2.75),(-width/2,0,2.75)],[(0,3,2,1)])
            # Narrow closure above the front screen; library crowns close their own tops.
            if wall=='front':wallpart(wall,'add',plaster,[(-3.09,0,2.71),(3.09,0,2.71),(3.09,0,2.75),(-3.09,0,2.75)],[(0,3,2,1)])
        else:
            # Real window opening from local u=-.15 to 2.65, z=1.04..2.35.
            for c,s in [((0,0,.995),(7.8,.015,.09)),((0,0,2.55),(7.8,.015,.40)),
                        ((-2.025,0,1.695),(3.75,.015,1.31)),((3.275,0,1.695),(1.25,.015,1.31))]:
                wb(wall,plaster,c,s,omit=(0,1,2,4,5))
        for z,d,h in [(.08,.08,.16),(.90,.065,.065),(.96,.09,.055),(2.58,.07,.06),(2.64,.12,.06),(2.70,.19,.06)]:
            wb(wall,wood,(0,d/2,z),(width,d,h),.008,omit=('bottom',))
        count=round(width/.72)
        for i in range(count):
            u=-width/2+(i+.5)*width/count;w=width/count-.075
            wb(wall,wood,(u,.018,.51),(width/count,.025,.79),omit=(0,1,2,4,5))
            # Panel rails and beveled raised inner field.
            for dz in [-.30,.30]:wb(wall,wood,(u,.055,.51+dz),(w,.07,.035),.008)
            for dx in [-w/2,w/2]:wb(wall,wood,(u+dx,.055,.51),(.035,.07,.63),.008)
    # Front opening: full 6 x 1.85 m screen remains EMPTY at y=5.2.
    g.box(dark,(0,5.40,1.71),(6.18,.035,2.00),omit=(0,2,3,4,5))
    for x in [-3.07,3.07]:
        g.box(wood,(x,5.25,1.705),(.12,.18,2.01),.015)
        g.box(brass,(x-math.copysign(.045,x),5.151,1.705),(.012,.016,1.88))
    for z in [.725,2.685]:
        g.box(wood,(0,5.25,z),(6.25,.18,.10),.012)
        g.box(brass,(0,5.151,z+(.035 if z<1 else -.035)),(6.05,.015,.012))
    # Low front cabinet with speakers, brass handles; cannot block lower screen corners.
    g.box(wood,(0,5.04,.285),(4.0,.55,.47),.025,omit=('bottom',))
    for x in [-1.48,-.50,.50,1.48]:
        g.box(wood,(x,4.755,.29),(.92,.035,.35),.012)
        g.box(brass,(x,4.72,.38),(.16,.025,.016),.005)
    for x in [-3.45,3.45]:
        g.box(wood,(x,5.03,.53),(.31,.31,.94),.03,omit=('bottom',))
        g.box(dark,(x,4.865,.55),(.25,.025,.82),.018)
        for z,r in [(.32,.084),(.65,.084),(.86,.04)]:
            g.sphere(dark,(x,4.843,z),(r,.018,r),16,6)
            g.tube(brass,[(x+r*math.cos(a),4.832,z+r*math.sin(a)) for a in np.linspace(0,math.tau,32,endpoint=False)],.004,4,True)
    # Persian rug with one intentionally non-repeating UV tile, bound edges, fine fringe.
    g.add(rug,[(-2.05,-.58,0),(2.05,-.58,0),(2.05,3.36,0),(-2.05,3.36,0)],[(0,1,2,3)],uvs=[[(0,0),(1,0),(1,1),(0,1)]])
    for y in [-.59,3.37]:
        for x in np.linspace(-2.02,2.02,112):g.tube(stone,[(x,y,.014),(x+.008,y+(-.085 if y<0 else .085),.009)],.003,4)
    # Four-place Chesterfield. Shoulder surface has y=-.12 at all four centerlines.
    g.box(wood,(0,.05,.15),(3.50,1.20,.16),.045,omit=('bottom',))
    g.box(velvet,(0,.10,.29),(3.50,1.15,.22),.10,omit=('bottom',))
    for x in [-1.52,1.52]:
        for y in [-.43,.55]:g.lathe(wood,(x,y,0),[(.075,0),(.055,.07),(.07,.11),(.045,.17),(.065,.22)],12)
    # Rounded and piped four seat cushions.
    for x in [-1.125,-.375,.375,1.125]:
        cushion(g,velvet,(x,.30,.405),(.365,.40,.075),cutoff=.39)
        g.tube(velvet,[(x+.365*math.copysign(abs(math.cos(a))**.33,math.cos(a)),.30+.400*math.copysign(abs(math.sin(a))**.33,math.sin(a)),.420) for a in np.linspace(0,math.tau,80,endpoint=False)],.006,6,True)
    # One continuous padded back: front quilting, rolled crown and finished rear.
    # A single surface avoids intersecting rolls/casings and visible dark cracks.
    nx=96;nz=20;vs=[];fs=[]
    profile=[(-.057,.40)]+[(None,z) for z in sorted(set([.85]+[.56+j/nz*.54 for j in range(nz+1)]))]
    profile += [(-.17,1.14),(-.22,1.17),(-.34,1.17),(-.44,1.15),(-.50,1.10),(-.52,1.04),(-.52,.40)]
    for fixed_y,z in profile:
        for i in range(nx+1):
            x=-1.53+i/nx*3.06
            # Exact shoulder reference is planar at z=.85; preserve this row explicitly.
            fade=min(1,max(0,(1.1-z)/.08))
            puff=.040*math.sin((x+1.5)/.375*math.pi)**2*math.sin((z-.85)/.18*math.pi)**2*fade
            y=fixed_y if fixed_y is not None else -.12-(z-.85)*.14+puff
            vs.append((x,y,z))
    for j in range(len(profile)):
        for i in range(nx):
            a=j*(nx+1)+i;b=((j+1)%len(profile))*(nx+1)+i
            fs.append((a,b,b+1,a+1))
    g.add(velvet,vs,fs,True)
    # Close the two exposed ends of the upholstered back.
    g.add(velvet,[vs[j*(nx+1)] for j in range(len(profile))],[tuple(reversed(range(len(profile))))],True)
    g.add(velvet,[vs[j*(nx+1)+nx] for j in range(len(profile))],[tuple(range(len(profile)))],True)
    # Buttons and diamond stitching provide real low-frequency depth, not a normal map.
    for z in [.67,.85,1.03]:
        for x in np.arange(-1.3125,1.501,.375):
            g.sphere(velvet,(float(x),-.12-(z-.85)*.14-.002,z),(.018,.008,.018),8,4)
    # Top roll and large upholstered scroll arms.
    for side in [-1,1]:
        x=side*1.66
        cushion(g,velvet,(x,.075,.60),(.185,.57,.30))
        g.tube(velvet,[(x,-.43,.90),(x,.58,.90)],.19,32)
        g.sphere(velvet,(x,.58,.90),(.19,.035,.19),32,12)
        g.sphere(velvet,(x,-.43,.90),(.19,.035,.19),32,12)
        g.tube(velvet,[(x+.176*math.cos(a),.608,.90+.176*math.sin(a)) for a in np.linspace(0,math.tau,48,endpoint=False)],.006,6,True)
        for a in np.linspace(math.pi,math.tau,13):
            dx=.14*math.cos(a);z=.59+.24*math.sin(a);power=2/.33
            surface_y=.075+.57*max(0,1-abs(dx/.185)**power-abs((z-.60)/.30)**power)**(1/power)
            g.sphere(brass,(x+dx,surface_y+.002,z),(.007,.004,.007),6,4)
    # Low oval table, turned legs, nested volumes and an open book.
    g.lathe(wood,(0,1.65,.36),[(0,0),(.67,0),(.69,.025),(.69,.065),(.66,.083),(0,.083)],48)
    for a in [0,math.pi/2,math.pi,3*math.pi/2]:
        x=.47*math.cos(a);y=1.65+.47*math.sin(a)
        g.lathe(wood,(x,y,0),[(.045,0),(.03,.06),(.03,.22),(.05,.27),(.035,.37)],10)
    g.lathe(brass,(.24,1.61,.447),[(0,0),(.17,0),(.17,.012),(0,.012)],32)
    cup=Geometry();cup.lathe(books,(.24,1.61,.46),[(.055,0),(.055,.09),(.064,.10),(.048,.10),(.045,.02)],32)
    vs,fs,ss,uv=cup.parts[books];g.add(books,vs,fs,True,[[([.87,.25]) for i in f] for f in fs])
    g.tube(brass,[(.30+.032*math.sin(a),1.61,.51+.035*math.cos(a)) for a in np.linspace(0,math.pi,16)],.007,6)
    # Side tables and practical brass floor lamps, visible from seated views.
    for side in [-1,1]:
        x=side*2.30;y=.10
        g.lathe(wood,(x,y,.51),[(0,0),(.31,0),(.33,.025),(.32,.06),(0,.06)],32)
        g.lathe(wood,(x,y,0),[(.23,0),(.25,.055),(.07,.08),(.055,.42),(.12,.51)],16)
        floorlamp(g,brass,linen,(side*2.52,-.68,0),light)
    book(g,books,(-.19,1.63,.447),(.34,.24,.038),3,horizontal=True)
    # Fireplace on the left wall, wide limestone mantel and an actual recessed hearth.
    wall='left';u=-.15
    wb(wall,dark,(u,.018,.62),(1.55,.025,1.12))
    wb(wall,stone,(u,.31,.055),(2.12,.75,.11),.02,omit=('bottom',))
    for x in [-.88,.88]:
        wb(wall,stone,(u+x,.28,.64),(.28,.48,1.18),.025,omit=('bottom',))
        for dx in [-.075,0,.075]:wb(wall,stone,(u+x+dx,.529,.68),(.022,.02,.76),.006)
        wb(wall,stone,(u+x,.29,1.15),(.35,.52,.13),.012)
        wb(wall,stone,(u+x,.29,.19),(.36,.53,.16),.01)
    wb(wall,stone,(u,.30,1.30),(2.15,.64,.17),.025)
    wb(wall,stone,(u,.27,1.16),(1.60,.47,.20),.018)
    wb(wall,brass,(u,.597,1.30),(1.94,.012,.012))
    # Inner brick sides and fireback; charcoal logs with red-hot cracked coals.
    for z in [.22,.36,.50,.64,.78,.92]:
        for x in np.arange(-.65,.66,.26):wb(wall,dark,(u+float(x),.045,z),(.245,.05,.12),.008)
    for i in range(5):
        a=-.48+i*.24
        wallpart(wall,'lathe',embers,(u+a,.24,.13),[(.06,0),(.09,.035),(.06,.075),(0,.08)],10)
    for offset in [-.16,.12]:
        wt(wall,dark,[(u-.51,.29+offset,.22),(u+.48,.29-offset,.25)],.065,10)
        wt(wall,embers,[(u-.43,.345+offset,.225),(u+.36,.345-offset,.27)],.018,6)
    # Low fire grate and brass fender.
    wt(wall,brass,[(u-.70,.55,.12),(u-.70,.55,.32),(u+.70,.55,.32),(u+.70,.55,.12)],.018,8)
    for x in np.linspace(-.65,.65,10):wt(wall,dark,[(u+x,.51,.16),(u+x,.51,.36)],.008,6)
    light('Fire embers',transform((u,.38,.40),wall),38,(1,.24,.055),.35)
    # Framed original landscape above mantel; walnut/burnished brass, not a false mirror.
    # Traditional wall clock, with a real dial, twelve indexes and hands.
    wallpart(wall,'sphere',wood,(u,.08,2.00),(.39,.065,.39),48,16)
    wallpart(wall,'add',stone,[(u,.151,2.00)]+[(u+.34*math.cos(a),.151,2.00+.34*math.sin(a)) for a in np.linspace(0,math.tau,48,endpoint=False)],[(0,(i+1)%48+1,i+1) for i in range(48)])
    wt(wall,brass,[(u+.35*math.cos(a),.164,2.00+.35*math.sin(a)) for a in np.linspace(0,math.tau,64,endpoint=False)],.012,8,True)
    for a in np.linspace(0,math.tau,12,endpoint=False):
        wt(wall,dark,[(u+.275*math.sin(a),.17,2+.275*math.cos(a)),(u+.313*math.sin(a),.17,2+.313*math.cos(a))],.007,6)
    wt(wall,dark,[(u-.13,.18,2.11),(u,.18,2),(u+.21,.18,2.15)],.009,8)
    wallpart(wall,'sphere',brass,(u,.185,2),(.018,.009,.018),12,6)
    for a in [-.78,.78]:
        wallpart(wall,'lathe',brass,(u+a,.30,1.39),[(.075,0),(.06,.025),(.025,.07),(.024,.23),(.065,.26)],16)
        wallpart(wall,'lathe',linen,(u+a,.30,1.65),[(.025,0),(.025,.16),(0,.16)],12)
    # Full-height built-in library, sides and rear; shelf backs cover wall, no outer boxes.
    for wall,center,width in [('back',-2.55,2.45),('back',2.55,2.45),('left',2.35,1.85),('left',-2.60,1.65)]:
        bookshelf(g,wood,books,brass,wallpart,wall,center,width)
    # Rear central landscape and a paneled door (not an extra seat).
    picture(g,books,wood,brass,wallpart,'back',0,1.74,1.75,.94)
    # Accepted window frame, mullions and drapery; the layered exterior is built separately.
    wall='right';center=1.25
    for z in [1.02,2.37]:wb(wall,wood,(center,.07,z),(2.96,.15,.12),.015)
    for x in [-.19,2.69,.75,1.75]:wb(wall,wood,(x,.065,1.70),(.075,.14,1.35),.009)
    wb(wall,wood,(center,.07,1.67),(2.83,.10,.045),.006)
    wb(wall,stone,(center,.18,.99),(3.03,.39,.075),.01)
    for x in [-.35,2.85]:
        vs=[];fs=[]
        for j in range(13):
            z=.12+j/12*2.38
            for i in range(17):
                u=x+(i/16-.5)*.46;d=.23+.065*math.cos(i/16*math.tau*4)
                vs.append((u,d,z))
        for j in range(12):
            for i in range(16):
                a=j*17+i;fs.append((a,a+17,a+18,a+1))
        wallpart(wall,'add',velvet,vs,fs,True)
    wt(wall,brass,[(-.70,.22,2.53),(3.20,.22,2.53)],.022,10)
    light('Cool night',transform((center,.12,1.85),wall),45,(.33,.48,1),2.4,target=(0,1.6,.6))
    # Closed paneled entry door on the rear right: the room has a real entrance.
    wall='right';door=-2.82
    wb(wall,wood,(door,.055,1.065),(.96,.10,2.13),.012)
    for du in [-.53,.53]:wb(wall,wood,(door+du,.105,1.10),(.09,.16,2.20),.012)
    wb(wall,wood,(door,.105,2.22),(1.15,.16,.10),.012)
    for z,h in [(.45,.60),(1.35,.92)]:
        for du in [-.23,.23]:
            wb(wall,wood,(door+du,.115,z),(.38,.028,h),.018)
            for dz in [-h/2,h/2]:wb(wall,brass,(door+du,.134,z+dz),(.35,.008,.008))
    wb(wall,brass,(door-.34,.13,1.04),(.05,.02,.17),.012)
    wt(wall,brass,[(door-.34,.15,1.06),(door-.34,.21,1.06),(door-.18,.21,1.06)],.016,10)
    # A pair of wall sconces at the screen sides, rear lamps, small picture lights.
    for wall,u in [('left',1.10),('right',-1.80),('back',-1.12),('back',1.12)]:
        wb(wall,brass,(u,.06,1.92),(.14,.09,.30),.025)
        wt(wall,brass,[(u,.12,1.86),(u,.30,1.86),(u,.30,2.04)],.018,10)
        wallpart(wall,'lathe',linen,(u,.30,1.99),[(.16,0),(.10,.23)],24)
        for z,r in [(1.99,.16),(2.22,.10)]:
            wt(wall,brass,[(u+r*math.cos(a),.30+r*math.sin(a),z) for a in np.linspace(0,math.tau,32,endpoint=False)],.006,6,True)
        wallpart(wall,'sphere',linen,(u,.30,2.07),(.035,.035,.055),12,6)
        light('Sconce',transform((u,.34,2.08),wall),52,(1,.72,.43),.18)
    # Soft bounced illumination, kept broad to avoid hot spots in an 8-bit atlas.
    light('Ceiling bounce',(0,1.45,2.48),135,(1,.85,.66),4.0,target=(0,1.45,0))
    light('Rear bounce',(0,-1.5,2.35),55,(1,.81,.62),2.5,target=(0,-.1,.7))
    light('Front fill',(0,3.9,2.45),65,(1,.84,.68),3.0,target=(0,5.4,1.6))
    return g.objects()


def cushion(g,mat,c,size,cutoff=None):
    # Superellipsoid with broad planar-looking faces and soft stuffed edges.
    vs=[];fs=[];n=32;rings=12
    power=lambda a:math.copysign(abs(a)**.33,a)
    for i in range(rings+1):
        v=-math.pi/2+math.pi*i/rings
        for j in range(n):
            u=math.tau*j/n
            vs.append((c[0]+size[0]*(0 if i in (0,rings) else power(math.cos(v)))*power(math.cos(u)),c[1]+size[1]*(0 if i in (0,rings) else power(math.cos(v)))*power(math.sin(u)),c[2]+size[2]*power(math.sin(v))))
    for i in range(rings):
        for j in range(n):
            a=i*n+j;b=i*n+(j+1)%n;cc=(i+1)*n+(j+1)%n;d=(i+1)*n+j
            if cutoff is None or max(vs[k][2] for k in (a,b,cc,d))>cutoff:fs.append((a,b,cc,d))
    g.add(mat,vs,fs,True)


def rounded_rect(g,mat,c,half,r,tube):
    pts=[]
    for sx,sy,start in [(1,1,0),(-1,1,90),(-1,-1,180),(1,-1,270)]:
        for a in np.linspace(start,start+90,7):
            a=math.radians(a);pts.append((c[0]+sx*(half[0]-r)+r*math.cos(a),c[1]+sy*(half[1]-r)+r*math.sin(a),c[2]))
    g.tube(mat,pts,tube,6,True)


def floorlamp(g,brass,linen,pos,light):
    x,y,z=pos
    g.lathe(brass,pos,[(0,0),(.22,0),(.23,.045),(.18,.07),(.07,.09),(.025,.14),(.025,1.48),(.055,1.51)],24)
    g.lathe(linen,(x,y,1.43),[(.32,0),(.21,.43)],48)
    for zz,r in [(1.43,.32),(1.86,.21)]:
        g.tube(brass,[(x+r*math.cos(a),y+r*math.sin(a),zz) for a in np.linspace(0,math.tau,48,endpoint=False)],.009,6,True)
    g.sphere(linen,(x,y,1.62),(.052,.052,.08),16,8)
    for a in [0,math.tau/3,2*math.tau/3]:
        g.tube(brass,[(x,y,1.49),(x+.30*math.cos(a),y+.30*math.sin(a),1.45)],.004,6)
    # Keep the diffuse bake emitter just outside the opaque decorative bulb.
    light('Floor lamp',(x,y+.10,1.62),85,(1,.72,.43),.22)


def book(g,mat,c,size,index,horizontal=False):
    temp=Geometry();temp.box(mat,c,size,omit=(0,1))
    vs,fs,ss,uv=temp.parts[mat]
    a=index%8*.75/8+.006;b=(index%8+1)*.75/8-.006
    tex=[[(a,.025),(b,.025),(b,.975),(a,.975)] for f in fs]
    g.add(mat,vs,fs,False,tex)


def bookshelf(g,wood,books,brass,wp,wall,center,width):
    def box(mat,c,size,b=0):wp(wall,'box',mat,c,size,b,('bottom',) if b else (0,1))
    box(wood,(center,.04,1.35),(width,.07,2.50))
    for x in [center-width/2,center+width/2]:box(wood,(x,.21,1.35),(.10,.40,2.57),.01)
    for z in [.17,.70,1.17,1.64,2.11,2.56]:box(wood,(center,.23,z),(width,.44,.065),.008)
    box(wood,(center,.23,2.61),(width+.14,.48,.09),.012)
    rng=np.random.default_rng(int((center+5)*100)+len(wall))
    for shelf,z in enumerate([.205,.735,1.205,1.675,2.145]):
        x=center-width/2+.12
        while x<center+width/2-.15:
            w=float(rng.uniform(.048,.105));h=float(rng.uniform(.24,.365));d=float(rng.uniform(.19,.25))
            if x+w>center+width/2-.1:break
            tmp=Geometry();book(tmp,books,(x+w/2,.32,z+h/2),(w,d,h),int(rng.integers(8)))
            for mat,(vs,fs,ss,uv) in tmp.parts.items():wp(wall,'add',mat,vs,fs,False,uv)
            x+=w+.009


def picture(g,books,wood,brass,wp,wall,u,z,w,h):
    wp(wall,'add',books,[(u-w/2,.035,z-h/2),(u+w/2,.035,z-h/2),(u+w/2,.035,z+h/2),(u-w/2,.035,z+h/2)],[(0,3,2,1)],False,[[(.755,.505),(.755,.995),(.995,.995),(.995,.505)]])
    for x in [u-w/2-.035,u+w/2+.035]:
        wp(wall,'box',wood,(x,.08,z),(.09,.12,h+.16),.012)
        wp(wall,'box',brass,(x,.149,z),(.018,.012,h+.12),.004)
    for zz in [z-h/2-.035,z+h/2+.035]:
        wp(wall,'box',wood,(u,.08,zz),(w+.16,.12,.09),.012)
        wp(wall,'box',brass,(u,.149,zz),(w+.12,.012,.018),.004)
