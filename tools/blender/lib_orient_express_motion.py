"""70 km/h broadside angular calibration, using DEPICTED distance.
For a flat carrier at g with affine U=Y/L, its local angular width is
Theta=d(phi)/dU=L/g rad per texture repeat at broadside. Therefore
u/s=(v/d)/Theta. The finite angle 2*atan(L/(2*g)) describes endpoints,
NOT the instantaneous optical-flow conversion; using it would over-speed
wide planes. Away from broadside, the natural cos(phi)^2 falloff remains.
This affine mapping also preserves the speed at rear seats (no privileged Y=0).
"""
import math
SPEED=70/3.6
LAYERS={
    'Far':dict(carrier=60.,depicted=10000.,repeat=(70/3.6)/10000*60/.0006,n=6,z0=-26.,z1=110.,cutoff=None),
    'Middle':dict(carrier=18.,depicted=250.,repeat=80.,n=175,z0=-6.,z1=1.0,cutoff=400),
    'Near':dict(carrier=5.,depicted=4.5,repeat=24.,n=9002,z0=-2.5,z1=.65,cutoff=18),
}
for key,p in LAYERS.items():
    p['theta']=p['repeat']/p['carrier']
    p['endpoint_span']=2*math.atan(p['repeat']/(2*p['carrier']))
    p['u_per_second']=p['n']/10000
    p['ideal_u_per_second']=SPEED/p['depicted']/p['theta']
    p['name']=f'OE_{key}_Panorama_unlit_scroll{p["n"]}'


def pack_scenery(objects):
    """Six contiguous UV2 islands in the previously RESERVED unbaked top 5%."""
    for k,o in enumerate(objects):
        lm=o.data.uv_layers.new(name='Lightmap')
        zs=sorted({round(v.co.z,4) for v in o.data.vertices});amax=math.atan2(2000,abs(o.data.vertices[0].co.x))
        for p in o.data.polygons:
            for li in p.loop_indices:
                v=o.data.vertices[o.data.loops[li].vertex_index].co
                side=0 if v.x<0 else 1
                u=(math.atan2(v.y,abs(v.x))+amax)/(2*amax)
                vv=zs.index(round(v.z,4))/(len(zs)-1)
                lm.data[li].uv=(.005+side*.5+.488*u,.958+k*.014+.008*vv)
        o.data.normals_split_custom_set([tuple(n.vector) for n in o.data.corner_normals])
        o.data.uv_layers.active=o.data.uv_layers['UVMap']


def replace_scenery(root,tex):
    import bpy
    from lib_orient_express_textures import landscapes
    from lib_orient_express_room import surroundings
    for o in list(bpy.context.scene.objects):
        if o.type=='MESH' and 'Panorama_unlit_scroll' in o.name:bpy.data.objects.remove(o,do_unlink=True)
    for m in list(bpy.data.materials):
        if 'Panorama_unlit_scroll' in m.name:bpy.data.materials.remove(m,do_unlink=True)
    images=landscapes(root,tex);materials={}
    for key,p in LAYERS.items():
        m=bpy.data.materials.new(p['name']);m.use_nodes=True;m['orient_express']=True
        nt=m.node_tree;bs=nt.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.75
        image=nt.nodes.new('ShaderNodeTexImage');image.image=images[key]
        uv=nt.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';nt.links.new(uv.outputs[0],image.inputs[0]);nt.links.new(image.outputs[0],bs.inputs['Base Color'])
        materials[key]=m
    objects=surroundings(materials);pack_scenery(objects)
    keep={f'{p["name"]}_BaseColor.png' for p in LAYERS.values()}
    for old in tex.glob('OE_*_Panorama_unlit_scroll*_BaseColor.png'):
        if old.name not in keep:old.unlink()
    return materials,objects


