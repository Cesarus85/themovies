"""FBX-derived motion audit and 72/90 fps window clips; no Unity, no motion blur.
Blender -b --factory-startup -P tools/blender/verify_orient_express_motion.py
Renders the static baked interior once per side. Actual exported triangles/UVs
provide window masks; subsequent frames sample U+t*N/10000 with periodic bilinear
filtering. No temporal blending hides flicker. Videos are diagnostic, not a Quest
performance/comfort test. Analytic tests cover the full textures and both rates.
"""
import sys
sys.dont_write_bytecode=True
from pathlib import Path
import json,math,re,subprocess,shutil
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_orient_express_preview import preview,aim
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Assets/TheMovies/Art/OrientExpress';LOG=ROOT/'Logs/art-preview'
FFMPEG=shutil.which('ffmpeg') or '/opt/homebrew/bin/ffmpeg'
W,H=960,640

def decode(path):
    raw=subprocess.check_output([FFMPEG,'-v','error','-i',str(path),'-f','rawvideo','-pix_fmt','rgb24','-threads','1','-'])
    im=bpy.data.images.load(str(path),check_existing=True);w,h=im.size
    return np.frombuffer(raw,np.uint8).reshape(h,w,3).copy()


def sample(rgb,uv,time,speed):
    h,w=rgb.shape[:2];x=((uv[:,0]+time*speed)%1)*w-.5;y=((1-uv[:,1])%1)*h-.5
    ix=np.floor(x).astype(int);iy=np.floor(y).astype(int);fx=(x-ix)[:,None];fy=(y-iy)[:,None]
    return ((rgb[iy%h,ix%w]*(1-fx)+rgb[iy%h,(ix+1)%w]*fx)*(1-fy)+(rgb[(iy+1)%h,ix%w]*(1-fx)+rgb[(iy+1)%h,(ix+1)%w]*fx)*fy)

bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(OUT/'OrientExpress.fbx'))
scene=bpy.context.scene;scene.world=bpy.data.worlds.new('Preview world')
from lib_verify_room_motion import audit as audit_effects
audit_effects(scene,'OrientExpress',ROOT)
for m in bpy.data.materials:m['orient_express']=True
spectral=[];textures={};speeds={}
for o in scene.objects:
    if o.type!='MESH' or 'scroll' not in o.name:continue
    n=int(re.search(r'scroll(\d+)',o.name).group(1));speed=n/10000
    rgb=decode(OUT/'Textures'/f'{o.name}_BaseColor.png');textures[o.name]=rgb;speeds[o.name]=speed
    signal=rgb.astype(np.float64)/255;signal-=signal.mean(axis=1,keepdims=True)
    energy=np.sum(abs(np.fft.rfft(signal,axis=1))**2,axis=(0,2));freq=np.arange(len(energy))*speed
    rates={}
    for rate in [72,90]:
        fraction=float(energy[freq>rate/2].sum()/max(energy.sum(),1e-20))
        # RGB8 quantization creates a tiny broadband residual even after filtering.
        assert fraction<.005,(o.name,rate,'temporal alias energy',fraction)
        rates[str(rate)]={'above_nyquist_energy_fraction':fraction,'texels_per_frame':speed*rgb.shape[1]/rate}
    assert np.array_equal(rgb[:,0],rgb[:,-1]),(o.name,'seam')
    probes=np.array([[.11,.51],[.499,.73],[.93,.88]])
    assert np.max(abs(sample(rgb,probes,0,speed)-sample(rgb,probes,1/speed,speed)))<.001,'time wrap jump'
    spectral.append({'material':o.name,'u_per_second':speed,'rates':rates,'period_s':1/speed,'horizontal_edge_error':0})

def configure(s):
    s.render.engine='CYCLES';s.cycles.samples=8;s.cycles.use_denoising=True
    prefs=bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type='METAL';prefs.get_devices()
        for d in prefs.devices:d.use=d.type=='METAL'
        s.cycles.device='GPU'
    except Exception:s.cycles.device='CPU'
cam,dimmers=preview(ROOT,OUT/'Textures',configure,quick=True,setup_only=True)
scene.render.resolution_x=W;scene.render.resolution_y=H;scene.cycles.samples=8
vs=[];fs=[];tri_uv=[];owners=[]
for o in scene.objects:
    if o.type!='MESH':continue
    me=o.data;me.calc_loop_triangles();offset=len(vs);vs.extend(o.matrix_world@v.co for v in me.vertices)
    for t in me.loop_triangles:
        fs.append(tuple(offset+i for i in t.vertices));tri_uv.append([tuple(me.uv_layers['UVMap'].data[i].uv) for i in t.loops]);owners.append(o.name)
