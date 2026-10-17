"""Orient Express in the photorealistic pipeline v2 (docs/ART-BRIEF.md section 15); Blender 5.2, no Unity.

Same accepted salon car as v1 (geometry, seats, markers, scenery), new: CC0 photo texture sets with normal and
AO/roughness/metal maps, window glass, directional baked light (three RNM atlases, normalized to a flat bake, RGBM),
the reflection panorama from the ReflectionProbe marker, previews with Khronos PBR Neutral.

Blender -b --factory-startup --python-exit-code 1 -P tools/blender/orient_express_v2.py
  --quick          fewer samples and a 2048 atlas (look development only, never commit its output)
  --textures-only  rebuild only the material texture sets (no new bake needed when only colours/maps change)
"""
import sys
sys.dont_write_bytecode = True
import json
import math
import time
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from lib_orient_express_textures import texture
from lib_orient_express_motion import replace_scenery
from lib_orient_express_room import build, SEATS, SCREEN
from lib_orient_express_geometry import Geometry
from lib_orient_express_uvs import uv_overlaps
from lib_orient_express_pbr import prepare
from lib_pbr_test_textures import material as pbr_material, png, srgb, pixels
from lib_seated_head_clearance import audit as audit_heads

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/TheMovies/Art/OrientExpress'
TEX = OUT / 'Textures'
PREVIEW = ROOT / 'Logs/art-preview'
BLEND = ROOT / 'art/blender'
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
QUICK = '--quick' in ARGS
N = 2048 if QUICK else 4096
SAMPLES = 128 if QUICK else 1024
BASIS = [(math.sqrt(2 / 3), 0, 1 / math.sqrt(3)), (-1 / math.sqrt(6), 1 / math.sqrt(2), 1 / math.sqrt(3)),
         (-1 / math.sqrt(6), -1 / math.sqrt(2), 1 / math.sqrt(3))]
WINDOWS = [-7.65, -5.1, -2.55, 0, 2.55, 5.1]
# Box of the reflection projection: the whole car (2.90 × 16.10 × 2.56 m); the probe sits in its centre.
PROBE = (0.0, -1.2, 1.28)
PROBE_HALF = (1.45, 8.05, 1.28)
# Glow (Blender emission strength = the app's _Emission), also written for Unity.
EMISSION = {'OE_Silk_glow_flicker2': 2.0, 'OE_Etched_glow': 1.2}
START = time.time()
for p in [TEX, PREVIEW, BLEND]:
    p.mkdir(parents=True, exist_ok=True)


def log(s):
    print(f'[OrientExpress v2 {time.time() - START:6.0f}s] {s}', flush=True)


def configure(scene):
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 8
    scene.cycles.diffuse_bounces = 4
    scene.cycles.transparent_max_bounces = 8
    prefs = bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type = 'METAL'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = d.type == 'METAL'
        scene.cycles.device = 'GPU' if any(d.type == 'METAL' for d in prefs.devices) else 'CPU'
    except Exception:
        scene.cycles.device = 'CPU'
    scene.view_settings.view_transform = 'Khronos PBR Neutral'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def light(name, loc, power, color, size, target):
    d = bpy.data.lights.new(name, 'AREA')
    d.energy, d.color, d.shape, d.size = power, color, 'DISK', size
    o = bpy.data.objects.new(name, d)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    aim(o, target)


def marker(name, loc, scale=None):
    o = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    o.empty_display_type = 'CUBE' if scale else 'ARROWS'
    if scale:
        o.scale = scale
    return o


# ---------------- Materials ----------------

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
# v1 procedural base colours that v2 keeps (pattern, glow colours); the photo sets replace the rest.
for name, color, kind in [('OE_Botanical_Marquetry', (.34, .19, .09), 'inlay'), ('OE_ArtDeco_Carpet', (.2, .2, .2), 'carpet'),
                          ('OE_Silk_glow_flicker2', (.90, .71, .42), 'shade'), ('OE_Etched_glow', (.80, .73, .55), 'glass')]:
    texture(TEX / f'{name}_BaseColor.png', color, kind)
manifest = prepare(TEX)
log('Texture sets prepared')
if '--textures-only' in ARGS:
    sys.exit(0)  # material maps only; geometry, bake and reflection stay as exported
