"""Reproducible 1920s picture palace, Blender 5.2. No Unity or add-ons needed.

Blender -b --factory-startup -P tools/blender/cinema.py -- Assets/TheMovies/Art/Cinema
Optional --preview-only rebuilds previews from art/blender/Cinema-baked.blend.
Optional --reanchor-only reuses that baked checkpoint, measures head markers and re-exports.
Optional --geometry-only saves a local sightline draft without baking or replacing the export.
Raw CC0 downloads are restricted to Poly Haven and verified against their API MD5.
"""
import os
import sys
sys.dont_write_bytecode = True
import json
import math
import hashlib
import subprocess
from pathlib import Path
import time
import bpy
import numpy as np
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_cinema import Geometry,texture
from lib_cinema_chairs import add_chairs,stamp
from lib_cinema_validation import normal_rows
from lib_cinema_markers import chair_layout,anchor_seated_heads,EYE_HEIGHT,BOX_FLOORS
from lib_cinema_uvs import uv_overlaps

ROOT=Path(__file__).resolve().parents[2]
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
OUT=(ROOT/(args[0] if args else 'Assets/TheMovies/Art/Cinema')).resolve()
assert OUT.is_relative_to(ROOT/'Assets/TheMovies/Art/Cinema'), 'Output must stay in Cinema worktree folder'
TEX=OUT/'Textures'; SOURCES=ROOT/'art/sources'; PREVIEW=ROOT/'Logs/art-preview'; BLEND=ROOT/'art/blender'
for p in (TEX,SOURCES,PREVIEW,BLEND):p.mkdir(parents=True,exist_ok=True)
START=time.time()

def log(s):print('[Cinema] '+s,flush=True)
def fetch(asset,model=False):
    for route in ('files','info'):
        dst=SOURCES/f'{asset}_{route}.json'
        if not dst.exists():subprocess.run(['curl','--fail','--location','--silent','--show-error',f'https://api.polyhaven.com/{route}/{asset}','-o',str(dst)],check=True)
    entry=json.loads((ROOT/'art/cinema-sources.json').read_text())[asset]
    assert entry['url'].startswith('https://dl.polyhaven.org/')
    dest=SOURCES/Path(entry['url']).name
    if not dest.exists():subprocess.run(['curl','--fail','--location','--silent','--show-error',entry['url'],'-o',str(dest)],check=True)
    assert hashlib.md5(dest.read_bytes()).hexdigest()==entry['md5'],str(dest)
    return dest

def aim(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
def light(name,loc,power,color=(1,.78,.51),size=2,target=None):
    d=bpy.data.lights.new(name,'AREA' if target else 'POINT');d.energy=power*.28;d.color=color
    if target:d.shape='DISK';d.size=size
    else:d.shadow_soft_size=size
    o=bpy.data.objects.new(name,d);bpy.context.scene.collection.objects.link(o);o.location=loc
    if target:aim(o,target)

def preview():
    scene=bpy.context.scene
    atlas=bpy.data.images.load(str(TEX/'Cinema_Light.png'),check_existing=True)
    dimmers=[]
    for m in bpy.data.materials:
        if not m.use_nodes or not m.get('cinema'):continue
        nt=m.node_tree;nt.nodes.clear()
        out=nt.nodes.new('ShaderNodeOutputMaterial');em=nt.nodes.new('ShaderNodeEmission')
        base=nt.nodes.new('ShaderNodeTexImage');base.image=bpy.data.images.load(str(TEX/f'{m.name}_BaseColor.png'),check_existing=True)
        uv=nt.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';nt.links.new(uv.outputs['UV'],base.inputs['Vector'])
        lm=nt.nodes.new('ShaderNodeTexImage');lm.image=atlas;lm.extension='EXTEND'
        uv2=nt.nodes.new('ShaderNodeUVMap');uv2.uv_map='Lightmap';nt.links.new(uv2.outputs['UV'],lm.inputs['Vector'])
        mul=nt.nodes.new('ShaderNodeMixRGB');mul.blend_type='MULTIPLY';mul.inputs[0].default_value=1
        nt.links.new(base.outputs['Color'],mul.inputs[1]);nt.links.new(lm.outputs['Color'],mul.inputs[2]);nt.links.new(mul.outputs[0],em.inputs['Color'])
        em.inputs['Strength'].default_value=2;nt.links.new(em.outputs[0],out.inputs['Surface']);dimmers.append(em.inputs['Strength'])
    for o in list(scene.objects):
        if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
    scene.world.color=(0,0,0)
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0
    scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
    scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB'
    data=bpy.data.cameras.new('Preview');cam=bpy.data.objects.new('Preview',data);scene.collection.objects.link(cam);scene.camera=cam
    views=[('Seat2-house-on',(-.375,0,EYE_HEIGHT),(0,11.9,4.25),17,1),('Seat2-house-12pct',(-.375,0,EYE_HEIGHT),(0,11.9,4.25),17,.12),('Overview-back-top',(0,-6.7,8.25),(0,4.3,2.5),15.5,1),('Gallery-and-seating',(6.5,8,5.8),(0,-2.5,4.2),19,1),
           ('Seat2-row-ahead-down',(-.375,0,EYE_HEIGHT),(-.375,1.1,.25),24,1),
           ('Seat2-neighbours-side',(-.375,0,EYE_HEIGHT),(1.05,-.40,.72),25,1),
           ('Seat-closeup',(-5.25,1.05,EYE_HEIGHT),(-4.125,-.36,.63),32,1)]
    # These views use the actual exported head markers and the current row floor.
    shift=Vector((0,float(scene.get('hall_shift_y',0)),0))
    for name,spot in [('Box-Spot_147-house-on','Spot_147'),('Gallery-Spot_125-house-on','Spot_125')]:
        eye=scene.objects[spot].location-shift+Vector((0,0,EYE_HEIGHT))
        views.append((name,eye,(0,11.9,4.5),20,1))
    for name,loc,target,lens,dim in views:
        shift=Vector((0,float(scene.get('hall_shift_y',0)),0))
        cam.location=Vector(loc)+(Vector((0,0,0)) if name.startswith('Seat2-') else shift)
        aim(cam,Vector(target)+shift);data.lens=lens;data.clip_start=.05;data.clip_end=100
        for sock in dimmers:sock.default_value=2*dim
        scene.render.filepath=str(PREVIEW/f'{name}.png');bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam,do_unlink=True)
    log('Baked previews complete')

