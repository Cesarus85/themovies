"""Independent OrientExpress FBX audit. No Unity; no accepted-room mutations.
Blender -b --factory-startup -P tools/blender/verify_orient_express.py
--draft validates the ignored draft checkpoint's geometry before the first bake.
"""
import sys
sys.dont_write_bytecode=True
import json
import math
import hashlib
import struct
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_orient_express_uvs import uv_overlaps,audit_padding
from lib_seated_head_clearance import audit as audit_heads
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Assets/TheMovies/Art/OrientExpress';LOG=ROOT/'Logs/art-preview';LOG.mkdir(parents=True,exist_ok=True)
draft='--draft' in sys.argv
bpy.ops.wm.read_factory_settings(use_empty=True)
if draft:bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/blender/OrientExpress-draft.blend'))
else:bpy.ops.import_scene.fbx(filepath=str(OUT/'OrientExpress.fbx'))
scene=bpy.context.scene;bpy.context.view_layer.update()
objects=[o for o in scene.objects if o.type=='MESH'];interior=[o for o in objects if 'unlit' not in o.name]
def tree(objs):
    vs=[];fs=[]
    for o in objs:
        o.data.calc_loop_triangles();offset=len(vs);vs.extend(o.matrix_world@v.co for v in o.data.vertices);fs.extend(tuple(offset+i for i in t.vertices) for t in o.data.loop_triangles)
    return BVHTree.FromPolygons(vs,fs,all_triangles=True)
# Independent coordinate specification, not imported from the builder.
seats=[(-1.125,0,0),(-.375,0,0),(.375,0,0),(1.125,0,0),(-.38,-3.1,0),(.38,-3.1,0),(-.83,-6.2,0),(.83,-6.2,0),(-.83,2.65,0),(.83,2.65,0)]
expected={f'Spot_{i:03d}':p for i,p in enumerate(seats)};expected.update({f'Seat{i+1}':seats[i] for i in range(4)})
expected.update(ScreenAnchor=(0,6.65,.9),ScreenTop=(0,6.65,2.4))
for name,p in expected.items():assert (scene.objects[name].matrix_world.translation-Vector(p)).length<1e-5,(name,'position')
for i,a in enumerate(seats):
    for b in seats[i+1:]:assert (Vector(a)-Vector(b)).length>=.4
bvh=tree(interior);screen_rays=0
for i,p in enumerate(seats):
    origin=Vector(p)+Vector((0,0,1.25))
    for x in np.linspace(-1.3,1.3,39):
        for z in np.linspace(.9,2.4,17):
            target=Vector((float(x),6.65,float(z)));delta=target-origin
            hit=bvh.ray_cast(origin,delta.normalized(),delta.length-.001)
            assert hit[0] is None,(i,'screen blocked',float(x),float(z),list(hit[0]) if hit[0] else None)
            screen_rays+=1
for x in np.linspace(-1.3,1.3,39):
    for z in np.linspace(.9,2.4,17):assert bvh.ray_cast(Vector((x,6.61,z)),Vector((0,1,0)),.08)[0] is None,'Screen volume occupied'
if draft:
    print('DRAFT geometry sightlines PASS:',screen_rays);sys.exit(0)
assert set(o.type for o in scene.objects)<={'EMPTY','MESH'}
assert {o.name for o in scene.objects if o.type=='EMPTY'}==set(expected)
heads=audit_heads(objects,scene.objects);(LOG/'orient-express-head-clearance.json').write_text(json.dumps(heads,indent=2)+'\n');assert heads['pass'],heads['failed_markers']
assert len(objects)<=14 and all(len(o.data.materials)==1 for o in objects)
materials={o.data.materials[0].name for o in objects};assert len(materials)<=14
stats={};tris=[];world_tris=[];special=[]
for o in objects:
    me=o.data;me.calc_loop_triangles();stats[o.name]=len(me.loop_triangles)
    assert [u.name for u in me.uv_layers]==['UVMap','Lightmap'],(o.name,'UV layers')
    assert me.has_custom_normals,(o.name,'explicit normals')
    lm=me.uv_layers['Lightmap'];tris.extend([[tuple(lm.data[i].uv) for i in t.loops] for t in me.loop_triangles])
    world_tris.extend([[tuple(o.matrix_world@me.vertices[i].co) for i in t.vertices] for t in me.loop_triangles]);special.extend(['unlit' in o.name]*len(me.loop_triangles))