keys = {'Ivory': 'OE_Ivory', 'Wood': 'OE_Walnut', 'Inlay': 'OE_Botanical_Marquetry', 'Brass': 'OE_Polished_brass',
        'Velvet': 'OE_Petrol_velvet_Fabric', 'Curtain': 'OE_Champagne_velvet', 'Carpet': 'OE_ArtDeco_Carpet',
        'Dark': 'OE_Ebony', 'Glass': 'OE_Etched_glow', 'Shade': 'OE_Silk_glow_flicker2', 'Window': 'OE_Window_glass'}
materials = {k: pbr_material(v, TEX) for k, v in keys.items()}
for name, strength in EMISSION.items():
    bpy.data.materials[name].node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = strength

# ---------------- Geometry (accepted v1 car) + window glass ----------------

meshes = build(materials, light)
glass = Geometry()
for side in [-1, 1]:
    for cy in WINDOWS:
        # Inside the opening, facing into the car; 1.81 × 1.13 m.
        verts = [(side * 1.476, cy - .905, .862), (side * 1.476, cy + .905, .862), (side * 1.476, cy + .905, 1.992), (side * 1.476, cy - .905, 1.992)]
        face = (3, 2, 1, 0) if side == 1 else (0, 1, 2, 3)
        glass.add(materials['Window'], verts, [face], uvs=[[[(0, 0), (1, 0), (1, 1), (0, 1)][i] for i in face]])
meshes += glass.objects()
for i, loc in enumerate(SEATS):
    marker(f'Spot_{i:03d}', loc)
    if i < 4:
        marker(f'Seat{i + 1}', loc)
marker('ScreenAnchor', SCREEN)
marker('ScreenTop', (SCREEN[0], SCREEN[1], SCREEN[2] + 1.5))
marker('ReflectionProbe', PROBE, PROBE_HALF)
bpy.context.view_layer.update()
heads = audit_heads(meshes, scene.objects)
(PREVIEW / 'orient-express-v2-heads.json').write_text(json.dumps(heads, indent=2) + '\n')
assert heads['pass'], heads['failed_markers']
scene.world = bpy.data.worlds.new('OrientExpress dusk ambient')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.35, .46, .65, 1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = .20

bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
room = bpy.context.object
room.name = 'OrientExpress interior'
bm = bmesh.new()
bm.from_mesh(room.data)
bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.000001)
bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=.000001)
bmesh.ops.triangulate(bm, faces=list(bm.faces))
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
bm.to_mesh(room.data)
bm.free()
room.data.update()
room.data.normals_split_custom_set([tuple(n.vector) for n in room.data.corner_normals])
log(f'Interior: {len(room.data.polygons)} triangles; heads PASS')

# ---------------- Lightmap UVs (as v1: denser furniture, compact hollow islands, overlap repair) ----------------

room.data.uv_layers[0].name = 'UVMap'
lm = room.data.uv_layers.new(name='Lightmap')
room.data.uv_layers.active = lm
parent = list(range(len(room.data.vertices)))


def root(i):
    while parent[i] != i:
        parent[i] = parent[parent[i]]
        i = parent[i]
    return i


for edge in room.data.edges:
    a, b = edge.vertices
    parent[root(a)] = root(b)
components = {}
for v in room.data.vertices:
    components.setdefault(root(v.index), []).append(v.index)
# Lightmap texel density per material (px/m at 4096, relative – the packer scales them together): large plain surfaces
# close to the eye need the most (v1's furniture heuristic left ceiling and carpet at 35 px/m: blocky and noisy),
# glass needs almost none (it is not baked).
DENSITY = {'OE_Ivory': 150, 'OE_ArtDeco_Carpet': 150, 'OE_Walnut': 180, 'OE_Botanical_Marquetry': 180, 'OE_Ebony': 120,
           'OE_Petrol_velvet_Fabric': 200, 'OE_Champagne_velvet': 120, 'OE_Polished_brass': 100, 'OE_Etched_glow': 80,
           'OE_Silk_glow_flicker2': 80, 'OE_Window_glass': 15}
vertex_material = {}
for poly in room.data.polygons:
    name = room.data.materials[poly.material_index].name
    for v in poly.vertices:
        vertex_material[v] = name
original = {}
for ids in components.values():
    coords = np.array([room.data.vertices[i].co[:] for i in ids])
    center = coords.mean(axis=0)
    names = [vertex_material.get(i) for i in ids]
    main = max(set(names), key=names.count)
    factor = DENSITY.get(main, 120) / 120
    if factor != 1:
        for i in ids:
            v = room.data.vertices[i]
            original[i] = v.co.copy()
            v.co = Vector(center) + (v.co - Vector(center)) * factor
