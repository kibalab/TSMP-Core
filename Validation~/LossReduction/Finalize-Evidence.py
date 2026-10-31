"""Bind the completed automated checks and final refs into one evidence summary."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    root = args.results
    parent = root.parent
    sparse, budget, fec = (parent / name for name in (
        'sparse-recovery-20261031', 'payload-budget-20261031', 'fec-feasibility-20261031'))
    checks = {
        'coreIntegration': root / 'core-safety-gamma-final/result.txt',
        'snapshotGamma': root / 'snapshot-gamma-serial/Play.txt',
        'snapshotLinear': root / 'snapshot-linear-serial/PlayLinear.txt',
        'encoderNative': budget / 'native-final/result.txt',
        'encoderVm': budget / 'vrc-final/result.txt',
        'productionMain': sparse / 'production-main/result.txt',
        'productionScreenedInput': sparse / 'production-screened-holdout/result.txt',
        'productionFreshInput': sparse / 'production-fresh/result.txt',
        'helperVmQuiet': sparse / 'udon-vm-final-quiet/result.txt',
        'helperVmSaturatedSuccess': sparse / 'udon-vm-budget-success-quiet/result.txt',
        'compiledDecoderHeap': sparse / 'decoder-heap-final/result.txt',
    }
    udon = list((root / 'udon-compile-final').glob('*-result.txt'))
    assert len(udon) == 1, 'Ambiguous Udon compilation result'
    checks['fullUdonCompilation'] = udon[0]
    for label, path in checks.items():
        assert path.read_text(encoding='utf-8-sig').splitlines()[0] == 'PASS', f'{label}: {path}'
    audits = {
        'sourceOwnership': root / 'source-audit-final.json',
        'productionRecount': root / 'production-independent-audit.json',
        'encoder': budget / 'independent-audit.json',
        'fecArtifacts': fec / 'independent-fec-audit.json',
        'fecCurrentComparison': fec / 'independent-fec-current-join-audit.json',
        'vmQuietTiming': sparse / 'udon-vm-final-quiet-summary.json',
        'vmSaturatedSuccessTiming': sparse / 'udon-vm-budget-success-quiet-summary.json',
    }
    loaded = {}
    for label, path in audits.items():
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        assert data['status'] == 'PASS', f'{label}: {path}'
        loaded[label] = data
    source = loaded['sourceOwnership']
    production = loaded['productionRecount']
    assert production['main']['observations'] == 20439
    assert production['screenedPriorInput']['observations'] == 900
    assert production['untouchedFreshInput']['observations'] == 900
    summary = dict(
        status='IMPLEMENTED_AND_AUTOMATICALLY_VALIDATED_WITH_SEPARATE_FEC_PROTOTYPE',
        finishedUtc=datetime.now(timezone.utc).isoformat(),
        repositories=source['repositories'],
        newCommits=sum(item['newCommits'] for item in source['repositories']),
        mainRefsPreserved=sum(item['checks']['mainPreserved'] for item in source['repositories']),
        sourceComparisons=source['comparisons'],
        existingGuidsPreserved=source['existingGuidsPreserved'],
        production={key: production[key] for key in ('main', 'screenedPriorInput', 'untouchedFreshInput')},
        requiredChecks={name: str(path) for name, path in checks.items()},
        requiredAudits={name: str(path) for name, path in audits.items()},
        implemented=['Bounded observed-byte sparse payload recovery', 'Independent transmission payload target'],
        prototypeOnly=['Protected-metadata, interleaved Reed-Solomon FEC receiver'],
        productionWireFormatChanged=False, merged=False, pushed=False, released=False,
        liveVrchatRun=False, liveServiceRun=False,
        limitations=[
            'Finite offline files and editor VM tests; no live VRChat FPS or VRCDN delivery measurements.',
            'Sparse recovery adds failure-only CPU cost, up to 384 candidate hypotheses; measured cases are not an absolute time bound.',
            'Transmission target does not fragment oversized fields and has no measured transport loss percentage.',
            'FEC is a separate reference receiver and experimental format, not TSMP runtime acceptance or Udon FEC performance.',
            'FEC RGB16 RS32 CRF18 loses one current sparse success; RGB16 CRF18 gains are short payloads only.',
            'Failed/import/pilot/concurrent runs remain preserved; serial and quiet final checks pass.',
        ])
    (root / 'summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps({key: summary[key] for key in ('status', 'newCommits', 'mainRefsPreserved', 'sourceComparisons', 'production')}, indent=2))


if __name__ == '__main__':
    main()