tree=BVHTree.FromPolygons(vs,fs,all_triangles=True);xyz=np.array([tuple(v) for v in vs]);tri_xyz=xyz[np.array(fs)];tri_uv=np.array(tri_uv)
clips=[]
for side in [-1,1]:
    label='left' if side<0 else 'right';cam.location=(-.375,0,1.25);aim(cam,(side*5,1,1.10));cam.data.lens=18;cam.data.clip_end=5000
    bpy.context.view_layer.update()
    scene.render.filepath=str(LOG/f'orient-express-motion-{label}-base.png');bpy.ops.render.render(write_still=True)
    base=decode(Path(scene.render.filepath));groups={name:([],[]) for name in textures}
    tangent=cam.data.sensor_width/(2*cam.data.lens);basis=cam.matrix_world.to_3x3();origin=cam.matrix_world.translation
    for iy in range(H):
        yy=(1-2*(iy+.5)/H)*tangent*H/W
        for ix in range(W):
            direction=basis@Vector(((2*(ix+.5)/W-1)*tangent,yy,-1));direction.normalize()
            point,normal,index,distance=tree.ray_cast(origin,direction,5000)
            if point is None or owners[index] not in groups:continue
            a,b,c=tri_xyz[index];e=b-a;f=c-a;p=np.asarray(point)-a;ee=e@e;ff=f@f;ef=e@f;pe=p@e;pf=p@f;det=ee*ff-ef*ef
            vv=(ff*pe-ef*pf)/det;ww=(ee*pf-ef*pe)/det
            uv=tri_uv[index,0]*(1-vv-ww)+tri_uv[index,1]*vv+tri_uv[index,2]*ww
            ids,coords=groups[owners[index]];ids.append(iy*W+ix);coords.append(uv)
    groups={name:(np.asarray(ids),np.asarray(uv)) for name,(ids,uv) in groups.items() if ids}
    assert len(groups)==3,('all three layers visible',label,{name:len(g[0]) for name,g in groups.items()})
    for rate in [72,90]:
        dest=LOG/f'orient-express-motion-{label}-{rate}fps.mp4'
        command=[FFMPEG,'-y','-v','error','-f','rawvideo','-pix_fmt','rgb24','-s',f'{W}x{H}','-r',str(rate),'-i','-','-an','-c:v','libx264','-preset','fast','-crf','18','-pix_fmt','yuv420p','-threads','2',str(dest)]
        proc=subprocess.Popen(command,stdin=subprocess.PIPE);previous=None;deltas=[];layer_max={name:0. for name in groups};layer_mean={name:[] for name in groups}
        for frame in range(rate*4):
            rgb=base.copy().reshape(-1,3)
            for name,(ids,uv) in groups.items():rgb[ids]=np.clip(np.rint(sample(textures[name],uv,frame/rate,speeds[name])),0,255).astype(np.uint8)
            if previous is not None:
                delta=abs(rgb.astype(np.float32)-previous)/255;deltas.append(float(np.mean(delta)))
                for name,(ids,uv) in groups.items():
                    layer_max[name]=max(layer_max[name],float(delta[ids].max()))
                    layer_mean[name].append(float(delta[ids].mean()))
            previous=rgb.astype(np.float32);proc.stdin.write(rgb.tobytes())
            if rate==90 and frame in [0,30,60]:
                # Store readable still samples without an extra codec dependency.
                png=LOG/f'orient-express-motion-{label}-t{frame:03d}.png'
                subprocess.run([FFMPEG,'-y','-v','error','-f','rawvideo','-pix_fmt','rgb24','-s',f'{W}x{H}','-i','-','-frames:v','1',str(png)],input=rgb.tobytes(),check=True)
        proc.stdin.close();assert proc.wait()==0
        assert all(v<.15 for v in layer_max.values()),('large local frame flash',layer_max)
        assert max(deltas)<.05,('large frame flash',label,rate,max(deltas))
        clips.append({'file':dest.name,'fps':rate,'frames':rate*4,'seconds':4,'mean_frame_delta':float(np.mean(deltas)),'maximum_frame_delta':max(deltas),'layer_maximum_channel_step':layer_max,'layer_mean_absolute_step':{name:float(np.mean(values)) for name,values in layer_mean.items()},'visible_pixels':{name:len(g[0]) for name,g in groups.items()}})
        print('MOTION CLIP',dest.name,'PASS',flush=True)
report={'validation':'PASS','method':'Actual FBX ray hits and UVs; bilinear repeat; no motion blur, temporal AA or frame blending','spectral':spectral,'clips':clips,'limits':'Offline 72/90 Hz sampling; not a Quest GPU performance, compositor, comfort or runtime shader test.'}
(LOG/'orient-express-motion-verification.json').write_text(json.dumps(report,indent=2)+'\n')
from lib_orient_express_motion import write_motion_report
write_motion_report(ROOT,report)
print('ORIENT EXPRESS MOTION PASS',flush=True)
