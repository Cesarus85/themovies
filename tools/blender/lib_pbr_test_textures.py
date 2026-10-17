"""CC0 inputs and explicit byte-exact PNG preparation; no display transform on data."""
import hashlib
import json
import struct
import subprocess
import zlib
from pathlib import Path
from urllib.parse import urlparse

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / 'art/sources/pbrtest'


def png(path, values):
    """Write bottom-up float pixels as RGB(A)8. Caller chooses transfer function."""
    data = np.floor(np.clip(values, 0, 1) * 255 + .5).astype(np.uint8)[::-1]
    h, w, channels = data.shape
    def chunk(tag, payload):
        return struct.pack('>I', len(payload)) + tag + payload + struct.pack('>I', zlib.crc32(tag + payload))
    raw = b''.join(b'\0' + row.tobytes() for row in data)
    Path(path).write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2 if channels == 3 else 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))


def pixels(path, size=None):
    image = bpy.data.images.load(str(path), check_existing=False)
    image.colorspace_settings.name = 'Non-Color'
    if size and tuple(image.size) != tuple(size):
        image.scale(*size)
    w, h = image.size
    data = np.empty(w * h * 4, np.float32)
    image.pixels.foreach_get(data)
    bpy.data.images.remove(image)
    return data.reshape(h, w, 4)


def srgb(linear):
    return np.where(linear <= .0031308, 12.92 * linear, 1.055 * np.maximum(linear, 0) ** (1 / 2.4) - .055)


def linear(encoded):
    return np.where(encoded <= .04045, encoded / 12.92, ((encoded + .055) / 1.055) ** 2.4)


def fetch_sources():
    manifest = json.loads((ROOT / 'art/pbr-test-sources.json').read_text())
    CACHE.mkdir(parents=True, exist_ok=True)
    for asset in manifest['assets'].values():
        for entry in asset['maps'].values():
            assert urlparse(entry['url']).hostname == 'dl.polyhaven.org'
            path = CACHE / entry['url'].split('/')[-1]
            if not path.exists():
                # Do not follow redirects to an unapproved download host.
                subprocess.run(['curl', '-fsS', '--retry', '2', entry['url'], '-o', str(path)], check=True)
            assert hashlib.md5(path.read_bytes()).hexdigest() == entry['md5'], path
    reused = manifest['reused_panorama']
    assert hashlib.sha256((ROOT / reused['path']).read_bytes()).hexdigest() == reused['sha256']
    return manifest


def prepare(texdir):
    manifest = fetch_sources()
    sets = {}
    for name, entry in manifest['assets'].items():
        sets[name] = {kind: pixels(CACHE / value['url'].split('/')[-1], (1024, 1024))[..., :3]
                      for kind, value in entry['maps'].items()}
    n = 1024
    yy, xx = np.mgrid[:n, :n] / n
    flat = np.zeros((n, n, 3), np.float32) + (.5, .5, 1)
    def source(asset, normal_strength=1):
        s = sets[asset]
        normal = s['nor_gl'] * 2 - 1
        normal[..., :2] *= normal_strength
        normal /= np.maximum(np.linalg.norm(normal, axis=2, keepdims=True), 1e-8)
        ao = s.get('AO', s.get('arm'))[..., 0]
        return s['Diffuse'].copy(), normal * .5 + .5, ao, s['Rough'][..., 0]
    def save(name, color, normal, ao, rough, metal=0):
        rgb = np.broadcast_to(color, (n, n, 3))
        orm = np.empty((n, n, 3), np.float32)
        orm[..., 0], orm[..., 1], orm[..., 2] = ao, rough, metal
        for suffix, data in [('BaseColor', rgb), ('Normal', normal), ('ORM', orm)]:
            png(texdir / f'{name}_{suffix}.png', data)
    color, normal, ao, rough = source('walnut_veneer', .32)
    save('PT_Walnut', color * (.62, .56, .50), normal, ao, .23 + .23 * rough)
    save('PT_Lacquer', color * (.19, .20, .22), flat, ao, .08)
    color, normal, ao, rough = source('white_plaster_02', .12)
    lum = np.mean(color, axis=2, keepdims=True)
    save('PT_Ivory', np.clip((1 + .10 * (lum / np.mean(lum) - 1)) * (.82, .79, .69), 0, 1), normal, ao, .78)
    color, normal, ao, rough = source('quatrefoil_jacquard_fabric', .6)
    lum = np.mean(color, axis=2, keepdims=True)
    # Original dyed-yarn trellis, not lighting: 60 cm repeat, 12 mm gold bands.
    distance = np.minimum(abs((xx + yy) % 1 - .5), abs((xx - yy) % 1 - .5))
    band = np.clip((.026 - distance) / .006, 0, 1)[..., None]
    dye = np.array([.12, .20, .21]) * (1 - band) + np.array([.48, .36, .19]) * band
    save('PT_Carpet', np.clip(lum / np.mean(lum) * dye, 0, 1), normal, ao, np.maximum(.85, rough))
    save('PT_Linen_glow_Shade', np.clip(.8 + (lum / np.mean(lum) - 1) * .05, 0, 1) * (1, .9, .72), normal, ao, .85)
    color, normal, ao, rough = source('velour_velvet', .6)
    lum = np.mean(color, axis=2, keepdims=True)
    save('PT_Petrol_velvet', np.clip(lum / np.mean(lum) * (.10, .29, .28), 0, 1), normal, ao, np.maximum(.82, rough))
    # Original fine finish variation only, never painted specular highlights.
    scratch = .5 + .5 * np.sin(xx * 1200 + .8 * np.sin(yy * 57))
    save('PT_Polished_brass', (.78, .64, .37), flat, 1, .24 + .02 * scratch, 1)
    smear = np.clip((np.sin(xx * 33 + np.sin(yy * 8)) + np.cos(yy * 47 + xx * 8)) * .25 + .15, 0, 1)
    save('PT_Window_glass', smear[..., None] * np.array([.025, .023, .020]), flat, 1, .035 + .10 * smear)
    scenery = pixels(ROOT / manifest['reused_panorama']['path'], (2048, 1024))[..., :3]
    png(texdir / 'PT_Scenery_unlit_BaseColor.png', scenery)
    # A full explicit set also for the unlit branch; the runtime ignores its data maps.
    png(texdir / 'PT_Scenery_unlit_Normal.png', flat)
    save_orm = np.ones((n, n, 3), np.float32)
    save_orm[..., 2] = 0
    png(texdir / 'PT_Scenery_unlit_ORM.png', save_orm)


