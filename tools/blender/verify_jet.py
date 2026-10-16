"""Independent Jet FBX audit. No Unity; no accepted-room mutations.
Blender -b --factory-startup -P tools/blender/verify_jet.py
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
from lib_jet_uvs import uv_overlaps,audit_padding
from lib_jet_motion import audit_motion
from lib_seated_head_clearance import audit as audit_heads
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Assets/TheMovies/Art/Jet';LOG=ROOT/'Logs/art-preview';LOG.mkdir(parents=True,exist_ok=True)
draft='--draft' in sys.argv
bpy.ops.wm.read_factory_settings(use_empty=True)
if draft:bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/blender/Jet-draft.blend'))
else:bpy.ops.import_scene.fbx(filepath=str(OUT/'Jet.fbx'))
scene=bpy.context.scene;bpy.context.view_layer.update()
from lib_verify_room_motion import audit as audit_effects
if '--draft' not in sys.argv:motion_evidence=audit_effects(scene,'Jet',ROOT)
objects=[o for o in scene.objects if o.type=='MESH'];interior=[o for o in objects if 'unlit' not in o.name.lower()]
def tree(objs):
    vs=[];fs=[]
    for o in objs:
        o.data.calc_loop_triangles();offset=len(vs);vs.extend(o.matrix_world@v.co for v in o.data.vertices);fs.extend(tuple(offset+i for i in t.vertices) for t in o.data.loop_triangles)
    return BVHTree.FromPolygons(vs,fs,all_triangles=True)
# Independent coordinate specification, deliberately not imported from builder.
seats=[(-1.125,0,0),(-.375,0,0),(.375,0,0),(1.125,0,0),
       (-2.35,1.7,0),(-2.35,2.85,0),(2.35,1.7,0),(2.35,2.85,0),
       (-1.72,-2.7,0),(-1.72,-4.,0),(1.72,-2.7,0),(1.72,-4.,0)]
expected={f'Spot_{i:03d}':p for i,p in enumerate(seats)};expected.update({f'Seat{i+1}':seats[i] for i in range(4)})
expected.update(ScreenAnchor=(0,5.48,.9),ScreenTop=(0,5.48,2.7))
for name,p in expected.items():assert (scene.objects[name].matrix_world.translation-Vector(p)).length<1e-5,(name,'position')
for i,a in enumerate(seats):
    for b in seats[i+1:]:assert (Vector(a)-Vector(b)).length>=.4
bvh=tree(interior);screen_rays=0
for i,p in enumerate(seats):
    origin=Vector(p)+Vector((0,0,1.25))
    for x in np.linspace(-1.7,1.7,39):
        for z in np.linspace(.9,2.7,17):
            target=Vector((float(x),5.48,float(z)));delta=target-origin
            hit=bvh.ray_cast(origin,delta.normalized(),delta.length-.001)
            assert hit[0] is None,(i,'screen blocked',float(x),float(z),list(hit[0]) if hit[0] else None)
            screen_rays+=1
for x in np.linspace(-1.7,1.7,39):
    for z in np.linspace(.9,2.7,17):assert bvh.ray_cast(Vector((x,5.43,z)),Vector((0,1,0)),.16)[0] is None,'Screen volume occupied'
if draft:
    print('DRAFT geometry sightlines PASS:',screen_rays);sys.exit(0)
assert set(o.type for o in scene.objects)<={'EMPTY','MESH'}
assert {o.name for o in scene.objects if o.type=='EMPTY'}==set(expected)
heads=audit_heads(objects,scene.objects);(LOG/'jet-head-clearance.json').write_text(json.dumps(heads,indent=2)+'\n');assert heads['pass'],heads['failed_markers']
assert len(objects)<=14 and all(len(o.data.materials)==1 for o in objects)
materials={o.data.materials[0].name for o in objects};assert len(materials)<=14
stats={};tris=[];world_tris=[];special=[]
for o in objects:
    me=o.data;me.calc_loop_triangles();stats[o.name]=len(me.loop_triangles)
    assert [u.name for u in me.uv_layers]==['UVMap','Lightmap'],(o.name,'UV layers')
    assert me.has_custom_normals,(o.name,'explicit normals')
    lm=me.uv_layers['Lightmap'];tris.extend([[tuple(lm.data[i].uv) for i in t.loops] for t in me.loop_triangles])
    world_tris.extend([[tuple(o.matrix_world@me.vertices[i].co) for i in t.vertices] for t in me.loop_triangles]);special.extend(['unlit' in o.name.lower()]*len(me.loop_triangles))
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
texture_info={};files={f'{m}_BaseColor.png' for m in materials}|{'Jet_Light.png'}
assert {p.name for p in (OUT/'Textures').glob('*.png')}==files
for p in sorted((OUT/'Textures').glob('*.png')):
    raw=p.read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n'
    w,h,bits,mode=struct.unpack('>IIBB',raw[16:26]);assert bits==8 and mode==2,(p.name,'RGB8')
    if 'Panorama' in p.name:assert w<=4096 and h<=2048
    elif p.name=='Jet_Light.png':assert (w,h)==(2048,2048)
    else:assert w<=2048 and h<=2048
    texture_info[p.name]={'dimensions':[w,h],'bits':bits,'bytes':len(raw)}
for m in (bpy.data.materials[n] for n in materials):
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');assert bs.inputs['Base Color'].is_linked
    tex=bs.inputs['Base Color'].links[0].from_node;assert tex.type=='TEX_IMAGE' and Path(tex.image.filepath).name==f'{m.name}_BaseColor.png'
    assert not bs.inputs['Alpha'].is_linked and bs.inputs['Alpha'].default_value==1
    if bs.inputs['Normal'].is_linked:
        normal=bs.inputs['Normal'].links[0].from_node;assert normal.type=='NORMAL_MAP' and not normal.inputs['Color'].is_linked
atlas=bpy.data.images.load(str(OUT/'Textures/Jet_Light.png'));pix=np.empty(N*N*4,np.float32);atlas.pixels.foreach_get(pix);pix=pix.reshape(N,N,4)
covered=pix[count>0,:3];atlas_stats={'covered_fraction':float((count>0).mean()),'clipped_fraction':float(np.any(covered>=1,axis=1).mean()),'percentiles':np.percentile(covered,[1,50,95,99,100],axis=0).tolist()}
# Visible-light audit, testing actual first-hit UV coordinates from all seats.
alltree=tree(objects);sample_colors=[];misses=0;black_hits=[]
triangle_owners=[o.name for o in objects for t in o.data.loop_triangles]
for p in seats:
    origin=Vector(p)+Vector((0,0,1.25))
    for elevation in np.linspace(-1.45,1.45,21):
        for angle in np.linspace(0,2*np.pi,96,endpoint=False):
            direction=Vector((np.cos(elevation)*np.cos(angle),np.cos(elevation)*np.sin(angle),np.sin(elevation)))
            point,normal,index,distance=alltree.ray_cast(origin,direction,2900)
            if point is None:misses+=1;continue
            if special[index]:continue
            a,b,c=world_tris[index];e=b-a;f=c-a;p1=np.asarray(point)-a;ee=e@e;ff=f@f;ef=e@f;pe=p1@e;pf=p1@f;det=ee*ff-ef*ef
            v=(ff*pe-ef*pf)/det;w=(ee*pf-ef*pe)/det
            uv=tris[index,0]*(1-v-w)+tris[index,1]*v+tris[index,2]*w;ix,iy=np.clip((uv*N).astype(int),0,N-1);sample_colors.append(pix[iy,ix,:3])
            if np.all(pix[iy,ix,:3]<1/255):black_hits.append({"mesh":triangle_owners[index],"point":list(point),"uv":list(uv),"normal":list(normal)})
assert misses<60,('Cabin leaks',misses)
(LOG/'jet-black-hits.json').write_text(json.dumps(black_hits,indent=2)+'\n')
black=float(np.all(np.asarray(sample_colors)<1/255,axis=1).mean())
# Real window apertures: ray passes through the deep, unglazed reveal on each side.
window_rays=0
for side in [-1,1]:
    for y in [-5.2+i*.8 for i in range(14)]:
        for dy in [-.075,0,.075]:
            for dz in [-.12,0,.12]:
                origin=Vector((side*2.82,y+dy,1.25+dz))
                assert bvh.ray_cast(origin,Vector((side,0,0)),.5)[0] is None,('Window blocked',side,y,dy,dz)
                window_rays+=1
# The solid panel corners and rear roof joint must not leak scenery/light.
for side in [-1,1]:
    for y in [-5.2+i*.8 for i in range(14)]:
        for dy in [-.3995,.3995]:
            assert bvh.ray_cast(Vector((side*2.82,y+dy,1.6395)),Vector((side,0,0)),.5)[0] is not None,('Window panel corner leak',side,y,dy)
for x in [-1.5,-.5,.5,1.5]:
    assert bvh.ray_cast(Vector((x,-5.7,2.445)),Vector((0,-1,0)),.5)[0] is not None,'Rear ceiling joint leak'
# UV motion check uses actual reimported vertices: positive offset in U must
# move a fixed texture feature toward world -Y on BOTH sky sides and the deck.
scroll=[]
for o in objects:
    if 'scroll' not in o.name:continue
    speed=2 if 'Sky' in o.name else 390
    assert f'unlit_scroll{speed}' in o.data.materials[0].name
    uv=o.data.uv_layers['UVMap'];o.data.calc_loop_triangles()
    for t in o.data.loop_triangles:
        pts=np.array([tuple(o.matrix_world@o.data.vertices[i].co) for i in t.vertices])
        coords=np.array([tuple(uv.data[i].uv) for i in t.loops]);duv=np.array([coords[1]-coords[0],coords[2]-coords[0]])
        basis=np.linalg.solve(duv,np.array([pts[1]-pts[0],pts[2]-pts[0]]))
        velocity=-speed/10000*basis[0]
        assert velocity[1]<0 and abs(velocity[0])<1e-4 and abs(velocity[2])<1e-4,(o.name,velocity.tolist())
        scroll.append({'mesh':o.name,'velocity_mps':velocity.tolist()})
    if 'Sky' in o.name:
        assert all(abs(abs((o.matrix_world@v.co).x)-900)<.01 for v in o.data.vertices)
    else:assert all(abs((o.matrix_world@v.co).z+45)<.01 for v in o.data.vertices)
# Real runtime clipping limit: all scenery vertices stay within 3km of every eye.
maximum_scenery=max((o.matrix_world@v.co-(Vector(p)+Vector((0,0,1.25)))).length
    for o in objects if 'unlit' in o.name.lower() for v in o.data.vertices for p in seats)
assert maximum_scenery<2990,('Scenery exceeds runtime far clip',maximum_scenery)
window_views=[]
for i,p in enumerate(seats):
    origin=Vector(p)+Vector((0,0,1.25));seen={'sky':0,'clouds':0}
    for side in [-1,1]:
        for y in [-5.2+j*.8 for j in range(14)]:
            for z in np.linspace(1.06,1.44,13):
                target=Vector((side*3.15,y,float(z)));direction=(target-origin).normalized()
                point,normal,index,distance=alltree.ray_cast(origin,direction,2990)
                if point is None:continue
                name=triangle_owners[index]
                if 'Sky' in name:seen['sky']+=1
                if 'Clouds' in name:seen['clouds']+=1
    assert seen['sky']>0 and seen['clouds']>0,(i,'Missing seated cloud/sky view',seen)
    window_views.append({'spot':f'Spot_{i:03d}',**seen})
seams={}
for p in (OUT/'Textures').glob('*scroll*.png'):
    image=bpy.data.images.load(str(p),check_existing=True);w,h=image.size;pixels=np.empty(w*h*4,np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(h,w,4)
    delta=float(abs(pixels[:,0,:3]-pixels[:,-1,:3]).max())
    slope=float(abs((pixels[:,1,:3]-pixels[:,0,:3])-(pixels[:,-1,:3]-pixels[:,-2,:3])).max())
    assert delta<=1/255+.00001 and slope<=1/255+.00001,(p.name,delta,slope)
    seams[p.name]={'maximum_edge_delta':delta,'maximum_slope_delta':slope}
assert (count>0).mean()>.10
assert black<.015,('Visible black bake',black)
assert atlas_stats['clipped_fraction']<.01,atlas_stats
motion=audit_motion(alltree,triangle_owners,seats,Vector(next(r['velocity_mps'] for r in scroll if 'Clouds' in r['mesh'])),Vector(next(r['velocity_mps'] for r in scroll if 'Sky' in r['mesh'])),bpy.data.images.load(str(OUT/'Textures/Jet_Clouds_Panorama_unlit_scroll390_BaseColor.png'),check_existing=True))
(LOG/'jet-motion-verification.json').write_text(json.dumps(motion,indent=2)+'\n')
result={'validation':'PASS','fbx_reimport':'PASS','fbx_sha256':hashlib.sha256((OUT/'Jet.fbx').read_bytes()).hexdigest(),
        'triangles':stats,'total_triangles':sum(stats.values()),'materials':len(materials),'mesh_objects':len(objects),
        'markers':expected,'head_clearance':heads,'sightlines':{'eye_height_m':1.25,'screen_m':[3.4,1.8],'grid':[39,17],'clear_rays':screen_rays,'blocked':0},
        'open_window_rays':window_rays,'scroll_motion':scroll,'angular_motion':motion,'scroll_seams':seams,'seated_window_views':window_views,'maximum_scenery_distance_m':maximum_scenery,'uv_overlaps':0,'uv_padding':padding,
        'atlas':atlas_stats,'visible_black_fraction':black,'visible_light_samples':len(sample_colors),'ray_misses':misses,'textures':texture_info}
(LOG/'jet-verification.json').write_text(json.dumps(result,indent=2)+'\n')
lines=['# Präsidentenjet – Art-Bericht','',f'FBX-Reimport und automatische Prüfungen: **PASS**. {sum(stats.values()):,} Dreiecke / 120.000, {len(materials)} Materialien / 14, zwölf Plätze.','',
       '| Mesh | Dreiecke |','| --- | ---: |']
lines += [f'| {k} | {v:,} |' for k,v in stats.items()]
lines += ['', '## Nachweise','',f'- {screen_rays:,} freie Leinwandstrahlen aus 1,25 m auf 3,4 × 1,8 m.',
          f"- Garantierter Kopf-Freiraum mindestens {heads['minimum_clearance_lower_bound_m']*100:.2f} cm (0,20 m Kopf, Höhen 1,10–1,35 m, konservative 5-mm-Abtastung).",
          '- Seat1–4 exakt an Standardkoordinaten; Spot_000–011 lückenlos, über Polstern und Boden.',
          f'- {window_rays} freie Strahlen durch 28 echte Fensteröffnungen; keine Glasscheiben.',
          '- UVMap und Lightmap; keine Flächen-/Texelüberschneidungen, mindestens 4 px Inselabstand und Atlasrand gemessen.',
          '- Cycles Diffuse Direct+Indirect ohne Color, 256 Samples, Denoise, 8 px Bake-Rand; Standard/None/−1 EV, RGB8.',
          f"- Gesättigte belegte Atlastexel: {atlas_stats['clipped_fraction']:.3%}; schwarze sichtbare Proben: {black:.3%}.",
          '- Horizontale Kulissennähte und Nahtgradienten geprüft; positive U-Bewegung schiebt beide Seiten nach −Y (hinten).',
          f'- Himmel und Wolken von jedem Sitz per Raytest sichtbar; größte Kulissenentfernung {maximum_scenery:.2f} m (<3.000 m).',
          '- Himmelsbänder 900 m entfernt (scroll2); opake Wolkendecke 45 m unter dem Boden (scroll390).',
          '', '## Texturen','']
lines += [f'- {k}: {v["dimensions"][0]}×{v["dimensions"][1]}, RGB8.' for k,v in texture_info.items()]
lines += ['', '## Vorschauen','',
          '- [Seat2 an](jet-seat2-on.png) · [Seat2 4 %](jet-seat2-04pct.png)',
          '- [Fenster links](jet-window-left.png) · [Fenster rechts](jet-window-right.png)',
          '- [Übersicht](jet-overview.png) · [Konferenz](jet-conference.png) · [Sessel](jet-seating.png)',
          '- Statische Vorschauen weiterhin gültig: Geometrie, Texturen und Bake unverändert. Frühere Bewegungsvorschau mit scroll1500 ist überholt; bei Bedarf mit --motion-preview neu rendern.',
          '', 'Vorschauen: Basisfarbe × gespeicherter Lichtatlas ×2; bei Filmlicht ×0,04. Glow ebenfalls gedimmt, Unlit unverändert. Keine Echtzeitlampen oder zusätzliches Filmlicht.',
          'Keine Leinwandfläche exportiert. Die dunkle Fläche ist die zurückgesetzte Schottwand. Vordere Deckenmitte lokal 2,80 m für den 2,70 m hohen ScreenTop, übrige Deckenmitte 2,45 m.',
          '', 'Geometrie, Leder-/Teppichmuster und Kulissen sind Originalarbeit. Himmel: Blender Multiple Scattering; Wolken: offline gerenderte prozedurale Cycles-Volumetrie. Holz: CC0 dark_wood / Poly Haven, verkleinert, getönt und teils gespiegelt; siehe docs/CREDITS.md und art/jet-sources.json. Keine offiziellen Zeichen, Logos oder Transparenz.',
          '', 'Der Look ist vom Nutzer auf Quest 3 abgenommen. Bewegung um 35 % gegenüber scroll600 reduziert (Prüfziel 1,17–1,43°/s): Seat1–4 etwa 1,19–1,41°/s (sichtbare Wolken, Median); Himmel höchstens 0,020°/s. Nähere Sofaplätze etwa 2,33–2,82°/s, Konferenzplätze etwa 1,70–1,71°/s (Median). Keine zusätzliche Ebene; alle Textur-Pixel und der Lichtatlas unverändert. Kein neuer Bake. Herleitung und Einzelwerte: docs/ART-BRIEF.md und jet-motion-verification.json.',
          f'Wolken: 0,039 U/s, 19,933 m/s zum Heck, Wiederholung alle 25,641 s. Himmel: 0,0002 U/s. Horizontale Kontrastenergie über der zeitlichen Nyquist-Grenze bei 72/90 Hz: {motion["temporal_sampling"]["72"]["horizontal_contrast_energy_above_temporal_nyquist_fraction"]:.3%} / {motion["temporal_sampling"]["90"]["horizontal_contrast_energy_above_temporal_nyquist_fraction"]:.3%}. Geometrie, Normalen, beide UV-Sätze und Marker nach FBX-Reimport exakt erhalten; alle PNG-Payloads per SHA-256 unverändert (jet-motion-preservation.json).',
          'Offen: erneute menschliche Quest-Abnahme der reduzierten Bewegung. Unity wurde nicht gestartet.',
          'Die vier akzeptierten Räume, App-Code, ProjectSettings und Packages wurden nicht verändert.',
          'Build: tools/blender/jet.py; unabhängige Prüfung: tools/blender/verify_jet.py.']
(LOG/'JET-REPORT.md').write_text('\n'.join(lines)+'\n')
print('JET VERIFICATION PASS',sum(stats.values()),'triangles;',len(materials),'materials;',screen_rays,'screen rays')
