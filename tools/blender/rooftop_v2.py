"""Rooftop terrace in the photorealistic pipeline v2 (docs/ART-BRIEF.md section 15); Blender 5.2, no Unity.

Accepted v1 terrace, skyline, night panorama, seats/markers (with the accepted head correction shift), festoon and
candle flicker, FireLight markers; new: CC0 photo texture sets (oiled teak, lime plaster, stone coping, linen,
striped and woven textiles, terracotta, blackened steel), RNM bake (panorama transparent while baking), reflection
panorama.

Blender -b --factory-startup --python-exit-code 1 -P tools/blender/rooftop_v2.py [-- --quick]
"""
import sys
sys.dont_write_bytecode = True
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from lib_rooftop_textures import texture, panorama
from lib_rooftop_room import build, surroundings, SEATS, SCREEN
from lib_rooftop_markers import correct_heads
from lib_room_motion import rooftop_effects
from lib_pbr_test_textures import material as pbr_material
from lib_pbr_sets import Sets, recolour, relief, save, scale_normal, flat
from lib_pbr_test_textures import pixels
import lib_room_v2 as v2

ROOT = Path(__file__).resolve().parents[2]
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
room = v2.Room('Rooftop', ROOT, quick='--quick' in ARGS)
TEX = room.tex
PROBE = (0.0, 2.2, 2.0)
PROBE_HALF = (7.0, 7.2, 4.0)
EMISSION = {'Rooftop_Festoon_bulb_flicker4': 2.0, 'Rooftop_Candle_bulb_flicker20': 2.0}
DENSITY = {'Rooftop_Teak': 150, 'Rooftop_Lime_Plaster': 110, 'Rooftop_Limestone': 130, 'Rooftop_Canvas_Linen': 170,
           'Rooftop_Indigo_Stripes': 170, 'Rooftop_Terracotta': 110, 'Rooftop_Olive_Leaves': 80, 'Rooftop_Lavender': 60,
           'Rooftop_Blackened_Metal': 80, 'Rooftop_Festoon_bulb': 40, 'Rooftop_City_Plaster': 12,
           'Rooftop_City_Windows_unlit': 2, 'Rooftop_Woven_Rug': 150}

# ---------------- Texture sets ----------------

# v1 procedural base colours first (patterns, foliage, bulbs, skyline, panorama), as the v1 build makes them.
for name, color, kind in [('Rooftop_Indigo_Stripes', (.12, .25, .30), 'stripe'), ('Rooftop_Woven_Rug', (.45, .39, .29), 'rug'),
                          ('Rooftop_Olive_Leaves', (.34, .43, .27), ''), ('Rooftop_Lavender', (.38, .29, .48), ''),
                          ('Rooftop_Festoon_bulb', (.96, .72, .37), ''), ('Rooftop_City_Plaster', (.16, .20, .25), 'stone'),
                          ('Rooftop_City_Windows_unlit', (.42, .30, .14), '')]:
    texture(TEX / f'{name}_BaseColor.png', color, kind)
panorama(ROOT, TEX / 'Rooftop_Panorama_unlit_BaseColor.png')
sets = Sets(['teak_veneer', 'white_plaster_02', 'rough_linen', 'quatrefoil_jacquard_fabric'])
n = 1024
c, nm, r, ao = sets.load('teak_veneer', 2048)
save(TEX, 'Rooftop_Teak', recolour(c, (.19, .11, .055), 1.1), scale_normal(nm, .6), ao, .40 + .2 * r, n=2048)
c, nm, r, ao = sets.load('white_plaster_02', n)
save(TEX, 'Rooftop_Lime_Plaster', recolour(c, (.62, .58, .48), .8), scale_normal(nm, .45), ao, .85 + .1 * r)
save(TEX, 'Rooftop_Limestone', recolour(c, (.46, .45, .38), 1.2), scale_normal(nm, .7), ao, .70 + .15 * r)
save(TEX, 'Rooftop_Terracotta', recolour(c, (.45, .19, .085), 1.2), scale_normal(nm, .6), ao, .75 + .15 * r)
c, nm, r, ao = sets.load('rough_linen', n)
save(TEX, 'Rooftop_Canvas_Linen', recolour(c, (.66, .62, .52), .9), scale_normal(nm, .7), ao, np.maximum(.8, r))
stripes = pixels(TEX / 'Rooftop_Indigo_Stripes_BaseColor.png', (n, n))[..., :3]
save(TEX, 'Rooftop_Indigo_Stripes', relief(stripes, c, .4), scale_normal(nm, .7), ao, np.maximum(.8, r))
c, nm, r, ao = sets.load('quatrefoil_jacquard_fabric', n)
rug = pixels(TEX / 'Rooftop_Woven_Rug_BaseColor.png', (n, n))[..., :3]
save(TEX, 'Rooftop_Woven_Rug', relief(rug, c, .4), scale_normal(nm, .8), ao, np.maximum(.88, r))
save(TEX, 'Rooftop_Blackened_Metal', np.array((.11, .12, .13)), flat(n), 1.0, .45, 1.0)
for name, rough in [('Rooftop_Olive_Leaves', .55), ('Rooftop_Lavender', .7), ('Rooftop_Festoon_bulb', .2), ('Rooftop_City_Plaster', .9)]:
    save(TEX, name, pixels(TEX / f'{name}_BaseColor.png', (n, n))[..., :3], flat(n), 1.0, rough)
