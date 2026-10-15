"""Angularly correct CC0 night panorama and opaque, layered garden geometry.

Coordinates are Blender metres. The sphere centre is the window's seated-eye
projection; angular source sampling and sphere UVs share the same parameters.
Only the visible angular sector plus a 30 cm head-motion margin is retained.
"""
import math
import bpy
import numpy as np
from mathutils import Vector
from lib_home_geometry import Geometry

CENTER=(4.2,2.8,1.25)
RADIUS=50.
AZIMUTH=(-78.,82.)
ELEVATION=(-30.,64.)
SOURCE_CENTER_U=.36

def panorama(source,path):
    source_image=bpy.data.images.load(str(source))
    w,h=source_image.size
    pixels=np.empty(w*h*4,np.float32);source_image.pixels.foreach_get(pixels)
    pixels=pixels.reshape(h,w,4)
    width,height=1742,1024
    # Pixel centres, bilinear sampling in the original linear HDR equirectangular image.
    u=(SOURCE_CENTER_U-np.linspace(*AZIMUTH,width,endpoint=False)/360-(AZIMUTH[1]-AZIMUTH[0])/width/720)%1
    v=.5+np.linspace(*ELEVATION,height,endpoint=False)/180+(ELEVATION[1]-ELEVATION[0])/height/360
    xx=u*w-.5;yy=v*h-.5;ix=np.floor(xx).astype(int);iy=np.floor(yy).astype(int)
    fx=(xx-ix)[None,:,None];fy=(yy-iy)[:,None,None]
    a=pixels[iy[:,None]%h,ix[None,:]%w,:3];b=pixels[iy[:,None]%h,(ix[None,:]+1)%w,:3]
    c=pixels[(iy[:,None]+1)%h,ix[None,:]%w,:3];d=pixels[(iy[:,None]+1)%h,(ix[None,:]+1)%w,:3]
    rgb=(a*(1-fx)+b*fx)*(1-fy)+(c*(1-fx)+d*fx)*fy
    # Photographic per-channel Reinhard shoulder rolls bright lights toward white.
    # Linear exposure -2.18 EV; no hard HDR clipping. Peak output is 246/255.
    rgb=np.maximum(rgb,0)*.22
    rgb=.92*rgb/(1+rgb)
    rgb=np.where(rgb<=.0031308,12.92*rgb,1.055*np.power(rgb,1/2.4)-.055)
    rgba=np.ones((height,width,4),np.float32);rgba[:,:,:3]=rgb
    image=bpy.data.images.new('Home_Night_unlit_BaseColor',width,height,alpha=False)
    image.colorspace_settings.name='sRGB';image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw=str(path);image.file_format='PNG';image.save()
    bpy.data.images.remove(source_image)
    return image

def build_window(materials,light):
    g=Geometry();night=materials['Night'];garden=materials['Garden'];wood=materials['Walnut'];stone=materials['Stone']
    # Curved far surface, inward facing. No plane or glass occupies the opening.
    nx,ny=80,40;vs=[];uv=[];fs=[]
    for j in range(ny+1):
        phi=math.radians(ELEVATION[0]+j/ny*(ELEVATION[1]-ELEVATION[0]))
        for i in range(nx+1):
            theta=math.radians(AZIMUTH[0]+i/nx*(AZIMUTH[1]-AZIMUTH[0]))
            vs.append((CENTER[0]+RADIUS*math.cos(phi)*math.cos(theta),CENTER[1]+RADIUS*math.cos(phi)*math.sin(theta),CENTER[2]+RADIUS*math.sin(phi)))
    for j in range(ny):
        for i in range(nx):
            a=j*(nx+1)+i;fs.append((a,a+nx+1,a+nx+2,a+1))
            uv.append([(i/nx,j/ny),(i/nx,(j+1)/ny),((i+1)/nx,(j+1)/ny),((i+1)/nx,j/ny)])
    g.add(night,vs,fs,True,uv)
    # Extend the accepted frame/sill outward; all original interior faces remain.
    for y in (1.365,4.235):g.box(wood,(4.36,y,1.695),(.32,.07,1.31),.008)
    g.box(wood,(4.36,2.8,2.365),(.32,2.80,.07),.008)
    g.box(stone,(4.36,2.8,1.005),(.34,2.96,.07),.008)
    # Cool stone terrace edge and slim iron garden railing 2.5 m beyond the window.
    g.box(garden,(6.74,3.0,.24),(.36,16,.48),.035)
    g.box(garden,(6.74,3.0,.51),(.46,16,.085),.018)
    for z in (.60,1.03):g.box(garden,(6.74,3,z),(.045,16,.045),.009)
    for y in np.arange(-4.7,11,.40):
        g.box(garden,(6.74,float(y),.80),(.025,.025,.47),.005)
    for y in (-4.8,-.8,3.2,7.2,10.8):
        g.box(garden,(6.74,y,.78),(.075,.075,.57),.012)
        g.sphere(garden,(6.74,y,1.095),(.055,.055,.055),10,6)
    # Branching deciduous trees: solid tapered limbs, no alpha cards or billboards.
    rng=np.random.default_rng(2145)
    def limb(start,direction,length,radius,depth):
        direction=Vector(direction).normalized();start=Vector(start)
        end=start+direction*length
        mid=start+direction*length*.52+Vector((float(rng.uniform(-.1,.1)),float(rng.uniform(-.1,.1)),.08))*length
        # Tapered rings with capped ends, transport-stable frame.
        ref=Vector((1,0,0)) if abs(direction.x)<.8 else Vector((0,1,0))
        a=direction.cross(ref).normalized();b=direction.cross(a).normalized();verts=[]
        for p,r in [(start,radius),(mid,radius*.65),(end,radius*.28)]:
            for k in range(6):verts.append(tuple(p+r*(a*math.cos(k*math.tau/6)+b*math.sin(k*math.tau/6))))
        faces=[tuple(range(5,-1,-1)),tuple(range(12,18))]
        faces += [(i*6+k,i*6+(k+1)%6,(i+1)*6+(k+1)%6,(i+1)*6+k) for i in range(2) for k in range(6)]
        g.add(garden,verts,faces,True)
        if depth:
            for k in range(3 if depth>2 else 2):
                angle=float(rng.uniform(0,math.tau));spread=float(rng.uniform(.35,.75))
                branch=direction*.65+(a*math.cos(angle)+b*math.sin(angle))*spread+Vector((0,0,.15))
                limb(end,branch,length*float(rng.uniform(.58,.78)),radius*.48,depth-1)
    for x,y,height in [(10.5,-3.1,1.75),(10.9,7.8,2.1),(9.6,12.0,1.6)]:
        limb((x,y,-.15),(.03,.04,1),height,.105,4)
    light('Window moon wash',(4.65,2.8,2.65),32,(.40,.57,1),2.0,target=(2.7,2.6,.1))
    light('Garden moon',(8,3,7),190,(.38,.51,.80),8,target=(7.5,3,0))
    return g.objects()
