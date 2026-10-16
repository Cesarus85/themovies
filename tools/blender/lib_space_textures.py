"""NASA-derived Earth maps and deterministic original surface/star textures."""
import hashlib
import json
import subprocess
from pathlib import Path
import bpy
import numpy as np


def save(path,rgb):
    h,w=rgb.shape[:2];p=np.ones((h,w,4),np.float32);p[:,:,:3]=np.clip(rgb,0,1)
    im=bpy.data.images.new(path.stem,w,h,alpha=False);im.colorspace_settings.name='sRGB'
    im.pixels.foreach_set(p.ravel());im.filepath_raw=str(path);im.file_format='PNG';im.save();return im


def source(root,key):
    e=json.loads((root/'art/space-sources.json').read_text())[key]
    assert e['url'].startswith('https://eoimages.gsfc.nasa.gov/')
    path=root/'art/sources'/Path(e['url']).name;path.parent.mkdir(parents=True,exist_ok=True)
    if not path.exists():subprocess.run(['curl','--fail','--location','--silent','--show-error',e['url'],'-o',str(path)],check=True)
    assert hashlib.sha256(path.read_bytes()).hexdigest()==e['sha256'],key
    im=bpy.data.images.load(str(path));im.scale(4096,2048)
    p=np.empty(4096*2048*4,np.float32);im.pixels.foreach_get(p);bpy.data.images.remove(im)
    return p.reshape(2048,4096,4)[:,:,:3]


def earth(root,tex):
    land=source(root,'day');cloud=source(root,'clouds').mean(axis=2)[:,:,None]
    # A single albedo, no directional lighting or terminator baked into the map.
    day=land*(1-cloud*.89)+cloud*.94
    day_im=save(tex/'Earth_Planet_BaseColor.png',day)
    night=source(root,'night')
    # Preserve the geographic VIIRS light pattern; suppress the blue base map,
    # stars and sea background from NASA's presentation composite.
    strength=np.maximum(night[:,:,0]-night[:,:,2]*.65-.025,0)
    lights=np.stack([strength, strength*.79, strength*.47],axis=2)*1.65
    night_im=save(tex/'Earth_Planet_Night.png',lights)
    return day_im,night_im


def stars(path):
    h,w=2048,4096;rng=np.random.default_rng(5520)
    rgb=np.zeros((h,w,3),np.float32);rgb[:]=(.0018,.003,.007)
    # Uniform distribution on the sphere; 1-2 pixel cores, no blur/glow kernels.
    for k in range(10500):
        x=int(rng.uniform(0,w));v=np.arcsin(rng.uniform(-1,1))/np.pi+.5;y=min(h-1,int(v*h))
        power=float(rng.uniform(.18,.90)**1.3)
        color=np.array((.80,.88,1.)) if k%4 else np.array((1.,.87,.70))
        rgb[y,x]=color*power
        if k%95==0:
            rgb[y,(x+1)%w]=color*power*.45
            if y+1<h:rgb[y+1,x]=color*power*.30
    return save(path,rgb)


def texture(path,color,kind=''):
    n=1024;y,x=np.mgrid[0:n,0:n].astype(np.float32)/n
    noise=np.random.default_rng(805).uniform(-1,1,(n,n));rgb=np.ones((n,n,3),np.float32)*color
    grain=1+.025*noise
    if kind=='wood':grain+=.065*np.sin(x*351+.65*np.sin(y*11))+.025*np.sin(x*883+np.sin(y*36))
    if kind=='leather':grain+=.025*np.sin(x*1100)*np.sin(y*1124)
    if kind=='fabric':grain+=.06*np.sin(x*2*np.pi*240)+.05*np.sin(y*2*np.pi*248)
    if kind=='floor':
        joint=(x<.005)|(y<.005);rgb[joint]*=.72
    if kind=='display':
        rgb[:]=(.009,.026,.039)
        grid=((x*16)%1<.018)|((y*16)%1<.018);rgb[grid]=(.025,.09,.12)
        r=np.sqrt((x-.37)**2+(y-.56)**2)
        for a in [.19,.23,.27]:rgb[abs(r-a)<.0025]=(.14,.62,.72)
        rgb[(abs(y-(.45+.06*np.sin(x*24)))<.003)&(x>.06)&(x<.65)]=(.55,.82,.76)
        for j in range(10):
            rgb[(x>.74)&(x<.74+.18*(.3+.7*((j*7)%10)/10))&(abs(y-(.18+j*.063))<.012)]=(.21,.63,.69)
        rgb[(x>.08)&(x<.56)&(y>.10)&(y<.12)]=(.90,.57,.24)
    return save(path,rgb*grain[:,:,None])
