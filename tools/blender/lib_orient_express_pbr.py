"""Orient Express pipeline v2: CC0 photo texture sets (Poly Haven) turned into BaseColor / Normal / ORM.

The accepted v1 look stays the reference: every photographic set is re-coloured so that its mean linear colour is
the accepted v1 material colour; only the structure (grain, pile, weave, plaster) comes from the photograph.
Raw downloads live in the ignored art/sources/oe-v2/, the manifest with checksums in art/orient-express-v2-sources.json.
"""
import hashlib
import json
import subprocess
from pathlib import Path
from urllib.parse import urlparse

import numpy as np

from lib_pbr_test_textures import png, pixels, srgb, linear

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / 'art/sources/oe-v2'
MANIFEST = ROOT / 'art/orient-express-v2-sources.json'
ASSETS = ['walnut_veneer', 'velour_velvet', 'white_plaster_02', 'quatrefoil_jacquard_fabric']
MAPS = ['Diffuse', 'nor_gl', 'Rough', 'AO', 'arm']


def api(url):
    # Python's default user agent is refused by the API; curl is what the other build scripts use.
    return json.loads(subprocess.run(['curl', '-fsS', '--retry', '2', url], check=True, capture_output=True).stdout)


def fetch():
    """Downloads the 2K PNG maps once (only dl.polyhaven.org, checksums from the API, kept in the manifest)."""
    CACHE.mkdir(parents=True, exist_ok=True)
    manifest = json.loads(MANIFEST.read_text()) if MANIFEST.exists() else {'license': 'CC0-1.0', 'powered_by': 'Poly Haven', 'assets': {}}
    for asset in ASSETS:
        entry = manifest['assets'].get(asset)
        if entry is None:
            files = api(f'https://api.polyhaven.com/files/{asset}')
            info = api(f'https://api.polyhaven.com/info/{asset}')
            maps = {}
            for kind in MAPS:
                if kind in files and '2k' in files[kind]:
                    f = files[kind]['2k']['png']
                    maps[kind] = {'url': f['url'], 'md5': f['md5'], 'size': f['size']}
            entry = {'authors': info.get('authors', {}), 'metadata': f'https://api.polyhaven.com/info/{asset}',
                     'source': f'https://polyhaven.com/a/{asset}', 'license': 'CC0-1.0', 'maps': maps}
            manifest['assets'][asset] = entry
        for value in entry['maps'].values():
            assert urlparse(value['url']).hostname == 'dl.polyhaven.org', value['url']
            path = CACHE / value['url'].split('/')[-1]
            if not path.exists():
                subprocess.run(['curl', '-fsS', '--retry', '2', value['url'], '-o', str(path)], check=True)
            assert hashlib.md5(path.read_bytes()).hexdigest() == value['md5'], path
    MANIFEST.write_text(json.dumps(manifest, indent=2) + '\n')
    return manifest


def load_set(manifest, asset, n):
    maps = manifest['assets'][asset]['maps']
    def get(kind):
        return pixels(CACHE / maps[kind]['url'].split('/')[-1], (n, n))[..., :3]
    color = get('Diffuse')
    normal = get('nor_gl') * 2 - 1
    rough = get('Rough')[..., 0]
    ao = get('AO')[..., 0] if 'AO' in maps else get('arm')[..., 0] if 'arm' in maps else np.ones_like(rough)
    return color, normal, rough, ao


def scale_normal(normal, strength):
    n = normal.copy()
    n[..., :2] *= strength
    n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-8)
    return n * .5 + .5


def recolour(encoded, target_linear, contrast=1.0):
    """Keeps the photo's structure, moves its mean (linear) to the accepted material colour."""
    lin = linear(np.clip(encoded, 0, 1))
    lum = lin.mean(axis=2, keepdims=True)
    rel = lum / max(float(lum.mean()), 1e-6)
    rel = 1 + (rel - 1) * contrast
    return srgb(np.clip(rel * np.array(target_linear), 0, 1))


def save(texdir, name, color, normal, ao, rough, metal=0.0, n=None):
    n = n or color.shape[0]
    # Photo AO maps are strong and blotchy, and Cycles computes the real occlusion into the bake anyway: keep only a
    # trace of the small-scale cavity in the ORM red channel (the app multiplies the baked light with it).
    ao = 1 - (1 - np.asarray(ao, np.float32)) * .35
    rgb = np.broadcast_to(color, (n, n, 3)).astype(np.float32)
    orm = np.empty((n, n, 3), np.float32)
    orm[..., 0], orm[..., 1], orm[..., 2] = ao, rough, metal
    png(texdir / f'{name}_BaseColor.png', rgb)
    png(texdir / f'{name}_Normal.png', np.broadcast_to(normal, (n, n, 3)))
    png(texdir / f'{name}_ORM.png', orm)


