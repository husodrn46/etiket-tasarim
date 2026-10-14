#!/usr/bin/env python3
"""Collect resolved NuGet license metadata and verbatim upstream/package notices."""
import argparse, hashlib, json, re, shutil, urllib.request, urllib.error
from pathlib import Path
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor


def collect(root, cache, output):
    packages = {}
    for project in ['AkelEtiket.App', 'AkelEtiket.Worker', 'AkelEtiket.Mcp']:
        assets = root / 'src' / project / 'obj/project.assets.json'
        if assets.exists():
            for name, meta in json.loads(assets.read_text())['libraries'].items():
                if meta.get('type') == 'package': packages[name] = meta
    records = []
    output.mkdir(parents=True, exist_ok=True)
    for name in sorted(packages):
        package, version = name.split('/')
        folder = cache / name.lower()
        spec = next(folder.glob('*.nuspec'))
        entries = {e.tag.rsplit('}', 1)[-1]: e for e in ET.parse(spec).getroot().iter()}
        def value(key):
            node = entries.get(key)
            return '' if node is None else (node.text or '').strip()
        repo_node = entries.get('repository')
        repo = {} if repo_node is None else repo_node.attrib
        record = {'package': package, 'version': version, 'license': value('license'),
                  'authors': value('authors'), 'copyright': value('copyright'),
                  'repository': repo, 'files': [], 'sources': []}
        dest = output / (package + '.' + version)
        dest.mkdir(exist_ok=True)
        for file in folder.rglob('*'):
            if file.is_file() and re.match(r'(?i)^(licen[sc]e|copying|copyright|notice|third[-_ ]?party)', file.name) and file.suffix.lower() not in ['.dll', '.xml', '.json']:
                relative = file.relative_to(folder)
                target = dest / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(file, target)
                record['files'].append(str(relative))
        record['_dest'] = str(dest)
        record['_repo'] = repo.get('url') or value('projectUrl')
        records.append(record)
    # Package metadata pins the source commit where available; never fetch a moving main branch.
    def fetch(record):
        if record['files']: return record
        repo = record['_repo'].removesuffix('.git').rstrip('/')
        if not repo.startswith('https://github.com/'): return record
        repo = repo[len('https://github.com/'):]
        refs = [record['repository']['commit']] if record['repository'].get('commit') else ['v' + record['version'], record['version'], 'v' + record['version'] + '.0']
        for ref in refs:
            for name in ['LICENSE', 'LICENSE.txt', 'LICENSE.md', 'LICENSE.TXT', 'license.txt', 'COPYING']:
                url = f'https://raw.githubusercontent.com/{repo}/{ref}/{name}'
                try:
                    with urllib.request.urlopen(url, timeout=15) as response: data = response.read(256 * 1024)
                    if len(data) < 100: continue
                    filename = 'UPSTREAM-' + name
                    (Path(record['_dest']) / filename).write_bytes(data)
                    record['files'].append(filename)
                    record['sources'].append({'url': url, 'sha256': hashlib.sha256(data).hexdigest()})
                    return record
                except (urllib.error.URLError, TimeoutError): pass
        return record
    with ThreadPoolExecutor(max_workers=6) as pool: records = list(pool.map(fetch, records))
    for record in records:
        record.pop('_dest'); record.pop('_repo')
    report = {'packages': records, 'missingLicenseFiles': [r['package'] + '/' + r['version'] for r in records if not r['files']]}
    (output / 'THIRD-PARTY-PACKAGES.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'packageCount': len(records), 'missingLicenseFiles': report['missingLicenseFiles']}), flush=True)
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--cache', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = collect(Path(__file__).resolve().parents[2], args.cache.resolve(), args.output.resolve())
    raise SystemExit(bool(result['missingLicenseFiles']))
