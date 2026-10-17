"""Home cinema in the photorealistic pipeline v2 (docs/ART-BRIEF.md section 15); Blender 5.2, no Unity.

Accepted v1 living room (sofa seats, library, fireplace with sculpted flames, candles, night window), with the
accepted effects (fire flicker, candles, twinkling night sky, FireLight markers); new: CC0 photo texture sets
(walnut, herringbone oak parquet, lime plaster, forest velvet, limestone hearth, woven rug), window glass, RNM bake,
reflection panorama. Unlike v1, plaster, limestone and charcoal keep their own materials (v2 budget is 20).

Blender -b --factory-startup --python-exit-code 1 -P tools/blender/home_v2.py [-- --quick]
"""
import sys
sys.dont_write_bytecode = True
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from lib_home_textures import fetch, make_texture
from lib_home_room import build
from lib_home_window import panorama, build_window
from lib_home_geometry import Geometry
from lib_room_motion import split_material, image_material, consolidate, rename_material, marker as motion_marker
from lib_rooftop_textures import save as save_rgb
from lib_pbr_test_textures import material as pbr_material, pixels
from lib_pbr_sets import Sets, recolour, relief, save, scale_normal, polished_metal, window_glass, flat
from lib_seated_head_clearance import audit as audit_heads
import lib_room_v2 as v2

ROOT = Path(__file__).resolve().parents[2]
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
room = v2.Room('Home', ROOT, quick='--quick' in ARGS)
TEX = room.tex
PROBE = (0.0, 1.3, 1.38)
PROBE_HALF = (4.2, 3.9, 1.38)
EMISSION = {'Home_Linen_emit': .6, 'Home_Candle_emit_flicker15': 1.5, 'Home_Fire_glow_flicker30': 2.2}
DENSITY = {'Home_Walnut': 170, 'Home_Parquet': 160, 'Home_Warm_Plaster': 130, 'Home_Velvet_Forest': 200,
           'Home_Persian_Rug': 150, 'Home_Brass': 90, 'Home_Limestone': 150, 'Home_Charcoal': 90, 'Home_Books_Decor': 110,
           'Home_Linen_emit': 70, 'Home_Garden_Night': 15, 'Home_Window_glass': 8}

# ---------------- Texture sets ----------------

for name, color, kind, source in [('Home_Persian_Rug', (.3, .05, .03), 'rug', None), ('Home_Books_Decor', (.3, .2, .1), 'books', None),
                                  ('Home_Linen_emit', (.92, .77, .50), 'linen', None), ('Home_Embers_glow', (.8, .12, .01), 'embers', None),
                                  ('Home_Garden_Night', (.12, .15, .18), 'stone', None), ('Home_Charcoal', (.060, .047, .039), '', None)]:
    make_texture(name, TEX / f'{name}_BaseColor.png', color, kind, source)
panorama(fetch(ROOT, 'solitude_night'), TEX / 'Home_Night_unlit_BaseColor.png')
sets = Sets(['walnut_veneer', 'herringbone_parquet', 'white_plaster_02', 'velour_velvet', 'quatrefoil_jacquard_fabric', 'rough_linen'])
n = 1024
c, nm, r, ao = sets.load('walnut_veneer', 2048)
save(TEX, 'Home_Walnut', recolour(c, (.13, .065, .029), 1.2), scale_normal(nm, .4), ao, .26 + .2 * r, n=2048)
c, nm, r, ao = sets.load('herringbone_parquet', 2048)
save(TEX, 'Home_Parquet', recolour(c, (.42, .30, .19), 1.1), scale_normal(nm, .6), ao, .28 + .25 * r, n=2048)
c, nm, r, ao = sets.load('white_plaster_02', n)
save(TEX, 'Home_Warm_Plaster', recolour(c, (.58, .53, .42), .15), scale_normal(nm, .15), 1.0, .9)
save(TEX, 'Home_Limestone', recolour(c, (.52, .48, .40), 1.4), scale_normal(nm, .8), ao, .65 + .2 * r)
save(TEX, 'Home_Charcoal', pixels(TEX / 'Home_Charcoal_BaseColor.png', (n, n))[..., :3], scale_normal(nm, .9), ao, .9)
c, nm, r, ao = sets.load('velour_velvet', n)
save(TEX, 'Home_Velvet_Forest', recolour(c, (.045, .11, .075), 1.1), scale_normal(nm, .7), ao, np.maximum(.82, r))
c, nm, r, ao = sets.load('quatrefoil_jacquard_fabric', 2048)
rug = pixels(TEX / 'Home_Persian_Rug_BaseColor.png', (2048, 2048))[..., :3]
save(TEX, 'Home_Persian_Rug', relief(rug, c, .35), scale_normal(nm, .8), ao, np.maximum(.88, r), n=2048)
c, nm, r, ao = sets.load('rough_linen', n)
save(TEX, 'Home_Linen_emit', pixels(TEX / 'Home_Linen_emit_BaseColor.png', (n, n))[..., :3], scale_normal(nm, .4), ao, .8)
save(TEX, 'Home_Books_Decor', pixels(TEX / 'Home_Books_Decor_BaseColor.png', (n, n))[..., :3], flat(n), 1.0, .55)
save(TEX, 'Home_Garden_Night', pixels(TEX / 'Home_Garden_Night_BaseColor.png', (n, n))[..., :3], flat(n), 1.0, .9)
save(TEX, 'Home_Embers_glow', pixels(TEX / 'Home_Embers_glow_BaseColor.png', (n, n))[..., :3], flat(n), 1.0, .9)
polished_metal(TEX, 'Home_Brass', (.80, .64, .36), .24)
window_glass(TEX, 'Home_Window_glass')
room.log('Texture sets prepared')
if '--textures-only' in ARGS:
    sys.exit(0)  # material maps only; bake and reflection stay

