"""Inventory named cases (not assertions); optionally verify real runner reports.
Usage: python audit-test-coverage.py --project <Unity project> --zlua <ZLuaTest project> --log <fresh log> ...
No third-party dependencies. Counts are source inventory, never claims of executed coverage.
"""
import argparse, collections, json, re
from pathlib import Path


def inventory(root, extension):
    if not root.is_dir():
        raise ValueError(f'Missing case source directory: {root}')
    items = []
    for file in sorted(root.rglob('*' + extension)):
        source = file.read_text(encoding='utf-8-sig')
        if extension == '.lua':
            source = re.sub(r'--\[(=*)\[[\s\S]*?\]\1\]', '', source)
            names = re.findall(r'^\s*function\s+M\.(test_\w+)\s*\(', source, re.M)
        else:
            source = re.sub(r'/\*[\s\S]*?\*/', '', source)
            names = re.findall(r'^\s*(test_\w+)\s*\(', source, re.M)
        duplicate = [n for n, count in collections.Counter(names).items() if count > 1]
        if duplicate:
            raise ValueError(f'Duplicate case names: {file}: {duplicate}')
        suite = re.search(r"defineSuite\(\s*['\"]([^'\"]+)", source) if extension == '.js' else None
        items.append(dict(file=file.relative_to(root).as_posix(), suite=suite[1] if suite else None, cases=names))
    ids = [x['suite'] for x in items if x['suite']]
    if len(ids) != len(set(ids)):
        raise ValueError('Duplicate suite IDs')
    return dict(files=len(items), namedCases=sum(len(x['cases']) for x in items), items=items)


def verify(log, expected):
    text = log.read_text(encoding='utf-8', errors='replace')
    reports = re.findall(r'\[ZenTS\]\[case-summary\] (\{[^\r\n]+\})', text)
    if len(reports) != 1:
        raise ValueError(f'{log}: expected exactly one case-summary, got {len(reports)}')
    report = json.loads(reports[0])
    actual = {s['id']: s for s in report['suites']}
    wanted = {s['suite']: len(s['cases']) for s in expected['items'] if s['suite']}
    if actual.keys() != wanted.keys():
        raise ValueError(f'{log}: missing/unexpected suites: {actual.keys() ^ wanted.keys()}')
    for name, count in wanted.items():
        s = actual[name]
        if s['total'] != count or s['passed'] + s['failed'] + s['skipped'] != count or s['failed']:
            raise ValueError(f'{log}: bad case result: {s}')
        if len(s['skips']) != s['skipped']:
            raise ValueError(f'{log}: missing skip reasons: {s}')
        for skip in s['skips']:
            if name != 'p3.alignment' or skip['name'] != 'test_main_thread_contract' or not skip.get('reason') or 'Player engine=HostJs' not in text:
                raise ValueError(f'{log}: unexpected skip: {skip}')
    for key in ['total', 'passed', 'failed', 'skipped']:
        if report[key] != sum(s[key] for s in actual.values()):
            raise ValueError(f'{log}: incorrect {key} aggregate')
    if report['total'] != expected['namedCases']:
        raise ValueError(f'{log}: source/execution count mismatch')
    # Also require the surrounding smoke/TS acceptance, not a detached unit report.
    if not re.search(r'summary: total=56 passed=56 failed=0', text):
        raise ValueError(f'{log}: full module matrix not green')
    if not re.search(r'Test summary: total=(8|10) passed=\1 failed=0', text) or '[ZenTS.TsDemo] ALL OK' not in text:
        raise ValueError(f'{log}: full smoke/TS acceptance not green')
    return dict(log=str(log), **{k:report[k] for k in ['total','passed','failed','skipped']})


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--project', required=True, type=Path)
    p.add_argument('--zlua', type=Path)
    p.add_argument('--log', action='append', type=Path, default=[])
    p.add_argument('--output', type=Path)
    args = p.parse_args()
    js = inventory(args.project / 'Assets/StreamingAssets/Tests/Js/cases', '.js')
    result = {'counting': 'Named test_* declarations, excluding comments; not assertion/line coverage.', 'zents': js}
    if args.zlua:
        result['zlua'] = inventory(args.zlua / 'Assets/StreamingAssets/Tests/Lua/cases', '.lua')
    result['executions'] = [verify(log, js) for log in args.log]
    payload = json.dumps(result, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(payload+'\n', encoding='utf-8')
    print(json.dumps({k:{a:b for a,b in v.items() if a!='items'} if isinstance(v,dict) else v for k,v in result.items()},ensure_ascii=True))

if __name__ == '__main__':
    main()
