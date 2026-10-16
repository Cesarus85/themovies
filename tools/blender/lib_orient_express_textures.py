"""Original salon surface artwork; pinned CC0 HDRI and opaque periodic scenery."""
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


_PHOTO_CACHE={}
def photographic_grain(asset):
    if asset in _PHOTO_CACHE:return _PHOTO_CACHE[asset]
    root=Path(__file__).resolve().parents[2]
    entry=json.loads((root/'art/orient-express-sources.json').read_text())[asset]
    assert entry['url'].startswith('https://dl.polyhaven.org/')
    source=root/'art/sources'/Path(entry['url']).name;source.parent.mkdir(parents=True,exist_ok=True)
    if not source.exists():subprocess.run(['curl','--fail','--location','--silent','--show-error',entry['url'],'-o',str(source)],check=True)
    assert hashlib.md5(source.read_bytes()).hexdigest()==entry['md5']
    im=bpy.data.images.load(str(source));im.scale(1024,1024)
    pixels=np.empty(1024*1024*4,np.float32);im.pixels.foreach_get(pixels)
    lum=pixels.reshape(1024,1024,4)[:,:,:3].mean(axis=2)
    grain=np.clip(lum/max(float(lum.mean()),.001),.45,1.65)**(.7 if asset=='dark_wood' else .55)
    bpy.data.images.remove(im);_PHOTO_CACHE[asset]=grain;return grain


def texture(path,color,kind=''):
    n=1024;y,x=np.mgrid[0:n,0:n].astype(np.float32)/(n-1)
    rng=np.random.default_rng(1928);noise=rng.uniform(-1,1,(n,n));rgb=np.ones((n,n,3))*color
    grain=1+.022*noise
    if kind in ('wood','inlay'):rgb*=photographic_grain('dark_wood')[:,:,None]
    if kind in ('velvet','curtain'):rgb*=photographic_grain('velour_velvet')[:,:,None]
    if kind in ('wood','inlay'):
        grain+=.025*np.sin(x*290+2*np.sin(y*11))+.018*np.sin(x*670+3*np.sin(y*7))
        grain+=.035*np.sin(x*22+2*np.sin(y*5))
    if kind in ('velvet','curtain','carpet'):
        grain+=.025*np.sin(x*1600)+.025*np.sin(y*1580)
    if kind=='brass':grain+=.10*np.cos(x*6.28)+.10*np.cos(y*12.56)
    if kind in ('inlay','glass'):
        # Original symmetric botanical marquetry: leaf sprays and stylised iris.
        edge=np.minimum.reduce([x,1-x,y,1-y]);gold=(.68,.48,.23) if kind=='inlay' else (.52,.57,.48)
        mask=((edge>.035)&(edge<.043))|((edge>.065)&(edge<.068))
        for side in [-1,1]:
            stem=.5+side*(.02+.21*np.sin(np.clip((y-.15)/.7,0,1)*np.pi*.6))
            mask|=(abs(x-stem)<.004)&(y>.15)&(y<.84)
            for cy in [.28,.43,.58,.72]:
                cx=.5+side*(.02+.21*np.sin((cy-.15)/.7*np.pi*.6))
                dx=x-cx;dy=y-cy
                a=dx*side*.7+dy*.7;b=-dx*side*.7+dy*.7
                mask|=(a/.115)**2+(b/.027)**2<1
        for cx in [.41,.5,.59]:mask|=((x-cx)/.055)**2+((y-.84)/(.08 if cx==.5 else .05))**2<1
        mask|=(abs(x-.5)<.004)&(y>.12)&(y<.81)
        rgb[mask]=gold
    if kind=='carpet':
        edge=np.minimum(x,1-x);rgb[:]=(.12,.19,.22)
        rgb[(edge>.055)&(edge<.125)]=(.42,.21,.16)
        line=((edge>.047)&(edge<.053))|((edge>.132)&(edge<.138));rgb[line]=(.70,.51,.27)
        xx=(x*8)%1-.5;yy=(y*34)%1-.5;diamond=abs(xx)+abs(yy)
        rgb[(abs(diamond-.34)<.027)&(edge>.16)]=(.53,.39,.22)
        rgb[(diamond<.08)&(edge>.16)]=(.62,.28,.18)
        rgb[(abs(xx)<.018)&(abs(yy)<.2)&(edge>.16)]=(.68,.48,.25)
    if kind=='shade':grain+=.075*np.cos(x*2*np.pi*32)
    return save(path,rgb*grain[:,:,None])


