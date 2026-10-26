#!/usr/bin/env python3
"""Rebuild unchanged Zlib upstream programs with the M5 pinned bass toolchain."""
from pathlib import Path
import subprocess,sys,json,hashlib,shutil,urllib.request,zipfile,io
ROOT=Path(__file__).resolve().parents[1]
PIN='ac6ef8006809c0ff5dabc8ff137a4623967697fe'
SRC=ROOT/f'artifacts/m5-external-rebuild/source/snes-test-roms-{PIN}'
BASS=ROOT/'artifacts/m5-external-rebuild/bass/bass.exe'
TITLES=['examples/hdma-repeat-entry','examples/hdma-indirect-repeating-pattern','examples/hdma-160scanline-hdma-entry','examples/hdma-to-cgram','examples/textbuffer-hello-world','examples/vram-writes-without-dma','effects/window-mask-logic','effects/repeating_hdma_pattern','effects/vmain-horizontal-scrolling','effects/vmain-vertical-scrolling','hardware-tests/vram-mid-scanline-test','hardware-tests/inidisp_brightness_0','hardware-tests/inidisp_brightness_delay','hardware-tests/inidisp_enable_display_mid_frame','hardware-tests/wrmpyb-in-flight','vmain-address-remapping/vmain-2bpp-with-remapping','vmain-address-remapping/vmain-4bpp-with-remapping','vmain-address-remapping/vmain-8bpp-with-remapping','vmain-address-remapping/vmain-mode7-image-with-8bit-remapping','effects/inidisp_fadein_fadeout']
DEST=ROOT/'testdata/m8-roms'
def run(c):subprocess.run(list(map(str,c)),cwd=SRC,check=True,timeout=60)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 if not SRC.exists() or not BASS.exists():subprocess.run([sys.executable,str(ROOT/'tools/rebuild-ppu-fixture.py')],check=True)
 pins=json.loads((ROOT/'testdata/m5-ppu-manifest.json').read_text())['external']
 cache=ROOT/'artifacts/m8-build-inputs';cache.mkdir(exist_ok=True);archive=cache/'snes-test-roms.zip'
 if not archive.exists():archive.write_bytes(urllib.request.urlopen('https://codeload.github.com/undisbeliever/snes-test-roms/zip/'+PIN).read())
 assert sha(archive)==pins['sourceArchiveSha256'],'Source archive pin'
 with zipfile.ZipFile(archive) as z:
  for entry in z.infolist():
   if entry.is_dir():continue
   relative=Path(*Path(entry.filename).parts[1:])
   assert (SRC/relative).read_bytes()==z.read(entry),'Modified upstream source: '+str(relative)
 # Generate every unchanged resource according to upstream GNUmakefile.
 for png in (SRC/'resources').rglob('*.png'):
  rel=png.relative_to(SRC/'resources');out=SRC/'gen'/rel.with_suffix('');out.parent.mkdir(parents=True,exist_ok=True)
  if '-tiles' in png.stem:
   fmt=png.stem.split('-')[-2];run([sys.executable,'tools/png2snes.py','-f',fmt,'-t',str(out)+'.tiles','-p',str(out)+'.pal',png])
  elif not png.stem.endswith('-palette'):
   fmt='2bpp' if '2bpp' in png.stem or rel.parts[0] in ['test-patterns'] or png.stem=='bg3' else '4bpp'
   run([sys.executable,'tools/image2snes.py','-f',fmt,'-t',str(out)+'.'+fmt,'-m',str(out)+'.tilemap','-p',str(out)+'.palette',png,png.with_stem(png.stem+'-palette')])
 for src in (SRC/'resources').rglob('*.asm'):
  out=SRC/'gen'/src.relative_to(SRC/'resources').with_suffix('.bin');out.parent.mkdir(parents=True,exist_ok=True);run([BASS,'-strict','-o',out,src])
 DEST.mkdir(exist_ok=True);entries=[]
 for title in TITLES:
  src=SRC/'src'/(title+'.asm');out=DEST/(title.split('/')[-1]+'.sfc')
  run([BASS,'-strict','-o',out,src]);run([sys.executable,'tools/write-sfc-checksum.py','--lorom',out])
  entries.append({'id':out.stem,'path':out.relative_to(ROOT).as_posix(),'sourcePath':'src/'+title+'.asm','sourceSha256':sha(src),'sha256':sha(out),'license':'Zlib','region':'Japan/NTSC header; explicit PAL comparison separately','mapper':'LoROM','chips':[],'internal':False})
 shutil.copyfile(SRC/'LICENSE',DEST/'LICENSE.txt')
 m={'source':'https://github.com/undisbeliever/snes-test-roms','commit':PIN,'unmodified':True,'build':'python tools/m8-rom-build.py','assembler':'M5 pinned ARM9 bass18 plus unchanged ISC strict65816 architecture','titles':entries,'sourceArchiveSha256':pins['sourceArchiveSha256'],'assemblerSha256':sha(BASS),'strictArchitectureSha256':sha(BASS.parent/'architectures/wdc65816-strict.arch'),'sourceArchiveCheckedFileByFile':True}
 (DEST/'manifest.json').write_text(json.dumps(m,indent=2)+'\n',newline='\n')
 print('Rebuilt',len(entries),'unchanged external programs')
if __name__=='__main__':main()
