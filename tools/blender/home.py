"""Reproducible Quest 3 Home. Blender 5.2, no add-ons or Unity invocation.

/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/blender/home.py -- Assets/TheMovies/Art/Home
Optional --preview-only uses the ignored Home-baked.blend checkpoint.
Optional --geometry-only creates a draft without replacing the delivered FBX.
"""
import sys
sys.dont_write_bytecode=True
import json
import math
import time
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from lib_home_textures import fetch,make_texture
from lib_home_room import build
from lib_home_uvs import uv_overlaps

ROOT=Path(__file__).resolve().parents[2]
ARGS=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
OUT=(ROOT/(ARGS[0] if ARGS else 'Assets/TheMovies/Art/Home')).resolve()
assert OUT==ROOT/'Assets/TheMovies/Art/Home','Output must be the Home folder in this worktree'
TEX=OUT/'Textures';PREVIEW=ROOT/'Logs/art-preview';BLEND=ROOT/'art/blender'
for p in [TEX,PREVIEW,BLEND]:p.mkdir(parents=True,exist_ok=True)
START=time.time()

def log(s):print('[Home] '+s,flush=True)
def aim(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
def light(name,loc,power,color=(1,.75,.48),size=.2,target=None):
    d=bpy.data.lights.new(name,'AREA' if target else 'POINT');d.energy=power;d.color=color
    if target:d.shape='DISK';d.size=size
    else:d.shadow_soft_size=size
    o=bpy.data.objects.new(name,d);bpy.context.scene.collection.objects.link(o);o.location=loc
    if target:aim(o,target)

def configure_cycles(scene):
    scene.render.engine='CYCLES';scene.cycles.samples=1024;scene.cycles.use_denoising=True
    scene.cycles.max_bounces=8;scene.cycles.diffuse_bounces=6
    prefs=bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type='METAL';prefs.get_devices()
        available=[d for d in prefs.devices if d.type=='METAL']
        for d in prefs.devices:d.use=d.type=='METAL'
        scene.cycles.device='GPU' if available else 'CPU'
    except Exception:scene.cycles.device='CPU'

def preview():
    scene=bpy.context.scene
    atlas=bpy.data.images.load(str(TEX/'Home_Light.png'),check_existing=True)
    dimmers=[]
    for m in bpy.data.materials:
        if not m.get('home'):continue
        nt=m.node_tree;nt.nodes.clear()
        out=nt.nodes.new('ShaderNodeOutputMaterial');em=nt.nodes.new('ShaderNodeEmission')
        base=nt.nodes.new('ShaderNodeTexImage');base.image=bpy.data.images.load(str(TEX/f'{m.name}_BaseColor.png'),check_existing=True)
        uv=nt.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';nt.links.new(uv.outputs['UV'],base.inputs['Vector'])
        lm=nt.nodes.new('ShaderNodeTexImage');lm.image=atlas;lm.extension='EXTEND'
        uv2=nt.nodes.new('ShaderNodeUVMap');uv2.uv_map='Lightmap';nt.links.new(uv2.outputs['UV'],lm.inputs['Vector'])
        mul=nt.nodes.new('ShaderNodeMixRGB');mul.blend_type='MULTIPLY';mul.inputs[0].default_value=1
        nt.links.new(base.outputs['Color'],mul.inputs[1]);nt.links.new(lm.outputs['Color'],mul.inputs[2])
        nt.links.new(mul.outputs[0],em.inputs['Color']);em.inputs['Strength'].default_value=2
        nt.links.new(em.outputs[0],out.inputs['Surface']);dimmers.append(em.inputs['Strength'])
    for o in list(scene.objects):
        if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
    scene.world.color=(0,0,0);scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=0
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0
    scene.compositing_node_group=None
    configure_cycles(scene);scene.cycles.samples=48
    scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
    data=bpy.data.cameras.new('Home preview');cam=bpy.data.objects.new('Home preview',data);scene.collection.objects.link(cam);scene.camera=cam
    views=[('home-seat2-on',(-.375,0,1.25),(0,5.2,1.55),18,1),
           ('home-seat2-04pct',(-.375,0,1.25),(0,5.2,1.55),18,.04),
           ('home-overview',(3.25,-1.70,2.44),(-.5,1.55,.95),16,1),
           ('home-fireplace',(-.375,0,1.25),(-4.0,1.85,1.36),21,1),
           ('home-library',(-.375,0,1.25),(0,-2.15,1.48),18,1),
           ('home-sofa',(2.8,2.75,1.85),(0,-.10,.75),27,1),
           ('home-window',(-.375,0,1.25),(4.0,2.72,1.50),21,1),
           ('home-entry',(-.375,0,1.25),(4.1,-1.15,1.35),23,1)]
    for name,loc,target,lens,dim in views:
        cam.location=loc;aim(cam,target);data.lens=lens;data.clip_start=.04;data.clip_end=30
        for sock in dimmers:sock.default_value=2*dim
        scene.render.filepath=str(PREVIEW/f'{name}.png');bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam,do_unlink=True)
    log('Eight baked-only previews complete (including 4% light; no added screen fill)')

