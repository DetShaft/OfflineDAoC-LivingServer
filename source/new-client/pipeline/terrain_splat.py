"""Decode DAoC's Catacombs-era ground blend (ter<id>.mpk) for MMOEngine.

    python terrain_splat.py 200 --check     # compare the decoded blend against the client's own baked ground (lod)

ter<id>.mpk holds textures.csv (per patch: the ground textures blended there, in order, with tiles per patch and
whether they tile) and patchXXYY-LL.dds weight maps (128 x 128 DXT3, XX = patch column east, YY = patch row south).
Decoded layout (verified with --check): each file carries three weights in RGB; its alpha is a copy of the next file's
red channel (multipass blending), so it is skipped. Weights then follow textures.csv order: weight 0 belongs to the
base texture (always 0: it fills underneath) and each later texture is laid over the result by its weight, the
non-tiling zone colour map last.
"""
import argparse, csv, io, sys
from collections import defaultdict
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from daoc_mpk import read_mpk
from assets_pipeline import CLIENT

# Pillow 9.1 named the filter Image.Resampling.BILINEAR; older Pillow keeps Image.BILINEAR.
_RESAMPLE = getattr(getattr(Image, 'Resampling', Image), 'BILINEAR')


def _plane(weight):
    """One coverage map as a 2D float array. A colour image keeps its first channel."""
    plane = np.asarray(weight, dtype=np.float32)
    if plane.ndim == 3:
        plane = plane[..., 0]
    return plane


def channel_shape(weights, default=(128, 128)):
    """The size most of these maps already are. A tie keeps the earliest one. Missing maps are ignored."""
    shapes = []
    for weight in weights:
        if weight is None:
            continue
        plane = _plane(weight)
        if plane.ndim == 2 and plane.shape[0] and plane.shape[1]:
            shapes.append((int(plane.shape[0]), int(plane.shape[1])))
    if not shapes:
        return default
    return max(shapes, key=lambda shape: (shapes.count(shape), -shapes.index(shape)))


def fit_channel(weight, shape):
    """weight at shape, or zeros when weight is missing. A map of another size is resampled:
    it is the same patch, only the sample count differs."""
    if weight is None:
        return np.zeros(shape, np.float32)
    plane = np.ascontiguousarray(_plane(weight))
    if plane.shape == shape:
        return plane
    image = Image.fromarray(plane, mode='F')
    image = image.resize((shape[1], shape[0]), _RESAMPLE)
    return np.asarray(image, dtype=np.float32)


def pack_weights(weights):
    """Four coverage maps as one RGBA image.

    A patch's blend maps are one picture of that patch. They are not always 128x128. Killaloe's
    are not, and a patch with fewer than four or eight overlays used to pad the rest with
    128x128 zeros. np.stack then raised ValueError (owner, 2026-11-14: the zone 253 build died
    on the splat patch after the painted ground was written). Missing maps become zeros of the
    size the real maps use, and a map of another size is resampled to that size. The shader
    samples the image by the patch's own 0..1 coordinates, so the sample count can differ from 128.
    """
    shape = channel_shape(weights)
    planes = [fit_channel(weight, shape) for weight in list(weights)[:4]]
    while len(planes) < 4:
        planes.append(np.zeros(shape, np.float32))
    packed = np.clip(np.stack(planes, axis=2), 0, 1) * 255
    return Image.fromarray(packed.astype(np.uint8), 'RGBA')


def decode(zone_id, ter_path=None):
    """{(px, py): [{'tex', 'tiles', 'tileable', 'weight': float 0..1}]} for every patch.

    Every weight in one patch has the same shape. Classic zones are 128x128. A zone whose maps
    are another size (Killaloe) keeps that size, and a missing map is zeros of it.

    ter_path is the archive itself. Zone 253's build passes retail zone 237's ter237.mpk, which is not
    under zones/zone253. Other zones keep the old lookup.
    """
    path = Path(ter_path) if ter_path else CLIENT / f'zones/zone{zone_id:03d}/ter{zone_id:03d}.mpk'
    ter = read_mpk(str(path))
    layers = defaultdict(list)
    for r in list(csv.reader(io.StringIO(ter['textures.csv'].decode('latin1'))))[1:]:
        if len(r) > 10 and r[0].strip().isdigit():
            layers[(int(r[0]), int(r[1]))].append({'tex': r[2].strip().lower(), 'tiles': float(r[9] or 1),
                                                   'tileable': r[10].strip() == '1'})
    files = defaultdict(list)
    for n in ter:
        if n.startswith('patch') and n.endswith('.dds'):
            files[(int(n[5:7]), int(n[7:9]))].append(n)
    out = {}
    for key, lst in layers.items():
        chans = []
        for f in sorted(files.get(key, [])):
            px = np.asarray(Image.open(io.BytesIO(ter[f])).convert('RGBA'), dtype=np.float32) / 255.0
            chans += [px[..., 0], px[..., 1], px[..., 2]]          # alpha duplicates the next file's red: skipped
        shape = channel_shape(chans)
        chans = [fit_channel(c, shape) for c in chans]
        for i, layer in enumerate(lst):
            layer['weight'] = chans[i] if i < len(chans) else np.zeros(shape, np.float32)
        out[key] = lst
    return out


def composite_means(patch, colors):
    """Low-frequency colour of one patch: base, then each layer over the result by its weight."""
    shape = channel_shape(layer['weight'] for layer in patch)
    c = np.ones((*shape, 3), np.float32) * colors[patch[0]['tex']]
    for layer in patch[1:]:
        w = fit_channel(layer['weight'], shape)[..., None]
        c = c * (1 - w) + colors[layer['tex']] * w
    return c


def check(zone_id):
    from texture_index import TextureIndex
    idx = TextureIndex()
    patches = decode(zone_id)
    lod = read_mpk(str(CLIENT / f'zones/zone{zone_id:03d}/lod{zone_id:03d}.mpk'))
    colors = {}
    for p in patches.values():
        for layer in p:
            if layer['tex'] not in colors:
                img = idx.image(layer['tex'])
                colors[layer['tex']] = np.asarray(img.convert('RGB').resize((1, 1), Image.BOX), np.float32)[0, 0] / 255.0
    scores = []
    for (x, y), p in sorted(patches.items()):
        if any(l['tex'] not in colors for l in p):
            continue
        ours = composite_means([l for l in p if l['tileable']], colors).mean(axis=2)
        ref = np.asarray(Image.open(io.BytesIO(lod[f'lod{x:02d}-{y:02d}.dds'])).convert('L').resize(ours.shape[::-1]),
                         np.float32)
        if ours.std() > 1e-3 and ref.std() > 1e-3:
            scores.append(np.corrcoef(ours.ravel(), ref.ravel())[0, 1])
    print(f'zone {zone_id}: {len(scores)} patches, correlation of decoded blend with the baked ground: '
          f'median {np.median(scores):.2f}, worst {min(scores):.2f}')
    return scores


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('zone', type=int)
    ap.add_argument('--check', action='store_true')
    a = ap.parse_args()
    if a.check:
        check(a.zone)
