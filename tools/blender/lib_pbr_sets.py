"""Generic CC0 photo texture sets for pipeline v2 rooms (Poly Haven, 2K PNG maps, checksums in a manifest).

Recolouring keeps the photo's structure and moves its mean linear colour to the accepted v1 material colour, so a
room keeps its approved palette. Raw downloads: ignored art/sources/pbr-sets/; manifest: art/pbr-sets-sources.json.
"""
import hashlib
import json
import subprocess
from pathlib import Path
from urllib.parse import urlparse

import numpy as np

from lib_pbr_test_textures import png, pixels, srgb, linear

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / 'art/sources/pbr-sets'
MANIFEST = ROOT / 'art/pbr-sets-sources.json'
MAPS = ['Diffuse', 'nor_gl', 'Rough', 'AO', 'arm']


def _api(url):
    return json.loads(subprocess.run(['curl', '-fsS', '--retry', '2', url], check=True, capture_output=True).stdout)


def fetch(assets):
    CACHE.mkdir(parents=True, exist_ok=True)
    manifest = json.loads(MANIFEST.read_text()) if MANIFEST.exists() else {'license': 'CC0-1.0', 'powered_by': 'Poly Haven', 'assets': {}}
    for asset in assets:
        entry = manifest['assets'].get(asset)
        if entry is None:
            files = _api(f'https://api.polyhaven.com/files/{asset}')
            info = _api(f'https://api.polyhaven.com/info/{asset}')
            maps = {kind: {k: files[kind]['2k']['png'][k] for k in ('url', 'md5', 'size')}
                    for kind in MAPS if kind in files and '2k' in files[kind]}
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


class Sets:
    def __init__(self, assets):
        self.manifest = fetch(assets)
        self.cache = {}

    def load(self, asset, n):
        key = (asset, n)
        if key not in self.cache:
            maps = self.manifest['assets'][asset]['maps']
            get = lambda kind: pixels(CACHE / maps[kind]['url'].split('/')[-1], (n, n))[..., :3]
            color = get('Diffuse')
            normal = get('nor_gl') * 2 - 1
            rough = get('Rough')[..., 0]
            ao = get('AO')[..., 0] if 'AO' in maps else get('arm')[..., 0] if 'arm' in maps else np.ones_like(rough)
            self.cache[key] = (color, normal, rough, ao)
        return self.cache[key]


def flat(n):
    return np.zeros((n, n, 3), np.float32) + (.5, .5, 1)


def scale_normal(normal, strength):
    n = normal.copy()
    n[..., :2] *= strength
    n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-8)
    return n * .5 + .5


def recolour(encoded, target_linear, contrast=1.0):
    lin = linear(np.clip(encoded, 0, 1))
    lum = lin.mean(axis=2, keepdims=True)
    rel = 1 + (lum / max(float(lum.mean()), 1e-6) - 1) * contrast
    return srgb(np.clip(rel * np.array(target_linear), 0, 1))


def relief(base_encoded, structure_encoded, amount):
    """A procedural v1 pattern (carpet, marquetry) woven into a photo's light/dark structure."""
    lum = linear(np.clip(structure_encoded, 0, 1)).mean(axis=2, keepdims=True)
    return srgb(np.clip(linear(np.clip(base_encoded, 0, 1)) * (1 + amount * (lum / lum.mean() - 1)), 0, 1))


def save(texdir, name, color, normal, ao, rough, metal=0.0, n=1024):
    # Photo AO is strong and blotchy, and the bake already holds the real occlusion: keep only a trace.
    ao = 1 - (1 - np.asarray(ao, np.float32)) * .35
    orm = np.empty((n, n, 3), np.float32)
    orm[..., 0], orm[..., 1], orm[..., 2] = ao, rough, metal
    png(texdir / f'{name}_BaseColor.png', np.broadcast_to(color, (n, n, 3)).astype(np.float32))
    png(texdir / f'{name}_Normal.png', np.broadcast_to(normal, (n, n, 3)))
    png(texdir / f'{name}_ORM.png', orm)


def polished_metal(texdir, name, colour, rough=.20, n=1024):
    yy, xx = np.mgrid[:n, :n] / n
    polish = .5 + .5 * np.sin(xx * 1400 + .9 * np.sin(yy * 61))
    save(texdir, name, np.array(colour), flat(n), 1.0, rough + .05 * polish, 1.0, n)


def window_glass(texdir, name, n=512):
    yy, xx = np.mgrid[:n, :n] / n
    smear = np.clip((np.sin(xx * 23 + 2 * np.sin(yy * 7)) + np.cos(yy * 31 + xx * 5)) * .22 + .1, 0, 1)
    save(texdir, name, smear[..., None] * np.array([.018, .017, .015]), flat(n), 1.0, .03 + .08 * smear, 0.0, n)


def existing(texdir, name, n=1024):
    """The accepted v1 base colour (pattern, glow colour). Kept once in the ignored art/sources/v1-basecolor/, so
    running a v2 script again starts from v1 rather than from its own previous output."""
    import shutil
    keep = ROOT / 'art/sources/v1-basecolor' / f'{name}_BaseColor.png'
    if not keep.exists():
        keep.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(texdir / f'{name}_BaseColor.png', keep)
    return pixels(keep, (n, n))[..., :3]
