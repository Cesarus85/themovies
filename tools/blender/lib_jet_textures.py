"""Original surface artwork and Blender-rendered sunset/cloud panoramas. CC0 wood source pinned in art/jet-sources.json."""
import math
import json
import hashlib
import subprocess
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector

def save(path,rgb):
    h,w=rgb.shape[:2];p=np.ones((h,w,4),np.float32);p[:,:,:3]=np.clip(rgb,0,1)
    im=bpy.data.images.new(path.stem,w,h,alpha=False);im.colorspace_settings.name='sRGB'
    im.pixels.foreach_set(p.ravel());im.filepath_raw=str(path);im.file_format='PNG';im.save();return im

def wood_source():
    root=Path(__file__).resolve().parents[2]
    entry=json.loads((root/'art/jet-sources.json').read_text())['dark_wood']
    assert entry['url'].startswith('https://dl.polyhaven.org/')
    path=root/'art/sources'/Path(entry['url']).name;path.parent.mkdir(parents=True,exist_ok=True)
    if not path.exists():subprocess.run(['curl','--fail','--location','--silent','--show-error',entry['url'],'-o',str(path)],check=True)
    assert hashlib.md5(path.read_bytes()).hexdigest()==entry['md5']
    image=bpy.data.images.load(str(path),check_existing=True);image.scale(1024,1024)
    rgb=np.empty(1024*1024*4,np.float32);image.pixels.foreach_get(rgb)
    return rgb.reshape(1024,1024,4)[:,:,:3]

def grade_cloud(rgb):
    lum=rgb@np.array((.2126,.7152,.0722))
    t=np.clip((lum-.13)/.52,0,1)**.65
    return np.array((.36,.39,.46))+t[:,:,None]*np.array((.59,.43,.21))

def texture(path,color,kind=''):
    n=1024;y,x=np.mgrid[0:n,0:n].astype(np.float32)/n
    noise=np.random.default_rng(747).uniform(-1,1,(n,n));rgb=np.ones((n,n,3),np.float32)*color
    grain=1+.018*noise
    if kind in ('wood','burl'):
        raw=wood_source();lum=raw@np.array((.2126,.7152,.0722))
        if kind=='burl':
            # Bookmatched veneer: mirror the real grain, with a warm finish.
            lum=np.concatenate((lum[:,:512],lum[:,:512][:,::-1]),axis=1)
        grain=np.clip(lum/max(lum.mean(),.001),.45,1.8)**.58
    if kind=='leather':grain+=.014*np.sin(x*1600)*np.sin(y*1593)
    if kind=='carpet':
        grain+=.04*np.sin(x*math.tau*210)+.04*np.sin(y*math.tau*220)
        # Small woven geometric lozenges, original and horizontally periodic.
        xx=(x*6)%1-.5;yy=(y*6)%1-.5;d=abs(xx)+abs(yy)
        rgb[abs(d-.32)<.018]=(.37,.30,.19)
        rgb[abs(d-.27)<.009]=(.23,.25,.27)
        rgb[(abs(xx)<.016)&(abs(yy)<.016)]=(.54,.41,.25)
    return save(path,rgb*grain[:,:,None])

def seamless(rgb,band=24):
    # Symmetric C1 seam guard. Keep the middle image, gradually converge both
    # edges to a common periodic value, not a sharp copy/paste strip.
    rgb=rgb.copy();edge=(rgb[:,0]+rgb[:,-1])*.5
    for j in range(band):
        t=j/(band-1);a=1-(t*t*(3-2*t))
        rgb[:,j]=rgb[:,j]*(1-a)+edge*a
        rgb[:,-1-j]=rgb[:,-1-j]*(1-a)+edge*a
    rgb[:,1]=rgb[:,0];rgb[:,-2]=rgb[:,-1]
    return rgb