room.log('Texture sets prepared')

# ---------------- Scene ----------------

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
keys = {'Teak': 'Rooftop_Teak', 'Plaster': 'Rooftop_Lime_Plaster', 'Stone': 'Rooftop_Limestone', 'Linen': 'Rooftop_Canvas_Linen',
        'Textile': 'Rooftop_Indigo_Stripes', 'Clay': 'Rooftop_Terracotta', 'Foliage': 'Rooftop_Olive_Leaves',
        'Lavender': 'Rooftop_Lavender', 'Metal': 'Rooftop_Blackened_Metal', 'bulb': 'Rooftop_Festoon_bulb',
        'City': 'Rooftop_City_Plaster', 'Rug': 'Rooftop_Woven_Rug'}
materials = {k: pbr_material(name, TEX) for k, name in keys.items()}
bpy.data.materials['Rooftop_Festoon_bulb'].node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 2.0
links = bpy.data.materials['Rooftop_Festoon_bulb'].node_tree
links.links.new(links.nodes['BaseColor'].outputs['Color'], links.nodes['Principled BSDF'].inputs['Emission Color'])
for key, name in [('Windows', 'Rooftop_City_Windows_unlit'), ('Panorama', 'Rooftop_Panorama_unlit')]:
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(str(TEX / f'{name}_BaseColor.png'))
    uv = nt.nodes.new('ShaderNodeUVMap')
    uv.uv_map = 'UVMap'
    nt.links.new(uv.outputs[0], tex.inputs[0])
    nt.links.new(tex.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
    materials[key] = m


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


meshes = build(materials, light) + surroundings(materials)
for i, loc in enumerate(SEATS):
    for name in [f'Spot_{i:03d}'] + ([f'Seat{i + 1}'] if i < 4 else []):
        o = bpy.data.objects.new(name, None)
        scene.collection.objects.link(o)
        o.location = loc
for name, z in [('ScreenAnchor', SCREEN[2]), ('ScreenTop', SCREEN[2] + 4)]:
    o = bpy.data.objects.new(name, None)
    scene.collection.objects.link(o)
    o.location = (SCREEN[0], SCREEN[1], z)
scene.world = bpy.data.worlds.new('Rooftop night ambient')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.34, .47, .72, 1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = .40

obj = v2.join_and_triangulate(meshes, 'Rooftop')
room.log(f'Terrace: {len(obj.data.polygons)} triangles')
v2.lightmap_uvs(room, obj, DENSITY, skip=('panorama',))
v2.bake_rnm(room, scene, obj, transparent=['Rooftop_Panorama_unlit'])
v2.separate_by_material(scene, obj)
# Accepted effects (foliage atlas, flickering festoons and candles, twinkling city, FireLight markers), then the
# accepted head correction (the whole terrace moves together; markers to their standard places).
rooftop_effects(TEX)
o = bpy.data.objects.new('ReflectionProbe', None)
scene.collection.objects.link(o)
o.location, o.scale = PROBE, PROBE_HALF
correct_heads(scene)
bpy.context.view_layer.update()
v2.unlit_as_emission([m for m in bpy.data.materials if 'unlit' in m.name])
v2.reflection(room, scene, tuple(scene.objects['ReflectionProbe'].location))
v2.previews(room, scene, [('seat2', (-.375, 0, 1.25), (-.375, 4, 1.25), 90),
                          ('lounge', (3.5, -3.5, 1.6), (-1.5, 3, .6), 80),
                          ('table', (-.4, -.4, 1.2), (-2.15, 0, .55), 60)])
v2.export(room, scene, EMISSION, max_materials=20, extra={'probe': {'position': PROBE, 'half_extents': PROBE_HALF}})