# ---------------- Scene ----------------

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
keys = {'Walnut': 'Home_Walnut', 'Parquet': 'Home_Parquet', 'Plaster': 'Home_Warm_Plaster', 'Velvet': 'Home_Velvet_Forest',
        'Rug': 'Home_Persian_Rug', 'Brass': 'Home_Brass', 'Stone': 'Home_Limestone', 'Charcoal': 'Home_Charcoal',
        'Books': 'Home_Books_Decor', 'Linen': 'Home_Linen_emit', 'Embers': 'Home_Embers_glow', 'Garden': 'Home_Garden_Night',
        'Window': 'Home_Window_glass'}
materials = {k: pbr_material(name, TEX) for k, name in keys.items()}
for m in materials.values():
    m['home'] = True
for name, strength in [('Home_Linen_emit', EMISSION['Home_Linen_emit']), ('Home_Embers_glow', 2.2)]:
    tree = bpy.data.materials[name].node_tree
    tree.links.new(tree.nodes['BaseColor'].outputs['Color'], tree.nodes['Principled BSDF'].inputs['Emission Color'])
    tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = strength
night = bpy.data.materials.new('Home_Night_unlit')
night.use_nodes = True
nt = night.node_tree
tex = nt.nodes.new('ShaderNodeTexImage')
tex.image = bpy.data.images.load(str(TEX / 'Home_Night_unlit_BaseColor.png'))
uv = nt.nodes.new('ShaderNodeUVMap')
uv.uv_map = 'UVMap'
nt.links.new(uv.outputs[0], tex.inputs[0])
nt.links.new(tex.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
materials['Night'] = night


def aim(o, target):
    o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler()


def light(name, loc, power, color=(1, .75, .48), size=.2, target=None):
    d = bpy.data.lights.new(name, 'AREA' if target else 'POINT')
    d.energy, d.color = power, color
    if target:
        d.shape, d.size = 'DISK', size
    else:
        d.shadow_soft_size = size
    o = bpy.data.objects.new(name, d)
    scene.collection.objects.link(o)
    o.location = loc
    if target:
        aim(o, target)


build(materials, light)
build_window(materials, light)
glass = Geometry()
glass.add(materials['Window'], [(4.23, 4.20, 1.06), (4.23, 1.40, 1.06), (4.23, 1.40, 2.31), (4.23, 4.20, 2.31)], [(0, 1, 2, 3)],
          False, [[(0, 0), (1, 0), (1, 1), (0, 1)]])
glass.objects()

# Accepted effects, as lib_room_motion.home_effects but without folding plaster/limestone/charcoal into one atlas.
import bmesh
for obj in [o for o in scene.objects if o.type == 'MESH']:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.000001)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=.000001)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
candle = split_material('Home_Linen_emit', 'Home_Candle_emit_flicker15', TEX, lambda p: p[0] < -3.8 and 1.64 < p[2] < 1.82)
candle.data.materials[0]['home'] = True
candle.data.materials[0].node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = EMISSION['Home_Candle_emit_flicker15']
k = 1024
y, x = np.mgrid[0:k, 0:k] / (k - 1)
heat = np.clip(1 - y * .80 - np.abs(x - .5) * .32, 0, 1)
flame_rgb = np.stack((.98 + heat * .02, .19 + .74 * heat, .012 + .32 * heat ** 3), axis=2)
flame_mat = image_material('Home_Flame_Source', save_rgb(TEX / 'Home_Flame_Source_BaseColor.png', flame_rgb), 2.2, 'home')
g = Geometry()


