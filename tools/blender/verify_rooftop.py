"""Independent audit of the delivered FBX, textures and actual seated-head surfaces.
Blender -b --factory-startup -P tools/blender/verify_rooftop.py
Audits the exported Rooftop FBX from a factory-clean scene.
Reports only under ignored Logs/art-preview. Fails on any broken requirement.
"""
import sys
sys.dont_write_bytecode=True
import json
import hashlib
from pathlib import Path
import struct
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_rooftop_uvs import uv_overlaps,audit_padding
from lib_seated_head_clearance import audit as audit_heads
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Assets/TheMovies/Art/Rooftop';PREVIEW=ROOT/'Logs/art-preview'
PREVIEW.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(OUT/'Rooftop.fbx'))
scene=bpy.context.scene
from lib_verify_room_motion import audit as audit_effects
if '--draft' not in sys.argv:motion_evidence=audit_effects(scene,'Rooftop',ROOT)
objects=[o for o in scene.objects if o.type=='MESH']
assert set(o.type for o in scene.objects)<={'MESH','EMPTY'},'Export contains helpers'
assert len(objects)<=14 and all(len(o.data.materials)==1 for o in objects)
materials={o.data.materials[0].name for o in objects};assert len(materials)==len(objects)
required={'Rooftop_Canvas_Linen','Rooftop_Blackened_Metal','Rooftop_Festoon_bulb_flicker4','Rooftop_Panorama_unlit_twinkle10'}
assert required<=materials
# Deliberately independent expected coordinates, rather than importing the builder's seats.
seat_points=[(-1.125,0,0),(-.375,0,0),(.375,0,0),(1.125,0,0),(-3.15,.08,0),(-4.35,.08,0),(-.7,-2.82,0),(.7,-2.82,0),(3.15,-2.67,0),(4.25,-2.67,0)]
expected={f'Seat{i+1}':seat_points[i] for i in range(4)}
expected.update({f'Spot_{i:03d}':p for i,p in enumerate(seat_points)})
expected.update(ScreenAnchor=(0,8.4,1.1),ScreenTop=(0,8.4,5.1))
assert {o.name for o in scene.objects if o.type=='EMPTY' and not o.name.startswith('FireLight_')}==set(expected)
for i,a in enumerate(seat_points):
    for b in seat_points[i+1:]:assert (Vector(a)-Vector(b)).length>=.4
for name,loc in expected.items():assert (scene.objects[name].matrix_world.translation-Vector(loc)).length<.00001,(name,'coordinates')

def bvh_for(objs):
    verts=[];faces=[]
    for o in objs:
        m=o.data;m.calc_loop_triangles();offset=len(verts)
        verts.extend(o.matrix_world@v.co for v in m.vertices)
        faces.extend(tuple(offset+i for i in t.vertices) for t in m.loop_triangles)
    return BVHTree.FromPolygons(verts,faces,all_triangles=True)

stats={};uvtris=[];normal_evidence={}
for o in objects:
    m=o.data;m.calc_loop_triangles();stats[o.name]=len(m.loop_triangles)
    assert [u.name for u in m.uv_layers]==['UVMap','Lightmap'],o.name
    assert m.has_custom_normals,(o.name,'lost explicit normals')
    normal_evidence[o.name]=True
    uv=m.uv_layers['Lightmap']
    uvtris.extend([[tuple(uv.data[l].uv) for l in t.loops] for t in m.loop_triangles])
assert sum(stats.values())<=120000
tris=np.asarray(uvtris,np.float64)
assert tris.min()>=0 and tris.max()<=1
ab=tris[:,1]-tris[:,0];ac=tris[:,2]-tris[:,0]
areas=np.abs(ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0])/2
assert np.min(areas)>1e-14,('degenerate UV triangle',float(areas.min()))
overlaps=uv_overlaps(tris);assert not overlaps,('UV intersections',len(overlaps))
padding=audit_padding(tris);assert padding['pass'],padding
# Raster audit at the actual atlas resolution, independent from the continuous SAT audit.
N=2048;count=np.zeros((N,N),np.uint16)
for pts in tris*N:
    mn=np.maximum(np.floor(pts.min(axis=0)-.5).astype(int),0);mx=np.minimum(np.ceil(pts.max(axis=0)-.5).astype(int),N-1)
    if np.any(mx<mn):continue
    a,b,c=pts;ab=b-a;ac=c-a;det=ab[0]*ac[1]-ab[1]*ac[0]
    if abs(det)<1e-12:continue
    yy,xx=np.mgrid[mn[1]:mx[1]+1,mn[0]:mx[0]+1];px=xx+.5-a[0];py=yy+.5-a[1]
    u=(px*ac[1]-py*ac[0])/det;v=(ab[0]*py-ab[1]*px)/det
    mask=(u>1e-6)&(v>1e-6)&(u+v<1-1e-6)
    count[mn[1]:mx[1]+1,mn[0]:mx[0]+1]+=mask.astype(np.uint16)
