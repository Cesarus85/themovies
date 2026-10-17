"""Presidential jet in the photorealistic pipeline v2 (docs/ART-BRIEF.md section 15); Blender 5.2, no Unity.

Accepted v1 cabin geometry, seats, markers and scenery (sunset sky, cloud deck at its calibrated speed); new: CC0
photo texture sets (lacquered walnut and olive burl, cream and navy leather, woven carpet), window glass in the 14
apertures per side, RNM bake, reflection panorama.

Blender -b --factory-startup --python-exit-code 1 -P tools/blender/jet_v2.py [-- --quick]
"""
import sys
sys.dont_write_bytecode = True
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from lib_jet_room import build, surroundings, SEATS, SCREEN, WINDOWS, rounded
from lib_jet_geometry import Geometry
from lib_jet_motion import CLOUD_NAME, SKY_NAME
from lib_pbr_test_textures import material as pbr_material
from lib_pbr_sets import Sets, recolour, relief, save, scale_normal, polished_metal, window_glass, existing, flat
from lib_seated_head_clearance import audit as audit_heads
import lib_room_v2 as v2

ROOT = Path(__file__).resolve().parents[2]
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
room = v2.Room('Jet', ROOT, quick='--quick' in ARGS)
TEX = room.tex
PROBE = (0.0, 0.0, 1.23)
PROBE_HALF = (3.0, 6.0, 1.23)
EMISSION = {'Jet_Warm_glow': 1.5}
DENSITY = {'Jet_Ivory': 150, 'Jet_Woven_Carpet': 140, 'Jet_Walnut': 180, 'Jet_Burl': 180, 'Jet_Cream_Leather_Fabric': 200,
           'Jet_Navy_Leather_Fabric': 180, 'Jet_Leather_Piping': 100, 'Jet_Polished_Brass': 90, 'Jet_Dark': 90,
           'Jet_Warm_glow': 60, 'Jet_Porcelain': 120, 'Jet_Window_glass': 15}

# ---------------- Texture sets ----------------

sets = Sets(['american_walnut_veneer', 'olive_veneer', 'leather_white', 'white_plaster_02', 'quatrefoil_jacquard_fabric'])
n = 1024
c, nm, r, ao = sets.load('american_walnut_veneer', 2048)
save(TEX, 'Jet_Walnut', recolour(c, (.13, .062, .026), 1.2), scale_normal(nm, .35), ao, .12 + .10 * r, n=2048)
c, nm, r, ao = sets.load('olive_veneer', n)
save(TEX, 'Jet_Burl', recolour(c, (.30, .16, .065), 1.35), scale_normal(nm, .3), ao, .10 + .08 * r)
c, nm, r, ao = sets.load('leather_white', n)
save(TEX, 'Jet_Cream_Leather_Fabric', recolour(c, (.62, .50, .33), 1.0), scale_normal(nm, .8), ao, .38 + .25 * r)
save(TEX, 'Jet_Navy_Leather_Fabric', recolour(c, (.05, .085, .13), 1.0), scale_normal(nm, .8), ao, .34 + .25 * r)
save(TEX, 'Jet_Leather_Piping', recolour(c, (.24, .17, .09), 1.0), scale_normal(nm, .6), ao, .35 + .2 * r)
c, nm, r, ao = sets.load('white_plaster_02', n)
save(TEX, 'Jet_Ivory', recolour(c, (.70, .64, .52), .2), scale_normal(nm, .08), 1.0, .45 + .1 * r)
c, nm, r, ao = sets.load('quatrefoil_jacquard_fabric', n)
save(TEX, 'Jet_Woven_Carpet', relief(existing(TEX, 'Jet_Woven_Carpet'), c, .35), scale_normal(nm, .8), ao, np.maximum(.88, r))
save(TEX, 'Jet_Warm_glow', existing(TEX, 'Jet_Warm_glow'), flat(n), 1.0, .5)
save(TEX, 'Jet_Porcelain', np.array((.84, .82, .74)), flat(n), 1.0, .05)
save(TEX, 'Jet_Dark', np.array((.20, .22, .25)), flat(n), 1.0, .32)
polished_metal(TEX, 'Jet_Polished_Brass', (.80, .66, .38))
window_glass(TEX, 'Jet_Window_glass')
room.log('Texture sets prepared')