def export_anchored(report):
    scene=bpy.context.scene
    report['head_anchors']=anchor_seated_heads(scene)
    log('Measured head anchors: '+json.dumps(report['head_anchors']))
    objects=[o for o in scene.objects if o.type=='MESH']
    np.savez_compressed(PREVIEW/'normals-before-export.npz',**{o.name:normal_rows(o) for o in objects})
    bpy.ops.export_scene.fbx(filepath=str(OUT/'Cinema.fbx'),use_selection=False,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    report['markers']={o.name:list(o.location) for o in scene.objects if o.type=='EMPTY'}
    report['elapsed_seconds']=round(time.time()-START)
    scene['cinema_build_metrics']=json.dumps(report)
    (PREVIEW/'metrics.json').write_text(json.dumps(report,indent=2)+'\n')
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND/'Cinema-baked.blend'))
    preview();log(f'Complete: {report["total_triangles"]} triangles; {len(objects)} materials')

if '--reanchor-only' in args:
    bpy.ops.wm.open_mainfile(filepath=str(BLEND/'Cinema-baked.blend'))
    scene=bpy.context.scene
    report=json.loads(scene['cinema_build_metrics']) if 'cinema_build_metrics' in scene else json.loads((PREVIEW/'metrics.json').read_text())
    report['bake_reused']=True
    export_anchored(report);sys.exit(0)

if '--preview-only' in args:
    bpy.ops.wm.open_mainfile(filepath=str(BLEND/'Cinema-baked.blend'));preview();sys.exit(0)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
velour=fetch('velour_velvet');jacquard=fetch('quatrefoil_jacquard_fabric');wood=fetch('dark_wood');chandelier=fetch('Chandelier_03',True)
materials={}
def material(name,color,source=None,pattern=None,emission=0):
    m=bpy.data.materials.new(name);m.use_nodes=True;m['cinema']=True
    m.diffuse_color=(*color,1);bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.85
    img=texture(name,TEX/f'{name}_BaseColor.png',color,source,pattern)
    tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=img
    uv=m.node_tree.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';m.node_tree.links.new(uv.outputs['UV'],tex.inputs['Vector']);m.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    if emission:bs.inputs['Emission Color'].default_value=(*color,1);bs.inputs['Emission Strength'].default_value=emission
    materials[name]=m;return m
