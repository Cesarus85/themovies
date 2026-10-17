"""Shared steps of the photorealistic pipeline v2 (docs/ART-BRIEF.md section 15), as proven on the Orient Express:
lightmap UVs with a texel density per material, RNM bakes (three directions + flat, blurred gain, RGBM), the
reflection panorama and Khronos PBR Neutral previews. Room scripts build their geometry, lights and materials
(lib_pbr_test_textures.material: nodes 'Normal' / 'SurfaceNormal' / 'Principled BSDF') and call these in order.
"""
import json
import math
import time
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

from lib_orient_express_uvs import uv_overlaps
from lib_pbr_test_textures import png, srgb, pixels

BASIS = [(math.sqrt(2 / 3), 0, 1 / math.sqrt(3)), (-1 / math.sqrt(6), 1 / math.sqrt(2), 1 / math.sqrt(3)),
         (-1 / math.sqrt(6), -1 / math.sqrt(2), 1 / math.sqrt(3))]


class Room:
    def __init__(self, name, root, quick=False, samples=1024):
        self.name = name
        self.out = root / f'Assets/TheMovies/Art/{name}'
        self.tex = self.out / 'Textures'
        self.preview = root / 'Logs/art-preview'
        self.blend = root / 'art/blender'
        self.quick = quick
        self.n = 2048 if quick else 4096
        self.samples = 128 if quick else samples
        self.start = time.time()
        for p in [self.tex, self.preview, self.blend]:
            p.mkdir(parents=True, exist_ok=True)

    def log(self, s):
        print(f'[{self.name} v2 {time.time() - self.start:6.0f}s] {s}', flush=True)


def configure(room, scene):
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = room.samples
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
    # The bake lights stand in for coves, sconces and fills: they light the room but must not show up as floating
    # discs in mirror reflections (glass, polished floors) of the previews and the reflection panorama.
    for o in scene.objects:
        if o.type == 'LIGHT':
            o.visible_glossy = False
    scene.view_settings.view_transform = 'Khronos PBR Neutral'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1


def join_and_triangulate(meshes, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.000001)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=.000001)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    obj.data.normals_split_custom_set([tuple(n.vector) for n in obj.data.corner_normals])
    return obj


