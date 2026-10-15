"""Test room for the Blender → Unity pipeline (and reference for the export/bake settings in docs/ART-BRIEF.md).

Builds a simple hall (floor, walls, stage, four seats, a lamp), the markers Seat1..Seat4 / ScreenAnchor / ScreenTop,
a second UV set "Lightmap", bakes the light with Cycles into <name>_Light.png and exports <name>.fbx.

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/blender/test_room.py -- <out folder> [name]

Coordinates (Blender, meters, Z up): the seats stand on the floor at z = 0 in a row along X, 0.75 m apart, centered on
the origin and facing +Y; the screen is at +Y.
"""
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "build/blender-test")
NAME = argv[1] if len(argv) > 1 else "Cinema"
LIGHTMAP_SIZE = 1024
os.makedirs(os.path.join(OUT, "Textures"), exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def material(name, color, emission=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.8
    if emission > 0:
        bsdf.inputs["Emission Color"].default_value = (*color, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission
    return m


def box(name, center, size, mat):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(mat)
    return o


def empty(name, location):
    o = bpy.data.objects.new(name, None)
    o.location = location
    o.empty_display_type = "ARROWS"
    o.empty_display_size = 0.3
    scene.collection.objects.link(o)
    return o


wall = material("Wall_Damask", (0.35, 0.08, 0.09))
floor = material("Carpet", (0.25, 0.05, 0.06))
gold = material("Gold_Trim", (0.8, 0.6, 0.25))
velvet = material("Velvet_Seats", (0.55, 0.05, 0.08))
bulb = material("Bulb_emit", (1.0, 0.85, 0.6), emission=25.0)

# Hall 14 m wide, from 4 m behind the seats to 10 m in front, 7 m high (inside faces).
meshes = [
    box("Floor", (0, 3, -0.05), (14, 14, 0.1), floor),
    box("Ceiling", (0, 3, 7.05), (14, 14, 0.1), wall),
    box("WallLeft", (-7.05, 3, 3.5), (0.1, 14, 7), wall),
    box("WallRight", (7.05, 3, 3.5), (0.1, 14, 7), wall),
    box("WallBack", (0, -4.05, 3.5), (14, 0.1, 7), wall),
    box("WallFront", (0, 10.05, 3.5), (14, 0.1, 7), wall),
    box("Stage", (0, 8.5, 0.4), (10, 3, 0.8), gold),
    box("Lamp", (0, 3, 6.6), (0.6, 0.6, 0.3), bulb),
]
for i in range(4):
    x = (i - 1.5) * 0.75
    meshes.append(box(f"SeatMesh{i + 1}", (x, -0.2, 0.25), (0.6, 0.6, 0.5), velvet))
    empty(f"Seat{i + 1}", (x, 0, 0))
empty("ScreenAnchor", (0, 9.9, 1.0))
empty("ScreenTop", (0, 9.9, 6.0))

# Bake lights (only for the bake; Unity gets the result as a texture).
for loc, energy in (((-4, 2, 5), 1500), ((4, 2, 5), 1500), ((0, 8, 5), 800)):
    data = bpy.data.lights.new("BakeLight", "POINT")
    data.energy = energy
    data.color = (1.0, 0.82, 0.6)
    light = bpy.data.objects.new("BakeLight", data)
    light.location = loc
    scene.collection.objects.link(light)

# One object: one lightmap atlas, few draw calls.
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
room = bpy.context.active_object
room.name = "Room"

# UV0 "UVMap": textures (1 unit = 1 m), UV1 "Lightmap": unique, non-overlapping islands for the baked light.
mesh = room.data
if not mesh.uv_layers:
    mesh.uv_layers.new(name="UVMap")
mesh.uv_layers[0].name = "UVMap"
lightmap = mesh.uv_layers.new(name="Lightmap")
mesh.uv_layers.active = lightmap
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02, scale_to_bounds=True)
bpy.ops.object.mode_set(mode="OBJECT")

# Bake diffuse light (direct + indirect, without the surface color) into the active image node of every material.
image = bpy.data.images.new(f"{NAME}_Light", LIGHTMAP_SIZE, LIGHTMAP_SIZE, float_buffer=True)
for m in mesh.materials:
    node = m.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    m.node_tree.nodes.active = node
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 64
scene.render.bake.margin = 8
bpy.ops.object.select_all(action="DESELECT")
room.select_set(True)
bpy.context.view_layer.objects.active = room
bpy.ops.object.bake(type="DIFFUSE", pass_filter={"DIRECT", "INDIRECT"}, use_clear=True, margin=8)

# Save at half brightness in sRGB: Unity's CinemaLit multiplies by 2 (_LightScale), so light up to 2× fits in 8 bits.
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
scene.view_settings.exposure = -1.0
scene.view_settings.gamma = 1.0
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGB"
scene.render.image_settings.color_depth = "8"
image.save_render(os.path.join(OUT, "Textures", f"{NAME}_Light.png"), scene=scene)

# Remove the bake helpers before export: image nodes and lights.
for m in mesh.materials:
    for node in [n for n in m.node_tree.nodes if n.type == "TEX_IMAGE"]:
        m.node_tree.nodes.remove(node)
mesh.uv_layers.active = mesh.uv_layers["UVMap"]
for o in [o for o in scene.objects if o.type == "LIGHT"]:
    bpy.data.objects.remove(o)

bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, f"{NAME}.fbx"),
    use_selection=False,
    object_types={"EMPTY", "MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    use_space_transform=True,
    bake_space_transform=True,
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    bake_anim=False,
    path_mode="AUTO",
)
print(f"[TheMovies] exported {OUT}/{NAME}.fbx and Textures/{NAME}_Light.png")