ivory=material('Ivory_Stucco',(.78,.69,.52))
gold=material('Gold_Ornament',(.72,.52,.23),pattern='gold')
velvet=material('Velvet_Crimson',(.45,.035,.055),velour)
damask=material('Damask_Oxblood',(.30,.06,.07),jacquard)
walnut=material('Walnut_Panelling',(.20,.10,.055),wood)
carpet=material('Carpet_Deco',(.16,.03,.04),pattern='carpet')
teal=material('Ceiling_Petrol',(.08,.17,.18))
dark=material('Stage_WarmShadow',(.075,.045,.045))
bulb=material('Bulb_emit',(.95,.67,.32),emission=.4)
# Dedicated chair fabric: keeps the approved curtains and balcony velvet untouched.
seat_velvet=material('Velvet_Seat',(.45,.035,.055),velour)
seat_image=next(n.image for n in seat_velvet.node_tree.nodes if n.type=='TEX_IMAGE')
pixels=np.empty(1024*1024*4,np.float32);seat_image.pixels.foreach_get(pixels);pixels=pixels.reshape(1024,1024,4)
yy,xx=np.mgrid[0:1024,0:1024].astype(np.float32)/1023
rad=(np.abs(2*xx-1)**3.25+np.abs(2*yy-1)**3.25)**(1/3.25)
# Low-contrast woven welt and a narrow stitched shadow in the same crimson family.
welt=np.exp(-((rad-.825)/.010)**2)
shadow=np.exp(-((rad-.849)/.014)**2)
pixels[:,:,:3]*=(1-.16*shadow)[:,:,None]
pixels[:,:,:3]+=(welt*.045)[:,:,None]*np.array([1,.32,.28])
seat_image.pixels.foreach_set(pixels.ravel());seat_image.save()
# Original cast-bronze ornament atlas: left shared panels, right richer aisle faces.
bronze=material('Bronze_Metal_SeatStandard',(.22,.145,.075))
bronze_image=next(n.image for n in bronze.node_tree.nodes if n.type=='TEX_IMAGE')
pixels=np.empty(1024*1024*4,np.float32);bronze_image.pixels.foreach_get(pixels);pixels=pixels.reshape(1024,1024,4)
u=(xx*2)%1;v=yy
# Stylized fluted fan and concentric beadwork, baked as color/cavity, no normal map.
dx=(u-.5)*1.15;dz=v-.34
radius=np.sqrt(dx*dx+dz*dz);angle=np.arctan2(dx,dz)
fan=(np.abs(angle)<1.05)&(radius>.06)&(radius<.43)
ribs=np.exp(-(np.sin(angle*8)/.15)**2)*fan
rim=np.exp(-((radius-.43)/.006)**2)*(np.abs(angle)<1.05)
stem=np.exp(-((u-.5)/.009)**2)*(v>.14)*(v<.36)
bead=np.maximum(ribs,np.maximum(rim,stem))
strength=np.where(xx>.5,1,.45)
pixels[:,:,:3]+=(bead*strength)[:,:,None]*np.array([.24,.17,.065])
# Recessed shadow next to each rib keeps the relief legible in the baked shader.
pixels[:,:,:3]*=(1-.20*np.roll(bead,3,axis=1))[:,:,None]
bronze_image.pixels.foreach_set(pixels.ravel());bronze_image.save()
g=Geometry()
# Interior shell: single inward-facing surfaces, never box backs.
g.add(damask,[(-12,-7.2,-.9),(-12,13.6,-.9),(-12,13.6,11.1),(-12,-7.2,11.1)],[(0,1,2,3)])
g.add(damask,[(12,-7.2,-.9),(12,-7.2,11.1),(12,13.6,11.1),(12,13.6,-.9)],[(0,1,2,3)])
g.add(damask,[(-12,-7.2,-.9),(-12,-7.2,11.1),(12,-7.2,11.1),(12,-7.2,-.9)],[(0,1,2,3)])
g.add(dark,[(-12,13.6,-.9),(12,13.6,-.9),(12,13.6,11.1),(-12,13.6,11.1)],[(0,1,2,3)])
g.add(teal,[(-12,-7.2,11.1),(-12,13.6,11.1),(12,13.6,11.1),(12,-7.2,11.1)],[(0,1,2,3)])
# Front apron plus six 1.65m terraces: two below, three above marker row.
g.box(carpet,(0,8.5475,-.94),(24,10.105,.08),omit=(0,1,2,3,4))
for row in range(-2,4):
    y=-row*1.65;z=row*.45
    g.box(carpet,(0,y-.63,z-.08),(24,1.65,.16),omit=(0,2,3,4))
    if row>-2:g.box(walnut,(0,y+.195,z-.225),(24,.04,.45),omit=(0,2,3,4))
    for side in (-1,1):
        # Aisles split each stadium riser into two walkable steps.
        g.box(carpet,(side*5.2,y+.4,z-.225),(1.25,.4,.06),omit=(0,2,3,4))
        g.box(gold,(side*5.2,y+.19,z+.012),(1.25,.035,.025),omit=(0,))
        g.box(gold,(side*5.2,y+.59,z-.2),(1.25,.035,.025),omit=(0,))
