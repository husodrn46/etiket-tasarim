#!/usr/bin/env python3
"""Build a local macOS application bundle (LabelHarbor.app). Never uploads or publishes.

The bundle is self-contained (the .NET runtime is inside) and signed ad hoc, which is enough to run it on the
Mac that built it. It is NOT notarized: on another Mac, Gatekeeper asks for confirmation on first launch.
Run on macOS; it uses sips, iconutil, codesign and ditto from the system.
"""
import argparse, hashlib, json, platform, plistlib, re, shutil, subprocess, sys
from pathlib import Path
from collect_notices import collect

ROOT = Path(__file__).resolve().parents[2]
# Same merge order as the Windows candidate: the desktop app's dependencies win.
PROJECTS = ['AkelEtiket.Mcp', 'AkelEtiket.Worker', 'AkelEtiket.App.Mac']
BUNDLE_ID = 'com.husodrn46.labelharbor'
LABEL_TYPE = BUNDLE_ID + '.akelabel'
EXECUTABLES = ['LabelHarbor', 'AkelEtiket.Mcp', 'AkelEtiket.Worker']


def run(command, log=None, **kwargs):
    if log is None:
        return subprocess.run([str(c) for c in command], check=True, **kwargs)
    with log.open('w') as handle:
        return subprocess.run([str(c) for c in command], check=True, stdout=handle, stderr=subprocess.STDOUT, **kwargs)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def default_version():
    props = (ROOT / 'Directory.Build.props').read_text(encoding='utf-8')
    return re.search(r'<Version>([^<]+)</Version>', props).group(1)


def make_icon(source_png, target_icns, work):
    """icns from the 256 px app image (sizes above 256 px are not produced; the source is not upscaled)."""
    iconset = work / 'LabelHarbor.iconset'
    iconset.mkdir(parents=True)
    for size, name in [(16, 'icon_16x16'), (32, 'icon_16x16@2x'), (32, 'icon_32x32'), (64, 'icon_32x32@2x'),
                       (128, 'icon_128x128'), (256, 'icon_128x128@2x'), (256, 'icon_256x256')]:
        run(['sips', '-z', size, size, source_png, '--out', iconset / (name + '.png')], stdout=subprocess.DEVNULL)
    run(['iconutil', '-c', 'icns', iconset, '-o', target_icns])


def info_plist(version):
    numeric = version.split('-')[0]
    return {
        'CFBundleName': 'LabelHarbor',
        'CFBundleDisplayName': 'LabelHarbor',
        'CFBundleIdentifier': BUNDLE_ID,
        'CFBundleExecutable': 'LabelHarbor',
        'CFBundleIconFile': 'LabelHarbor',
        'CFBundlePackageType': 'APPL',
        'CFBundleInfoDictionaryVersion': '6.0',
        'CFBundleShortVersionString': numeric,
        'CFBundleVersion': numeric,
        'CFBundleDevelopmentRegion': 'tr',
        'CFBundleLocalizations': ['tr', 'en'],
        'LSApplicationCategoryType': 'public.app-category.productivity',
        'LSMinimumSystemVersion': '14.0',
        'NSHighResolutionCapable': True,
        'NSHumanReadableCopyright': 'MIT License',
        'CFBundleDocumentTypes': [{
            'CFBundleTypeName': 'LabelHarbor label',
            'CFBundleTypeRole': 'Editor',
            'LSHandlerRank': 'Owner',
            'LSItemContentTypes': [LABEL_TYPE],
        }],
        'UTExportedTypeDeclarations': [{
            'UTTypeIdentifier': LABEL_TYPE,
            'UTTypeDescription': 'LabelHarbor label',
            'UTTypeConformsTo': ['public.data'],
            'UTTypeTagSpecification': {'public.filename-extension': ['akelabel']},
        }],
    }