if '--preview-only' in ARGS:
    bpy.ops.wm.open_mainfile(filepath=str(BLEND/'Home-baked.blend'));preview();sys.exit(0)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
materials={}
def material(key,name,color,kind='',source=None,emission=0):
    m=bpy.data.materials.new(name);m.use_nodes=True;m['home']=True;m.diffuse_color=(*color,1)
    bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.85
    img=make_texture(name,TEX/f'{name}_BaseColor.png',color,kind,source)
    tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=img
    uv=m.node_tree.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap'
    m.node_tree.links.new(uv.outputs['UV'],tex.inputs['Vector']);m.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    if emission:
        m.node_tree.links.new(tex.outputs['Color'],bs.inputs['Emission Color']);bs.inputs['Emission Strength'].default_value=emission
    materials[key]=m
material('Walnut','Home_Walnut',(.32,.175,.087),'wood',source=fetch(ROOT,'dark_wood'))
material('Parquet','Home_Parquet',(.88,.79,.68),source=fetch(ROOT,'herringbone_parquet'))
material('Plaster','Home_Warm_Plaster',(.64,.61,.51))
material('Velvet','Home_Velvet_Forest',(.12,.245,.195),'velvet')
material('Rug','Home_Persian_Rug',(.3,.05,.03),'rug')
material('Brass','Home_Brass',(.61,.43,.21))
material('Stone','Home_Limestone',(.66,.61,.51),'stone')
material('Charcoal','Home_Charcoal',(.060,.047,.039))
material('Books','Home_Books_Decor',(.3,.2,.1),'books')
material('Linen','Home_Linen_emit',(.92,.77,.50),'linen',emission=.22)
material('Embers','Home_Embers_glow',(.8,.12,.01),'embers',emission=2.2)
material('Night','Home_Night_unlit',(.03,.05,.1),'night',emission=.25)
meshes=build(materials,light)
for i,x in enumerate([-1.125,-.375,.375,1.125]):
    for name in [f'Seat{i+1}',f'Spot_{i:03d}']:
        o=bpy.data.objects.new(name,None);scene.collection.objects.link(o);o.location=(x,0,0);o.empty_display_type='ARROWS'
for name,z in [('ScreenAnchor',.78),('ScreenTop',2.63)]:
    o=bpy.data.objects.new(name,None);scene.collection.objects.link(o);o.location=(0,5.2,z)