def flame(cx, cy, z, h, w, lean):
    vs, fs, uvs, segments = [], [], [], 6
    for j, (height, width, bend) in enumerate([(0, .72, 0), (.22, 1, -.12), (.54, .67, .25), (.80, .30, .65), (1, .008, 1)]):
        for s in range(segments):
            a = math.tau * s / segments
            vs.append((cx + math.cos(a) * w * .48 * width, cy + math.sin(a) * w * width + lean * bend, z + h * height))
    for j in range(4):
        for s in range(segments):
            fs.append((j * segments + s, j * segments + (s + 1) % segments, (j + 1) * segments + (s + 1) % segments, (j + 1) * segments + s))
            uvs.append([(s / 6, j / 4), ((s + 1) / 6, j / 4), ((s + 1) / 6, (j + 1) / 4), (s / 6, (j + 1) / 4)])
    g.add(flame_mat, vs, fs, False, uvs)


for i, (dy, h, w, lean) in enumerate([(-.39, .34, .074, .06), (-.23, .50, .09, -.055), (-.04, .59, .105, .065), (.14, .43, .083, -.05), (.33, .36, .07, .04)]):
    flame(-3.87 + (i % 2) * .055, 1.70 + dy, .255, h, w, lean)
for cy in (.92, 2.48):
    g.lathe(candle.data.materials[0], (-3.90, cy, 1.81), [(.008, 0), (.014, .025), (.006, .048), (.0005, .065)], 8)
g.objects()
consolidate(['Home_Embers_glow', 'Home_Flame_Source'], 'Home_Fire_glow_flicker30', TEX, 'home', 2.2)
motion_marker('FireLight_350', (-3.62, 1.70, .44))
motion_marker('FireLight_65', (-3.89, .92, 1.84))
motion_marker('FireLight_70', (-3.89, 2.48, 1.84))
meshes = [o for o in scene.objects if o.type == 'MESH']

for i, x in enumerate([-1.125, -.375, .375, 1.125]):
    for name in [f'Seat{i + 1}', f'Spot_{i:03d}']:
        o = bpy.data.objects.new(name, None)
        scene.collection.objects.link(o)
        o.location = (x, 0, 0)
for name, z in [('ScreenAnchor', .78), ('ScreenTop', 2.63)]:
    o = bpy.data.objects.new(name, None)
    scene.collection.objects.link(o)
    o.location = (0, 5.2, z)
o = bpy.data.objects.new('ReflectionProbe', None)
scene.collection.objects.link(o)
o.location, o.scale = PROBE, PROBE_HALF
bpy.context.view_layer.update()
heads = audit_heads(meshes, scene.objects)
room.log(f'Head clearance: {"PASS" if heads["pass"] else "as accepted in v1: " + str(heads["failed_markers"])}')
scene.world = bpy.data.worlds.new('Home ambient')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.26, .22, .17, 1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = .32

obj = v2.join_and_triangulate(meshes, 'Home')
room.log(f'Room: {len(obj.data.polygons)} triangles')
v2.lightmap_uvs(room, obj, DENSITY, skip=('_unlit',))
v2.bake_rnm(room, scene, obj, transparent=['Home_Night_unlit'])
v2.separate_by_material(scene, obj)
rename_material('Home_Night_unlit', 'Home_Night_unlit_twinkle12', TEX)
v2.unlit_as_emission([m for m in bpy.data.materials if 'unlit' in m.name])
v2.reflection(room, scene, PROBE)
v2.previews(room, scene, [('seat2', (-.375, 0, 1.25), (-.375, 4, 1.25), 90),
                          ('fireplace', (-.375, 0, 1.25), (-4.0, 1.85, 1.0), 70),
                          ('library', (-.375, 0, 1.25), (0, -2.15, 1.48), 80),
                          ('window', (-.375, 0, 1.25), (4.0, 2.72, 1.50), 70)])
v2.export(room, scene, EMISSION, extra={'probe': {'position': PROBE, 'half_extents': PROBE_HALF}})
