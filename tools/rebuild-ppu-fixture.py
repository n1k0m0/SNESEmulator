#!/usr/bin/env python3
"""Reproduce the unchanged Zlib test ROM with pinned external assembler tooling.
No emulator code is imported. Requires Windows, Python3 and Pillow.
"""
from pathlib import Path
import hashlib,io,json,shutil,subprocess,urllib.request,zipfile
root=Path(__file__).resolve().parents[1]
m=json.loads((root/'testdata/m5-ppu-manifest.json').read_text())
e=m['external'];cache=root/'artifacts/m5-external-rebuild';cache.mkdir(parents=True,exist_ok=True)
def unpack(name,url,expected):
 data=urllib.request.urlopen(url).read()
 if hashlib.sha256(data).hexdigest()!=expected:raise SystemExit('Archive hash mismatch: '+name)
 target=cache/name;zipfile.ZipFile(io.BytesIO(data)).extractall(target);return target
bass=unpack('bass','https://github.com/ARM9/bass/releases/download/v18/bass-windows.zip',e['assemblerArchiveSha256'])
sources=unpack('source','https://codeload.github.com/undisbeliever/snes-test-roms/zip/'+e['commit'],e['sourceArchiveSha256'])/('snes-test-roms-'+e['commit'])
fork='9db6088a378061afc7b82f50997a5b9a1d951175'
assembler=unpack('fork','https://codeload.github.com/undisbeliever/bass-untech/zip/'+fork,e['assemblerForkArchiveSha256'])/('bass-untech-'+fork)
shutil.copyfile(assembler/'bass/data/architectures/wdc65816-strict.arch',bass/'architectures/wdc65816-strict.arch')
(sources/'gen/textbuffer').mkdir(parents=True,exist_ok=True)
subprocess.run(['python','tools/png2snes.py','-f','1bpp','-t','gen/textbuffer/font-1bpp-tiles.tiles','-p','gen/textbuffer/font-1bpp-tiles.pal','resources/textbuffer/font-1bpp-tiles.png'],cwd=sources,check=True)
output=cache/'vram-mid-scanline-test.sfc'
subprocess.run([str(bass/'bass.exe'),'-strict','-o',str(output),str(sources/e['sourcePath'])],cwd=root,check=True)
subprocess.run(['python',str(sources/'tools/write-sfc-checksum.py'),'--lorom',str(output)],check=True)
expected=next(f['sha256'] for f in m['files'] if f['path'].endswith('.sfc'))
actual=hashlib.sha256(output.read_bytes()).hexdigest()
if actual!=expected:raise SystemExit('Rebuilt ROM identity mismatch: '+actual)
print('Unchanged upstream ROM reproduced exactly:',actual)
