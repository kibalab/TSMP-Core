"""Offline RGB16 candidate screen. CPU diagnostic, never GPU/transport certification."""
from __future__ import annotations
import argparse, csv, hashlib, json, time, zlib
from pathlib import Path
import numpy as np
from PIL import Image


def sample_blocks(image: np.ndarray, count: int, start: int, size: int = 4,
                  mode: str = 'mean') -> np.ndarray:
    width = image.shape[1] // 8
    ids = np.arange(start, start + count)
    xy = np.stack((ids % width * 8, ids // width * 8), axis=-1)
    offset = (8 - size) // 2
    grid_y, grid_x = np.mgrid[0:size, 0:size]
    pixels = image[xy[:, 1, None, None] + offset + grid_y,
                   xy[:, 0, None, None] + offset + grid_x].astype(np.float32)
    if mode == 'median':
        return np.median(pixels, axis=(1, 2))
    if mode == 'trim':
        ordered = np.sort(pixels.reshape(count, size * size, 3), axis=1)
        trim = max(1, size * size // 8)
        return ordered[:, trim:-trim].mean(axis=1)
    if mode == 'weighted':
        axis = np.minimum(np.arange(size) + 1, np.arange(size, 0, -1))
        weights = (axis[:, None] * axis[None, :]).astype(np.float32)
        return (pixels * weights[None, :, :, None]).sum(axis=(1, 2)) / weights.sum()
    return pixels.mean(axis=(1, 2))


def ycocg(values):
    return np.stack((values[..., 0] * .25 + values[..., 1] * .5 + values[..., 2] * .25,
                     values[..., 0] - values[..., 2],
                     values[..., 1] - .5 * (values[..., 0] + values[..., 2])), axis=-1)


def unpack(payload: bytes):
    raw = np.frombuffer(payload, np.uint8).astype(np.uint32)
    raw = np.pad(raw, (0, (-len(raw)) % 3)).reshape(-1, 3)
    words = raw[:, 0] | (raw[:, 1] << 8) | (raw[:, 2] << 16)
    symbols = np.stack((words & 4095, words >> 12), axis=-1).ravel()
    return symbols[: (len(payload) * 8 + 11) // 12].astype(np.int32)


def pack(symbols: np.ndarray, byte_count: int):
    symbols = np.pad(symbols.astype(np.uint32), (0, (-len(symbols)) % 2)).reshape(-1, 2)
    words = symbols[:, 0] | (symbols[:, 1] << 12)
    return np.stack((words & 255, (words >> 8) & 255, (words >> 16) & 255), axis=-1).ravel()[:byte_count].astype(np.uint8)


def seeds(rgb, axes):
    result = []
    for channel in range(3):
        calibration = axes[channel, :, channel]
        low, high = calibration[0], calibration[-1]
        if abs(float(high - low)) > .00001:
            estimated = np.rint(np.clip((rgb[:, channel] - low) / (high - low), 0, 1) * 15).astype(np.int32)
        else:
            estimated = np.full(len(rgb), 8, np.int32)
        ids = np.clip(estimated[:, None] + np.arange(-4, 5), 0, 15)
        distance = np.abs(rgb[:, channel, None] - calibration[ids])
        result.append(ids[np.arange(len(rgb)), np.argmin(distance, axis=1)])
    return np.stack(result, axis=-1)


def forward_palette(axes, model):
    levels = (24 + (np.arange(16) * 208 + 7) // 15).astype(np.float32)
    center = np.stack([np.array([np.interp(128, levels, axes[c, :, component]) for component in range(3)]) for c in range(3)])
    base = center.mean(axis=0)
    if model == 'full':
        return axes, base * 2
    if model == 'centered':
        return axes - center[:, None, :] + base[None, None, :], base * 2
    if model == 'affine':
        # Fit observed RGB as an affine function of known transmitted RGB.
        known = np.full((48, 4), 128, np.float32)
        known[:, 3] = 1
        for c in range(3):
            known[c * 16:(c + 1) * 16, c] = levels
        coeff, _, _, _ = np.linalg.lstsq(known, axes.reshape(48, 3), rcond=None)
        fit = []
        for c in range(3):
            fit.append(levels[:, None] * coeff[c][None, :])
        return np.stack(fit), -coeff[3]
    if model == 'affine-closed':
        centered = levels - np.float32(128)
        denominator = np.sum(centered * centered, dtype=np.float32)
        slopes = np.sum(axes * centered[None, :, None], axis=1, dtype=np.float32) / denominator
        predictors = centered[None, :, None] * slopes[:, None, :]
        base = axes.mean(axis=(0, 1), dtype=np.float32)
        return predictors, -base
    if model == 'diag':
        out = np.zeros_like(axes)
        for c in range(3): out[c, :, c] = axes[c, :, c]
        return out, np.zeros(3, np.float32)
    raise ValueError(model)


def decode(rgb, axes, model='diag', radius=2, weights=(2, .85, .85),
           global_seed=False, report_expected=None):
    seed = seeds(rgb, axes)
    if global_seed:
        for c in range(3):
            seed[:, c] = np.argmin(abs(rgb[:, c, None] - axes[c, :, c][None, :]), axis=1)
    predictors, correction = forward_palette(axes, model)
    zz, yy, xx = np.mgrid[-radius:radius+1, -radius:radius+1, -radius:radius+1]
    deltas = np.stack((xx.ravel(), yy.ravel(), zz.ravel()), axis=-1)
    output = np.empty(len(rgb), np.int32)
    diagnostics = {'outsideRadius': 0, 'wrongSymbols': 0, 'correctCandidateRankSum': 0, 'correctCandidateRankMax': 0,
                   'misclassifiedButInside': 0, 'closeTopTwo': 0}
    for start in range(0, len(rgb), 256):
        end = min(start+256, len(rgb))
        unbounded = seed[start:end, None, :] + deltas[None, :, :]
        valid = ((unbounded >= 0) & (unbounded <= 15)).all(axis=-1)
        ids = np.clip(unbounded, 0, 15)
        predicted = predictors[0, ids[..., 0]] + predictors[1, ids[..., 1]] + predictors[2, ids[..., 2]] - correction
        diff = ycocg(rgb[start:end, None, :]) - ycocg(predicted)
        distance = (diff * diff * weights).sum(axis=-1)
        distance[~valid] = np.inf
        winner = np.argmin(distance, axis=1)
        choice = ids[np.arange(end-start), winner]
        output[start:end] = choice[:, 0] | (choice[:, 1] << 4) | (choice[:, 2] << 8)
        if report_expected is not None:
            expected = report_expected[start:end]
            true_levels = np.stack((expected & 15, (expected >> 4) & 15, (expected >> 8) & 15), axis=-1)
            inside = (np.abs(true_levels - seed[start:end]) <= radius).all(axis=1)
            diagnostics['outsideRadius'] += int((~inside).sum())
            wrong = output[start:end] != expected
            diagnostics['wrongSymbols'] += int(wrong.sum())
            diagnostics['misclassifiedButInside'] += int((wrong & inside).sum())
            correct_rgb = predictors[0, true_levels[:, 0]] + predictors[1, true_levels[:, 1]] + predictors[2, true_levels[:, 2]] - correction
            correct_diff = ycocg(rgb[start:end]) - ycocg(correct_rgb)
            correct_distance = (correct_diff * correct_diff * weights).sum(axis=-1)
            rank = 1 + (distance < correct_distance[:, None] - 1e-4).sum(axis=1)
            if inside.any():
                diagnostics['correctCandidateRankSum'] += int(rank[inside].sum())
                diagnostics['correctCandidateRankMax'] = max(diagnostics['correctCandidateRankMax'], int(rank[inside].max()))
            sorted_two = np.partition(distance, 1, axis=1)[:, :2]
            diagnostics['closeTopTwo'] += int((abs(sorted_two[:, 0] - sorted_two[:, 1]) < 1).sum())
    return output, diagnostics


def load_rows(evidence, scope, limit):
    rows = []
    for kind, manifest_file in [('obs', 'obs-gpu-manifest.json'), ('matrix', 'matrix-gpu-manifest.json')]:
        if scope == 'obs' and kind != 'obs': continue
        manifest = json.loads((evidence / manifest_file).read_text())
        profiles = {x['name']: x for x in manifest['profiles']}
        with (evidence / ('gpu-obs' if kind == 'obs' else 'gpu-matrix') / 'frames.csv').open(newline='') as stream:
            for row in csv.DictReader(stream):
                if 'rgb16' not in row['profile']: continue
                if scope == 'obs' and row['status'] == 'EXACT': continue
                if row['expectedFrameUnknown'] == 'True': continue
                profile = profiles[row['profile']]
                row['sourceImage'] = str(Path(profile['framesDirectory']) / row['file'])
                row['payloadFile'] = str(Path(profile['expectedPayloadDirectory']) / (Path(row['expectedFile']).stem + '.bin'))
                row['kind'] = kind
                rows.append(row)
    if scope == 'screen':
        # Every known OBS payload failure; deterministic samples across matrix
        # conditions and three payload sizes. Never choose on candidate outcome.
        obs = [r for r in rows if r['kind'] == 'obs' and r['rejectionStage'] == 'PAYLOAD_CRC']
        matrix = [r for r in rows if r['kind'] == 'matrix' and r['expectedFrame'] in ['1','2','3','100','101','102','199','200','201','298','299','300']]
        rows = obs + matrix
    if limit: rows = rows[:limit]
    return rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--scope', choices=['obs','screen','all'], default='obs')
    parser.add_argument('--limit', type=int, default=0)
    parser.add_argument('--variants', default='default')
    parser.add_argument('--luma-weights', default='2', help='Comma-separated Y weights; Co/Cg remain .85')
    args = parser.parse_args()
    args.results.mkdir(parents=True, exist_ok=False)
    (args.results / 'source.py').write_bytes(Path(__file__).read_bytes())
    variants=[]
    for size, sampling in [(4,'mean'), (2,'mean'), (4,'weighted'), (4,'trim'), (4,'median'), (6,'weighted')]:
        for model in ['diag','full','centered','affine','affine-closed']:
            for y_weight in [float(value) for value in args.luma_weights.split(',')]:
                suffix = '' if y_weight == 2 else f'-y{y_weight:g}'
                variants.append({'name': f'{model}-s{size}-{sampling}{suffix}', 'size': size, 'sampling': sampling, 'model': model, 'radius': 2, 'weights': (y_weight,.85,.85)})
    if args.variants != 'default':
        wanted = set(args.variants.split(','))
        variants = [v for v in variants if v['name'] in wanted]
    rows = load_rows(args.evidence, args.scope, args.limit)
    summaries = {v['name']: {'frames':0,'exact':0,'wrongBytes':0,'wrongSymbols':0,'savedBaselineFailures':0,'lostBaselineExact':0} for v in variants}
    fields = ['profile','file','expectedFrame','priorGpuStatus','priorGpuStage','priorGpuPayloadCrc','cpuPayloadCrc','payloadBytes','variant','byteMismatches','symbolMismatches','outsideRadius','misclassifiedButInside','correctCandidateRankSum','correctCandidateRankMax','closeTopTwo','cpuSeconds']
    gpu_crc_comparisons = 0
    gpu_crc_mismatches = 0
    started = time.perf_counter()
    with (args.results / 'frames.csv').open('w', newline='') as stream:
        writer=csv.DictWriter(stream, fieldnames=fields); writer.writeheader()
        for index, row in enumerate(rows):
            image=np.asarray(Image.open(row['sourceImage']).convert('RGB'))
            payload=Path(row['payloadFile']).read_bytes(); expected=unpack(payload)
            calibration_start=5*(image.shape[1]//8)
            payload_start=(5+(48+(image.shape[1]//8)-1)//(image.shape[1]//8))*(image.shape[1]//8)
            cache={}
            baseline_exact=None
            for variant in variants:
                tick=time.perf_counter(); size=variant['size']; sampling=variant['sampling']
                key=(size,sampling)
                if key not in cache:
                    cache[key]=(sample_blocks(image,len(expected),payload_start,size,sampling), sample_blocks(image,48,calibration_start,size,sampling).reshape(3,16,3))
                rgb,axes=cache[key]
                actual,diag=decode(rgb,axes,variant['model'],variant['radius'],variant['weights'],report_expected=expected)
                actual_bytes = pack(actual,len(payload))
                cpu_crc = f'{zlib.crc32(actual_bytes.tobytes()):08X}'
                byte_mismatches=int((actual_bytes!=np.frombuffer(payload,np.uint8)).sum())
                if variant['name']=='diag-s4-mean':
                    baseline_exact=byte_mismatches==0
                    if row.get('payloadCrcActual'):
                        gpu_crc_comparisons += 1
                        gpu_crc_mismatches += int(cpu_crc != row['payloadCrcActual'].upper())
                summary=summaries[variant['name']]; summary['frames']+=1; summary['exact']+=int(byte_mismatches==0); summary['wrongBytes']+=byte_mismatches; summary['wrongSymbols']+=diag['wrongSymbols']
                if baseline_exact is not None:
                    summary['savedBaselineFailures']+=int(not baseline_exact and byte_mismatches==0)
                    summary['lostBaselineExact']+=int(baseline_exact and byte_mismatches!=0)
                writer.writerow({'profile':row['profile'],'file':row['file'],'expectedFrame':row['expectedFrame'],'priorGpuStatus':row['status'],'priorGpuStage':row['rejectionStage'],'priorGpuPayloadCrc':row.get('payloadCrcActual',''),'cpuPayloadCrc':cpu_crc,'payloadBytes':len(payload),'variant':variant['name'],'byteMismatches':byte_mismatches,'symbolMismatches':diag.pop('wrongSymbols'),**diag,'cpuSeconds':time.perf_counter()-tick})
            stream.flush()
            print(f'{index+1}/{len(rows)} {row["profile"]} {row["file"]}',flush=True)
    result={'status':'CPU_DIAGNOSTIC_ONLY','scope':args.scope,'variants':variants,'summary':summaries,'seconds':time.perf_counter()-started,'rows':len(rows),'baselineGpuCrcComparisons':gpu_crc_comparisons,'baselineGpuCrcMismatches':gpu_crc_mismatches,'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    (args.results/'summary.json').write_text(json.dumps(result,indent=2))
    print(json.dumps(result,indent=2),flush=True)


if __name__=='__main__': main()