assert not np.any(count>1),'Overlapping atlas texels'
assert (count>0).mean()>.10,('Insufficient atlas surface coverage',float((count>0).mean()))
# Audit the whole spherical head envelope, then retain floor/back/sightline tests.
head_clearance=audit_heads(objects,scene.objects)
(PREVIEW/'rooftop-head-clearance.json').write_text(json.dumps(head_clearance,indent=2)+'\n')
assert head_clearance['pass'],head_clearance['failed_markers']
bvh=bvh_for(objects)
upholstery=bvh_for([o for o in objects if o.name=='Rooftop_Canvas_Linen'])
floor=bvh_for([o for o in objects if o.name in ('Rooftop_Teak','Rooftop_Woven_Rug')])
spots=[];sightlines=[]
for i in range(10):
    name=f'Spot_{i:03d}';p=scene.objects[name].matrix_world.translation
    hit,n,face,distance=upholstery.ray_cast(p+Vector((0,0,.85)),Vector((0,-1,0)),.3)
    assert hit is not None and n.y>.8 and abs(distance-(.22 if i<4 else .20))<.0001,(name,'padded back',distance)
    ground,normal,_,_=floor.ray_cast(p+Vector((0,0,.05)),Vector((0,0,-1)),.1)
    assert ground is not None and normal.z>.95 and abs(ground.z-p.z)<.011,(name,'floor')
    cushion,n,_,_=upholstery.ray_cast(p+Vector((0,.15,.78)),Vector((0,0,-1)),.4)
    assert cushion is not None and .44<cushion.z<.52,(name,'missing seat cushion')
    spots.append({'name':name,'head_floor':list(p),'back_clearance_m':distance,'cushion_top_m':cushion.z})
    origin=p+Vector((0,0,1.25))
    assert bvh.find_nearest(origin)[3]>.10,(name,'eye inside furniture')
    for x in np.linspace(-4.75,4.75,39):
        for z in np.linspace(1.1,5.1,17):
            target=Vector((float(x),8.4,float(z)));delta=target-origin
            hit=bvh.ray_cast(origin,delta.normalized(),delta.length-.001)
            assert hit[0] is None,(name,'blocked screen',x,z,list(hit[0]) if hit[0] else None)
            sightlines.append([name,float(x),float(z)])
