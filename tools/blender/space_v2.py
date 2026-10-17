"""Space lounge in the photorealistic pipeline v2 (docs/ART-BRIEF.md section 15); Blender 5.2, no Unity.

Accepted v1 deck, seats, markers, Earth (planet shader, slow rotation, Sun marker), stars and navigation beacons;
new: CC0 photo texture sets (polished granite deck, walnut, cognac leather, woven fabric, satin composite), a
panoramic glass skin over the open front and sides, RNM bake, reflection panorama.

Blender -b --factory-startup --python-exit-code 1 -P tools/blender/space_v2.py [-- --quick]
"""
import sys
sys.dont_write_bytecode = True
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from lib_space_room import build, surroundings, perimeter, SEATS, SCREEN, SUN
from lib_space_geometry import Geometry
from lib_room_motion import space_effects
from lib_pbr_test_textures import material as pbr_material
from lib_pbr_sets import Sets, recolour, relief, save, scale_normal, polished_metal, window_glass, existing, flat
from lib_seated_head_clearance import audit as audit_heads
import lib_room_v2 as v2

ROOT = Path(__file__).resolve().parents[2]
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
room = v2.Room('Space', ROOT, quick='--quick' in ARGS)
TEX = room.tex
PROBE = (0.0, 3.0, 3.1)
PROBE_HALF = (8.0, 8.0, 3.15)
EMISSION = {'Space_Cove_glow': 1.5, 'Space_Instruments_glow': 1.2}
DENSITY = {'Space_Basalt': 110, 'Space_Porcelain': 90, 'Space_Graphite': 90, 'Space_Walnut': 150,
           'Space_Cognac_Leather_Fabric': 200, 'Space_Woven_Fabric': 140, 'Space_Champagne_Metal': 90,
           'Space_Cove_glow': 50, 'Space_Instruments_glow': 80, 'Space_Foliage': 120, 'Space_Window_glass': 8}

# ---------------- Texture sets ----------------

sets = Sets(['granite_tile', 'walnut_veneer', 'leather_white', 'rough_linen', 'white_plaster_02'])
n = 1024
c, nm, r, ao = sets.load('granite_tile', 2048)
save(TEX, 'Space_Basalt', recolour(c, (.045, .055, .060), 1.1), scale_normal(nm, .25), ao, .16 + .12 * r, n=2048)
c, nm, r, ao = sets.load('walnut_veneer', n)
save(TEX, 'Space_Walnut', recolour(c, (.22, .12, .055), 1.2), scale_normal(nm, .35), ao, .20 + .18 * r)
c, nm, r, ao = sets.load('leather_white', n)
save(TEX, 'Space_Cognac_Leather_Fabric', recolour(c, (.36, .15, .055), 1.0), scale_normal(nm, .8), ao, .36 + .25 * r)
c, nm, r, ao = sets.load('rough_linen', n)
save(TEX, 'Space_Woven_Fabric', recolour(c, (.16, .22, .23), 1.1), scale_normal(nm, .7), ao, np.maximum(.8, r))
c, nm, r, ao = sets.load('white_plaster_02', n)
save(TEX, 'Space_Porcelain', recolour(c, (.68, .68, .63), .15), scale_normal(nm, .05), 1.0, .30 + .08 * r)
save(TEX, 'Space_Graphite', recolour(c, (.06, .075, .09), .2), scale_normal(nm, .05), 1.0, .42 + .08 * r)
save(TEX, 'Space_Foliage', existing(TEX, 'Space_Foliage'), flat(n), 1.0, .55)
save(TEX, 'Space_Cove_glow', existing(TEX, 'Space_Cove_glow'), flat(n), 1.0, .5)
save(TEX, 'Space_Instruments_glow', existing(TEX, 'Space_Instruments_glow'), flat(n), 1.0, .15)
polished_metal(TEX, 'Space_Champagne_Metal', (.78, .70, .56), .26)
window_glass(TEX, 'Space_Window_glass')
room.log('Texture sets prepared')