def material(name, texdir):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bs = nodes.get('Principled BSDF')
    uv = nodes.new('ShaderNodeUVMap')
    uv.uv_map = 'UVMap'
    textures = {}
    for suffix in ['BaseColor', 'Normal', 'ORM']:
        image = bpy.data.images.load(str(texdir / f'{name}_{suffix}.png'), check_existing=True)
        image.colorspace_settings.name = 'sRGB' if suffix == 'BaseColor' else 'Non-Color'
        node = nodes.new('ShaderNodeTexImage')
        node.name = suffix
        node.image = image
        links.new(uv.outputs['UV'], node.inputs['Vector'])
        textures[suffix] = node
    links.new(textures['BaseColor'].outputs['Color'], bs.inputs['Base Color'])
    channels = nodes.new('ShaderNodeSeparateColor')
    links.new(textures['ORM'].outputs['Color'], channels.inputs['Color'])
    links.new(channels.outputs['Green'], bs.inputs['Roughness'])
    links.new(channels.outputs['Blue'], bs.inputs['Metallic'])
    normal = nodes.new('ShaderNodeNormalMap')
    normal.name = 'SurfaceNormal'
    normal.uv_map = 'UVMap'
    links.new(textures['Normal'].outputs['Color'], normal.inputs['Color'])
    links.new(normal.outputs['Normal'], bs.inputs['Normal'])
    if 'velvet' in name:
        bs.inputs['Sheen Weight'].default_value = .25
        bs.inputs['Sheen Roughness'].default_value = .7
    if 'glow' in name:
        links.new(textures['BaseColor'].outputs['Color'], bs.inputs['Emission Color'])
        bs.inputs['Emission Strength'].default_value = 2.2
    if 'unlit' in name:
        emission = nodes.new('ShaderNodeEmission')
        links.new(textures['BaseColor'].outputs['Color'], emission.inputs['Color'])
        links.new(emission.outputs[0], nodes.get('Material Output').inputs['Surface'])
    if 'glass' in name:
        # Thin Fresnel reflection over a transparent surface, no refraction slab.
        # This is the closest Cycles reference to the contract's additive pane;
        # its energy-conserving mix is explicitly documented as a runtime difference.
        transparent = nodes.new('ShaderNodeBsdfTransparent')
        glossy = nodes.new('ShaderNodeBsdfGlossy')
        fresnel = nodes.new('ShaderNodeFresnel')
        fresnel.inputs['IOR'].default_value = 1.5
        mix = nodes.new('ShaderNodeMixShader')
        links.new(channels.outputs['Green'], glossy.inputs['Roughness'])
        links.new(normal.outputs['Normal'], glossy.inputs['Normal'])
        links.new(fresnel.outputs[0], mix.inputs[0])
        links.new(transparent.outputs[0], mix.inputs[1])
        links.new(glossy.outputs[0], mix.inputs[2])
        links.new(mix.outputs[0], nodes.get('Material Output').inputs['Surface'])
    return mat