def prepare(texdir):
    """All v2 texture sets. Procedural v1 base colours (marquetry, carpet pattern, shades, etched glass) are kept and
    get photographic relief and roughness; everything else is rebuilt from the photo sets."""
    manifest = fetch()
    big, small = 2048, 1024
    flat_small = np.zeros((small, small, 3), np.float32) + (.5, .5, 1)
    yy, xx = np.mgrid[:small, :small] / small

    # Walnut veneer: the hero surface, satin varnish (clear coat roughness .22–.42 following the grain).
    color, normal, rough, ao = load_set(manifest, 'walnut_veneer', big)
    save(texdir, 'OE_Walnut', recolour(color, (.15, .075, .034), 1.25), scale_normal(normal, .45), ao, .22 + .20 * rough)
    # Ebony doors: the same veneer, darker and hand-polished.
    save(texdir, 'OE_Ebony', recolour(color, (.035, .028, .022), .8), scale_normal(normal, .25), ao, .16 + .10 * rough)
    # Marquetry: v1 botanical pattern, veneer relief and varnish on top.
    w_color, w_normal, w_rough, w_ao = load_set(manifest, 'walnut_veneer', small)
    inlay = pixels(texdir / 'OE_Botanical_Marquetry_BaseColor.png')[..., :3]
    # The v1 pattern is flat colour: give both the dark ground and the light inlaid leaves real veneer grain, and darken
    # the ground to walnut like the panelling around it.
    grain = linear(w_color).mean(axis=2, keepdims=True)
    grain = 1 + 1.3 * (grain / grain.mean() - 1)
    lin = linear(inlay)
    light = (lin.mean(axis=2, keepdims=True) > .12).astype(np.float32)
    lin = lin * (light + (1 - light) * .45)
    save(texdir, 'OE_Botanical_Marquetry', srgb(np.clip(lin * grain, 0, 1)), scale_normal(w_normal, .3), w_ao, .24 + .16 * w_rough)

    # Velvet upholstery and curtains: deep pile, hardly any gloss (sheen comes from the shader).
    color, normal, rough, ao = load_set(manifest, 'velour_velvet', small)
    save(texdir, 'OE_Petrol_velvet_Fabric', recolour(color, (.035, .10, .105), 1.1), scale_normal(normal, .7), ao, np.maximum(.82, rough))
    save(texdir, 'OE_Champagne_velvet', recolour(color, (.45, .32, .16), 1.1), scale_normal(normal, .7), ao, np.maximum(.8, rough))

    # Ceiling: matt ivory paint over plaster.
    color, normal, rough, ao = load_set(manifest, 'white_plaster_02', small)
    save(texdir, 'OE_Ivory', recolour(color, (.68, .60, .44), .25), scale_normal(normal, .10), 1.0, .72 + .1 * rough)

    # Carpet: v1 Art Deco pattern woven into a real pile structure.
    color, normal, rough, ao = load_set(manifest, 'quatrefoil_jacquard_fabric', small)
    carpet = pixels(texdir / 'OE_ArtDeco_Carpet_BaseColor.png', (small, small))[..., :3]
    lum = linear(color).mean(axis=2, keepdims=True)
    pile = srgb(np.clip(linear(carpet) * (1 + .35 * (lum / lum.mean() - 1)), 0, 1))
    save(texdir, 'OE_ArtDeco_Carpet', pile, scale_normal(normal, .8), ao, np.maximum(.88, rough))

    # Lamp shades and etched glass keep their v1 colours (they glow), with a fine fabric / frosted relief.
    for name, strength, roughness in [('OE_Silk_glow_flicker2', .35, .6), ('OE_Etched_glow', .1, .35)]:
        base = pixels(texdir / f'{name}_BaseColor.png', (small, small))[..., :3]
        save(texdir, name, base, scale_normal(normal, strength), 1.0, roughness)

    # Polished brass: no painted highlights, only a very fine directional polish in the roughness.
    polish = .5 + .5 * np.sin(xx * 1400 + .9 * np.sin(yy * 61))
    save(texdir, 'OE_Polished_brass', np.array((.80, .66, .38)), flat_small, 1.0, .20 + .05 * polish, 1.0, small)

    # Window glass: faint smears (base colour = what the light catches), slightly rougher where smeared.
    smear = np.clip((np.sin(xx * 23 + 2 * np.sin(yy * 7)) + np.cos(yy * 31 + xx * 5)) * .22 + .1, 0, 1)
    save(texdir, 'OE_Window_glass', smear[..., None] * np.array([.018, .017, .015]), flat_small, 1.0, .03 + .08 * smear, 0.0, small)
    return manifest