# ---------------- Scene ----------------

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
keys = {'Ivory': 'Space_Porcelain', 'Graphite': 'Space_Graphite', 'Floor': 'Space_Basalt', 'Wood': 'Space_Walnut',
        'Leather': 'Space_Cognac_Leather_Fabric', 'Fabric': 'Space_Woven_Fabric', 'Metal': 'Space_Champagne_Metal',
        'Glow': 'Space_Cove_glow', 'Display': 'Space_Instruments_glow', 'Foliage': 'Space_Foliage', 'Window': 'Space_Window_glass'}
materials = {k: pbr_material(name, TEX) for k, name in keys.items()}
for name, strength in EMISSION.items():
    bpy.data.materials[name].node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = strength


def aim(o, target):
    o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler()


def light(name, loc, power, color, size, target):
    d = bpy.data.lights.new(name, 'AREA')
    d.energy, d.color, d.shape, d.size = power, color, 'DISK', size
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
# Panoramic glass over the open front and sides (the rear is a bulkhead), just outside the station ribs.
glass = Geometry()
low, high = perimeter(-.03, .29), perimeter(-.03, 5.94)
for i in range(len(low)):
    j = (i + 1) % len(low)
    if low[i][1] < -3.05 or low[j][1] < -3.05:
        continue
    quad = [low[i], low[j], high[j], high[i]]
    a, b, c = (Vector(p) for p in quad[:3])
    inward = Vector((0, 3, 0)) - Vector((a.x, a.y, 0))
    face = (0, 1, 2, 3) if (b - a).cross(c - a).dot(inward) > 0 else (3, 2, 1, 0)
    glass.add(materials['Window'], quad, [face], uvs=[[[(0, 0), (1, 0), (1, 1), (0, 1)][k] for k in face]])
meshes += glass.objects()
for i, loc in enumerate(SEATS):
    marker(f'Spot_{i:03d}', loc)
    if i < 4:
        marker(f'Seat{i + 1}', loc)
marker('ScreenAnchor', SCREEN)
marker('ScreenTop', (SCREEN[0], SCREEN[1], SCREEN[2] + 4))
marker('Sun', SUN)
marker('ReflectionProbe', PROBE, PROBE_HALF)
bpy.context.view_layer.update()
heads = audit_heads(meshes, scene.objects)
assert heads['pass'], heads['failed_markers']
scene.world = bpy.data.worlds.new('Space ambient')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.32, .46, .68, 1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = .16

obj = v2.join_and_triangulate(meshes, 'Space interior')
room.log(f'Interior: {len(obj.data.polygons)} triangles; heads PASS')
v2.lightmap_uvs(room, obj, DENSITY)
v2.bake_rnm(room, scene, obj)
v2.separate_by_material(scene, obj)

# Earth (planet shader in the app) and the star sphere, as accepted; then the accepted navigation beacons.
scenery = {}
for key, name, image in [('Earth', 'Earth_Planet', 'Earth_Planet_BaseColor.png'), ('Stars', 'Space_Panorama_unlit', 'Space_Panorama_unlit_BaseColor.png')]:
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(str(TEX / image))
    uv = nt.nodes.new('ShaderNodeUVMap')
    uv.uv_map = 'UVMap'
    nt.links.new(uv.outputs[0], tex.inputs[0])
    nt.links.new(tex.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
    scenery[key] = m
planet, sky = surroundings(scenery)
for o, offset in [(planet, .970), (sky, .985)]:
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.00001)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    lm = o.data.uv_layers.new(name='Lightmap')
    for a, b in zip(o.data.uv_layers['UVMap'].data, lm.data):
        b.uv = (.003 + a.uv.x * .994, offset + a.uv.y * .005)
    o.data.uv_layers.active = o.data.uv_layers['UVMap']
v2.unlit_as_emission(scenery.values())
space_effects(TEX)

v2.reflection(room, scene, PROBE)
v2.previews(room, scene, [('seat2', (-.375, 0, 1.25), (-.375, 4, 1.25), 90),
                          ('lounge', (5.5, -3.5, 1.7), (-2, 4, .8), 85),
                          ('floor', (-.375, 0, 1.25), (-1.5, 3.5, 0), 80),
                          ('window', (3.5, 1.0, 1.4), (8, 4, 1.4), 75)])
v2.export(room, scene, EMISSION, extra={'probe': {'position': PROBE, 'half_extents': PROBE_HALF}})
