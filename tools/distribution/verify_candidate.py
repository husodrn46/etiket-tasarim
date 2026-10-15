#!/usr/bin/env python3
"""Offline integrity/privacy checks for a generated candidate; does not execute it."""
import hashlib, importlib.util, json, struct, sys, zipfile
from pathlib import Path

root = Path(__file__).resolve().parents[2]
candidate = Path(sys.argv[1]).resolve()
result = json.loads((candidate/'CANDIDATE-RESULT.json').read_text())
manifest = json.loads((candidate/'PAYLOAD-MANIFEST.json').read_text())
spec = importlib.util.spec_from_file_location('source_scan', root/'tools/publication/prepare_source.py')
scan = importlib.util.module_from_spec(spec); spec.loader.exec_module(scan)
findings = []
for kind in ['installer','portable']:
    file = candidate/result[kind]['file']
    assert hashlib.sha256(file.read_bytes()).hexdigest() == result[kind]['sha256'], kind+' hash mismatch'
with zipfile.ZipFile(candidate/result['portable']['file']) as archive:
    assert archive.testzip() is None
    names={n.removeprefix(result.get('portableRoot','Etiket-Tasarim/')):n for n in archive.namelist()}
    assert set(names)==set(manifest), 'Portable file set mismatch'
    for name, expected in manifest.items():
        data=archive.read(names[name])
        assert hashlib.sha256(data).hexdigest()==expected, 'Payload hash mismatch: '+name
        assert not name.lower().endswith(('.pdb','.nupkg','.pyc')), 'Build artifact: '+name
        assert not any(part in {'.git','Ayarlar','GS1','BaskiGecmisi'} for part in Path(name).parts), 'User data directory: '+name
        scan.scan(data,name,findings)
    for name in ['Etiket Tasarim','AkelEtiket.Mcp','AkelEtiket.Worker']:
        config=json.loads(archive.read(names[name+'.runtimeconfig.json']))['runtimeOptions']
        assert 'frameworks' not in config and 'framework' not in config, 'Framework-dependent app: '+name
        assert all(f['version']=='10.0.13' for f in config['includedFrameworks']), 'Runtime patch mismatch'
    if result.get('sourceLicenseChosen'):
        assert hashlib.sha256(archive.read(names['LICENSE'])).hexdigest()==result['sourceLicenseSha256']
        assert archive.read(names['LICENSE']).startswith(b'MIT License')
    notices=json.loads(archive.read(names['licenses/THIRD-PARTY-PACKAGES.json']))
    assert not notices['missingLicenseFiles']
    assert all(record['files'] for record in notices['packages'])
assert not findings, 'Private data findings: '+json.dumps(findings)
script=(root/'installer/EtiketTasarim.nsi').read_text()
assert 'RequestExecutionLevel user' in script
assert 'Software\\Classes' not in script, 'Unexpected file association write'
assert 'RMDir /r' not in (candidate/'uninstall-files.nsh').read_text(), 'Recursive user file removal'
assert '$APPDATA' not in (candidate/'uninstall-files.nsh').read_text()
report={'payloadFilesVerified':len(manifest),'licensePackages':len(notices['packages']),
        'privateDataFindings':findings,'runtimeVersion':'10.0.13','portableCrcAndHashesValid':True,
        'installerHashValid':True,'packageOwnedUninstallOnly':True,'automaticFileAssociations':False,
        'windowsExecuted':False,'physicalPrinterTested':False,'published':False}
(candidate/'VERIFICATION.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(report,ensure_ascii=False,indent=2))