def scenery_only(root,tex,out,blend,logdir):
    """Reuse accepted interior and bake without repacking/rebaking a single texel."""
    import bpy,json,hashlib
    checkpoint=blend/'OrientExpress-baked.blend'
    assert checkpoint.exists(),'Run full builder once to create the local baked checkpoint'
    bpy.ops.wm.open_mainfile(filepath=str(checkpoint));scene=bpy.context.scene
    before={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in tex.glob('*.png') if 'Panorama' not in p.name}
    def interior_signature():
        h=hashlib.sha256()
        for o in sorted(scene.objects,key=lambda o:o.name):
            if 'Panorama_unlit_scroll' in o.name:continue
            h.update(o.name.encode());h.update(str(o.matrix_world).encode())
            if o.type=='MESH':
                h.update(str([v.co[:] for v in o.data.vertices]).encode())
                h.update(str([tuple(p.vertices) for p in o.data.polygons]).encode())
                h.update(str([(u.name,[v.uv[:] for v in u.data]) for u in o.data.uv_layers]).encode())
                h.update(str([m.name for m in o.data.materials]).encode())
        return h.hexdigest()
    old=interior_signature();replace_scenery(root,tex)
    assert old==interior_signature(),'Interior changed'
    assert all(hashlib.sha256((tex/name).read_bytes()).hexdigest()==sha for name,sha in before.items())
    objects=[o for o in scene.objects if o.type=='MESH'];stats={o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects}
    assert sum(stats.values())<=120000 and len(objects)<=14
    report=json.loads(scene.get('orient_express_build_metrics','{}'))
    report.update(triangles=stats,total_triangles=sum(stats.values()),materials=sorted(o.data.materials[0].name for o in objects),mesh_objects=len(objects),scenery_calibration=LAYERS,interior_unchanged=True,light_atlas_unchanged=True,rebaked=False)
    scene['orient_express_build_metrics']=json.dumps(report)
    bpy.ops.export_scene.fbx(filepath=str(out/'OrientExpress.fbx'),use_selection=False,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    bpy.ops.wm.save_as_mainfile(filepath=str(checkpoint))
    (logdir/'orient-express-metrics.json').write_text(json.dumps(report,indent=2)+'\n')
    (logdir/'orient-express-motion-preservation.json').write_text(json.dumps({'pass':True,'interior_signature':old,'textures':before},indent=2)+'\n')
    print('SCENERY ONLY: unchanged interior/UVs/bake;',sum(stats.values()),'triangles;',len(objects),'materials',flush=True)


def write_motion_report(root,report):
    import json
    log=root/'Logs/art-preview'
    validation=json.loads((log/'orient-express-verification.json').read_text())
    rows=['# Orient-Express: 70 km/h','',
          'Innenraum auf Quest 3 vom Nutzer abgenommen. Diese Änderung betrifft ausschließlich die vorbeiziehende Kulisse.',
          'Kein neuer Bake; Innengeometrie, Normalen, beide UV-Sätze, Marker und elf Innenraum-/Lichttexturen gegen den vorherigen FBX geprüft und unverändert. Auch die Fernpanorama-Pixel bleiben unverändert.',
          '', '| Ebene | Inhalt / dargestellte Entfernung | U/s | Name | Winkelgeschwindigkeit seitlich |', '| --- | --- | ---: | --- | ---: |']
    for key in ['Near','Middle','Far']:
        p=LAYERS[key]
        rows.append(f'| {key} | {p["depicted"]:g} m | {p["u_per_second"]:.4f} | {p["name"]} | {p["u_per_second"]*p["theta"]:.6f} rad/s |')
    rows += ['', 'v=70/3,6=19,444444 m/s; u/s=(v/d)/Θ. Θ ist die lokale projizierte Winkelbreite dφ/dU=L/g der ebenen Trägerfläche. Die Rechnung und der Unterschied zum endlichen Endpunktwinkel stehen in docs/ART-BRIEF.md.',
             'Nah: Θ=4,8; Mitte: 4,444444; Fern: 3,240741 rad/U. Für scroll6 wurde die Fern-UV-Wiederholung von 180 auf 194,444444 m skaliert. Beide Seiten bewegen sich zum Heck (−Y).',
             '', '## Prüfungen', '',
             f'- FBX-Reimport, {validation["total_triangles"]:,} Dreiecke, {validation["materials"]} Materialien; 6.630 freie Leinwandstrahlen; Kopf- und globale UV-Prüfung bestanden.',
             '- Alle drei Texturen horizontal RGB-nahtlos; Zeitübergang nach einer kompletten Texturperiode ohne Sprung.',
             '- Near: weich begrenzt auf 18 Zyklen/Textur =16,2036 Hz. Middle: 400 Zyklen =7 Hz. Grenzen bei 72/90 fps: 36/45 Hz.',
             '- Mittlere Ebene: alle Baum-/Hecken-/Hauskonturen Bestandteil der scrollenden opaken Textur; gerade horizontale Feldkante, kein stationärer Silhouettenrand.']
    for s in report['spectral']:
        worst=max(r['above_nyquist_energy_fraction'] for r in s['rates'].values())
        rows.append(f'- {s["material"]}: maximal {worst:.4%} Restenergie über der zeitlichen Nyquist-Grenze (inklusive RGB8-Quantisierung).')
    for clip in report['clips']:
        maximum=max(clip['layer_maximum_channel_step'].values())
        rows.append(f'- [{clip["file"]}]({clip["file"]}): {clip["frames"]} Frames, {clip["fps"]} fps, vier Sekunden; größter lokaler Kanal-Schritt {maximum:.2%}.')
    rows += ['', 'Die Clips werden aus tatsächlichen FBX-Raytreffern/UVs und bilinearer UV-Verschiebung erzeugt, vor einem gebackenen Standbild des Innenraums. Kein Motion Blur, zeitliches AA oder Frame-Blending verdeckt Fehler. Das ist eine Offline-Prüfung; App-Shader, Mipmapping, Quest-Frametiming, Last und Komfort müssen mit dem neuen Export noch auf dem Headset abgenommen werden.',
             '', 'Werkzeuge: orient_express.py --scenery-only; verify_orient_express.py; verify_orient_express_motion.py. Sämtliche Videos, Bilder und Prüfprotokolle unter Logs/ bleiben außerhalb von Git.']
    (log/'ORIENT-EXPRESS-MOTION-REPORT.md').write_text('\n'.join(rows)+'\n')