scene.world=bpy.data.worlds.new('Home ambient');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.26,.22,.17,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.32
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join();room=bpy.context.object;room.name='Home'
bm=bmesh.new();bm.from_mesh(room.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001)
bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(room.data);bm.free()
room.data.update();room.data.normals_split_custom_set([tuple(n.vector) for n in room.data.corner_normals])
assert len(room.data.polygons)<=120000,len(room.data.polygons)
log(f'Built {len(room.data.polygons)} triangles, {len(materials)} materials')
if '--geometry-only' in ARGS:
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND/'Home-draft.blend'));sys.exit(0)
room.data.uv_layers[0].name='UVMap';lm=room.data.uv_layers.new(name='Lightmap');room.data.uv_layers.active=lm
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=1.25,island_margin=0,area_weight=.5,scale_to_bounds=True)
bpy.ops.uv.pack_islands(rotate=True,margin_method='FRACTION',margin=4.1/2048,shape_method='CONVEX')
bpy.ops.object.mode_set(mode='OBJECT')
repaired=0
for repair in range(5):
    room.data.calc_loop_triangles();layer=room.data.uv_layers['Lightmap']
    tris=np.array([[tuple(layer.data[i].uv) for i in t.loops] for t in room.data.loop_triangles])
    overlaps=uv_overlaps(tris)
    ab=tris[:,1]-tris[:,0];ac=tris[:,2]-tris[:,0]
    degenerate=np.flatnonzero(abs(ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0])<1e-13).tolist()
    if not overlaps and not degenerate:break
    assert repair<4,('Unresolved UV overlaps',len(overlaps))
    split=sorted({room.data.loop_triangles[j].polygon_index for i,j in overlaps}|{room.data.loop_triangles[j].polygon_index for j in degenerate})
    world_area=sum(p.area for p in room.data.polygons)
    uv_area=float(np.abs(ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0]).sum())/2
    scale=math.sqrt(uv_area/world_area)
    for k,index in enumerate(split):
        poly=room.data.polygons[index]
        pts=[room.data.vertices[v].co for v in poly.vertices]
        edge=(pts[1]-pts[0]);axis=edge.normalized();other=poly.normal.cross(axis).normalized()
        # Project isolated faces in their own plane; preserve area relative to existing UVs.
        for li,pt in zip(poly.loop_indices,pts):
            delta=pt-pts[0];layer.data[li].uv=(2+(k%32)*.1+delta.dot(axis)*scale,2+(k//32)*.1+delta.dot(other)*scale)
    repaired+=len(split);log(f'Repairing {len(split)} folded UV faces')
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True,margin_method='FRACTION',margin=4.1/2048,shape_method='CONVEX')
    bpy.ops.object.mode_set(mode='OBJECT')
log('UV intersection audit passed')
image=bpy.data.images.new('Home_Light',2048,2048,float_buffer=True)
for m in room.data.materials:
    n=m.node_tree.nodes.new('ShaderNodeTexImage');n.name='BakeTarget';n.image=image;m.node_tree.nodes.active=n
configure_cycles(scene);scene.render.bake.margin=8
log('Starting 2048² / 1024 sample Cycles DIFFUSE Direct + Indirect bake')
bpy.ops.object.bake(type='DIFFUSE',pass_filter={'DIRECT','INDIRECT'},use_clear=True,margin=8)
scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=-1;scene.view_settings.gamma=1
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
ng=bpy.data.node_groups.new('Home Atlas Denoise','CompositorNodeTree')
ng.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor');scene.compositing_node_group=ng
node=ng.nodes.new('CompositorNodeImage');node.image=image;denoise=ng.nodes.new('CompositorNodeDenoise');out=ng.nodes.new('NodeGroupOutput')
ng.links.new(node.outputs['Image'],denoise.inputs['Image']);ng.links.new(denoise.outputs['Image'],out.inputs['Image'])
data=bpy.data.cameras.new('Home atlas');cam=bpy.data.objects.new('Home atlas',data);scene.collection.objects.link(cam);scene.camera=cam
room.hide_render=True;scene.render.resolution_x=2048;scene.render.resolution_y=2048;scene.render.resolution_percentage=100
scene.cycles.samples=1;scene.render.filepath=str(TEX/'Home_Light.png');bpy.ops.render.render(write_still=True)
room.hide_render=False;scene.compositing_node_group=None;bpy.data.node_groups.remove(ng)
for m in room.data.materials:m.node_tree.nodes.remove(m.node_tree.nodes['BakeTarget'])
room.data.uv_layers.active=room.data.uv_layers['UVMap']
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.separate(type='MATERIAL');bpy.ops.object.mode_set(mode='OBJECT')
for o in list(scene.objects):
    if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
objects=[o for o in scene.objects if o.type=='MESH']
for o in objects:
    o.name=next(m.name for m in o.data.materials if m);o.data.uv_layers.active=o.data.uv_layers['UVMap']
scene.cycles.samples=1024
stats={o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects}
report={'triangles':stats,'total_triangles':sum(stats.values()),'materials':sorted(m.name for m in materials.values()),
        'mesh_objects':len(objects),'seating_places':4,'lightmap':[2048,2048],'bake_samples':1024,
        'bake_passes':['DIRECT','INDIRECT'],'bake_margin_px':8,'pack_margin_uv':4.1/2048,
        'atlas_denoise':'OpenImageDenoise compositor, linear irradiance','view':'Standard / None / exposure -1',
        'device':scene.cycles.device,'blender':bpy.app.version_string,'repaired_uv_faces':repaired,
        'elapsed_seconds':round(time.time()-START)}
assert report['total_triangles']<=120000 and len(objects)<=12
bpy.ops.export_scene.fbx(filepath=str(OUT/'Home.fbx'),use_selection=False,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
scene['home_build_metrics']=json.dumps(report)
(PREVIEW/'home-metrics.json').write_text(json.dumps(report,indent=2)+'\n')
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND/'Home-baked.blend'))
preview();log(f'Complete: {report["total_triangles"]} triangles, {len(objects)} material batches')