assert sum(stats.values())<=120000
tris=np.asarray(tris,np.float64);world_tris=np.asarray(world_tris);special=np.asarray(special)
assert tris.min()>=0 and tris.max()<=1
ab=tris[:,1]-tris[:,0];ac=tris[:,2]-tris[:,0];areas=np.abs(ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0])/2
assert areas.min()>1e-14,('degenerate UVs',areas.min())
overlap=uv_overlaps(tris);assert not overlap,('UV intersections',len(overlap))
padding=audit_padding(tris);assert padding['pass'],padding
# Independent raster occupancy check and diffuse atlas statistics.
N=2048;count=np.zeros((N,N),np.uint16)
for pts in tris[~special]*N:
    mn=np.maximum(np.floor(pts.min(axis=0)-.5).astype(int),0);mx=np.minimum(np.ceil(pts.max(axis=0)-.5).astype(int),N-1)
    if np.any(mx<mn):continue
    a,b,c=pts;e=b-a;f=c-a;det=e[0]*f[1]-e[1]*f[0]
    if abs(det)<1e-12:continue
    yy,xx=np.mgrid[mn[1]:mx[1]+1,mn[0]:mx[0]+1];px=xx+.5-a[0];py=yy+.5-a[1]
    u=(px*f[1]-py*f[0])/det;v=(e[0]*py-e[1]*px)/det
    count[mn[1]:mx[1]+1,mn[0]:mx[0]+1]+=((u>1e-6)&(v>1e-6)&(u+v<1-1e-6)).astype(np.uint16)