# Check every source texture reference and PNG header, not only Blender's decoded dimensions.
textures={};atlas_stats={}
expected_files={f'{m}_BaseColor.png' for m in materials}|{'Rooftop_Light.png'}
assert {p.name for p in (OUT/'Textures').glob('*.png')}==expected_files
for path in sorted((OUT/'Textures').glob('*.png')):
    header=path.read_bytes()[:33];assert header[:8]==b'\x89PNG\r\n\x1a\n'
    width,height,depth,mode=struct.unpack('>IIBB',header[16:26]);assert depth==8 and mode in (2,6),(path.name,'not RGB/RGBA8')
    assert width<= (4096 if 'Panorama' in path.name else 2048) and height<=2048
    if path.name=='Rooftop_Light.png':assert (width,height,mode)==(2048,2048,2)
    if path.name not in ('Rooftop_Light.png','Rooftop_Foliage_Atlas_BaseColor.png') and 'Panorama' not in path.name:assert (width,height)==(1024,1024)
    if 'Panorama' in path.name:assert (width,height,mode)==(4096,2048,2)
    textures[path.name]={'size':[width,height],'bits':depth,'bytes':path.stat().st_size}
    if 'Panorama' in path.name:
        im=bpy.data.images.load(str(path));p=np.empty(width*height*4,np.float32);im.pixels.foreach_get(p)
        sky=p.reshape(height,width,4)[:,:,:3]
        assert float(sky.max())<.99,'Panorama has clipped highlights'
        textures[path.name]['peak_srgb']=float(sky.max())
        textures[path.name]['clipped_pixels']=int(np.any(sky>=1,axis=2).sum())
    if path.name=='Rooftop_Light.png':
        im=bpy.data.images.load(str(path));p=np.empty(N*N*4,np.float32);im.pixels.foreach_get(p);rgb=p.reshape(N,N,4)[:,:,:3]
        covered=rgb[count>0]
        atlas_stats={'surface_fraction':float((count>0).mean()),'positive_area_intersections':0,'overlapping_texels':0,
                     'covered_srgb_percentiles':np.percentile(covered,[1,50,95,99,100],axis=0).tolist(),
                     'clipped_fraction':float(np.any(covered>=1,axis=1).mean()),
                     'black_fraction':float(np.all(covered<1/255,axis=1).mean())}
        assert atlas_stats['clipped_fraction']<.01,atlas_stats
        # Hidden contacts can have black irradiance; visible surfaces are checked below.
# Sample first-hit surfaces over a full sphere from each actual seated eye.
# This distinguishes visible black bake failures from occluded furniture contacts.
world_tris=[];unlit_triangles=[]
for o in objects:
    unlit_triangles.extend(['unlit' in o.data.materials[0].name.lower()]*len(o.data.loop_triangles))
    world_tris.extend([[tuple(o.matrix_world@o.data.vertices[i].co) for i in t.vertices] for t in o.data.loop_triangles])
