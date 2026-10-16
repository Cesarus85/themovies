"""Independent Space FBX audit. No Unity; no accepted-room mutations.
Blender -b --factory-startup -P tools/blender/verify_space.py
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
from lib_space_uvs import uv_overlaps,audit_padding
from lib_seated_head_clearance import audit as audit_heads
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Assets/TheMovies/Art/Space';LOG=ROOT/'Logs/art-preview';LOG.mkdir(parents=True,exist_ok=True)
draft='--draft' in sys.argv
bpy.ops.wm.read_factory_settings(use_empty=True)
if draft:bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/blender/Space-draft.blend'))
else:bpy.ops.import_scene.fbx(filepath=str(OUT/'Space.fbx'))
scene=bpy.context.scene;bpy.context.view_layer.update()
objects=[o for o in scene.objects if o.type=='MESH'];interior=[o for o in objects if o.name not in ('Earth','Space_Panorama')]
def tree(objs):
    vs=[];fs=[]
    for o in objs:
        o.data.calc_loop_triangles();offset=len(vs);vs.extend(o.matrix_world@v.co for v in o.data.vertices);fs.extend(tuple(offset+i for i in t.vertices) for t in o.data.loop_triangles)
    return BVHTree.FromPolygons(vs,fs,all_triangles=True)
# Independent coordinate specification, deliberately not imported from builder.
seats=[(-1.125,0,0),(-.375,0,0),(.375,0,0),(1.125,0,0),(-4.1,-.6,0),(-3.2,-.6,0),(3.2,-.6,0),(4.1,-.6,0)]
expected={f'Spot_{i:03d}':p for i,p in enumerate(seats)};expected.update({f'Seat{i+1}':seats[i] for i in range(4)})
expected.update(ScreenAnchor=(0,8.3,1.35),ScreenTop=(0,8.3,5.35),Sun=(-900,160,-80))
for name,p in expected.items():assert (scene.objects[name].matrix_world.translation-Vector(p)).length<1e-5,(name,'position')
for i,a in enumerate(seats):
    for b in seats[i+1:]:assert (Vector(a)-Vector(b)).length>=.4
bvh=tree(interior);screen_rays=0
for i,p in enumerate(seats):
    origin=Vector(p)+Vector((0,0,1.25))
    for x in np.linspace(-4.75,4.75,39):
        for z in np.linspace(1.35,5.35,17):
            target=Vector((float(x),8.3,float(z)));delta=target-origin
            hit=bvh.ray_cast(origin,delta.normalized(),delta.length-.001)
            assert hit[0] is None,(i,'screen blocked',float(x),float(z),list(hit[0]) if hit[0] else None)
            screen_rays+=1
for x in np.linspace(-4.75,4.75,39):
    for z in np.linspace(1.35,5.35,17):assert bvh.ray_cast(Vector((x,8.23,z)),Vector((0,1,0)),.16)[0] is None,'Screen volume occupied'
if draft:
    print('DRAFT geometry sightlines PASS:',screen_rays);sys.exit(0)
assert set(o.type for o in scene.objects)<={'EMPTY','MESH'}
assert {o.name for o in scene.objects if o.type=='EMPTY'}==set(expected)|{'Earth_rotate5520'}
heads=audit_heads(objects,scene.objects);(LOG/'space-head-clearance.json').write_text(json.dumps(heads,indent=2)+'\n');assert heads['pass'],heads['failed_markers']
assert len(objects)<=14 and all(len(o.data.materials)==1 for o in objects)
materials={o.data.materials[0].name for o in objects};assert len(materials)<=14
stats={};tris=[];world_tris=[];special=[]
for o in objects:
    me=o.data;me.calc_loop_triangles();stats[o.name]=len(me.loop_triangles)
    assert [u.name for u in me.uv_layers]==['UVMap','Lightmap'],(o.name,'UV layers')
    assert me.has_custom_normals,(o.name,'explicit normals')
    lm=me.uv_layers['Lightmap'];tris.extend([[tuple(lm.data[i].uv) for i in t.loops] for t in me.loop_triangles])
    world_tris.extend([[tuple(o.matrix_world@me.vertices[i].co) for i in t.vertices] for t in me.loop_triangles]);special.extend([o.name in ('Earth','Space_Panorama')]*len(me.loop_triangles))
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
texture_info={};files={f'{m}_BaseColor.png' for m in materials}|{'Earth_Planet_Night.png','Space_Light.png'}
assert {p.name for p in (OUT/'Textures').glob('*.png')}==files
for p in sorted((OUT/'Textures').glob('*.png')):
    raw=p.read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n'
    w,h,bits,mode=struct.unpack('>IIBB',raw[16:26]);assert bits==8 and mode==2,(p.name,'RGB8')
    if 'Planet' in p.name or 'Panorama' in p.name:assert (w,h)==(4096,2048)
    elif p.name=='Space_Light.png':assert (w,h)==(2048,2048)
    else:assert w<=2048 and h<=2048
    texture_info[p.name]={'dimensions':[w,h],'bits':bits,'bytes':len(raw)}
for m in (bpy.data.materials[n] for n in materials):
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');assert bs.inputs['Base Color'].is_linked
    tex=bs.inputs['Base Color'].links[0].from_node;assert tex.type=='TEX_IMAGE' and Path(tex.image.filepath).name==f'{m.name}_BaseColor.png'
    assert not bs.inputs['Alpha'].is_linked and bs.inputs['Alpha'].default_value==1
    if bs.inputs['Normal'].is_linked:
        normal=bs.inputs['Normal'].links[0].from_node;assert normal.type=='NORMAL_MAP' and not normal.inputs['Color'].is_linked
atlas=bpy.data.images.load(str(OUT/'Textures/Space_Light.png'));pix=np.empty(N*N*4,np.float32);atlas.pixels.foreach_get(pix);pix=pix.reshape(N,N,4)
covered=pix[count>0,:3];atlas_stats={'covered_fraction':float((count>0).mean()),'clipped_fraction':float(np.any(covered>=1,axis=1).mean()),'percentiles':np.percentile(covered,[1,50,95,99,100],axis=0).tolist()}
assert atlas_stats['clipped_fraction']<.01,atlas_stats
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
assert misses==0,('Open universe ray misses',misses)
(LOG/'space-black-hits.json').write_text(json.dumps(black_hits,indent=2)+'\n')
black=float(np.all(np.asarray(sample_colors)<1/255,axis=1).mean())
# Orbital hierarchy, complete-sphere bounds (including all rotation phases).
earth=scene.objects['Earth'];stars=scene.objects['Space_Panorama'];rotator=scene.objects['Earth_rotate5520'];sun=scene.objects['Sun']
assert earth.parent==rotator and stars.parent is None
center=Vector((280,1000,-1080));assert (rotator.matrix_world.translation-center).length<.002
assert (earth.matrix_world.translation-center).length<.002
# FBX bake_space_transform bakes Z-up geometry to Y-up local space.
# On Blender reimport the +90 degree object basis maps local Y back to world Z.
up=(rotator.matrix_world.to_3x3()@Vector((0,1,0))).normalized();assert .2<up.angle(Vector((0,0,1)))<.8,('rotation tilt',list(up))
assert 'planet' in earth.data.materials[0].name.lower() and 'unlit' in stars.data.materials[0].name.lower()
for obj,c,r,inward in [(earth,center,1350,False),(stars,Vector((0,0,1.25)),2500,True)]:
    for v in obj.data.vertices:assert abs((obj.matrix_world@v.co-c).length-r)<.003,(obj.name,'sphere radius')
    for p in obj.data.polygons:
        d=obj.matrix_world@p.center-c;n=obj.matrix_world.to_3x3()@p.normal
        assert (n.dot(d)<0)==inward,(obj.name,'face orientation')
    # Reimport preserved spherical UV0, including rotation of Earth parent.
    for p in obj.data.polygons:
        for li in p.loop_indices:
            raw=obj.data.vertices[obj.data.loops[li].vertex_index].co
            v=Vector((raw.x,-raw.z,raw.y)) # recover authored Blender Z-up coordinates
            uv=obj.data.uv_layers['UVMap'].data[li].uv
            assert abs(uv.y-(.5+math.asin(max(-1,min(1,v.z/v.length)))/math.pi))<2e-5,(obj.name,'latitude')
            if abs(v.z/v.length)<.99999:assert abs((uv.x+math.atan2(v.y,v.x)/(2*math.pi)+.5)%1-.5)<2e-5,(obj.name,'longitude')
maximum_earth=max((Vector(p)+Vector((0,0,1.25))-center).length+1350 for p in seats)
maximum_stars=max((Vector(p)+Vector((0,0,1.25))-Vector((0,0,1.25))).length+2500 for p in seats)
assert maximum_earth<2900 and maximum_stars<2900
# Earth visibility around a MAXIMUM opaque 9.5x4m screen: sample actual globe
# intersections, rejecting foreground furniture/window structure and screen crossings.
et=tree([earth]);sun_dir=sun.matrix_world.translation.normalized();earth_views=[]
for i,p in enumerate(seats):
    origin=Vector(p)+Vector((0,0,1.25));hits=[];sectors={'below':0,'left':0,'right':0};days=0;nights=0
    for az in np.linspace(-1.3,1.3,131):
        for el in np.linspace(-.20,.38,45):
            direction=Vector((math.sin(az)*math.cos(el),math.cos(az)*math.cos(el),math.sin(el)))
            point,normal,idx,dist=et.ray_cast(origin,direction,2900)
            if point is None:continue
            if bvh.ray_cast(origin,direction,dist-.05)[0] is not None:continue
            t=(8.3-origin.y)/direction.y;proj=origin+direction*t
            if -4.75<=proj.x<=4.75 and 1.35<=proj.z<=5.35:continue
            sector='below' if proj.z<1.35 and abs(proj.x)<=4.75 else 'left' if proj.x<0 else 'right'
            sectors[sector]+=1;cosine=(point-center).normalized().dot(sun_dir)
            days+=cosine>.12;nights+=cosine<-.05;hits.append(cosine)
    assert len(hits)>180 and sectors['below']>15 and sectors['left']+sectors['right']>50,(i,'Earth visible sectors',sectors,len(hits))
    assert days>15 and nights>15,(i,'day/night terminator',days,nights)
    earth_views.append({'spot':f'Spot_{i:03d}','visible_globe_rays':len(hits),'sectors':sectors,'day_rays':days,'night_rays':nights})
assert (count>0).mean()>.10,('Atlas occupancy',float((count>0).mean()))
assert black<.01,('Visible black bake',black)
result={'validation':'PASS','fbx_reimport':'PASS','fbx_sha256':hashlib.sha256((OUT/'Space.fbx').read_bytes()).hexdigest(),'triangles':stats,'total_triangles':sum(stats.values()),'materials':len(materials),'mesh_objects':len(objects),'markers':expected,'head_clearance':heads,'sightlines':{'eye_height_m':1.25,'screen_m':[9.5,4],'grid':[39,17],'clear_rays':screen_rays,'blocked':0},'earth_views_with_max_screen':earth_views,'maximum_earth_distance_all_rotation_phases_m':maximum_earth,'maximum_star_distance_m':maximum_stars,'earth_radius_m':1350,'rotation_period_seconds':5520,'rotation_up':list(up),'sun_direction':list(sun_dir),'uv_overlaps':0,'uv_padding':padding,'atlas':atlas_stats,'visible_black_fraction':black,'visible_light_samples':len(sample_colors),'textures':texture_info}
(LOG/'space-verification.json').write_text(json.dumps(result,indent=2)+'\n')
lines=['# Weltraum-Lounge – Art-Bericht','',f"FBX-Reimport und automatisierte Prüfungen: **PASS**. {sum(stats.values()):,} Dreiecke / 120.000, {len(materials)} Materialien / 14, acht Plätze.",'','| Mesh | Dreiecke |','| --- | ---: |']
lines += [f'| {k} | {v:,} |' for k,v in stats.items()]
lines += ['', '## Nachweise','',f'- {screen_rays:,} freie Sichtlinien aus 1,25 m Augenhöhe auf 9,5 × 4 m.',f"- Alle zwölf Sitzmarker bestehen die kontinuierliche Kopfhülle; garantierter Freiraum mindestens {heads['minimum_clearance_lower_bound_m']*100:.2f} cm.",'- Erde aus jedem Spot unterhalb und seitlich der maximalen Leinwand sichtbar; Tag- und Nachtflächen per tatsächlichem Mesh-Raytest nachgewiesen.',f'- Vollständige Erde auch während Rotation höchstens {maximum_earth:.2f} m, Sterne {maximum_stars:.2f} m von den Augen entfernt.', '- Sun und Earth_rotate5520 inklusive Kindhierarchie und Achsneigung im FBX erhalten.', '- UVMap und Lightmap, keine UV-Flächen-/Texelüberschneidungen; Inselabstand und Atlasrand mindestens 4 px gemessen.',f"- Atlas: {atlas_stats['clipped_fraction']:.3%} gesättigte belegte Texel; {black:.3%} schwarze sichtbare Lichtproben.", '- 256 Samples, Cycles Diffuse Direct+Indirect ohne Color; Denoise; 8 px Rand; Standard/None/−1 EV/RGB8.', '- Erde und Sterne werden nicht gebacken. 1.350-m-Erdradius hält die vollständige Kugel innerhalb der geforderten 2.900 m.', '', '## Texturen','']
lines += [f'- {k}: {v["dimensions"][0]}×{v["dimensions"][1]}, RGB8.' for k,v in texture_info.items()]
lines += ['', '## Vorschauen','', '- [Seat2 an](space-seat2-on.png) · [Seat2 4 %](space-seat2-04pct.png) · [Maximale Leinwand](space-seat2-max-screen.png)', '- [Seitenfenster](space-side.png) · [Übersicht](space-overview.png) · [Möbel und Rückwand](space-lounge.png)', '', 'Diffuse Innenraumflächen aus Basisfarbe und gebackenem Atlas; Glow-Flächen mit dimmbarer Eigenemission. Kein Echtzeitlicht oder zusätzliches Filmlicht.', 'Erde: Vorschau-Shader mit Sun-gesteuertem Tag/Nacht-Mix, Stadtlichtern und blauem Rand. Die maximale', 'Leinwand im separaten Bild ist nur eine Vorschauhilfe und fehlt im FBX. Die regulären Ansichten zeigen die freie Rahmenöffnung.', '', 'NASA Blue Marble/Clouds 2002 und Black Marble 2012, public domain; Links, Autoren und Änderungen in docs/CREDITS.md.', 'Originales prozedurales Sternenpanorama; keine weiteren Fremdassets. Quellen-SHA256 in art/space-sources.json.', '', 'Offen: menschliche gestalterische Abnahme sowie tatsächliche App-/Quest-Darstellung, Planetshader und Laufzeitrotation.', 'Unity wurde nicht gestartet. Die drei akzeptierten Räume und App-Code wurden nicht verändert.', 'Build: tools/blender/space.py; unabhängige Prüfung: tools/blender/verify_space.py.', 'Maschinenlesbar: space-metrics.json, space-verification.json, space-head-clearance.json.']
(LOG/'SPACE-REPORT.md').write_text('\n'.join(lines)+'\n')
print('SPACE VERIFICATION PASS',sum(stats.values()),'triangles;',len(materials),'materials;',screen_rays,'screen rays; earth',maximum_earth,'m')