g.box(carpet,(0,-6.8025,1.27),(24,.795,.16),omit=(0,1,2,3,4))
# Stage is below the screen lower edge. No screen mesh.
g.box(walnut,(0,12.25,-.45),(23,2.7,.7),omit=(0,2,3,4))
g.box(gold,(0,10.9,-.08),(23,.12,.1),.025,omit=('bottom',))
# Profiled proscenium: three concentric flattened arches above the full clear opening.
def portal_path(w,top,y):
    r=.72;pts=[(-w,y,-.1),(-w,y,top-r)]
    pts +=[(-w+r-r*math.cos(t),y,top-r+r*math.sin(t)) for t in np.linspace(0,math.pi/2,12)]
    pts.append((w-r,y,top));pts +=[(w-r+r*math.sin(t),y,top-r+r*math.cos(t)) for t in np.linspace(0,math.pi/2,12)];pts.append((w,y,-.1));return pts
for i in range(3):
    w=10.85+i*.35;top=9.75+i*.42;y=11.8-i*.25
    g.tube(ivory,portal_path(w,top,y),.19,8)
    g.tube(gold,portal_path(w-.11,top-.11,y-.12),.065,8)
    g.tube(gold,portal_path(w+.13,top+.13,y-.07),.044,6)
# Fan/sunburst crest.
for j in range(25):
    a=math.pi*j/24
    g.tube(gold,[(.36*math.cos(a),11.1,9.93+.22*math.sin(a)),(1.0*math.cos(a),11.1,9.93+.66*math.sin(a))],.027,6)
g.sphere(gold,(0,11.06,9.97),(.35,.08,.24),16,8)
# Deep pleated side curtains, constrained outside +/-9.5m at every height.
for side in (-1,1):
    vs=[];fs=[];nx=36;nz=28
    for iz in range(nz+1):
        t=iz/nz;z=-.05+t*9.45
        inner=9.62+.46*math.sin(math.pi*t)**2
        for ix in range(nx+1):
            u=ix/nx;x=side*(inner+(10.69-inner)*u)
            y=11.72+.17*math.sin(u*math.tau*7)+.1*math.cos(t*math.pi)
            vs.append((x,y,z))
    for iz in range(nz):
        for ix in range(nx):
            a=iz*(nx+1)+ix;face=(a,a+1,a+nx+2,a+nx+1);fs.append(face if side==1 else face[::-1])
    g.add(velvet,vs,fs,True)
    g.tube(gold,[(side*(10.1+.56*u),11.45+.09*math.sin(u*math.pi),3.65-.13*math.sin(u*math.pi)) for u in np.linspace(0,1,15)],.055,8)
    for x in (10.12,10.24):
        g.tube(gold,[(side*x,11.42,3.66),(side*(x-.06),11.42,3.05)],.022,6)
        g.lathe(gold,(side*(x-.06),11.42,2.85),[(.05,0),(.08,.15),(.03,.23)],8)
# Five swagged valance scallops. Lowest point 8.79m, above max screen top 8.6.
for k in range(5):
    left=-9.6+k*3.84;vs=[];fs=[];nx=36;ny=8
    for j in range(ny+1):
        t=j/ny
        for i in range(nx+1):
            u=i/nx;bottom=9.37-.58*math.sin(math.pi*u)
            z=9.52*(1-t)+bottom*t
            vs.append((left+u*3.84,11.48-.12*math.sin(math.pi*u)-.065*math.sin(t*math.pi*6),z))
    for j in range(ny):
        for i in range(nx):a=j*(nx+1)+i;fs.append((a+nx+1,a+nx+2,a+1,a))
    g.add(velvet,vs,fs,True)
    edge=[(left+u*3.84,11.32-.12*math.sin(math.pi*u),9.37-.58*math.sin(math.pi*u)) for u in np.linspace(0,1,60)]
    g.tube(gold,edge,.035,6)
    for i in range(65):
        u=i/64;x=left+u*3.84;z=9.37-.58*math.sin(math.pi*u)
        g.tube(gold,[(x,11.3-.12*math.sin(math.pi*u),z),(x,11.3-.12*math.sin(math.pi*u),z-.095)],.009,4)
