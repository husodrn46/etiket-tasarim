#!/usr/bin/env python3
"""Build a local Windows x64 candidate. Never uploads or publishes."""
import argparse, hashlib, json, os, shutil, struct, subprocess, zipfile
from pathlib import Path
from collect_notices import collect

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = ['AkelEtiket.Mcp', 'AkelEtiket.Worker', 'AkelEtiket.App']


def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--makensis', type=Path, required=True)
    parser.add_argument('--cache', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--version', default='2.0.0')
    parser.add_argument('--reuse-stage', type=Path, help='Explicitly reuse already published stages for local checks.')
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists(): raise SystemExit('Output already exists; refusing to overwrite it.')
    output.mkdir(parents=True)
    stages = args.reuse_stage.resolve() if args.reuse_stage else output / 'stages'
    for project in PROJECTS:
        if args.reuse_stage: continue
        command = [str(args.dotnet.resolve()), 'publish', str(ROOT/'src'/project/(project+'.csproj')),
                   '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
                   '-p:EnableWindowsTargeting=true', '-p:PublishReadyToRun=false', '-p:UseSharedCompilation=false',
                   '-p:DebugType=none', '-p:DebugSymbols=false', '-p:NuGetAudit=false', '-m:1',
                   '-p:Version='+args.version, '-o', str(stages/project)]
        with (output/(project+'.publish.log')).open('w') as log:
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    payload = output/'payload'
    payload.mkdir()
    # Same merge order as build.ps1: desktop dependencies win.
    for project in PROJECTS: shutil.copytree(stages/project, payload, dirs_exist_ok=True)
    for file in (stages/'AkelEtiket.App').rglob('*'):
        if file.is_file() and digest(file) != digest(payload/file.relative_to(stages/'AkelEtiket.App')):
            raise RuntimeError('Dependency merge mismatch: '+str(file))
    docs = payload/'Belgeler'; docs.mkdir(exist_ok=True)
    for name in ['README.md', 'README.tr.md', 'NOTICE']: shutil.copy2(ROOT/name, docs/name)
    for name in ['GENEL-URUN.md','LISANSLAR.md','USER-GUIDE.md','INTERNATIONALIZATION.md','WORKFLOWS.md','WORKFLOWS.tr.md']: shutil.copy2(ROOT/'docs'/name, docs/name)
    samples = payload/'Örnekler'; samples.mkdir(exist_ok=True)
    for file in (ROOT/'samples').iterdir():
        if file.is_file() and file.suffix in ['.csv','.xlsx','.akelabel','.png']: shutil.copy2(file, samples/file.name)
    shutil.copytree(ROOT/"samples"/"packs", samples/"packs", dirs_exist_ok=True)
    notices = collect(ROOT, args.cache.resolve(), payload/'licenses')
    if notices['missingLicenseFiles']: raise RuntimeError('Missing upstream license files; installer not built.')
    for file in payload.rglob('*.pdb'): file.unlink()
    executables = ['Etiket Tasarim.exe', 'AkelEtiket.Mcp.exe', 'AkelEtiket.Worker.exe']
    for name in executables:
        data = (payload/name).read_bytes()
        pe = struct.unpack_from('<I',data,0x3c)[0]
        if data[:2] != b'MZ' or data[pe:pe+4] != b'PE\0\0' or struct.unpack_from('<H',data,pe+4)[0] != 0x8664:
            raise RuntimeError('Not a Windows x64 apphost: '+name)
    for required in ['coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll','libSkiaSharp.dll','en/AkelEtiket.Core.resources.dll']:
        if not (payload/required).exists(): raise RuntimeError('Missing runtime file: '+required)
    files = {str(p.relative_to(payload)).replace(os.sep,'/'): digest(p) for p in payload.rglob('*') if p.is_file()}
    (output/'PAYLOAD-MANIFEST.json').write_text(json.dumps(files,ensure_ascii=False,indent=2)+'\n')
    uninstall = output/'uninstall-files.nsh'
    lines = ['Delete "$INSTDIR\\'+name.replace('/','\\')+'"' for name in files]
    dirs = sorted({str(p.relative_to(payload)).replace(os.sep,'\\') for p in payload.rglob('*') if p.is_dir()},key=len,reverse=True)
    lines += ['RMDir "$INSTDIR\\'+name+'"' for name in dirs]
    uninstall.write_text('\n'.join(lines)+'\n')
    installer = output/('Etiket-Tasarim-Kurulum-'+args.version+'-aday.exe')
    command = [str(args.makensis.resolve()), '-DPAYLOAD='+str(payload), '-DOUTPUT='+str(installer),
               '-DAPP_VERSION='+args.version, '-DUNINSTALL_LIST='+str(uninstall), str(ROOT/'installer/EtiketTasarim.nsi')]
    with (output/'installer-build.log').open('w') as log:
        subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,check=True)
    portable = output/('Etiket-Tasarim-'+args.version+'-win-x64-aday.zip')
    with zipfile.ZipFile(portable,'w',zipfile.ZIP_DEFLATED) as archive:
        for name in files: archive.write(payload/name,'Etiket-Tasarim/'+name)
    result={'version':args.version,'provisionalProductName':True,'sourceLicenseChosen':False,
            'files':len(files),'payloadBytes':sum((payload/f).stat().st_size for f in files),
            'installer':{'file':installer.name,'sha256':digest(installer),'bytes':installer.stat().st_size},
            'portable':{'file':portable.name,'sha256':digest(portable),'bytes':portable.stat().st_size},
            'windowsInstallationTested':False,'physicalPrinterTested':False,'published':False}
    (output/'CANDIDATE-RESULT.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(result,ensure_ascii=False,indent=2))


if __name__=='__main__': main()
