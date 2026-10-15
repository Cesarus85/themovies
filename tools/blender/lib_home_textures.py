"""Deterministic original textile/decor atlases and checksum-locked CC0 wood inputs."""
import hashlib
import json
import math
import subprocess
from pathlib import Path
import bpy
import numpy as np


def fetch(root, asset):
    sources = root / 'art/sources'
    sources.mkdir(parents=True, exist_ok=True)
    entry = json.loads((root / 'art/home-sources.json').read_text())[asset]
    assert entry['url'].startswith('https://dl.polyhaven.org/')
    dest = sources / Path(entry['url']).name
    if not dest.exists():
        subprocess.run(['curl', '--fail', '--location', '--silent', '--show-error', entry['url'], '-o', str(dest)], check=True)
    assert hashlib.md5(dest.read_bytes()).hexdigest() == entry['md5'], dest
    return dest


def make_texture(name, path, color, kind='', source=None):
    n = 2048 if kind == 'rug' else 1024
    y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
    rng = np.random.default_rng(739)
    grain = rng.uniform(-1, 1, (n, n)).astype(np.float32)
    rgb = np.ones((n, n, 3), np.float32) * color
    rgb *= (1 + grain * .035)[:, :, None]
    if source:
        im = bpy.data.images.load(str(source)); im.scale(n, n)
        pixels = np.empty(n*n*4, np.float32); im.pixels.foreach_get(pixels)
        rgb = pixels.reshape(n, n, 4)[:, :, :3] * np.array(color)
        bpy.data.images.remove(im)
    if kind == 'wood' and source:
        lum=rgb.mean(axis=2);rgb=np.clip(lum/max(float(lum.mean()),.01),.45,1.65)[:,:,None]*np.array(color)
    if kind == 'wood' and not source:
        # Continuous walnut grain for molding, furniture and shelf edges.
        bands = np.sin(x*90 + 3*np.sin(y*7) + 2*np.sin(x*19+y*10))
        fine = np.sin(x*730 + 4*np.sin(y*12))
        rgb *= (1 + .13*bands + .035*fine)[:, :, None]
    if kind == 'velvet':
        rgb *= (1 + .055*np.sin(x*math.tau*440)*np.sin(y*math.tau*440) + .06*np.sin(x*14+y*19))[:, :, None]
    if kind == 'stone':
        veins = np.exp(-(np.sin(x*14+y*7+1.3*np.sin(y*11))/.075)**2)
        rgb *= (1-.18*veins + .06*np.sin(y*55+x*6))[:, :, None]
    if kind == 'linen':
        rgb *= (.9 + .06*np.sin(x*math.tau*160) + .04*np.sin(y*math.tau*160))[:, :, None]
    if kind == 'rug':
        # Original Persian-inspired Heriz medallion, palmettes and guard borders.
        u=2*x-1; v=2*y-1; edge=np.minimum(1-abs(u),1-abs(v))
        rgb[:]=[.32,.065,.044]
        rgb[edge<.15]=[.075,.14,.135]
        for e in [.018,.038,.145,.17,.205]:
            rgb[abs(edge-e)<.005]=[.73,.52,.25]
        border=(edge>.05)&(edge<.132)
        angle=np.where(abs(u)>abs(v),v,u)*40
        arabesque=(np.sin(angle*2)**2+(edge-.092)**2/.001<.6)&border
        rgb[arabesque]=[.71,.38,.17]
        rgb[border & (np.cos(angle*2)>.94)]=[.76,.65,.40]
        diamond=abs(u)*1.05+abs(v)*.85
        rgb[(diamond<.60)&(diamond>.53)]=[.78,.58,.31]
        rgb[diamond<.53]=[.065,.16,.16]
        radius=np.sqrt((u*1.5)**2+(v*1.15)**2);theta=np.arctan2(v*1.15,u*1.5)
        star=radius<(.28+.035*np.cos(theta*12))
        rgb[star]=[.74,.49,.22]
        rgb[radius<(.17+.02*np.cos(theta*8))]=[.37,.075,.045]
        rgb[radius<.06]=[.77,.65,.39]
        # Repeating small leaves and rosettes in the field, handwoven irregularity.
        a=(x*24)%1-.5;b=(y*32)%1-.5
        rr=np.sqrt(a*a+b*b);aa=np.arctan2(b,a)
        petal=(rr<.18+.06*np.cos(aa*6))&(diamond>.68)&(edge>.22)
        rgb[petal]=[.66,.43,.22]
        rgb[(rr<.06)&(diamond>.68)&(edge>.22)]=[.16,.25,.22]
        stem=(abs(a+.12*np.sin(b*7))<.022)&(diamond>.68)&(edge>.22)
        rgb[stem]=[.11,.23,.20]
        rgb *= (1+.09*grain+.055*np.sin(x*math.tau*760))[:,:,None]
    if kind == 'books':
        # Eight bindings (left 75%), a landscape painting (upper right), ceramic glaze.
        colors=np.array([[.29,.064,.036],[.14,.24,.19],[.12,.16,.23],[.45,.28,.12],
                         [.40,.14,.08],[.29,.23,.12],[.13,.22,.24],[.52,.39,.23]])
        ix=np.minimum((x/.75*8).astype(int),7);rgb=colors[ix]*(1+.06*grain)[:,:,None]
        local=(x/.75*8)%1
        gold=(abs(y-.12)<.006)|(abs(y-.17)<.006)|(abs(y-.83)<.006)|(abs(y-.88)<.006)
        frame=((abs(local-.13)<.010)|(abs(local-.87)<.010))&(y>.22)&(y<.77)
        label=(local>.28)&(local<.72)&(y>.56)&(y<.65)
        letter=label&(np.sin(y*410)>.2)&(np.sin(local*37+y*15)>.0)
        rgb[(gold|frame|letter)&(x<.75)]=[.50,.36,.17]
        # Original atmospheric landscape: layered hills, irregular firs and reflected sunset.
        region=(x>=.75)&(y>.50);u=(x-.75)*4;v=(y-.5)*2
        haze=np.exp(-((v-.46)/.25)**2)
        landscape=np.stack((.29+.23*haze,.35+.18*haze,.36+.09*haze),axis=2)
        for level,tint in [(0,[.32,.37,.34]),(1,[.22,.30,.27]),(2,[.14,.23,.20])]:
            contour=.36+.055*level+.075*np.sin(u*(8+level*2)+level*2)+.035*np.sin(u*23+level)+.009*np.sin(u*139)
            mountain=v<contour
            landscape[mountain]=tint
        lake=v<.29;landscape[lake]=[.34,.39,.33]
        ripple=(.02*np.sin(v*680+np.sin(u*71)*3)+.013*np.sin(v*1200+u*180))
        landscape[lake]+=ripple[lake,None]
        sun=((u-.70)*1.8)**2+(v-.76)**2<.0017;landscape[sun]=[.78,.70,.51]
        for tx in [.04,.10,.15,.21,.84,.90,.96]:
            top=.38+.18*(.5+.5*math.sin(tx*63));base=.20
            taper=np.clip((top-v)/(top-base),0,1)
            branches=(.60+.40*np.cos((v-top)*125))
            tree=(abs(u-tx)<.038*taper*branches)&(v>base)&(v<top)
            landscape[tree]=[.065,.13,.115]
            trunk=(abs(u-tx)<.0025)&(v>.18)&(v<top)
            landscape[trunk]=[.075,.13,.115]
        landscape*= (1+.06*grain+.015*np.sin(y*900))[:,:,None]
        rgb[region]=landscape[region]
        rgb[(x>=.75)&(y<=.50)]=[.20,.36,.32]
    if kind == 'night':
        rgb[:]=[.027,.045,.095]
        rgb += (y*y)[:,:,None]*np.array([.035,.025,.025])
        for i in range(38):
            left=i/38; height=rng.uniform(.12,.48)
            building=(x>left)&(x<left+.028)&(y<height)
            rgb[building]=[.022,.029,.046]
            windows=building&(((x-left)*900)%5<1.3)&((y*200)%5<1.5)
            rgb[windows]=np.array([.72,.46,.19])*rng.uniform(.35,1)
        for i in range(100):
            sx,sy=rng.uniform(0,1),rng.uniform(.5,1)
            rgb[(x-sx)**2+(y-sy)**2<rng.uniform(.4,1.8)/n**2]=[.34,.42,.56]
        moon=(x-.72)**2+(y-.77)**2<.0009;rgb[moon]=[.55,.60,.64]
    if kind == 'embers':
        hot=(np.sin(x*26+np.sin(y*18))*np.sin(y*31+x*5))**2
        rgb=np.stack((.45+.5*hot,.035+.29*hot,.008+.028*hot),axis=2)
    rgba=np.ones((n,n,4),np.float32);rgba[:,:,:3]=np.clip(rgb,0,1)
    im=bpy.data.images.new(name,n,n,alpha=False);im.colorspace_settings.name='sRGB'
    im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(path);im.file_format='PNG';im.save()
    return im