# Side wall paneling, fluted pilasters, capitals and shell ornaments.
for side in (-1,1):
    x=side*11.86
    g.box(walnut,(x,2.5,.55),(.22,19.4,2.9),omit=((0,2) if side==1 else (0,4)))
    for z,width in [(2.05,.13),(7.5,.18),(9.85,.23),(10.55,.3)]:
        g.box(ivory,(side*11.7,2.5,z),(.35,19.4,width),.03)
        g.box(gold,(side*11.49,2.5,z-.08),(.04,19.4,.045))
    for y in [-5.6,-1.8,2,5.8,9.6]:
        g.box(ivory,(side*11.55,y,5),(.5,.58,9),.05)
        for dy in [-.19,0,.19]:g.tube(gold,[(side*11.26,y+dy,2.25),(side*11.26,y+dy,9.25)],.026,6)
        for z,sy,sx in [(1.9,.85,.65),(2.1,.7,.6),(9.25,.8,.67),(9.45,1.03,.8)]:g.box(ivory,(side*11.53,y,z),(sx,sy,.18),.045)
        for dy in [-.32,.32]:
            # Stylized Ionic scrolls and acanthus leaves visible from the room.
            g.tube(gold,[(side*11.08,y+dy+.12*math.cos(t),9.2+.12*math.sin(t)) for t in np.linspace(0,math.tau,20)],.035,6,True)
        for a in range(5):g.sphere(gold,(side*11.12,y+(a-2)*.12,9.02+(.1 if a%2 else 0)),(.06,.07,.2),6,4)
    for y in [-3.7,.1,3.9,7.7]:
        # Raised double border around each damask panel.
        for dy in [-1.5,1.5]:g.box(gold,(side*11.69,y+dy,5.9),(.05,.045,5.9))
        for z in [2.95,8.85]:g.box(gold,(side*11.69,y,z),(.05,3,.045))
        # Three-arm Art Deco sconces with glowing frosted shades.
        g.sphere(gold,(side*11.45,y,5.25),(.1,.22,.48),10,6)
        for dy in [-.3,0,.3]:
            g.tube(gold,[(side*11.4,y,5.1),(side*11.05,y+dy,5.1),(side*10.95,y+dy,5.55)],.04,8)
            g.lathe(bulb,(side*10.95,y+dy,5.5),[(.12,0),(.17,.12),(.13,.42),(.07,.5)],10)
            g.lathe(gold,(side*10.95,y+dy,5.49),[(.13,0),(.13,.045)],10)
        light('Sconce',(side*10.6,y,5.65),165,size=.5)
        light('Sconce wall wash',(side*10.8,y,6.1),110,size=1.5,target=(side*12,y,7.3))
# Side opera boxes with turned balusters, scalloped panels and top rails.
def baluster(x,y,z,geometry=g):geometry.lathe(ivory,(x,y,z),[(.09,0),(.09,.08),(.055,.13),(.1,.28),(.065,.43),(.035,.62),(.07,.72),(.07,.77)],8)
for side in (-1,1):
    for y in [-2.7,3.15,8.2]:
        box_floor=BOX_FLOORS[y]
        box_base=Geometry()
        box_base.box(ivory,(side*10.82,y,3.4),(2.25,3.2,.3),.065)
        box_base.box(walnut,(side*10.82,y,3.58),(2.1,3.05,.08))
        stamp(g,box_base,(0,0,box_floor-3.62))
        box_front=Geometry()
        box_front.box(velvet,(side*9.69,y,3.85),(.1,3.12,.5))
        for yy in np.linspace(y-1.45,y+1.45,11):baluster(side*9.62,yy,4.06,box_front)
        box_front.box(gold,(side*9.61,y,4.88),(.21,3.3,.1),.035)
        for yy in [y-1.57,y+1.57]:
            for xx in np.linspace(9.65,11.75,7):baluster(side*xx,yy,4.06,box_front)
            box_front.box(gold,(side*10.7,yy,4.88),(2.2,.19,.1),.025)
        for yy in [y-1,y,y+1]:box_front.sphere(gold,(side*9.58,yy,3.85),(.055,.17,.14),10,6)
        for mat,(vs,fs,smooth,uvs) in box_front.parts.items():
            start=len(g.parts.get(mat,([],[],[],[]))[1])
            g.add(mat,[(x,y,box_floor+(z-3.62)*(.75/1.31)) for x,y,z in vs],fs,False,uvs)
            g.parts[mat][2][start:]=smooth
# Rear gallery behind the six ground rows, open undercroft.
g.box(ivory,(0,-6.45,4.65),(23.6,1.5,.35),.07)
gallery_front=Geometry()
gallery_front.box(velvet,(0,-5.66,4.97),(23.6,.12,.48))
for x in np.linspace(-11.6,11.6,71):baluster(x,-5.6,5.18,gallery_front)
gallery_front.box(gold,(0,-5.6,5.99),(23.7,.22,.12),.035)
for mat,(vs,fs,smooth,uvs) in gallery_front.parts.items():
    start=len(g.parts.get(mat,([],[],[],[]))[1])
    g.add(mat,[(x,y,4.825+(z-4.825)*(.80/1.225)) for x,y,z in vs],fs,False,uvs)
    g.parts[mat][2][start:]=smooth
