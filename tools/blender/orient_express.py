"""Build, bake, export and preview OrientExpress; Blender 5.2. No Unity or add-ons.
Blender -b --factory-startup -P tools/blender/orient_express.py -- Assets/TheMovies/Art/OrientExpress
--scenery-only: preserve accepted interior and bake, replace scenery only.
--geometry-only: ignored draft checkpoint; --preview-only: reuse baked checkpoint.
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
from lib_orient_express_textures import texture
from lib_orient_express_motion import replace_scenery,scenery_only
from lib_orient_express_room import build,surroundings,SEATS,SCREEN
from lib_orient_express_uvs import uv_overlaps
from lib_orient_express_preview import preview,aim
from lib_seated_head_clearance import audit as audit_heads

ROOT=Path(__file__).resolve().parents[2]
ARGS=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
OUT=(ROOT/(ARGS[0] if ARGS else 'Assets/TheMovies/Art/OrientExpress')).resolve()
assert OUT==ROOT/'Assets/TheMovies/Art/OrientExpress','Output must be OrientExpress in this worktree'
TEX=OUT/'Textures';PREVIEW=ROOT/'Logs/art-preview';BLEND=ROOT/'art/blender'
for p in [TEX,PREVIEW,BLEND]:p.mkdir(parents=True,exist_ok=True)
START=time.time()
def log(s):print('[OrientExpress] '+s,flush=True)
def light(name,loc,power,color,size,target):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=color;d.shape='DISK';d.size=size
    o=bpy.data.objects.new(name,d);bpy.context.scene.collection.objects.link(o);o.location=loc;aim(o,target)
def configure_cycles(scene):
    scene.render.engine='CYCLES';scene.cycles.samples=256;scene.cycles.use_denoising=True
    scene.cycles.max_bounces=8;scene.cycles.diffuse_bounces=4
    prefs=bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type='METAL';prefs.get_devices()
        available=[d for d in prefs.devices if d.type=='METAL']
        for d in prefs.devices:d.use=d.type=='METAL'
        scene.cycles.device='GPU' if available else 'CPU'
    except Exception:scene.cycles.device='CPU'

def marker(name,loc):
    o=bpy.data.objects.new(name,None);bpy.context.scene.collection.objects.link(o);o.location=loc;o.empty_display_type='ARROWS';return o

if '--preview-only' in ARGS:
    bpy.ops.wm.open_mainfile(filepath=str(BLEND/'OrientExpress-baked.blend'));preview(ROOT,TEX,configure_cycles,'--quick' in ARGS);sys.exit(0)
if '--scenery-only' in ARGS:
    scenery_only(ROOT,TEX,OUT,BLEND,PREVIEW)
    if '--no-previews' not in ARGS:preview(ROOT,TEX,configure_cycles,'--quick' in ARGS)
    sys.exit(0)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
materials={}
def material(key,name,color,kind='',emission=0,img=None):
    m=bpy.data.materials.new(name);m.use_nodes=True;m['orient_express']=True;m.diffuse_color=(*color,1)
    bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.75
    image=img or texture(TEX/f'{name}_BaseColor.png',color,kind)
    tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
    uv=m.node_tree.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';m.node_tree.links.new(uv.outputs[0],tex.inputs[0]);m.node_tree.links.new(tex.outputs[0],bs.inputs['Base Color'])
    if emission:m.node_tree.links.new(tex.outputs[0],bs.inputs['Emission Color']);bs.inputs['Emission Strength'].default_value=emission
    materials[key]=m
material('Ivory','OE_Ivory',(.73,.69,.57))
material('Wood','OE_Walnut',(.33,.18,.085),'wood')
material('Inlay','OE_Botanical_Marquetry',(.34,.19,.09),'inlay')
material('Brass','OE_Polished_brass',(.61,.44,.23),'brass')
material('Velvet','OE_Petrol_velvet_Fabric',(.10,.25,.27),'velvet')
material('Curtain','OE_Champagne_velvet',(.55,.40,.21),'curtain')
material('Carpet','OE_ArtDeco_Carpet',(.2,.2,.2),'carpet')
material('Dark','OE_Ebony',(.075,.065,.055),'wood')
material('Glass','OE_Etched_glow',(.80,.73,.55),'glass',emission=.6)
material('Shade','OE_Silk_glow',(.90,.71,.42),'shade',emission=.7)
meshes=build(materials,light)
for i,loc in enumerate(SEATS):
    marker(f'Spot_{i:03d}',loc)
    if i<4:marker(f'Seat{i+1}',loc)
marker('ScreenAnchor',SCREEN);marker('ScreenTop',(SCREEN[0],SCREEN[1],SCREEN[2]+1.5))
bpy.context.view_layer.update()
heads=audit_heads(meshes,scene.objects)
(PREVIEW/'orient-express-build-heads.json').write_text(json.dumps(heads,indent=2)+'\n')
assert heads['pass'],heads['failed_markers']
scene.world=bpy.data.worlds.new('OrientExpress bake ambient');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.35,.46,.65,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.20
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join();room=bpy.context.object;room.name='OrientExpress interior'
bm=bmesh.new();bm.from_mesh(room.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001)
bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS');bm.to_mesh(room.data);bm.free()
room.data.update();room.data.normals_split_custom_set([tuple(n.vector) for n in room.data.corner_normals])
log(f'Interior: {len(room.data.polygons)} triangles; heads PASS')
if '--geometry-only' in ARGS:
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND/'OrientExpress-draft.blend'));sys.exit(0)
room.data.uv_layers[0].name='UVMap';lm=room.data.uv_layers.new(name='Lightmap');room.data.uv_layers.active=lm
# Furniture is allocated extra lightmap density; unwrap temporary component scale,
# then restore exact geometry before baking. Reference-pipeline algorithm.
parent=list(range(len(room.data.vertices)))
def root(i):
    while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
    return i
for edge in room.data.edges:
    a,b=edge.vertices;parent[root(a)]=root(b)
components={}
for v in room.data.vertices:components.setdefault(root(v.index),[]).append(v.index)
original={}
for ids in components.values():
    coords=np.array([room.data.vertices[i].co[:] for i in ids]);center=coords.mean(axis=0)
    furniture=abs(center[0])<1.4 and -8.5<center[1]<.8 and center[2]>.04 and np.ptp(coords,axis=0).max()<2
    thin=np.ptp(coords,axis=0).min()<.065 and np.ptp(coords,axis=0).max()>.2
    factor=4.0 if thin else 2.4 if furniture else 1
    if factor!=1:
        for i in ids:
            v=room.data.vertices[i];original[i]=v.co.copy();v.co=Vector(center)+(v.co-Vector(center))*factor
room.data.update()
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.25,island_margin=0,area_weight=.5,scale_to_bounds=True);bpy.ops.object.mode_set(mode='OBJECT')
for i,co in original.items():room.data.vertices[i].co=co
room.data.update()
# Planar closed coves otherwise become giant hollow rectangular UV islands.
# Cut only low-fill, roughly square islands into eight connected sectors before
# packing; keep every triangle's shape and area. This recovers the empty centers
# without wasting the 2048 atlas or sacrificing the mandatory 4px gutters.
room.data.calc_loop_triangles();layer=room.data.uv_layers['Lightmap']
uv_faces=[np.array([tuple(layer.data[li].uv) for li in p.loop_indices]) for p in room.data.polygons]
uparent=list(range(len(uv_faces)))
def uroot(i):
    while uparent[i]!=i:uparent[i]=uparent[uparent[i]];i=uparent[i]
    return i
edges={}
for i,pts in enumerate(uv_faces):
    for a,b in zip(pts,np.roll(pts,-1,axis=0)):
        key=tuple(sorted((tuple(a),tuple(b))))
        if key in edges:uparent[uroot(i)]=uroot(edges[key])
        else:edges[key]=i
islands={}
for i in range(len(uv_faces)):islands.setdefault(uroot(i),[]).append(i)
ring_cuts=0
for ids in islands.values():
    pts=np.concatenate([uv_faces[i] for i in ids]);lo=pts.min(axis=0);hi=pts.max(axis=0);size=hi-lo
    if min(size)<1e-7 or len(ids)<24:continue
    area=sum(abs(np.cross(uv_faces[i][1]-uv_faces[i][0],uv_faces[i][2]-uv_faces[i][0]))/2 for i in ids)
    if area/(size[0]*size[1])>.42 or not .35<size[0]/size[1]<2.85:continue
    center=(hi+lo)/2
    for i in ids:
        d=(uv_faces[i].mean(axis=0)-center)/size;sector=int((math.atan2(d[1],d[0])+math.pi)/(math.tau)*8)%8
        offset=Vector((2+(ring_cuts%4)*2+sector*.2,2+(ring_cuts//4)*2))
        for li in room.data.polygons[i].loop_indices:layer.data[li].uv+=offset
    ring_cuts+=1
log(f'Cut {ring_cuts} hollow UV islands into compact sectors')
def pack():
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True,margin_method='FRACTION',margin=5.2/2048,shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')
pack();repaired=0
for repair in range(6):
    room.data.calc_loop_triangles();layer=room.data.uv_layers['Lightmap']
    tris=np.array([[tuple(layer.data[i].uv) for i in t.loops] for t in room.data.loop_triangles])
    overlaps=uv_overlaps(tris);ab=tris[:,1]-tris[:,0];ac=tris[:,2]-tris[:,0]
    degenerate=np.flatnonzero(abs(ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0])<1e-13).tolist()
    if not overlaps and not degenerate:break
    assert repair<5,('UV overlaps',len(overlaps),len(degenerate))
    split=sorted({room.data.loop_triangles[j].polygon_index for i,j in overlaps}|{room.data.loop_triangles[j].polygon_index for j in degenerate})
    scale=math.sqrt(float(np.abs(ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0]).sum())/2/sum(p.area for p in room.data.polygons))
    for k,index in enumerate(split):
        poly=room.data.polygons[index];pts=[room.data.vertices[v].co for v in poly.vertices];axis=(pts[1]-pts[0]).normalized();other=poly.normal.cross(axis).normalized()
        for li,pt in zip(poly.loop_indices,pts):
            delta=pt-pts[0];layer.data[li].uv=(2+(k%32)*.1+delta.dot(axis)*scale,2+(k//32)*.1+delta.dot(other)*scale)
    repaired+=len(split);log(f'Repairing {len(split)} folded faces');pack()
# Reserve the top 5% of the atlas for the three unbaked scenery bands. They are
# never baked or read by unlit materials, but have valid global UV2 too.
for d in room.data.uv_layers['Lightmap'].data:d.uv.y*=.95
log('UV audit passed; starting Cycles 256-sample diffuse-only bake')
image=bpy.data.images.new('OrientExpress_Light',2048,2048,float_buffer=True)
for m in room.data.materials:
    n=m.node_tree.nodes.new('ShaderNodeTexImage');n.name='BakeTarget';n.image=image;m.node_tree.nodes.active=n
configure_cycles(scene);scene.render.bake.margin=8
bpy.ops.object.bake(type='DIFFUSE',pass_filter={'DIRECT','INDIRECT'},use_clear=True,margin=8)
scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=-1;scene.view_settings.gamma=1
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
ng=bpy.data.node_groups.new('OrientExpress Atlas Denoise','CompositorNodeTree');ng.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor');scene.compositing_node_group=ng
node=ng.nodes.new('CompositorNodeImage');node.image=image;denoise=ng.nodes.new('CompositorNodeDenoise');out=ng.nodes.new('NodeGroupOutput');ng.links.new(node.outputs['Image'],denoise.inputs['Image']);ng.links.new(denoise.outputs['Image'],out.inputs['Image'])
data=bpy.data.cameras.new('OrientExpress atlas');cam=bpy.data.objects.new('OrientExpress atlas',data);scene.collection.objects.link(cam);scene.camera=cam
room.hide_render=True;scene.render.resolution_x=2048;scene.render.resolution_y=2048;scene.render.resolution_percentage=100
scene.cycles.samples=1;scene.render.filepath=str(TEX/'OrientExpress_Light.png');bpy.ops.render.render(write_still=True)
room.hide_render=False;scene.compositing_node_group=None;bpy.data.node_groups.remove(ng)
for m in room.data.materials:m.node_tree.nodes.remove(m.node_tree.nodes['BakeTarget'])
room.data.uv_layers.active=room.data.uv_layers['UVMap']
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.separate(type='MATERIAL');bpy.ops.object.mode_set(mode='OBJECT')
for o in list(scene.objects):
    if o.type in ('LIGHT','CAMERA'):bpy.data.objects.remove(o,do_unlink=True)
for o in scene.objects:
    if o.type=='MESH':o.name=next(m.name for m in o.data.materials if m)
log('Preparing three calibrated scrolling layers')
scenery_materials,scenery=replace_scenery(ROOT,TEX);materials.update(scenery_materials)
objects=[o for o in scene.objects if o.type=='MESH']
stats={o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects}
assert sum(stats.values())<=120000 and len(materials)<=14
scene.cycles.samples=256;bpy.context.view_layer.update()
report={'triangles':stats,'total_triangles':sum(stats.values()),'materials':sorted(m.name for m in materials.values()),'mesh_objects':len(objects),'seats':len(SEATS),'bake_samples':256,'passes':['DIRECT','INDIRECT'],'bake_margin_px':8,'denoise':'OpenImageDenoise, linear irradiance','atlas':[2048,2048],'view':'Standard/None/-1 EV/RGB8','scenery_baked':False,'repaired_uv_faces':repaired,'compact_ring_islands':ring_cuts,'device':scene.cycles.device,'elapsed_seconds':round(time.time()-START),'blender':bpy.app.version_string}
scene['orient_express_build_metrics']=json.dumps(report)
bpy.ops.export_scene.fbx(filepath=str(OUT/'OrientExpress.fbx'),use_selection=False,object_types={'EMPTY','MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
(PREVIEW/'orient-express-metrics.json').write_text(json.dumps(report,indent=2)+'\n');bpy.ops.wm.save_as_mainfile(filepath=str(BLEND/'OrientExpress-baked.blend'))
log(f'Exported {sum(stats.values())} triangles / {len(materials)} materials')
preview(ROOT,TEX,configure_cycles,'--quick' in ARGS)
log('Complete')