def rendered_scenery(root,tex,configure):
    names=['Jet_Sky_Panorama_unlit_scroll2','Jet_Clouds_Panorama_unlit_scroll25']
    files=[tex/f'{n}_BaseColor.png' for n in names]
    if all(p.exists() for p in files):return [bpy.data.images.load(str(p),check_existing=True) for p in files]
    original=bpy.context.window.scene
    scene=bpy.data.scenes.new('Jet scenery rendering');bpy.context.window.scene=scene
    configure(scene);scene.cycles.samples=48
    scene.render.resolution_x=2048;scene.render.resolution_y=1024;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
    scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=0
    scene.world=bpy.data.worlds.new('Original high altitude sunset');scene.world.use_nodes=True
    nt=scene.world.node_tree;bg=nt.nodes.get('Background');sky=nt.nodes.new('ShaderNodeTexSky');sky.sky_type='MULTIPLE_SCATTERING';sky.sun_elevation=math.radians(4);sky.sun_rotation=math.radians(125);sky.altitude=8000;sky.air_density=.9;sky.aerosol_density=1.2;sky.sun_disc=False
    nt.links.new(sky.outputs[0],bg.inputs[0]);bg.inputs[1].default_value=.5
    data=bpy.data.cameras.new('Jet panorama');cam=bpy.data.objects.new('Jet panorama',data);scene.collection.objects.link(cam);scene.camera=cam
    data.type='PANO';data.panorama_type='EQUIRECTANGULAR';cam.rotation_euler=(math.pi/2,0,0)
    tmp=root/'Logs/art-preview/jet-sky-render.png';scene.render.filepath=str(tmp);bpy.ops.render.render(write_still=True)
    image=bpy.data.images.load(str(tmp));p=np.empty(2048*1024*4,np.float32);image.pixels.foreach_get(p);p=p.reshape(1024,2048,4)[:,:,:3]
    # Keep only sky, map an angular range to the far vertical scenery band.
    h,w=2048,4096;v=np.linspace(0,1,h);elev=np.arctan((-180+2980*v)/1600)
    rows=np.clip(((.5+elev/math.pi)*1023).astype(int),0,1023)
    cols=np.linspace(0,2047,w).astype(int);rgb=p[rows[:,None],cols[None,:]]
    # Warm, thin high-altitude horizon and soft sunset band (no terrestrial ground).
    blend=np.exp(-((elev+.015)/.105)**2)[:,None,None]*.26
    rgb=rgb*(1-blend)+np.array((.96,.57,.30))*blend
    save(files[0],seamless(rgb))
    # Cloud deck is a real Cycles participating-volume render. Periodic torus
    # noise across image X gives horizontal repetition without alpha cards.
    data.type='ORTHO';data.ortho_scale=1200;cam.location=(0,0,700);cam.rotation_euler=(0,0,0)
    scene.render.resolution_x=2048;scene.render.resolution_y=1024;scene.cycles.samples=64
    bpy.ops.mesh.primitive_cube_add(size=1,location=(0,0,75));cloud=bpy.context.object;cloud.name='Offline volumetric cloud field';cloud.scale=(1800,1100,150)
    m=bpy.data.materials.new('Offline clouds only');m.use_nodes=True;n=m.node_tree.nodes;l=m.node_tree.links;n.clear();out=n.new('ShaderNodeOutputMaterial');vol=n.new('ShaderNodeVolumePrincipled');vol.inputs['Color'].default_value=(.97,.98,1,1);vol.inputs['Anisotropy'].default_value=.2;l.new(vol.outputs[0],out.inputs['Volume']);cloud.data.materials.append(m)
    geo=n.new('ShaderNodeNewGeometry');sep=n.new('ShaderNodeSeparateXYZ');l.new(geo.outputs['Position'],sep.inputs[0])
    def mathnode(op,a,b=None):
        z=n.new('ShaderNodeMath');z.operation=op
        if hasattr(a,'node'):l.new(a,z.inputs[0])
        else:z.inputs[0].default_value=a
        if b is not None:
            if hasattr(b,'node'):l.new(b,z.inputs[1])
            else:z.inputs[1].default_value=b
        return z.outputs[0]
    angle=mathnode('MULTIPLY',sep.outputs['X'],math.tau/1200)
    combine=n.new('ShaderNodeCombineXYZ')
    l.new(mathnode('MULTIPLY',mathnode('SINE',angle),2.5),combine.inputs['X']);l.new(mathnode('MULTIPLY',mathnode('COSINE',angle),2.5),combine.inputs['Y']);l.new(mathnode('MULTIPLY',sep.outputs['Y'],1/100),combine.inputs['Z'])
    noise=n.new('ShaderNodeTexNoise');noise.noise_dimensions='4D';noise.inputs['Scale'].default_value=1;noise.inputs['Detail'].default_value=4;noise.inputs['Roughness'].default_value=.68
    l.new(combine.outputs[0],noise.inputs['Vector']);l.new(mathnode('MULTIPLY',sep.outputs['Z'],.008),noise.inputs['W'])
    density=mathnode('SUBTRACT',mathnode('MULTIPLY',noise.outputs['Fac'],2.1),mathnode('ADD',mathnode('MULTIPLY',sep.outputs['Z'],1/180),.25))
    l.new(mathnode('MULTIPLY',mathnode('MAXIMUM',density,0),.035),vol.inputs['Density'])
    bpy.ops.mesh.primitive_plane_add(size=1,location=(0,0,-1));floor=bpy.context.object;floor.scale=(1900,1200,1)
    base=bpy.data.materials.new('Offline cloud base');base.diffuse_color=(.85,.88,.94,1);floor.data.materials.append(base)
    sun_data=bpy.data.lights.new('Offline sunset','SUN');sun=bpy.data.objects.new('Offline sunset',sun_data);scene.collection.objects.link(sun);sun.rotation_euler=(math.radians(63),math.radians(-20),math.radians(-35));sun_data.energy=3;sun_data.color=(1,.72,.45);sun_data.angle=.12
    sky.sun_elevation=math.radians(9);bg.inputs[1].default_value=.8
    scene.render.filepath=str(root/'Logs/art-preview/jet-cloud-render.png');bpy.ops.render.render(write_still=True)
    image=bpy.data.images.load(scene.render.filepath);p=np.empty(2048*1024*4,np.float32);image.pixels.foreach_get(p);p=p.reshape(1024,2048,4)[:,:,:3]
    save(files[1],seamless(seamless(grade_cloud(p),32).transpose(1,0,2),32).transpose(1,0,2))
    bpy.context.window.scene=original
    for o in list(scene.objects):bpy.data.objects.remove(o,do_unlink=True)
    bpy.data.scenes.remove(scene)
    return [bpy.data.images.load(str(p),check_existing=True) for p in files]