for x in [-9,-6,6,9]:
    g.box(ivory,(x,-6.2,2.9),(.32,.38,3.2),.04)
# Rear gallery wall: repeated gilded panels, pilasters and a sunburst cartouche.
for z in [6.25,9.9,10.55]:
    g.box(ivory,(0,-7.03,z),(23.5,.2,.14),.025)
    g.box(gold,(0,-6.9,z-.11),(23.5,.035,.045))
for x in [-10,-6,-2,2,6,10]:
    g.box(ivory,(x,-6.98,8.05),(.27,.23,3.55),.035)
    g.box(gold,(x,-6.83,8.05),(.045,.035,3.3))
    g.box(ivory,(x,-6.94,9.8),(.5,.3,.18),.03)
for x in [-8,-4,0,4,8]:
    for dx in [-1.55,1.55]:g.box(gold,(x+dx,-7.08,8.05),(.035,.04,2.8))
    for z in [6.65,9.45]:g.box(gold,(x,-7.08,z),(3.1,.04,.035))
    g.tube(gold,[(x+.38*math.cos(t),-6.97,8.3+.62*math.sin(t)) for t in np.linspace(0,math.tau,40,endpoint=False)],.035,6,True)
    for j in range(9):
        a=math.pi*j/8
        g.tube(gold,[(x+.13*math.cos(a),-6.94,8.1+.13*math.sin(a)),(x+.29*math.cos(a),-6.94,8.1+.42*math.sin(a))],.022,6)
for x in [-8,0,8]:
    g.box(gold,(x,-6.91,7.1),(.28,.18,.5),.06)
    g.lathe(bulb,(x,-6.65,7.15),[(.13,0),(.18,.25),(.1,.48)],10)
    light('Gallery sconce',(x,-6.25,7.4),85,size=.7)
for x in [-10,10]:
    g.box(walnut,(x,-7.08,2.9),(1.25,.18,2.9),omit=(1,))
    for dx in [-.68,.68]:g.box(ivory,(x+dx,-6.96,2.9),(.12,.17,3.1),.02)
    g.box(ivory,(x,-6.96,4.42),(1.48,.17,.13),.02)
    for dx in [-.43,.43]:g.box(gold,(x+dx,-6.95,3.0),(.025,.03,2.2))
    for z in [1.9,4.1]:g.box(gold,(x,-6.95,z),(.88,.03,.025))
    g.sphere(gold,(x+.38,-6.91,2.85),(.035,.035,.055),8,4)
# Ceiling coffers and rosette; no inaccessible box backs.
for x in [-10,-6,-2,2,6,10]:
    for y in [-5,-1,3,7,10.1]:
        for dx in [-1.7,1.7]:
            g.box(ivory,(x+dx,y,10.94),(.12,3.25,.18),omit=(5,))
            g.box(gold,(x+dx*.96,y,10.82),(.035,3.13,.045),omit=(5,))
        for dy in [-1.6,1.6]:g.box(ivory,(x,y+dy,10.94),(3.5,.12,.18),omit=(5,))
        g.sphere(gold,(x,y,10.96),(.17,.17,.04),12,4)
for r in [1.5,1.7,2.05]:g.tube(gold,[(r*math.cos(t),2.6+r*math.sin(t),10.77) for t in np.linspace(0,math.tau,80,endpoint=False)],.065,8,True)
for j in range(24):
    a=math.tau*j/24;g.sphere(ivory,(1.27*math.cos(a),2.6+1.27*math.sin(a),10.82),(.15,.15,.08),8,4)
# Original seat placement; the entire completed, baked hall is anchored at export.
chair_positions=chair_layout()
chair_stats=add_chairs(g,chair_positions,seat_velvet,walnut,gold,bronze)
log('Chair budgets: '+json.dumps({k:v for k,v in chair_stats.items() if k!='rows'}))
# Screen markers move with the portal; seated head markers are measured at export.
for name,loc in [('ScreenAnchor',(0,11.9,.4)),('ScreenTop',(0,11.9,8.6))]:
    o=bpy.data.objects.new(name,None);scene.collection.objects.link(o);o.location=loc;o.empty_display_type='ARROWS'