def landscapes(root,tex):
    entry=json.loads((root/'art/orient-express-sources.json').read_text())['clarens_night_02']
    assert entry['url'].startswith('https://dl.polyhaven.org/')
    source=root/'art/sources'/Path(entry['url']).name;source.parent.mkdir(parents=True,exist_ok=True)
    if not source.exists():subprocess.run(['curl','--fail','--location','--silent','--show-error',entry['url'],'-o',str(source)],check=True)
    assert hashlib.md5(source.read_bytes()).hexdigest()==entry['md5']
    im=bpy.data.images.load(str(source));im.scale(4096,2048)
    p=np.empty(4096*2048*4,np.float32);im.pixels.foreach_get(p);p=p.reshape(2048,4096,4)[:,:,:3]
    # Full 360 degree longitude. Cylindrical reprojection of elevations -24..62°,
    # matching the 60m distant strip, retains photographed hills and village lights.
    z=np.linspace(-26,110,2048);lat=np.arctan2(z-1.25,60)
    rows=np.clip((.5+lat/np.pi)*2047,0,2047);lo=rows.astype(int);f=(rows-lo)[:,None,None]
    rgb=p[lo]*(1-f)+p[np.minimum(lo+1,2047)]*f
    rgb=np.maximum(rgb,0)*.075;rgb=.78*rgb/(1+rgb)
    rgb=np.where(rgb<=.0031308,12.92*rgb,1.055*rgb**(1/2.4)-.055)
    # Restrained blue-hour grade, no borrowed/synthetic replacement sky.
    rgb*=np.array([.86,.98,1.08]);rgb=np.clip(rgb,0,1)
    # Small periodic edge blend, identical first/last columns, no visible wrap step.
    for k in range(32):
        w=(1-k/32)**2*.5;a=rgb[:,k].copy();b=rgb[:,-1-k].copy()
        rgb[:,k]=a*(1-w)+b*w;rgb[:,-1-k]=b*(1-w)+a*w
    from lib_orient_express_motion import LAYERS
    far=save(tex/f'{LAYERS["Far"]["name"]}_BaseColor.png',rgb)
    bpy.data.images.remove(im)
    # Opaque ground band: geometry stops at the horizon. Grass, hedge, fence and
    # ballast are painted INTO an opaque RGB band, never alpha billboards.
    h,w=1024,4096;y,x=np.mgrid[0:h,0:w].astype(np.float32);u=x/(w-1);v=y/(h-1)
    noise=np.random.default_rng(1930).uniform(-1,1,(h,w));rgb=np.zeros((h,w,3))
    rgb[:]=(.105,.15,.16);rgb+=(noise*.025)[:,:,None]
    grass=v>.32;rgb[grass]=np.stack([.12+.025*noise,.19+.03*noise,.16+.02*noise],axis=-1)[grass]
    ballast=v<.22;rgb[ballast]=np.stack([.18+.065*noise,.20+.065*noise,.23+.065*noise],axis=-1)[ballast]
    rail=(abs(v-.19)<.006)|(abs(v-.075)<.005);rgb[rail]=(.29,.31,.32)
    sleepers=(np.cos(u*2*np.pi*8)>.65)&(v<.24);rgb[sleepers]=(.08,.075,.065)
    hedge=.63+.045*np.sin(u*2*np.pi*9)+.026*np.cos(u*2*np.pi*23)
    bush=(v>hedge)&(v<.88+.03*np.sin(u*2*np.pi*7));rgb[bush]=(.075,.115,.115)
    fence=((np.cos(u*2*np.pi*6)>.82)&(v>.57)&(v<.96))|((abs(v-.73)<.008)|(abs(v-.87)<.008))
    rgb[fence]=(.28,.28,.23)
    # Fine periodic multi-scale foliage/soil modulation avoids flat paper shapes.
    detail=np.zeros((h,w),np.float32)
    for frequency,amplitude in [(7,.06),(29,.055),(83,.045),(211,.03),(503,.018)]:
        detail+=amplitude*np.sin(u*2*np.pi*frequency+.9*np.sin(v*frequency*.31))*np.cos(v*frequency*.59)
    rgb*=1+detail[:,:,None];rgb+=noise[:,:,None]*.018
    # Ground-top hue blends into distant field; a straight opaque top is horizon.
    mix=np.clip((v-.92)/.08,0,1)[:,:,None];rgb=rgb*(1-mix)+np.array([.115,.16,.16])*mix
    # The deep opaque skirt samples V=0. Keep this row uniform so grazing
    # downward views show ground, not infinitely stretched sleeper stripes.
    rgb[0,:,:]=(.12,.14,.145)
    rgb[:,-1]=rgb[:,0]
    rgb=motion_filter(rgb,12,18)
    near=save(tex/f'{LAYERS["Near"]["name"]}_BaseColor.png',rgb)
    middle=middle_landscape(tex/f'{LAYERS["Middle"]["name"]}_BaseColor.png')
    return {'Far':far,'Middle':middle,'Near':near}