room.data.update()
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=1.25, island_margin=0, area_weight=.5, scale_to_bounds=True)
bpy.ops.object.mode_set(mode='OBJECT')
for i, co in original.items():
    room.data.vertices[i].co = co
room.data.update()


def pack():
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin_method='FRACTION', margin=10.4 / 4096, shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')


pack()
for repair in range(6):
    room.data.calc_loop_triangles()
    layer = room.data.uv_layers['Lightmap']
    tris = np.array([[tuple(layer.data[i].uv) for i in t.loops] for t in room.data.loop_triangles])
    overlaps = uv_overlaps(tris)
    ab = tris[:, 1] - tris[:, 0]
    ac = tris[:, 2] - tris[:, 0]
    degenerate = np.flatnonzero(abs(ab[:, 0] * ac[:, 1] - ab[:, 1] * ac[:, 0]) < 1e-13).tolist()
    if not overlaps and not degenerate:
        break
    assert repair < 5, ('UV overlaps', len(overlaps), len(degenerate))
    split = sorted({room.data.loop_triangles[j].polygon_index for i, j in overlaps} | {room.data.loop_triangles[j].polygon_index for j in degenerate})
    scale = math.sqrt(float(np.abs(ab[:, 0] * ac[:, 1] - ab[:, 1] * ac[:, 0]).sum()) / 2 / sum(p.area for p in room.data.polygons))
    for k, index in enumerate(split):
        poly = room.data.polygons[index]
        pts = [room.data.vertices[v].co for v in poly.vertices]
        axis = (pts[1] - pts[0]).normalized()
        other = poly.normal.cross(axis).normalized()
        for li, pt in zip(poly.loop_indices, pts):
            delta = pt - pts[0]
            layer.data[li].uv = (2 + (k % 32) * .1 + delta.dot(axis) * scale, 2 + (k // 32) * .1 + delta.dot(other) * scale)
    log(f'Repairing {len(split)} folded faces')
    pack()
# Top 5 % of the atlas stays free for the (unbaked) scenery bands.
for d in room.data.uv_layers['Lightmap'].data:
    d.uv.y *= .95
log('Lightmap UVs ready')

# ---------------- Bake: three RNM directions + flat normal, RGBM ----------------

configure(scene)
names = [m.name for m in room.data.materials]


def set_bake_normal(direction):
    for name in names:
        tree = bpy.data.materials[name].node_tree
        node = tree.nodes['SurfaceNormal']
        for link in list(node.inputs['Color'].links):
            tree.links.remove(link)
        node.inputs['Color'].default_value = (*(float(x) * .5 + .5 for x in direction), 1)


def restore_normals():
    for name in names:
        tree = bpy.data.materials[name].node_tree
        tree.links.new(tree.nodes['Normal'].outputs['Color'], tree.nodes['SurfaceNormal'].inputs['Color'])


def metals_off():
    saved = []
    for name in names:
        tree = bpy.data.materials[name].node_tree
        socket = tree.nodes['Principled BSDF'].inputs['Metallic']
        links = [l.from_socket for l in socket.links]
        for l in list(socket.links):
            tree.links.remove(l)
        saved.append((tree, socket, socket.default_value, links))
        socket.default_value = 0.0
    return saved


def metals_on(saved):
    for tree, socket, value, links in saved:
        socket.default_value = value
        for a in links:
            tree.links.new(a, socket)


def denoise(image, stem):
    group = bpy.data.node_groups.new('Atlas denoise', 'CompositorNodeTree')
    group.interface.new_socket(name='Image', in_out='OUTPUT', socket_type='NodeSocketColor')
    source = group.nodes.new('CompositorNodeImage')
    source.image = image
    node = group.nodes.new('CompositorNodeDenoise')
    out = group.nodes.new('NodeGroupOutput')
    group.links.new(source.outputs['Image'], node.inputs['Image'])
    group.links.new(node.outputs['Image'], out.inputs['Image'])
    scene.compositing_node_group = group
    cam = bpy.data.objects.new('Atlas denoise', bpy.data.cameras.new('Atlas denoise'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    hidden = [(o, o.hide_render) for o in scene.objects if o.type == 'MESH']
    for o, _ in hidden:
        o.hide_render = True
    samples = scene.cycles.samples
    scene.cycles.samples = 1
    scene.render.resolution_x, scene.render.resolution_y = image.size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'OPEN_EXR'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.color_depth = '32'
    path = PREVIEW / f'{stem}-linear.exr'
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    for o, old in hidden:
        o.hide_render = old
    scene.cycles.samples = samples
    bpy.data.objects.remove(cam, do_unlink=True)
    scene.compositing_node_group = None
    bpy.data.node_groups.remove(group)
    return pixels(path)[..., :3]


def bake(direction, stem):
    metals = metals_off()
    set_bake_normal(direction)
    room.select_set(True)
    bpy.context.view_layer.objects.active = room
    room.data.uv_layers.active = room.data.uv_layers['Lightmap']
    image = bpy.data.images.new(stem, N, N, alpha=True, float_buffer=True)
    image.colorspace_settings.name = 'Non-Color'
    for name in names:
        tree = bpy.data.materials[name].node_tree
        node = tree.nodes.new('ShaderNodeTexImage')
        node.name = 'BakeTarget'
        node.image = image
        tree.nodes.active = node
    margin = 16 if N == 4096 else 8
    scene.render.bake.margin = margin
    start = time.monotonic()
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'DIRECT', 'INDIRECT'}, use_clear=True, margin=margin, uv_layer='Lightmap')
    data = denoise(image, stem)
    for name in names:
        nodes = bpy.data.materials[name].node_tree.nodes
        nodes.remove(nodes['BakeTarget'])
    bpy.data.images.remove(image)
    restore_normals()
    metals_on(metals)
    room.data.uv_layers.active = room.data.uv_layers['UVMap']
    log(f'{stem}: {time.monotonic() - start:.0f}s, max {data.max():.3f}')
    return data


def encode_rgbm(data, path):
    data = np.clip(np.nan_to_num(data), 0, 8)
    multiplier = np.maximum(1 / 255, np.ceil(np.max(data, axis=2, keepdims=True) / 8 * 255) / 255)
    png(path, np.concatenate((srgb(data / (multiplier * 8)), multiplier), axis=2))


atlases = [bake(d, f'oe-v2-rnm{i}') for i, d in enumerate(BASIS)]
flat = bake((0.0, 0.0, 1.0), 'oe-v2-flat')
def blur(values, mask, sigma):
    """Masked separable Gaussian: smooth inside UV islands without pulling in the empty atlas between them."""
    radius = int(3 * sigma)
    offsets = np.arange(-radius, radius + 1)
    kernel = np.exp(-.5 * (offsets / sigma) ** 2)
    kernel /= kernel.sum()
    def conv(a):
        for axis in (0, 1):
            a = sum(w * np.roll(a, int(k), axis=axis) for k, w in zip(offsets, kernel))
        return a
    if values.ndim == 3:
        mask = mask[..., None]
    weight = conv(mask.astype(np.float32))
    return np.where(mask, conv(values * mask) / np.maximum(weight, 1e-6), values)


# Remaining bake noise (ceilings are lit only indirectly): a light blur inside the islands, about 1 cm.
for atlas in atlases + [flat]:
    lit = atlas.max(axis=2) > 1e-5
    atlas[...] = blur(atlas, lit, 1.5 if N == 4096 else 1.0)
# The per-texel gain (flat bake ÷ mean of the three directions) only depends on where the light comes from, which
# changes slowly over a surface; computed from noisy single texels it turns sampling noise into blotches.
mean = (atlases[0] + atlases[1] + atlases[2]) / 3
peak = mean.max(axis=2)
valid = peak > 1e-5
ratio = np.where(valid, flat.max(axis=2) / np.maximum(peak, 1e-5), 1.0)
gain = np.clip(blur(np.clip(ratio, .25, 4.0), valid, 6.0 if N == 4096 else 3.0), .25, 4.0)[..., None]
for i, data in enumerate(atlases):
    encode_rgbm(data * gain, TEX / f'OrientExpress_RNM{i}.png')
log(f'RNM atlases written ({N}²), {float((data * gain > 8).mean()) * 100:.3f} % of texels above the RGBM range')
old = TEX / 'OrientExpress_Light.png'
if old.exists():
    old.unlink()

# ---------------- Split per material, scenery, reflection, previews, export ----------------

room.data.uv_layers.active = room.data.uv_layers['UVMap']
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='MATERIAL')
bpy.ops.object.mode_set(mode='OBJECT')
for o in scene.objects:
    if o.type == 'MESH':
        o.name = next(m.name for m in o.data.materials if m)
scenery_materials, scenery = replace_scenery(ROOT, TEX)
# The scenery is a finished photograph (unlit in the app): glow as itself in reflections and previews.
for m in scenery_materials.values():
    nt = m.node_tree
    emission = nt.nodes.new('ShaderNodeEmission')
    nt.links.new(next(n for n in nt.nodes if n.type == 'TEX_IMAGE').outputs['Color'], emission.inputs['Color'])
    nt.links.new(emission.outputs[0], nt.nodes['Material Output'].inputs['Surface'])
bpy.context.view_layer.update()

cam_data = bpy.data.cameras.new('Reflection capture')
cam = bpy.data.objects.new('Reflection capture', cam_data)
scene.collection.objects.link(cam)
cam.location = PROBE
cam.rotation_euler = (math.pi / 2, 0, 0)  # image centre +Y, up +Z (the app's calibration)
cam_data.type = 'PANO'
cam_data.panorama_type = 'EQUIRECTANGULAR'
scene.camera = cam
scene.render.resolution_x, scene.render.resolution_y = 2048, 1024
scene.render.image_settings.file_format = 'OPEN_EXR'
scene.render.image_settings.color_mode = 'RGB'
scene.render.image_settings.color_depth = '16'
scene.render.filepath = str(TEX / 'OrientExpress_Reflection.exr')
bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(cam, do_unlink=True)
log('Reflection panorama rendered')


def previews():
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.color_depth = '8'
    scene.render.resolution_x, scene.render.resolution_y = 1440, 900
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = .015
    data = bpy.data.cameras.new('Preview')
    cam = bpy.data.objects.new('Preview', data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    data.sensor_fit = 'HORIZONTAL'
    data.clip_start = .03
    data.clip_end = 20000
    views = [('seat2', (-.375, 0, 1.25), (-.375, 4, 1.25), 90),
             ('table', (.45, -.55, 1.12), (1.17, -1.4, .82), 55),
             ('salon', (.5, 1.6, 1.3), (-.4, -6.5, .7), 80),
             ('window', (.35, -2.0, 1.3), (1.47, -2.55, 1.4), 70)]
    records = []
    for name, pos, target, fov in views:
        cam.location = pos
        aim(cam, target)
        data.angle = math.radians(fov)
        scene.render.filepath = str(PREVIEW / f'orient-express-v2-{name}.png')
        bpy.ops.render.render(write_still=True)
        records.append({'name': name, 'position_blender_m': list(pos), 'target': list(target), 'horizontal_fov_degrees': fov})
    (PREVIEW / 'orient-express-v2-cameras.json').write_text(json.dumps(records, indent=2) + '\n')
    bpy.data.objects.remove(cam, do_unlink=True)


previews()
log('Previews rendered')

for o in list(scene.objects):
    if o.type in ('LIGHT', 'CAMERA'):
        bpy.data.objects.remove(o, do_unlink=True)
objects = [o for o in scene.objects if o.type == 'MESH']
stats = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects}
material_names = sorted({m.name for o in objects for m in o.data.materials if m})
assert sum(stats.values()) <= 200000 and len(material_names) <= 20, (sum(stats.values()), material_names)
(TEX / 'OrientExpress_Materials.json').write_text(json.dumps({k: {'emission': v} for k, v in EMISSION.items()}, indent=2) + '\n')
bpy.ops.export_scene.fbx(filepath=str(OUT / 'OrientExpress.fbx'), use_selection=False, object_types={'EMPTY', 'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         use_space_transform=True, bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
                         add_leaf_bones=False, bake_anim=False, path_mode='AUTO')
report = {'pipeline': 'v2', 'triangles': stats, 'total_triangles': sum(stats.values()), 'materials': material_names,
          'atlas': N, 'samples': SAMPLES, 'rnm_basis': BASIS, 'rgbm_range': 8, 'probe': {'position': PROBE, 'half_extents': PROBE_HALF},
          'emission': EMISSION, 'quick': QUICK, 'elapsed_seconds': round(time.time() - START), 'blender': bpy.app.version_string}
(PREVIEW / 'orient-express-v2-metrics.json').write_text(json.dumps(report, indent=2) + '\n')
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND / 'OrientExpress-v2-baked.blend'))
log(f'Exported {sum(stats.values())} triangles / {len(material_names)} materials')