def main():
    if sys.platform != 'darwin':
        raise SystemExit('This script builds a macOS bundle and must run on macOS.')
    host = 'arm64' if platform.machine() == 'arm64' else 'x64'
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--dotnet', type=Path, default=Path(shutil.which('dotnet') or Path.home() / '.dotnet' / 'dotnet'))
    parser.add_argument('--cache', type=Path, default=Path.home() / '.nuget' / 'packages', help='NuGet package folder (for license notices).')
    parser.add_argument('--output', type=Path, required=True, help='New, empty output folder.')
    parser.add_argument('--arch', choices=['arm64', 'x64'], default=host)
    parser.add_argument('--version', default=default_version())
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?", args.version):
        raise SystemExit('Invalid version.')
    license_text = (ROOT / 'LICENSE').read_text()
    if not license_text.startswith('MIT License') or 'Permission is hereby granted' not in license_text:
        raise SystemExit('The selected source license is missing or invalid.')
    output = args.output.resolve()
    if output.exists():
        raise SystemExit('Output already exists; refusing to overwrite it.')
    output.mkdir(parents=True)

    runtime = 'osx-' + args.arch
    stages = output / 'stages'
    for project in PROJECTS:
        run([args.dotnet.resolve(), 'publish', ROOT / 'src' / project / (project + '.csproj'),
             '-c', 'Release', '-r', runtime, '--self-contained', 'true',
             '-p:PublishReadyToRun=false', '-p:UseSharedCompilation=false', '-p:DebugType=none', '-p:DebugSymbols=false',
             '-p:NuGetAudit=false', '-m:1', '-p:Version=' + args.version, '-o', stages / project],
            log=output / (project + '.publish.log'), cwd=ROOT)

    app = output / 'LabelHarbor.app'
    contents = app / 'Contents'
    macos = contents / 'MacOS'
    resources = contents / 'Resources'
    macos.mkdir(parents=True)
    resources.mkdir()
    for project in PROJECTS:
        shutil.copytree(stages / project, macos, dirs_exist_ok=True)
    desktop = stages / 'AkelEtiket.App.Mac'
    for file in desktop.rglob('*'):
        if file.is_file() and digest(file) != digest(macos / file.relative_to(desktop)):
            raise RuntimeError('Dependency merge mismatch: ' + str(file))
    for file in macos.rglob('*.pdb'):
        file.unlink()

    # Data lives in Contents/Resources: codesign treats folders inside Contents/MacOS as code, and a folder name
    # with dots (licenses/Package.1.2.3) is rejected as a malformed nested bundle. The app looks for its documents
    # and samples next to the executable, as on Windows, so those two folders are linked from there.
    shutil.copy2(ROOT / 'LICENSE', resources / 'LICENSE')
    docs = resources / 'Belgeler'
    docs.mkdir()
    for name in ['README.md', 'README.tr.md', 'NOTICE']:
        shutil.copy2(ROOT / name, docs / name)
    for name in ['GENEL-URUN.md', 'LISANSLAR.md', 'USER-GUIDE.md', 'INTERNATIONALIZATION.md', 'WORKFLOWS.md', 'WORKFLOWS.tr.md']:
        shutil.copy2(ROOT / 'docs' / name, docs / name)
    samples = resources / 'Örnekler'
    samples.mkdir()
    for file in (ROOT / 'samples').iterdir():
        if file.is_file() and file.suffix in ['.csv', '.xlsx', '.akelabel', '.png']:
            shutil.copy2(file, samples / file.name)
    shutil.copytree(ROOT / 'samples' / 'packs', samples / 'packs', dirs_exist_ok=True)
    for name in ['Belgeler', 'Örnekler']:
        (macos / name).symlink_to(Path('..') / 'Resources' / name, target_is_directory=True)
    notices = collect(ROOT, args.cache.resolve(), resources / 'licenses', projects=PROJECTS)
    if notices['missingLicenseFiles']:
        raise RuntimeError('Missing upstream license files; bundle not built: ' + ', '.join(notices['missingLicenseFiles']))

    make_icon(ROOT / 'src' / 'AkelEtiket.App.Mac' / 'Assets' / 'AkelEtiket-256.png', resources / 'LabelHarbor.icns', output / 'icon')
    with (contents / 'Info.plist').open('wb') as handle:
        plistlib.dump(info_plist(args.version), handle)
    (contents / 'PkgInfo').write_text('APPL????')
    for name in EXECUTABLES:
        if not (macos / name).is_file():
            raise RuntimeError('Missing executable: ' + name)

    # Ad hoc signature: runs on this Mac. Distribution to other Macs needs a Developer ID signature and notarization.
    run(['codesign', '--force', '--deep', '--sign', '-', app], log=output / 'codesign.log')
    run(['codesign', '--verify', '--deep', '--strict', app], log=output / 'codesign-verify.log')

    archive = output / f'LabelHarbor-{args.version}-macos-{args.arch}.zip'
    run(['ditto', '-c', '-k', '--keepParent', app, archive])
    manifest = {
        'version': args.version, 'runtime': runtime, 'bundleIdentifier': BUNDLE_ID,
        'signature': 'ad hoc (not notarized)', 'archive': archive.name, 'sha256': digest(archive),
        'files': sum(1 for f in app.rglob('*') if f.is_file()),
        'bytes': sum(f.stat().st_size for f in app.rglob('*') if f.is_file()),
        'published': False,
    }
    (output / 'PACKAGE-MANIFEST.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    shutil.rmtree(stages)
    shutil.rmtree(output / 'icon')
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