meshes=g.objects();log('Procedural geometry built')
# Import actual CC0 chandelier mesh, simplify from ~29k to ~9k, recolor brass.
with bpy.data.libraries.load(str(chandelier),link=False) as (src,dst):dst.objects=[n for n in src.objects if n=='Chandelier_03']
ch=dst.objects[0];scene.collection.objects.link(ch)
bpy.context.view_layer.objects.active=ch;ch.select_set(True)
for mod in list(ch.modifiers):ch.modifiers.remove(mod)
dec=ch.modifiers.new('Quest reduction','DECIMATE');dec.ratio=.30;bpy.ops.object.modifier_apply(modifier=dec.name)
imported_triangles=sum(len(p.vertices)-2 for p in ch.data.polygons)
ch.data.materials.clear();ch.data.materials.append(gold)
ch.location=(0,2.6,8.4);ch.scale=(3.2,3.2,2.2)
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
# Center imported local bounding box and suspend it below the rosette.
coords=[v.co for v in ch.data.vertices];mins=Vector(tuple(min(v[i] for v in coords) for i in range(3)));maxs=Vector(tuple(max(v[i] for v in coords) for i in range(3)))
for v in ch.data.vertices:v.co-=Vector(((mins.x+maxs.x)/2,(mins.y+maxs.y)/2,maxs.z))
ch.location=(0,2.6,10.6);meshes.append(ch)
# Supplemental luminous candle tips and crystal drops on a generous central ring.
g2=Geometry()
for i in range(12):
    a=math.tau*i/12;x=1.12*math.cos(a);y=2.6+1.12*math.sin(a)
    g2.sphere(bulb,(x,y,9.12),(.075,.075,.17),8,6)
    g2.sphere(bulb,(x,y,8.73),(.035,.035,.13),6,4)
meshes+=g2.objects()
light('Chandelier down',(0,2.6,8.5),1600,size=3,target=(0,2,0))
light('Chandelier ceiling',(0,2.6,9.7),700,size=2,target=(0,2.6,11))
light('Gallery fill',(0,-5.5,8.7),650,color=(1,.86,.7),size=5,target=(0,-2,0))
for x in [-7,0,7]:
    light('House fill',(x,-1,8.8),1000,color=(1,.86,.68),size=5,target=(x,1,0))
    light('Stage velvet',(x,9.3,7.8),550,size=3,target=(x,12,6.4))
for side in (-1,1):
    for y in [-3,3,8]:light('Cornice bounce',(side*10.9,y,9.8),220,size=2,target=(side*11.8,y,10.7))
scene.world=bpy.data.worlds.new('Warm ambient');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.22,.16,.11,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.38
# Join first so lightmap unwrapping is global. Keep UVMap explicitly bound in material nodes.
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join();room=bpy.context.object;room.name='Cinema'
# Remove exact coincident vertices and degenerate faces (sphere poles), retain deliberate hard normals.
import bmesh
bm=bmesh.new();bm.from_mesh(room.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001);bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(room.data);bm.free()
# Smooth chair surfaces use split vertex normals. Freeze evaluated corner normals
# explicitly; architectural flat/smooth flags are preserved exactly as generated.
room.data.update()
room.data.normals_split_custom_set([tuple(n.vector) for n in room.data.corner_normals])
if '--geometry-only' in args:
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.separate(type='MATERIAL');bpy.ops.object.mode_set(mode='OBJECT')
    for o in scene.objects:
        if o.type=='MESH':o.name='Cinema_'+next(m.name for m in o.data.materials if m)
    anchor_seated_heads(scene)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND/'Cinema-sightline-draft.blend'))
    log('Geometry-only sightline checkpoint complete');sys.exit(0)
room.data.uv_layers[0].name='UVMap'
for uv in list(room.data.uv_layers)[1:]:room.data.uv_layers.remove(uv)
lm=room.data.uv_layers.new(name='Lightmap');room.data.uv_layers.active=lm
log(f'Unwrapping {len(room.data.polygons)} faces')
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=1.25,island_margin=0,area_weight=.5,scale_to_bounds=True)
bpy.ops.uv.pack_islands(rotate=True,margin_method='FRACTION',margin=6/4096,shape_method='CONVEX')
bpy.ops.object.mode_set(mode='OBJECT')
# Smart projection can fold tiny imported ornament triangles onto neighbouring
# triangles at a shared UV vertex. Isolate only those faces and repack the same
# global atlas; geometry and UVMap stay untouched.
lightmap_repair_faces=0
for repair_pass in range(4):
    room.data.calc_loop_triangles();layer=room.data.uv_layers['Lightmap']
    uvtris=np.array([[tuple(layer.data[i].uv) for i in t.loops] for t in room.data.loop_triangles])
    overlaps=uv_overlaps(uvtris)
    if not overlaps:break
    assert repair_pass<3,('Unresolved UV intersections',len(overlaps))
    split=sorted({room.data.loop_triangles[j].polygon_index for i,j in overlaps})
    for index,face_index in enumerate(split):
        for li in room.data.polygons[face_index].loop_indices:
            layer.data[li].uv.x+=2+index*2
    lightmap_repair_faces+=len(split)
    log(f'Isolated {len(split)} folded ornament UV faces; repacking atlas')
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True,margin_method='FRACTION',margin=6/4096,shape_method='CONVEX')
    bpy.ops.object.mode_set(mode='OBJECT')