assert not np.any(count>1),'Atlas pixel overlap'
print('UV coverage',float((count>0).mean()),'padding',padding,flush=True)
texture_info={};files={f'{m}_BaseColor.png' for m in materials}|{'OrientExpress_Light.png'}
assert {p.name for p in (OUT/'Textures').glob('*.png')}==files
for p in sorted((OUT/'Textures').glob('*.png')):
    raw=p.read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n'
    w,h,bits,mode=struct.unpack('>IIBB',raw[16:26]);assert bits==8 and mode==2,(p.name,'RGB8')
    if 'Panorama' in p.name:assert w<=4096 and h<=2048
    elif p.name=='OrientExpress_Light.png':assert (w,h)==(2048,2048)
    else:assert w<=2048 and h<=2048
    texture_info[p.name]={'dimensions':[w,h],'bits':bits,'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest()}
for m in (bpy.data.materials[n] for n in materials):
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');assert bs.inputs['Base Color'].is_linked
    tex=bs.inputs['Base Color'].links[0].from_node;assert tex.type=='TEX_IMAGE' and Path(tex.image.filepath).name==f'{m.name}_BaseColor.png'
    assert not bs.inputs['Alpha'].is_linked and bs.inputs['Alpha'].default_value==1
    if bs.inputs['Normal'].is_linked:
        normal=bs.inputs['Normal'].links[0].from_node;assert normal.type=='NORMAL_MAP' and not normal.inputs['Color'].is_linked
atlas=bpy.data.images.load(str(OUT/'Textures/OrientExpress_Light.png'));pix=np.empty(N*N*4,np.float32);atlas.pixels.foreach_get(pix);pix=pix.reshape(N,N,4)
covered=pix[count>0,:3];atlas_stats={'covered_fraction':float((count>0).mean()),'clipped_fraction':float(np.any(covered>=1,axis=1).mean()),'percentiles':np.percentile(covered,[1,50,95,99,100],axis=0).tolist()}
assert atlas_stats['clipped_fraction']<.01,atlas_stats

# Sample actual first-hit atlas values, including walls, furniture and ceiling.
alltree=tree(objects);sample_colors=[];black_hits=[];misses=0
triangle_owners=[o.name for o in objects for t in o.data.loop_triangles]
for seat in seats:
    origin=Vector(seat)+Vector((0,0,1.25))
    for elevation in np.linspace(-1.45,1.45,21):
        for angle in np.linspace(0,2*np.pi,96,endpoint=False):
            direction=Vector((np.cos(elevation)*np.cos(angle),np.cos(elevation)*np.sin(angle),np.sin(elevation)))
            point,normal,index,distance=alltree.ray_cast(origin,direction,5000)
            if point is None:misses+=1;continue
            if special[index] or 'glow' in triangle_owners[index]:continue
            a,b,c=world_tris[index];e=b-a;f=c-a;p1=np.asarray(point)-a;ee=e@e;ff=f@f;ef=e@f;pe=p1@e;pf=p1@f;det=ee*ff-ef*ef
            if abs(det)<1e-20:continue
            v=(ff*pe-ef*pf)/det;w=(ee*pf-ef*pe)/det
            uv=tris[index,0]*(1-v-w)+tris[index,1]*v+tris[index,2]*w;ix,iy=np.clip((uv*N).astype(int),0,N-1);sample_colors.append(pix[iy,ix,:3])
            if np.all(pix[iy,ix,:3]<1/255):black_hits.append({'mesh':triangle_owners[index],'point':list(point),'uv':list(uv)})
black=float(np.all(np.asarray(sample_colors)<1/255,axis=1).mean())
(LOG/'orient-express-black-hits.json').write_text(json.dumps(black_hits,indent=2)+'\n')
assert black<.01,('visible unlit bake holes',black)
# No sight rays may escape between the roof and window cornice.
for y in [-7,-4,-1,1,4,6]:
    for side in [-1,1]:
        assert bvh.ray_cast(Vector((0,y,2.32)),Vector((side,0,0)),1.6)[0] is not None,'Open roof seam'
# UV scrolling directions and texture wrap continuity, checked from exported data.
scroll=[]
for o in objects:
    if 'scroll' not in o.name:continue
    n=4 if 'Far' in o.name else 90;distance=60 if n==4 else 5;repeat=180 if n==4 else 24
    me=o.data;uv=me.uv_layers['UVMap']
    sides=set()
    for p in me.polygons:
        pts=np.array([tuple(o.matrix_world@me.vertices[me.loops[li].vertex_index].co) for li in p.loop_indices])
        us=np.array([uv.data[li].uv.x for li in p.loop_indices]);sides.add(int(np.sign(pts[0,0])))
        assert np.max(abs(abs(pts[:,0])-distance))<.001
        slope=np.linalg.lstsq(np.stack([pts[:,1],np.ones(len(pts))],axis=1),us,rcond=None)[0][0]
        assert abs(slope-1/repeat)<1e-6,(o.name,'U direction',slope)
        assert (o.matrix_world.to_3x3()@p.normal).x*pts[0,0]<0,'Scenery faces inward'
    assert sides=={-1,1}
    img=bpy.data.images.load(str(OUT/'Textures'/f'{o.name}_BaseColor.png'),check_existing=True)
    w,h=img.size;pixels=np.empty(w*h*4,np.float32);img.pixels.foreach_get(pixels);pixels=pixels.reshape(h,w,4)
    seam=float(abs(pixels[:,0,:3]-pixels[:,-1,:3]).max());assert seam<=1/255+1e-6,(o.name,'wrap seam',seam)
    # Positive shader sampling offset makes a fixed texel move by -speed/slope.
    velocity=-(n/10000)/slope;assert velocity<0
    scroll.append({'material':o.name,'N':n,'distance_from_center_m':distance,'repeat_m':repeat,'world_feature_velocity_y_m_s':velocity,'both_sides_rearward':True,'max_wrap_channel_error':seam})
assert len(scroll)==2
assert (count>0).mean()>.10
result={'validation':'PASS','fbx_reimport':'PASS','fbx_sha256':hashlib.sha256((OUT/'OrientExpress.fbx').read_bytes()).hexdigest(),'triangles':stats,'total_triangles':sum(stats.values()),'materials':len(materials),'mesh_objects':len(objects),'markers':expected,'head_clearance':heads,'sightlines':{'eye_height_m':1.25,'screen_m':[2.6,1.5],'grid':[39,17],'clear_rays':screen_rays,'blocked':0},'uv_overlaps':0,'uv_padding':padding,'atlas':atlas_stats,'scroll':scroll,'visible_black_bake_fraction':black,'visible_bake_samples':len(sample_colors),'exterior_ray_misses':misses,'textures':texture_info}
(LOG/'orient-express-verification.json').write_text(json.dumps(result,indent=2)+'\n')
lines=['# Orient-Express – Art-Bericht','',f"FBX-Reimport und Prüfungen: **PASS**. {sum(stats.values()):,} Dreiecke / 120.000; {len(materials)} Materialien / 14; zehn Plätze.",'','| Mesh | Dreiecke |','| --- | ---: |']
lines += [f'| {k} | {v:,} |' for k,v in stats.items()]
lines += ['',f'- {screen_rays:,} freie Leinwandstrahlen, 39 × 17 pro Spot, Augenhöhe 1,25 m; freie Fläche 2,6 × 1,5 m.',f"- Garantierter Kopf-Freiraum ≥ {heads['minimum_clearance_lower_bound_m']*100:.2f} cm über dem kontinuierlichen Höhenintervall 1,10–1,35 m.",'- Kopfprojektionen bleiben über Sitzpolstern, alle Marker über dem Boden.', f'- Sichtbare schwarze Bake-Proben: {black:.3%} aus {len(sample_colors):,} Treffern.', '- UVMap und Lightmap, keine UV-Flächen- oder Texelüberschneidung; gemessene Inselabstände und Atlasrand ≥4 px.', f"- Lichtatlas: {atlas_stats['clipped_fraction']:.3%} gesättigte belegte Texel.", '- Cycles Diffuse Direct+Indirect ohne Color, 256 Samples, Denoising, 8 px Margin, Standard/None/−1 EV, RGB8.', '- Beide Seiten: U wächst nach +Y; positiver App-UV-Offset verschiebt Bildmerkmale nach −Y zum Heck. Far scroll4 bei 60 m, Near scroll90 bei 5 m.', '- Horizontale Texturränder RGB-identisch. Opake Nahkulisse endet unter dem Horizont; kein Alpha/Glas in den Fenstern.', '', '## Texturen','']
lines += [f'- {k}: {v["dimensions"][0]}×{v["dimensions"][1]}, RGB8.' for k,v in texture_info.items()]
lines += ['', '## Vorschauen','', '- [Seat2 an](orient-express-seat2-on.png) · [Seat2 4 %](orient-express-seat2-04pct.png)', '- [Fenster links](orient-express-window-left.png) · [Fenster rechts](orient-express-window-right.png)', '- [Übersicht](orient-express-overview.png) · [Salon](orient-express-salon.png)', '- [Projektionsvorschau](orient-express-seat2-projection.png) (nur Vorschauhilfe, nicht im Export)' , '', 'Die Vorschauen verwenden ausschließlich Basisfarben × Lichtatlas ×2; bei Filmlicht ×0,04. Glow-Flächen dimmen ebenfalls; die Landschaft bleibt ungedimmt. Kein zusätzliches Echtzeitlicht.', '', 'Die Stirntür liegt hinter dem Projektionsportal; die App zeichnet die Leinwand selbst. Vier Standardsessel, zwei Bankplätze, zwei Essplätze und zwei vordere Sessel. Originales AV-Monogramm für den erfundenen Salon Aster Vale.', 'CC0 Clarens Night 02 / Poly Haven; Bearbeitung und Urheber in docs/CREDITS.md, Eingangschecksumme in art/orient-express-sources.json.', '', 'Offen: menschliche gestalterische Abnahme und tatsächliche Quest-/App-Darstellung inklusive Laufzeit-Scrollrichtung, Dimmung, Bildskalierung und Performance.', 'Die Vorzeichenprüfung setzt positive UV-Abtastverschiebung der dokumentierten scroll<N>-Konvention voraus. Kein App-Code wurde gelesen/geändert und Unity wurde nicht gestartet.', 'Build: tools/blender/orient_express.py; Prüfung: tools/blender/verify_orient_express.py. Akzeptierte Räume unverändert.']
(LOG/'ORIENT-EXPRESS-REPORT.md').write_text('\n'.join(lines)+'\n')
print('ORIENT EXPRESS VERIFICATION PASS',sum(stats.values()),'triangles;',len(materials),'materials;',screen_rays,'screen rays')
