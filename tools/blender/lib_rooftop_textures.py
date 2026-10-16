"""Original opaque surface textures and a checksum-pinned CC0 360 degree HDRI."""
import hashlib
import json
import subprocess
from pathlib import Path
import bpy
import numpy as np


def panorama(root,path):
    entry=json.loads((root/'art/rooftop-sources.json').read_text())['clarens_night_02']
    assert entry['url'].startswith('https://dl.polyhaven.org/')
    source=root/'art/sources'/Path(entry['url']).name
    source.parent.mkdir(parents=True,exist_ok=True)
    if not source.exists():
        subprocess.run(['curl','--fail','--location','--silent','--show-error',entry['url'],'-o',str(source)],check=True)
    assert hashlib.md5(source.read_bytes()).hexdigest()==entry['md5']
    im=bpy.data.images.load(str(source));im.scale(4096,2048)
    pixels=np.empty(4096*2048*4,np.float32);im.pixels.foreach_get(pixels)
    rgb=np.maximum(pixels.reshape(2048,4096,4)[:,:,:3],0)
    # Preserve the entire photograph and its angular proportions. A soft shoulder
    # contains lamps; exposure keeps the movie brighter than the distant city.
    rgb=rgb*.025
    rgb=.68*rgb/(1+rgb)
    rgb=np.where(rgb<=.0031308,12.92*rgb,1.055*rgb**(1/2.4)-.055)
    bpy.data.images.remove(im)
    return save(path,rgb)


def save(path,rgb):
    h,w=rgb.shape[:2];p=np.ones((h,w,4),np.float32);p[:,:,:3]=np.clip(rgb,0,1)
    im=bpy.data.images.new(path.stem,w,h,alpha=False);im.colorspace_settings.name='sRGB'
    im.pixels.foreach_set(p.ravel());im.filepath_raw=str(path);im.file_format='PNG';im.save();return im


def texture(path,color,kind=''):
    n=1024;y,x=np.mgrid[0:n,0:n].astype(np.float32)/n
    noise=np.random.default_rng(815).uniform(-1,1,(n,n))
    rgb=np.ones((n,n,3))*color
    grain=1+.035*noise
    if kind=='wood':grain+=.024*np.sin(x*391+.4*np.sin(y*9))+.018*np.sin(x*913+np.sin(y*31))
    if kind in ('linen','stripe','rug'):
        grain+=.05*np.sin(x*2*np.pi*251)+.035*np.sin(y*2*np.pi*241)
    if kind=='stripe':
        stripe=(x*8)%1;rgb[stripe<.32]=(.69,.66,.55);rgb[(stripe>.34)&(stripe<.38)]=(.44,.31,.18)
    if kind=='rug':
        edge=np.minimum.reduce([x,1-x,y,1-y]);rgb[:]=(.45,.39,.29)
        rgb[(edge<.08)&(edge>.04)]=(.12,.22,.24)
        diamond=np.abs(((x*22)%1)-.5)+np.abs(((y*32)%1)-.5)
        rgb[(diamond<.10)&(edge>.11)]=(.22,.29,.27)
    if kind=='stone':grain+=.008*np.sin(x*35+np.sin(y*12))
    return save(path,rgb*grain[:,:,None])