# One atlas, 256 sample Cycles diffuse direct+indirect, no albedo.
image=bpy.data.images.new('Cinema_Light',4096,4096,float_buffer=True)
for m in room.data.materials:
    n=m.node_tree.nodes.new('ShaderNodeTexImage');n.name='BakeTarget';n.image=image;m.node_tree.nodes.active=n
scene.render.engine='CYCLES';scene.cycles.samples=256;scene.cycles.use_denoising=True;scene.cycles.max_bounces=6;scene.cycles.diffuse_bounces=4
prefs=bpy.context.preferences.addons['cycles'].preferences
try:
    prefs.compute_device_type='METAL';prefs.get_devices()
    for d in prefs.devices:d.use=d.type=='METAL'
    scene.cycles.device='GPU'
except Exception:scene.cycles.device='CPU'
scene.render.bake.margin=8;scene.render.bake.use_clear=True
log('Starting 4096 / 256 sample Cycles bake')
bpy.ops.object.bake(type='DIFFUSE',pass_filter={'DIRECT','INDIRECT'},use_clear=True,margin=8)
scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=-1;scene.view_settings.gamma=1
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
# Bake does not execute Cycles render denoising. Apply OIDN explicitly in
# the compositor to the linear irradiance atlas, including the 8px dilation.
ng=bpy.data.node_groups.new('AtlasDenoise','CompositorNodeTree')
ng.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
scene.compositing_node_group=ng
node=ng.nodes.new('CompositorNodeImage');node.image=image
denoise=ng.nodes.new('CompositorNodeDenoise')
outnode=ng.nodes.new('NodeGroupOutput')
ng.links.new(node.outputs['Image'],denoise.inputs['Image']);ng.links.new(denoise.outputs['Image'],outnode.inputs['Image'])
camdata=bpy.data.cameras.new('AtlasDenoise');cam=bpy.data.objects.new('AtlasDenoise',camdata);scene.collection.objects.link(cam);scene.camera=cam
for o in scene.objects:
    if o.type=='MESH':o.hide_render=True
scene.render.resolution_x=4096;scene.render.resolution_y=4096;scene.render.resolution_percentage=100
scene.cycles.samples=1;scene.render.filepath=str(TEX/'Cinema_Light.png')
bpy.ops.render.render(write_still=True)
for o in scene.objects:
    if o.type=='MESH':o.hide_render=False
scene.compositing_node_group=None;bpy.data.node_groups.remove(ng);bpy.data.objects.remove(cam,do_unlink=True)
scene.cycles.samples=256
log('Lightmap saved')
# Export one object per material, with global UV2 preserved.
for m in room.data.materials:
    n=m.node_tree.nodes.get('BakeTarget')
    if n:m.node_tree.nodes.remove(n)
room.data.uv_layers.active=room.data.uv_layers['UVMap']
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.separate(type='MATERIAL');bpy.ops.object.mode_set(mode='OBJECT')
for o in list(scene.objects):
    if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
objects=[o for o in scene.objects if o.type=='MESH']
for o in objects:
    used=next(m for m in o.data.materials if m);o.name='Cinema_'+used.name
    o.data.uv_layers.active=o.data.uv_layers['UVMap']
stats={o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects}
assert sum(stats.values())<=250000,stats
assert len(objects)<=16
report={'triangles':stats,'total_triangles':sum(stats.values()),'materials':sorted(materials),'markers':{o.name:list(o.location) for o in scene.objects if o.type=='EMPTY'},'lightmap':[4096,4096],'basecolors':[1024,1024],'chairs':len(chair_positions),'chair_detail':chair_stats,'imported_chandelier_triangles':imported_triangles,'bake_samples':256,'atlas_denoise':'OpenImageDenoise compositor, linear irradiance','device':scene.cycles.device,'elapsed_seconds':round(time.time()-START),'blender':bpy.app.version_string}
report['bake_reused']=False
report['lightmap_repair_faces']=lightmap_repair_faces
report['balconies']={'eye_height':EYE_HEIGHT,'gallery_front_height':.80,'box_front_height':.75,'box_floors':BOX_FLOORS,'previous_gallery_front_height':1.225,'previous_box_front_height':1.31,'previous_box_floor':3.62}
export_anchored(report)