def motion_filter(rgb,start,stop):
    """Periodic horizontal band limit, no runtime blur and no alpha.
    Near: 18 cycles/tile *0.9002 tile/s =16.2036 Hz <72/2 Hz.
    A cosine shoulder avoids the ringing of a hard FFT cutoff.
    """
    frequency=np.fft.rfftfreq(rgb.shape[1])*rgb.shape[1]
    t=np.clip((frequency-start)/(stop-start),0,1)
    gain=.5+.5*np.cos(np.pi*t)
    rgb=np.fft.irfft(np.fft.rfft(rgb,axis=1)*gain[None,:,None],n=rgb.shape[1],axis=1)
    rgb=np.clip(rgb,0,1);edge=(rgb[:,0]+rgb[:,-1])*.5;rgb[:,0]=edge;rgb[:,-1]=edge
    return rgb


def middle_landscape(path):
    """Original opaque field panorama. Straight horizontal field boundary at top.
    Hedges/trees/houses are painted wholly BELOW it, so every silhouette scrolls
    with its own texture. No stationary tree-shaped polygon edge or sky cutout.
    """
    h,w=1024,4096;y,x=np.mgrid[0:h,0:w].astype(np.float32);u=x/(w-1);v=y/(h-1)
    rgb=np.ones((h,w,3))*np.array([.105,.15,.13])
    grain=.018*np.sin(u*2*np.pi*29+v*18)+.012*np.sin(u*2*np.pi*83+v*47)
    rgb*=1+grain[:,:,None]
    for bottom,top,color in [(0,.35,[.10,.145,.13]),(.35,.62,[.13,.17,.14]),(.62,.84,[.095,.14,.12])]:
        edge=top+.016*np.sin(u*2*np.pi*5)+.012*np.cos(u*2*np.pi*11)
        rgb[(v>=bottom)&(v<edge)]=color
    # Soft, discontinuous hedgerows and individually rounded tree crowns.
    rng=np.random.default_rng(192870)
    for i in range(75):
        cx=float(rng.uniform(0,1));cy=float(rng.uniform(.77,.87));r=float(rng.uniform(.003,.008))
        dx=(u-cx+.5)%1-.5
        crown=(dx/r)**2+((v-cy)/(.035+r*2))**2<1
        rgb[crown]=np.array([.055,.10,.085])+rng.uniform(-.008,.008)
        trunk=(abs(dx)<r*.12)&(v<cy)&(v>cy-.065);rgb[trunk]=(.085,.09,.075)
    for cx in [.06,.19,.38,.56,.73,.91]:
        dx=(u-cx+.5)%1-.5;base=.78
        wall=(abs(dx)<.010)&(v>base)&(v<base+.05);rgb[wall]=(.25,.24,.19)
        roof=(v>=base+.05)&(v<base+.08-abs(dx)*2.2)&(abs(dx)<.014);rgb[roof]=(.11,.10,.095)
        for wx in [-.005,.004]:
            lamp=(abs(dx-wx)<.0011)&(abs(v-base-.025)<.009);rgb[lamp]=(.75,.51,.23)
    # Quiet top is a distant straight field edge, not a fake fixed tree silhouette.
    blend=np.clip((v-.91)/.09,0,1)[:,:,None]
    rgb=rgb*(1-blend)+np.array([.10,.14,.125])*blend
    return save(path,motion_filter(rgb,250,400))
