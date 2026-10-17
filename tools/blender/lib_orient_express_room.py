"""Original 1928 salon 'AV / Aster Vale'. No railway logos or official insignia."""
import math
import numpy as np
from lib_orient_express_geometry import Geometry

SEATS=[(-1.125,0,0),(-.375,0,0),(.375,0,0),(1.125,0,0),(-.38,-3.1,0),(.38,-3.1,0),(-.83,-6.2,0),(.83,-6.2,0),(-.83,2.65,0),(.83,2.65,0)]
SCREEN=(0,6.65,.9)


def build(m,light):
    g=Geometry()
    def box(k,c,s,b=0,omit=()):g.box(m[k],c,s,b,omit)
    def tube(k,p,r=.012,n=8,closed=False):g.tube(m[k],p,r,n,closed)
    def lathe(k,c,p,n=24):g.lathe(m[k],c,p,n)
    def panel(k,verts,reverse=False):g.add(m[k],verts,[(3,2,1,0) if reverse else (0,1,2,3)],uvs=[[(0,0),(1,0),(1,1),(0,1)]])
    # Interior 2.90 x 16 m, single-sided floor, shallow arched clerestory ceiling.
    panel('Carpet',[(-1.45,-9.25,0),(1.45,-9.25,0),(1.45,6.85,0),(-1.45,6.85,0)])
    xs=np.linspace(-1.45,1.45,29)
    def roof(x):return 2.56-.18*(abs(x)/1.45)**6
    for a,b in zip(xs[:-1],xs[1:]):
        g.add(m['Ivory'],[(a,-9.25,roof(a)),(a,6.85,roof(a)),(b,6.85,roof(b)),(b,-9.25,roof(b))],[(0,1,2,3)],True)
    windows=[-7.65,-5.1,-2.55,0,2.55,5.1]
    for side in [-1,1]:
        x=side*1.47
        box('Wood',(x,-1.2,.39),(.04,16.1,.78),omit=(2 if side==1 else 4,))
        box('Wood',(x,-1.2,2.255),(.04,16.1,.45))
        for z in [.08,.75,.82,2.03,2.22]:box('Brass',(side*1.438,-1.2,z),(.024,16.1,.018))
        for cy in windows:
            # Real rectangular openings 1.80 x 1.15, with no glass faces.
            for dy in [-1.13,1.13]:box('Wood',(side*1.46,cy+dy,1.43),(.06,.46,1.22))
            for dy in [-.94,.94]:box('Brass',(side*1.431,cy+dy,1.425),(.035,.035,1.24))
            for z in [.84,2.015]:box('Wood',(side*1.42,cy,z),(.07,1.92,.055))
            # Matched botanical inlay panels on lower wainscot and window piers.
            verts=[(side*1.435,cy-.82,.13),(side*1.435,cy+.82,.13),(side*1.435,cy+.82,.70),(side*1.435,cy-.82,.70)]
            panel('Inlay',verts,side==1)
            for dy in [-1.12,1.12]:
                panel('Inlay',[(side*1.421,cy+dy-.14,.91),(side*1.421,cy+dy+.14,.91),(side*1.421,cy+dy+.14,1.96),(side*1.421,cy+dy-.14,1.96)],side==1)
            # Pleated curtain mesh, narrowing at a real tie-back; min |X| 1.356.
            for edge in [-1,1]:
                verts=[];faces=[]
                for iz in range(13):
                    t=iz/12;z=.86+t*1.18;waist=1-.46*math.exp(-((t-.38)/.15)**2)
                    for j in range(17):
                        q=j/16;yy=cy+edge*(.80+.25*q*waist)
                        xx=side*(1.39+.028*math.cos(q*8*math.pi)*waist)
                        verts.append((xx,yy,z))
                for iz in range(12):
                    for j in range(16):
                        a=iz*17+j;face=(a,a+1,a+18,a+17);faces.append(face if side*edge<0 else face[::-1])
                g.add(m['Curtain'],verts,faces,True)
                tube('Brass',[(side*1.355,cy+edge*(.80+.13*q),1.31-.025*math.sin(q*math.pi)) for q in np.linspace(0,1,9)],.011)
                lathe('Brass',(side*1.36,cy+edge*.91,1.22),[(.008,0),(.022,.04),(.012,.08)],12)
            box('Curtain',(side*1.409,cy,2.065),(.07,2.13,.13))
            box('Brass',(side*1.363,cy,2.005),(.016,2.10,.018))
            # Brass luggage racks high above the sight volume, with airy slats.
            if cy<5:
                for dy in [-.76,.76]:tube('Brass',[(side*1.43,cy+dy,2.16),(side*1.16,cy+dy,2.22),(side*1.13,cy+dy,2.31)],.013)
                for xx in [1.17,1.25,1.33]:tube('Brass',[(side*xx,cy-.79,2.23),(side*xx,cy+.79,2.23)],.010)
            # Cool window fill, used ONLY for baking.
            light('Dusk window',(side*1.52,cy,1.6),9,(.40,.58,1),1.3,(0,cy,.8))
        # Close the narrow terminal wall segments beyond the six windows.
        for yy,sz in [(-9.03,.44),(6.57,.56)]:box('Wood',(side*1.47,yy,1.43),(.06,sz,1.22))
    # Arched transverse roof ribs, bordered ivory coffers and ceiling pendants.
    for cy in [-8.7,-6.4,-3.85,-1.3,1.25,3.8,6.35]:
        tube('Wood',[(float(x),cy,roof(x)-.018) for x in xs],.025,8)
        tube('Brass',[(float(x),cy-.03,roof(x)-.035) for x in xs],.007,6)
    for x in [-.78,.78]:tube('Brass',[(x,-9.15,roof(x)-.025),(x,6.7,roof(x)-.025)],.009)
    for cy in [-7.5,-5,-2.5,.9,3.25,5.45]:
        lathe('Brass',(0,cy,2.32),[(0,.22),(.15,.22),(.18,.18),(.18,.13),(.25,.095),(.25,.075)],32)
        lathe('Glass',(0,cy,2.32),[(0,.012),(.12,.018),(.22,.06),(.235,.08)],32)
        lathe('Brass',(0,cy,2.32),[(0,0),(.045,0),(.055,.016)],20)
        light('Ceiling opal',(0,cy,2.29),24,(1,.77,.48),.55,(0,cy,.2))
    # End vestibule doors. Screen floats before the front door (no screen mesh).
    for cy,front in [(-9.20,False),(6.82,True)]:
        box('Dark',(0,cy,1.28),(2.9,.06,2.56))
        for xx in [-.98,.98]:box('Inlay',(xx,cy+(.04 if not front else -.04),1.1),(.86,.025,2.12))
        box('Wood',(0,cy+(.045 if not front else -.045),1.08),(.84,.055,2.16),.018)
        for xx in [-.46,.46]:box('Brass',(xx,cy+(.08 if not front else -.08),1.11),(.035,.035,2.23))
        facey=cy+(.08 if not front else -.08)
        panel('Glass',[(-.32,facey,.95),(.32,facey,.95),(.32,facey,1.98),(-.32,facey,1.98)],front)
        tube('Brass',[(.31,facey+(.025 if not front else -.025),.80),(.31,facey+(.025 if not front else -.025),.94)],.018)
        box('Brass',(0,facey,.06),(.87,.04,.08))
    for xx in [-1.335,1.335]:box('Brass',(xx,6.69,1.65),(.025,.035,1.56))
    for z in [.865,2.435]:box('Brass',(0,6.69,z),(2.70,.035,.025))
    # Invented AV monogram: geometry-only interlaced initials on rear door crown.
    yy=-9.12
    for pts in [[(-.18,yy,2.22),(-.07,yy,2.43),(.04,yy,2.22)],[(-.13,yy,2.30),(-.015,yy,2.30)],[(0,yy,2.43),(.09,yy,2.22),(.19,yy,2.43)]]:tube('Brass',pts,.009)
    # Four compact salon armchairs + two-place settee + two dining chairs.
    for i,(x,y,z) in enumerate(SEATS):
        dining=i in (6,7);w=.56 if dining else .72 if i in (4,5) else .59
        for dx in [-w*.37,w*.37]:
            for dy in [-.29,.30]:
                tube('Wood',[(x+dx,y+dy,.06),(x+dx*.9,y+dy*.9,.40)],.022)
                lathe('Brass',(x+dx,y+dy,0),[(.024,.02),(.024,.09)],10)
        box('Wood',(x,y+.02,.37),(w+.015,.76,.09),.024,('bottom',))
        g.softbox(m['Velvet'],(x,y+.03,.48),(w,.69,.19),.37,seg=24,rings=10)
        # Low backs preserve all rear-seat rays to the lower screen corners.
        g.softbox(m['Wood' if dining else 'Velvet'],(x,y-.36,.70),(w+.035,.15,.50),.40,seg=24,rings=10)
        g.softbox(m['Velvet'],(x,y-.272,.73),(w-.035,.12,.44),.40,seg=24,rings=10)
        for dx in [-w*.48,w*.48]:
            if i in [4,5] and ((i==4 and dx>0) or (i==5 and dx<0)):continue
            tube('Wood',[(x+dx,y-.25,.42),(x+dx,y-.25,.68),(x+dx,y+.28,.68),(x+dx,y+.28,.42)],.022)
            g.softbox(m['Velvet'],(x+dx,y+.025,.695),(.065,.61,.075),.5,seg=16,rings=8)
        # Piping and three small upholstery buttons, entirely below head envelope.
        # Half sunk into the pile of the back cushion (its front lies at y-.2128 there), not floating before it.
        for dx in [-.15,0,.15]:g.sphere(m['Brass'],(x+dx,y-.2135,.78),(.009,.006,.009),8,4)
        tube('Brass',[(x-w/2+.025,y+.357,.47),(x+w/2-.025,y+.357,.47)],.005,6)
    # Join the two settee seats visually with one base and a continuous low back.
    box('Wood',(0,-3.08,.34),(1.66,.75,.08),.025,('bottom',))
    box('Wood',(0,-3.47,.65),(1.65,.09,.48),.03)
    def table(x,y,r,h=.66,lamp=True):
        lathe('Brass',(x,y,0),[(0,.015),(.13,.015),(.15,.035),(.04,.09),(.025,h-.045)],24)
        lathe('Wood',(x,y,0),[(0,h-.05),(r,h-.05),(r,h),(0,h)],32)
        lathe('Brass',(x,y,0),[(r+.003,h-.028),(r+.003,h-.014)],32)
        if lamp:
            lathe('Brass',(x,y,h),[(0,0),(.064,0),(.07,.025),(.035,.055),(.02,.23),(.045,.26)],24)
            # Pleated silk shade with geometry fringe, opaque on both faces.
            profile=[(.14,.23),(.085,.42),(.078,.42),(.133,.23)]
            lathe('Shade',(x,y,h),profile,40)
            for j in range(24):
                t=j*math.tau/24;xx=x+.14*math.cos(t);yy=y+.14*math.sin(t)
                tube('Brass',[(xx,yy,h+.23),(xx,yy,h+.18)],.0035,5)
            lathe('Brass',(x,y,h),[(.14,.23),(.14,.24)],32)
            light('Table silk',(x,y,h+.24),7,(1,.68,.36),.18,(x,y,h-.1))
    # Tables with lamps are behind the standard row; front wall sconces stay flush.
    for side in [-1,1]:
        for cy in [-1.4,-4.55,-7.8]:table(side*1.17,cy,.21)
        for cy in [1.3,3.85]:
            box('Brass',(side*1.405,cy,1.52),(.035,.23,.47))
            panel('Glass',[(side*1.38,cy-.095,1.32),(side*1.38,cy+.095,1.32),(side*1.38,cy+.095,1.72),(side*1.38,cy-.095,1.72)],side==1)
            light('Etched wall',(side*1.36,cy,1.50),5,(1,.76,.48),.22,(0,cy,1))
    for x in [-.83,.83]:
        table(x,-5.33,.28,.72,False)
        table(x,3.65,.26,.64,False)
        # A small cup and saucer give the forward seating group a human scale.
        lathe('Ivory',(x,3.65,.645),[(0,0),(.073,0),(.073,.012),(0,.012)],24)
        lathe('Ivory',(x,3.65,.656),[(.03,0),(.04,.07),(.034,.07),(.025,.012)],24)
    # Rear hospitality cabinet with cup saucers and geometric marquetry front.
    box('Wood',(0,-8.38,.46),(1.25,.48,.90),.025)
    box('Brass',(0,-8.38,.93),(1.29,.50,.025))
    panel('Inlay',[(-.58,-8.125,.12),(.58,-8.125,.12),(.58,-8.125,.84),(-.58,-8.125,.84)])
    for x in [-.37,0,.37]:
        lathe('Ivory',(x,-8.32,.95),[(0,0),(.075,0),(.075,.015),(0,.015)],20)
        lathe('Ivory',(x,-8.32,.96),[(.035,0),(.045,.075),(.039,.075),(.03,.015)],20)
    return g.objects()


def surroundings(m):
    """Three flat opaque bands; affine U preserves broadside speed at every row.
    Tree/hedge silhouettes live inside RGB. All geometric top edges are horizontal.
    """
    from lib_orient_express_motion import LAYERS
    g=Geometry()
    for key,p in LAYERS.items():
        x=p['carrier'];repeat=p['repeat']
        levels=[(-200.,p['z0'],0,0),(p['z0'],p['z1'],0,1)] if key=='Near' else [(p['z0'],p['z1'],0,1)]
        for side in [-1,1]:
            for z0,z1,v0,v1 in levels:
                verts=[(side*x,-2000,z0),(side*x,2000,z0),(side*x,2000,z1),(side*x,-2000,z1)]
                face=(3,2,1,0) if side==1 else (0,1,2,3)
                uv=[(-2000/repeat,v0),(2000/repeat,v0),(2000/repeat,v1),(-2000/repeat,v1)]
                g.add(m[key],verts,[face],uvs=[[uv[i] for i in face]])
    return g.objects()