# ---------------- Scene ----------------

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
keys = {'Ivory': 'Jet_Ivory', 'Wood': 'Jet_Walnut', 'Burl': 'Jet_Burl', 'Leather': 'Jet_Cream_Leather_Fabric',
        'Navy': 'Jet_Navy_Leather_Fabric', 'Carpet': 'Jet_Woven_Carpet', 'Brass': 'Jet_Polished_Brass',
        'Piping': 'Jet_Leather_Piping', 'Dark': 'Jet_Dark', 'Glow': 'Jet_Warm_glow', 'Porcelain': 'Jet_Porcelain',
        'Window': 'Jet_Window_glass'}
materials = {k: pbr_material(name, TEX) for k, name in keys.items()}
for name, strength in EMISSION.items():
    bpy.data.materials[name].node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = strength


def aim(o, target):
    o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler()


def light(name, loc, power, color, size, target):
    d = bpy.data.lights.new(name, 'AREA')
    d.energy, d.color, d.shape, d.size = power * .72, color, 'DISK', size
    o = bpy.data.objects.new(name, d)
    scene.collection.objects.link(o)
    o.location = loc
    aim(o, target)


def marker(name, loc, scale=None):
    o = bpy.data.objects.new(name, None)
    scene.collection.objects.link(o)
    o.location = loc
    if scale:
        o.scale = scale
    return o


meshes = build(materials, light)
glass = Geometry()
for side in [-1, 1]:
    for y in WINDOWS:
        # In the outer aperture of each window reveal, facing into the cabin.
        pts = [(side * 3.14, y + u, 1.25 + v) for u, v in rounded(.28, .40, .075)]
        face = tuple(range(len(pts)))
        glass.add(materials['Window'], pts, [face if side > 0 else face[::-1]])
meshes += glass.objects()
for i, loc in enumerate(SEATS):
    marker(f'Spot_{i:03d}', loc)
    if i < 4:
        marker(f'Seat{i + 1}', loc)
marker('ScreenAnchor', SCREEN)
marker('ScreenTop', (SCREEN[0], SCREEN[1], SCREEN[2] + 1.8))
marker('ReflectionProbe', PROBE, PROBE_HALF)
bpy.context.view_layer.update()
heads = audit_heads(meshes, scene.objects)
assert heads['pass'], heads['failed_markers']
scene.world = bpy.data.worlds.new('Jet sunset ambient')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.45, .53, .68, 1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = .20

obj = v2.join_and_triangulate(meshes, 'Jet interior')
room.log(f'Interior: {len(obj.data.polygons)} triangles; heads PASS')
v2.lightmap_uvs(room, obj, DENSITY)
v2.bake_rnm(room, scene, obj)
v2.separate_by_material(scene, obj)

# Scenery: the accepted sunset sky and cloud deck, as they are (unlit, scrolling in the app).
scenery = {}
for key, name in [('Sky', SKY_NAME), ('Cloud', CLOUD_NAME)]:
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    image = nt.nodes.new('ShaderNodeTexImage')
    image.image = bpy.data.images.load(str(TEX / f'{name}_BaseColor.png'))
    uv = nt.nodes.new('ShaderNodeUVMap')
    uv.uv_map = 'UVMap'
    nt.links.new(uv.outputs[0], image.inputs[0])
    nt.links.new(image.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
    scenery[key] = m
for oi, o in enumerate(surroundings(scenery)):
    lm = o.data.uv_layers.new(name='Lightmap')
    for pi, p in enumerate(o.data.polygons):
        u0, v0 = .01 + pi * .49, .966 + oi * .016
        for li, uv in zip(p.loop_indices, [(u0, v0), (u0 + .46, v0), (u0 + .46, v0 + .009), (u0, v0 + .009)]):
            lm.data[li].uv = uv
    o.data.uv_layers.active = o.data.uv_layers['UVMap']
v2.unlit_as_emission(scenery.values())

v2.reflection(room, scene, PROBE)
v2.previews(room, scene, [('seat2', (-.375, 0, 1.25), (-.375, 4, 1.25), 90),
                          ('club', (1.0, .6, 1.25), (2.35, 2.3, .8), 70),
                          ('window', (1.9, -1.0, 1.3), (3.1, -.4, 1.25), 60),
                          ('lounge', (0, 4.6, 1.5), (0, -4, .9), 85)])
v2.export(room, scene, EMISSION, extra={'probe': {'position': PROBE, 'half_extents': PROBE_HALF}})