world_tris=np.asarray(world_tris)
image=bpy.data.images.load(str(OUT/'Textures/Rooftop_Light.png'),check_existing=True)
pixels=np.empty(N*N*4,np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(N,N,4)
visible=[];missing=[]
for i in range(10):
    origin=scene.objects[f'Spot_{i:03d}'].matrix_world.translation+Vector((0,0,1.25))
    for elevation in np.linspace(-1.45,1.45,25):
        for azimuth in np.linspace(0,2*np.pi,120,endpoint=False):
            direction=Vector((np.cos(elevation)*np.cos(azimuth),np.cos(elevation)*np.sin(azimuth),np.sin(elevation)))
            hit,normal,index,distance=bvh.ray_cast(origin,direction,350)
            if hit is None:missing.append([i,float(elevation),float(azimuth)]);continue
            if unlit_triangles[index]:visible.append(np.ones(3)*.5);continue
            a,b,c=world_tris[index];e=b-a;f=c-a;p=np.asarray(hit)-a
            ee=e@e;ff=f@f;ef=e@f;pe=p@e;pf=p@f;det=ee*ff-ef*ef
            v=(ff*pe-ef*pf)/det;w=(ee*pf-ef*pe)/det
            uv=tris[index,0]*(1-v-w)+tris[index,1]*v+tris[index,2]*w
            ix,iy=np.clip((uv*N).astype(int),0,N-1);visible.append(pixels[iy,ix,:3])
visible=np.asarray(visible);black=float(np.all(visible<1/255,axis=1).mean())
print('Visible surface audit:',len(visible),'rays;',len(missing),'misses;',black,'black fraction')
assert not missing,('Sphere coverage misses',len(missing),missing[:5])
assert black<.01,('Visible black lightmap failures',black)
visible_evidence={'rays':len(visible),'shell_misses':len(missing),'black_fraction':black,
                  'srgb_percentiles':np.percentile(visible,[1,50,99],axis=0).tolist()}
for m in bpy.data.materials:
    if m.name not in materials:continue
    assert m.use_nodes
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    assert bs.inputs['Base Color'].is_linked,(m.name,'missing base texture')
    # Blender's FBX importer inserts a neutral Normal Map node even with no FBX map.
    # Reject actual normal inputs, not that empty importer placeholder.
    if bs.inputs['Normal'].is_linked:
        normal=bs.inputs['Normal'].links[0].from_node
        assert normal.type=='NORMAL_MAP' and not normal.inputs['Color'].is_linked,'Normal texture prohibited'
    assert all(Path(n.image.filepath).name==f'{m.name}_BaseColor.png' for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image)
    image=bs.inputs['Base Color'].links[0].from_node.image
    assert Path(image.filepath).name==f'{m.name}_BaseColor.png'
# No alpha textures and exact spherical longitude/latitude UV0 on reimport.
pano=next(o for o in objects if 'Panorama' in o.name)
center=Vector((0,-.10,1.25));radius=150.;rotation=.31*2*np.pi
for mat in (o.data.materials[0] for o in objects):
    bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    assert not bs.inputs['Alpha'].is_linked and bs.inputs['Alpha'].default_value==1
for poly in pano.data.polygons:
    centroid=pano.matrix_world@poly.center-center
    normal=pano.matrix_world.to_3x3()@poly.normal
    assert normal.dot(centroid)<0,'Panorama faces must face inward'
    for li in poly.loop_indices:
        pt=pano.matrix_world@pano.data.vertices[pano.data.loops[li].vertex_index].co-center
        assert abs(pt.length-radius)<.0001
        uv=np.array(pano.data.uv_layers['UVMap'].data[li].uv)
        v=.5+np.arcsin(np.clip(pt.z/pt.length,-1,1))/np.pi
        assert abs(uv[1]-v)<1e-5
        if abs(pt.z)<149.999:
            u=((rotation-np.arctan2(pt.y,pt.x))/(2*np.pi))%1
            assert abs((uv[0]-u+.5)%1-.5)<1e-5
# Keep the projection plane and its immediate depth reserve empty.
for x in np.linspace(-4.75,4.75,39):
    for z in np.linspace(1.1,5.1,17):
        assert bvh.ray_cast(Vector((x,8.38,z)),Vector((0,1,0)),.12)[0] is None,'Modeled screen face'
# Every seat can see over the parapet into all three city sectors. Model vegetation
# may interrupt individual rays, but not close off a sector. Count the actual rays.
city_views=[]
for i,p in enumerate(seat_points):
    origin=Vector(p)+Vector((0,0,1.25));entry={'spot':f'Spot_{i:03d}'}
    for side,center_angle in [('right',0),('left',np.pi),('back',-np.pi/2)]:
        clear=0;hits=[]
        for a in np.linspace(center_angle-.45,center_angle+.45,31):
            direction=Vector((np.cos(a),np.sin(a),0))
            limits=[]
            if direction.x>1e-6:limits.append((7.05-origin.x)/direction.x)
            if direction.x< -1e-6:limits.append((-7.05-origin.x)/direction.x)
            if direction.y>1e-6:limits.append((9.35-origin.y)/direction.y)
            if direction.y< -1e-6:limits.append((-5.15-origin.y)/direction.y)
            distance=min(t for t in limits if t>0)
            hit=bvh.ray_cast(origin,direction,distance)
            if hit[0] is None:clear+=1
            else:hits.append(list(hit[0]))
        assert clear>=24,(i,side,'city sector blocked',clear)
        entry[side]={'clear':clear,'rays':31}
    city_views.append(entry)
# Confirm each real roof with an actual imported-mesh downward ray.
roof_samples=[(-14,5,.35),(-16,-10,.1),(14,-5,.3),(16,10,.45),(-4,-16,.2),(6,-21,-2),(-22,21,-1.8),(23,21,-2),(27,-18,-3),(-24,-21,-2),(0,27,-1.8),(9,32,-2)]
roof_bvh=bvh_for([o for o in objects if o.name in ('Rooftop_City_Plaster','Rooftop_Limestone')])
for x,y,z in roof_samples:
    hit=roof_bvh.ray_cast(Vector((x,y-.10,z+2)),Vector((0,0,-1)),2.2)[0]
    assert hit is not None and abs(hit.z-(z+.12))<.002,('missing roof geometry',x,y,hit)
    assert 10<=np.hypot(x,y)<=40
# Near roofs are actual mesh components, with a different bearing under head motion.
city=next(o for o in objects if o.name=='Rooftop_City_Plaster')
near=min((city.matrix_world@v.co for v in city.data.vertices),key=lambda v:(v-Vector((-10,10,.35))).length)
far=min((pano.matrix_world@v.co for v in pano.data.vertices),key=lambda v:(v-Vector((-145,5,.35))).length)
assert 10<=near.xy.length<=40 and far.length>140
angles=[]
for dx in [0,.3]:
    eye=Vector((-.375+dx,0,1.25));a=near-eye;b=far-eye
    angles.append(np.arctan2(a.y,a.x)-np.arctan2(b.y,b.x))
parallax=float(abs(angles[1]-angles[0])*180/np.pi);assert parallax>.2
exterior={'city_sectors':city_views,'relative_parallax_30cm_deg':parallax,'parallax_imported_vertices':[list(near),list(far)],'panorama_radius_m':150,
          'panorama_angular_mapping':'full 360x180 longitude/latitude checked on imported vertices',
          'panorama_unlit':True,'opaque_geometry':True,'screen_plane_empty':True,'verified_near_roofs':roof_samples}
result={'exterior':exterior,'validation':'PASS','fbx_reimport':'PASS','fbx_sha256':hashlib.sha256((OUT/'Rooftop.fbx').read_bytes()).hexdigest(),'triangles':stats,'total_triangles':sum(stats.values()),
        'materials':len(materials),'mesh_objects':len(objects),'markers':expected,'seated_head_surface_checks':spots,'head_clearance':head_clearance,
        'sightlines':{'eye_height_m':1.25,'screen_size_m':[9.5,4],'grid':[39,17],'clear':len(sightlines),'blocked':0},
        'atlas':atlas_stats,'visible_surface_light':visible_evidence,'atlas_padding':padding,'textures':textures,'custom_normals':normal_evidence}
(PREVIEW/'rooftop-verification.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))

# Human-readable handoff stays out of Git with all preview and verification output.
lines=['# Dachterrasse – Art-Bericht','',
       f"FBX-Reimport und alle automatischen Prüfungen: **{result['validation']}**.",
       f"{result['total_triangles']:,} Dreiecke / 120.000; {len(materials)} Materialien und Mesh-Batches / 14; zehn Sitzplätze.",
       '', '## Geometrie und Dateien','', '| Material / Mesh | Dreiecke |', '| --- | ---: |']
lines += [f'| {name} | {value:,} |' for name,value in sorted(stats.items())]
lines += ['', 'Die Basisfarben heißen jeweils `Textures/<Material>_BaseColor.png`: 13 × 1024²,',
          'dazu `Rooftop_Panorama_unlit_twinkle10_BaseColor.png` mit 4096×2048 und `Rooftop_Light.png` mit 2048², alle RGB8.',
          'Export: `Assets/TheMovies/Art/Rooftop/Rooftop.fbx`. Kein Leinwandmesh; nur Rahmen und bauliche Wand dahinter.',
          '', '## Prüfungen','',
          '- Seat1–4 exakt an den Standardkoordinaten; Spot_000–009 lückenlos, Boden und Sitzpolster vorhanden.',
          f"- Kopfhülle Ø20 cm, Mittenhöhe 1,10–1,35 m: alle 14 Marker bestanden, garantierter Freiraum ≥{head_clearance['minimum_clearance_lower_bound_m']*100:.2f} cm.",
          '- Gesamte Terrasse starr um Y=−0,10 m verschoben; weitere Spots +0,08 m relativ zu ihren Sitzen. Kein neuer Bake.',
          '- 6.630 freie Sichtstrahlen aus 1,25 m Augenhöhe auf die ganze 9,5 × 4 m Projektionsfläche.',
          '- 930 zusätzliche horizontale Strahlen: jeder Platz hat offene Stadtblicke links, rechts und hinten.',
          '- Zwölf reale Nachbardächer per Reimport-Raytest; messbare Parallaxe zwischen Nahgeometrie und 150-m-Panorama.',
          '- UV0 und globale UV2 vorhanden, keine positiven UV-Flächenüberlappungen oder überlappenden Atlastexel.',
          f"- UV-Inselabstand mindestens 4 px geprüft; Atlasrand {padding['atlas_border_px']:.3f} px.",
          f"- 30.000 Strahlen auf sichtbare Flächen: keine Hülllücken; {black:.3%} schwarze Lichtatlasproben (Grenze <1%).",
          f"- Gesättigte Lichtatlas-Flächentexel: {atlas_stats['clipped_fraction']:.3%}; keine großflächige Überbelichtung.",
          '- Alle Materialien opak; keine Normalmaps; Panorama-Winkelabbildung und nach innen gerichtete Flächen geprüft.',
          '', '## Licht und Gestaltung','',
          'Cycles Diffuse Direct + Indirect ohne Color, 256 Samples, OpenImageDenoise, 8 px Bake-Margin.',
          'PNG mit Standard/None und −1 EV. Vorschauen zeigen Basisfarbe × Atlas ×2, bei Filmlicht zusätzlich ×0,04;',
          'Panorama und ferne Fenster bleiben unlit. Keine Echtzeitlampen und kein Filmlicht in diesen Vorschauen.',
          'Nach Sichtkontrolle: Holzmaserung beruhigt, bodennahes Hafenfoto durch die echte CC0-Hügelstadt bei Nacht ersetzt,',
          'Olivenblätter räumlich modelliert und mit abgeschirmten Topf-Uplights beleuchtet. Die Lichtatlas-Auflösung',
          'wird auf Möbel und Vegetation gewichtet; entfernte Architektur erhält geringere Texeldichte.',
          '', '## Vorschauen','',
          '- [Seat2, Licht an](rooftop-seat2-on.png) · [Seat2, 4 %](rooftop-seat2-04pct.png)',
          '- [Links](rooftop-left.png) · [Rechts](rooftop-right.png) · [Stadt hinten](rooftop-back.png)',
          '- [Übersicht](rooftop-overview.png) · [Lounge](rooftop-lounge.png)',
          '', 'Quelle: Clarens Night 02, Dimitrios Savva / Jarod Guest, Poly Haven, CC0; vollständige Herkunft und Bearbeitung in `docs/CREDITS.md`.',
          'Reproduzierbarer Build: `tools/blender/rooftop.py`; unabhängiger Exporttest: `tools/blender/verify_rooftop.py`.',
          'Maschinenlesbare Details: `rooftop-metrics.json` und `rooftop-verification.json`.',
          '', 'Gestaltung auf Quest 3 vom Nutzer als ausgezeichnet bestätigt. Erneute Kopfpositionskontrolle im Headset bleibt offen.',
          'Unity wurde nicht gestartet. Kino/Heimkino, App-Code, Packages und ProjectSettings bleiben unverändert.',
          'Logs, Blender-Checkpoints und Rohquellen sind von Git ausgeschlossen.']
(PREVIEW/'ROOFTOP-REPORT.md').write_text('\n'.join(lines)+'\n')

# Reference rooms are imported read-only and reported, never repaired or exported.
# Their failures are explicit findings, not a reason to silently change accepted art.
room_heads={'Rooftop':head_clearance}
for room_name in ('Home','Cinema'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/f'Assets/TheMovies/Art/{room_name}/{room_name}.fbx'))
    expected_names={f'Seat{i+1}' for i in range(4)}|{f'Spot_{i:03d}' for i in range(4 if room_name=='Home' else 152)}
    actual_names={o.name for o in bpy.context.scene.objects if o.name.startswith(('Seat','Spot_'))}
    assert actual_names==expected_names,(room_name,'missing or unexpected seat markers')
    room_heads[room_name]=audit_heads([o for o in bpy.context.scene.objects if o.type=='MESH'],bpy.context.scene.objects)
(PREVIEW/'all-rooms-head-clearance.json').write_text(json.dumps(room_heads,indent=2)+'\n')
with (PREVIEW/'ROOFTOP-REPORT.md').open('a') as report:
    report.write('\n## Kopffreiheit aller Räume (importierte FBX-Geometrie)\n\n')
    for name,check in room_heads.items():
        summary=f"{name}: {check['marker_count']-len(check['failed_markers'])}/{check['marker_count']} Marker bestanden; Mindestfreiraum {check['minimum_clearance_lower_bound_m']*100:.2f} cm."
        print(summary)
        report.write('- '+summary+'\n')
        if check['failed_markers']:
            report.write('  Fehlgeschlagen, unverändert: '+', '.join(check['failed_markers'])+'.\n')
    report.write('\nVollständige Einzelmessungen inkl. nominaler 1,25-m-Höhe: `all-rooms-head-clearance.json`.\n')