def lightmap_uvs(room, obj, density, default=120, skip=()):
    """Smart-project + pack with a relative texel density per material (scaled while unwrapping, then restored).
    Materials in `skip` (unlit scenery) are shrunk to almost nothing; the top 5 % of the atlas stays free."""
    me = obj.data
    if me.uv_layers[0].name != 'UVMap':
        me.uv_layers[0].name = 'UVMap'
    while len(me.uv_layers) > 1:
        me.uv_layers.remove(me.uv_layers[1])
    lm = me.uv_layers.new(name='Lightmap')
    me.uv_layers.active = lm
    parent = list(range(len(me.vertices)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for edge in me.edges:
        a, b = edge.vertices
        parent[root(a)] = root(b)
    components = {}
    for v in me.vertices:
        components.setdefault(root(v.index), []).append(v.index)
    vertex_material = {}
    for poly in me.polygons:
        name = me.materials[poly.material_index].name
        for v in poly.vertices:
            vertex_material[v] = name
    original = {}
    for ids in components.values():
        names = [vertex_material.get(i) for i in ids]
        main = max(set(names), key=names.count)
        factor = .003 if any(k in (main or '').lower() for k in skip) else density.get(main, default) / default
        if factor != 1:
            center = Vector(np.array([me.vertices[i].co[:] for i in ids]).mean(axis=0))
            for i in ids:
                v = me.vertices[i]
                original[i] = v.co.copy()
                v.co = center + (v.co - center) * factor
    me.update()
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.25, island_margin=0, area_weight=.5, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    for i, co in original.items():
        me.vertices[i].co = co
    me.update()

    def pack():
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.pack_islands(rotate=True, margin_method='FRACTION', margin=10.4 / 4096, shape_method='CONCAVE')
        bpy.ops.object.mode_set(mode='OBJECT')

    pack()
    for repair in range(6):
        me.calc_loop_triangles()
        layer = me.uv_layers['Lightmap']
        tris = np.array([[tuple(layer.data[i].uv) for i in t.loops] for t in me.loop_triangles])
        overlaps = uv_overlaps(tris)
        ab = tris[:, 1] - tris[:, 0]
        ac = tris[:, 2] - tris[:, 0]
        degenerate = np.flatnonzero(abs(ab[:, 0] * ac[:, 1] - ab[:, 1] * ac[:, 0]) < 1e-13).tolist()
        if not overlaps and not degenerate:
            break
        assert repair < 5, ('UV overlaps', len(overlaps), len(degenerate))
        split = sorted({me.loop_triangles[j].polygon_index for i, j in overlaps} | {me.loop_triangles[j].polygon_index for j in degenerate})
        scale = math.sqrt(float(np.abs(ab[:, 0] * ac[:, 1] - ab[:, 1] * ac[:, 0]).sum()) / 2 / sum(p.area for p in me.polygons))
        for k, index in enumerate(split):
            poly = me.polygons[index]
            pts = [me.vertices[v].co for v in poly.vertices]
            axis = (pts[1] - pts[0]).normalized()
            other = poly.normal.cross(axis).normalized()
            for li, pt in zip(poly.loop_indices, pts):
                delta = pt - pts[0]
                layer.data[li].uv = (2 + (k % 32) * .1 + delta.dot(axis) * scale, 2 + (k // 32) * .1 + delta.dot(other) * scale)
        room.log(f'Repairing {len(split)} folded faces')
        pack()
    for d in me.uv_layers['Lightmap'].data:
        d.uv.y *= .95
    room.log('Lightmap UVs ready')


def _set_bake_normal(names, direction):
    for name in names:
        tree = bpy.data.materials[name].node_tree
        node = tree.nodes.get('SurfaceNormal')
        if node is None:
            continue
        for link in list(node.inputs['Color'].links):
            tree.links.remove(link)
        node.inputs['Color'].default_value = (*(float(x) * .5 + .5 for x in direction), 1)


def _restore_normals(names):
    for name in names:
        tree = bpy.data.materials[name].node_tree
        if 'SurfaceNormal' in tree.nodes and 'Normal' in tree.nodes:
            tree.links.new(tree.nodes['Normal'].outputs['Color'], tree.nodes['SurfaceNormal'].inputs['Color'])


def _metals_off(names):
    saved = []
    for name in names:
        tree = bpy.data.materials[name].node_tree
        bs = tree.nodes.get('Principled BSDF')
        if bs is None:
            continue
        socket = bs.inputs['Metallic']
        links = [l.from_socket for l in socket.links]
        for l in list(socket.links):
            tree.links.remove(l)
        saved.append((tree, socket, socket.default_value, links))
        socket.default_value = 0.0
    return saved


def _metals_on(saved):
    for tree, socket, value, links in saved:
        socket.default_value = value
        for a in links:
            tree.links.new(a, socket)


def _denoise(room, scene, image, stem):
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
    path = room.preview / f'{room.name.lower()}-v2-{stem}-linear.exr'
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    for o, old in hidden:
        o.hide_render = old
    scene.cycles.samples = samples
    bpy.data.objects.remove(cam, do_unlink=True)
    scene.compositing_node_group = None
    bpy.data.node_groups.remove(group)
    return pixels(path)[..., :3]


def _bake(room, scene, obj, names, direction, stem, transparent=()):
    metals = _metals_off(names)
    # Photographic domes and backdrops must not shut out the sky or dominate the bounce light while baking.
    swapped = []
    for name in transparent:
        tree = bpy.data.materials[name].node_tree
        out = tree.nodes['Material Output']
        old = [l.from_socket for l in out.inputs['Surface'].links]
        node = tree.nodes.new('ShaderNodeBsdfTransparent')
        tree.links.new(node.outputs[0], out.inputs['Surface'])
        swapped.append((tree, out, node, old))
    _set_bake_normal(names, direction)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    obj.data.uv_layers.active = obj.data.uv_layers['Lightmap']
    image = bpy.data.images.new(stem, room.n, room.n, alpha=True, float_buffer=True)
    image.colorspace_settings.name = 'Non-Color'
    for name in names:
        tree = bpy.data.materials[name].node_tree
        node = tree.nodes.new('ShaderNodeTexImage')
        node.name = 'BakeTarget'
        node.image = image
        tree.nodes.active = node
    margin = 16 if room.n == 4096 else 8
    scene.render.bake.margin = margin
    start = time.monotonic()
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'DIRECT', 'INDIRECT'}, use_clear=True, margin=margin, uv_layer='Lightmap')
    data = _denoise(room, scene, image, stem)
    for name in names:
        nodes = bpy.data.materials[name].node_tree.nodes
        nodes.remove(nodes['BakeTarget'])
    bpy.data.images.remove(image)
    _restore_normals(names)
    _metals_on(metals)
    for tree, out, node, old in swapped:
        tree.nodes.remove(node)
        for socket in old:
            tree.links.new(socket, out.inputs['Surface'])
    obj.data.uv_layers.active = obj.data.uv_layers['UVMap']
    room.log(f'{stem}: {time.monotonic() - start:.0f}s, max {data.max():.3f}')
    return data


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


def encode_rgbm(data, path):
    data = np.clip(np.nan_to_num(data), 0, 8)
    multiplier = np.maximum(1 / 255, np.ceil(np.max(data, axis=2, keepdims=True) / 8 * 255) / 255)
    png(path, np.concatenate((srgb(data / (multiplier * 8)), multiplier), axis=2))


def bake_rnm(room, scene, obj, transparent=()):
    """Three RNM directions plus a flat bake; denoised, lightly blurred, normalized per texel to the flat bake, RGBM."""
    configure(room, scene)
    names = [m.name for m in obj.data.materials]
    atlases = [_bake(room, scene, obj, names, d, f'rnm{i}', transparent) for i, d in enumerate(BASIS)]
    flat = _bake(room, scene, obj, names, (0.0, 0.0, 1.0), 'flat', transparent)
    sigma = 1.5 if room.n == 4096 else 1.0
    for atlas in atlases + [flat]:
        atlas[...] = blur(atlas, atlas.max(axis=2) > 1e-5, sigma)
    mean = (atlases[0] + atlases[1] + atlases[2]) / 3
    peak = mean.max(axis=2)
    valid = peak > 1e-5
    ratio = np.where(valid, flat.max(axis=2) / np.maximum(peak, 1e-5), 1.0)
    gain = np.clip(blur(np.clip(ratio, .25, 4.0), valid, 4 * sigma), .25, 4.0)[..., None]
    over = 0.0
    for i, data in enumerate(atlases):
        encode_rgbm(data * gain, room.tex / f'{room.name}_RNM{i}.png')
        over = max(over, float((data * gain > 8).mean()))
    old = room.tex / f'{room.name}_Light.png'
    if old.exists():
        old.unlink()
    room.log(f'RNM atlases written ({room.n}²), {over * 100:.3f} % of texels above the RGBM range')


def separate_by_material(scene, obj):
    obj.data.uv_layers.active = obj.data.uv_layers['UVMap']
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.separate(type='MATERIAL')
    bpy.ops.object.mode_set(mode='OBJECT')
    for o in scene.objects:
        if o.type == 'MESH' and o.data.materials:
            o.name = next(m.name for m in o.data.materials if m)


def unlit_as_emission(materials):
    """Scenery is a finished photograph (unlit in the app): let it glow as itself in reflections and previews."""
    for m in materials:
        nt = m.node_tree
        image = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None)
        if image is None:
            continue
        emission = nt.nodes.new('ShaderNodeEmission')
        nt.links.new(image.outputs['Color'], emission.inputs['Color'])
        nt.links.new(emission.outputs[0], nt.nodes['Material Output'].inputs['Surface'])


def reflection(room, scene, probe):
    configure(room, scene)
    cam_data = bpy.data.cameras.new('Reflection capture')
    cam = bpy.data.objects.new('Reflection capture', cam_data)
    scene.collection.objects.link(cam)
    cam.location = probe
    cam.rotation_euler = (math.pi / 2, 0, 0)  # image centre +Y, up +Z (the app's calibration)
    cam_data.type = 'PANO'
    cam_data.panorama_type = 'EQUIRECTANGULAR'
    cam_data.clip_end = 20000
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 2048, 1024
    scene.render.image_settings.file_format = 'OPEN_EXR'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.color_depth = '16'
    scene.render.filepath = str(room.tex / f'{room.name}_Reflection.exr')
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    room.log('Reflection panorama rendered')


def previews(room, scene, views):
    """views: (name, position, target, horizontal fov in degrees)."""
    configure(room, scene)
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.color_depth = '8'
    scene.render.resolution_x, scene.render.resolution_y = 1440, 900
    scene.cycles.samples = min(room.samples, 512)
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = .015
    data = bpy.data.cameras.new('Preview')
    cam = bpy.data.objects.new('Preview', data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    data.sensor_fit = 'HORIZONTAL'
    data.clip_start = .03
    data.clip_end = 20000
    for name, pos, target, fov in views:
        cam.location = pos
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        data.angle = math.radians(fov)
        scene.render.filepath = str(room.preview / f'{room.name.lower()}-v2-{name}.png')
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    room.log('Previews rendered')


def export(room, scene, emission, max_triangles=200000, max_materials=20, extra=None):
    for o in list(scene.objects):
        if o.type in ('LIGHT', 'CAMERA'):
            bpy.data.objects.remove(o, do_unlink=True)
    objects = [o for o in scene.objects if o.type == 'MESH']
    stats = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects}
    names = sorted({m.name for o in objects for m in o.data.materials if m})
    assert sum(stats.values()) <= max_triangles and len(names) <= max_materials, (sum(stats.values()), names)
    (room.tex / f'{room.name}_Materials.json').write_text(json.dumps({k: {'emission': v} for k, v in emission.items()}, indent=2) + '\n')
    bpy.ops.export_scene.fbx(filepath=str(room.out / f'{room.name}.fbx'), use_selection=False, object_types={'EMPTY', 'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             use_space_transform=True, bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
                             add_leaf_bones=False, bake_anim=False, path_mode='AUTO')
    report = {'pipeline': 'v2', 'triangles': stats, 'total_triangles': sum(stats.values()), 'materials': names,
              'atlas': room.n, 'samples': room.samples, 'quick': room.quick, 'emission': emission,
              'elapsed_seconds': round(time.time() - room.start), 'blender': bpy.app.version_string, **(extra or {})}
    (room.preview / f'{room.name.lower()}-v2-metrics.json').write_text(json.dumps(report, indent=2) + '\n')
    bpy.ops.wm.save_as_mainfile(filepath=str(room.blend / f'{room.name}-v2-baked.blend'))
    room.log(f'Exported {sum(stats.values())} triangles / {len(names)} materials')
