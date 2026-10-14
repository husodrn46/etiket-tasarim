#!/usr/bin/env python3
"""Copy a vetted source snapshot without private history or local state. Never pushes."""
import hashlib
import io
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PUBLIC_DOCS = {'docs/GENEL-URUN.md', 'docs/MIMARI.md', 'docs/LISANSLAR.md', 'docs/KULLANIM-GELISTIRMELERI.md'}
GENERATED_REPORTS = {'SOURCE-MANIFEST.json', 'PUBLICATION-CHECK.json'}
PRIVATE_ASSETS = {'src/AkelEtiket.App/Assets/akel-logo.png', 'src/AkelEtiket.Design/Assets/akel-logo.png'}
RULES = {
    'company_identity': re.compile(r'AKEL\s+MELAM[İI]N|akelmelamin[.]com', re.I),
    'company_gln': re.compile(r'8685[ -]?3442[ -]?00013|8685[ ]?34420'),
    'internal_machine': re.compile(r'AKL-PC\d+|AKEL-SRV\d+', re.I),
    'private_key': re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    'provider_key': re.compile(r'sk-(?:proj-|ant-api03-)[A-Za-z0-9_-]{40,}'),
    'github_token': re.compile(r'gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{60,}'),
}


def scan(data, location, findings, depth=0):
    for encoding in ('utf-8', 'utf-16-le', 'cp1254'):
        text = data.decode(encoding, errors='ignore')
        for name, rule in RULES.items():
            if rule.search(text): findings.append({'file': location, 'rule': name, 'encoding': encoding})
    if depth < 3 and zipfile.is_zipfile(io.BytesIO(data)):
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            for item in archive.infolist():
                if item.file_size > 32 * 1024 * 1024:
                    raise ValueError(f'Archive entry too large: {location}/{item.filename}')
                if not item.is_dir(): scan(archive.read(item), location + '/' + item.filename, findings, depth + 1)


def main():
    if len(sys.argv) != 2: raise SystemExit('Usage: python tools/publication/prepare_source.py <new-empty-directory>')
    target = Path(sys.argv[1]).resolve()
    if target == ROOT or ROOT in target.parents: raise SystemExit('Destination must be outside the working repository.')
    if target.exists() and any(target.iterdir()): raise SystemExit('Destination is not empty; nothing was overwritten.')
    paths = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=ROOT).decode().split('\0')
    included, excluded, findings = [], [], []
    for name in sorted(set(filter(None, paths))):
        if name in GENERATED_REPORTS: continue
        if name in PRIVATE_ASSETS or name.startswith('docs/') and name not in PUBLIC_DOCS:
            excluded.append(name)
            continue
        source = ROOT / name
        if not source.is_file() or source.is_symlink() or not source.resolve().is_relative_to(ROOT):
            raise ValueError(f'Not a regular source file: {name}')
        data = source.read_bytes()
        scan(data, name, findings)
        included.append((name, hashlib.sha256(data).hexdigest()))
    report = {'files': len(included), 'excluded': excluded, 'findings': findings, 'historyIncluded': False, 'published': False}
    # Report labels and paths only; never print any matching secret/identifier value.
    print(json.dumps(report, ensure_ascii=False, indent=2))
    if findings: raise SystemExit('Source scan failed. Nothing was copied.')
    target.mkdir(parents=True, exist_ok=True)
    for name, _ in included:
        destination = target / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / name, destination)
    (target / 'SOURCE-MANIFEST.json').write_text(json.dumps(dict(included), ensure_ascii=False, indent=2) + '\n')
    (target / 'PUBLICATION-CHECK.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')


if __name__ == '__main__': main()
