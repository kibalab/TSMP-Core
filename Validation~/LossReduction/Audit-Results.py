"""Independently recount production CSV pairs for the loss-reduction report."""
import argparse
import csv
import hashlib
import json
from pathlib import Path


def rows(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def index(values):
    result = {(row['profile'], row['file']): row for row in values}
    assert len(result) == len(values), 'Duplicate observation identity'
    return result


def compare(before_rows, after_rows):
    before, after = index(before_rows), index(after_rows)
    assert before.keys() == after.keys(), 'Changed observation set'
    result = dict(observations=len(before), beforeExact=0, afterExact=0,
                  additionalExact=0, lostPreviouslyExact=0, wrongAccepted=0,
                  additionalOriginalBytes=0, beforeRetryReadbacks=0, afterRetryReadbacks=0,
                  sparseSearches=0, sparseHypotheses=0, maximumHypotheses=0)
    for key, old in before.items():
        new = after[key]
        assert old['expectedBytes'] == new['expectedBytes'] and old['expectedFrame'] == new['expectedFrame']
        assert old['status'] in ('EXACT', 'REJECTED') and new['status'] in ('EXACT', 'REJECTED')
        old_exact, new_exact = old['status'] == 'EXACT', new['status'] == 'EXACT'
        result['beforeExact'] += old_exact
        result['afterExact'] += new_exact
        result['additionalExact'] += new_exact and not old_exact
        result['lostPreviouslyExact'] += old_exact and not new_exact
        if new_exact:
            assert new['headerValid'] == 'True' and new['payloadCrcVerified'] == 'True'
            assert new['actualFrame'] == new['expectedFrame'] and new['bytes'] == new['expectedBytes']
            result['wrongAccepted'] += int(new['byteMismatches']) != 0
        if new_exact and not old_exact:
            result['additionalOriginalBytes'] += int(new['expectedBytes'])
            assert int(new['recoveredSparsePayloads']) == 1
        searches, hypotheses = int(new['sparseSearches']), int(new['sparseHypotheses'])
        assert 0 <= searches <= 1 and 0 <= hypotheses <= 384
        if old_exact:
            assert searches == 0 and hypotheses == 0, 'Previously exact output performed sparse work'
        result['sparseSearches'] += searches
        result['sparseHypotheses'] += hypotheses
        result['maximumHypotheses'] = max(result['maximumHypotheses'], hypotheses)
        for field in ('headerAttempts', 'payloadAttempts'):
            assert old[field] == new[field], 'Sparse recovery added a GPU retry'
            result['beforeRetryReadbacks'] += int(old[field])
            result['afterRetryReadbacks'] += int(new[field])
    assert result['lostPreviouslyExact'] == result['wrongAccepted'] == 0
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--prior-evidence', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    sparse = args.evidence
    sources = [args.prior_evidence / 'default-production/production-frames.csv',
               sparse / 'production-main/production-frames.csv']
    result = {'main': compare(rows(sources[0]), rows(sources[1]))}
    for label, directory in [('screenedPriorInput', 'production-screened-holdout'),
                             ('untouchedFreshInput', 'production-fresh')]:
        source = sparse / directory / 'production-frames.csv'
        sources.append(source)
        values = rows(source)
        assert {row['variant'] for row in values} == {'PriorRecovery', 'SparseRecovery'}
        result[label] = compare([r for r in values if r['variant'] == 'PriorRecovery'],
                                [r for r in values if r['variant'] == 'SparseRecovery'])
    result['status'] = 'PASS'
    result['sourceHashes'] = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({key: value for key, value in result.items() if key != 'sourceHashes'}, indent=2))


if __name__ == '__main__':
    main()
