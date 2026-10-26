#!/usr/bin/env python3
"""Rebuild the unmodified licensed external tech demo and retain its notices."""
from pathlib import Path
import importlib.util,subprocess,hashlib,json,shutil
ROOT=Path(__file__).resolve().parents[1];PIN='1c06fee8fbaffbc720ca5fee3dd8c5f2f5838254'
spec=importlib.util.spec_from_file_location('refs',ROOT/'tools/m8-reference-build.py');b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
repo=ROOT/'artifacts/m8-game-source'
if not repo.exists():b.run(['git','clone','https://github.com/undisbeliever/unnamed-snes-engine.git',repo])
b.run(['git','checkout','--detach',PIN],repo);b.run(['git','submodule','update','--init','--recursive'],repo)
assert not subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=repo,text=True).strip()
b.wsl(['make','-j4','WERR=0','CXX=g++ -include cstdint'],repo/'wiz')
b.wsl(['make','-j4','WERR=0','CXX=g++ -include cstdint'],repo/'terrific-audio-driver/wiz')
rust=ROOT/'artifacts/m8-rust/rustup/toolchains/1.85.1-x86_64-unknown-linux-gnu/bin'
if not (rust/'cargo').exists():b.run(['python',ROOT/'tools/m8-rust-bootstrap.py'])
b.wsl(['env','WERR=0','CXX=g++ -include cstdint','RUSTC='+b.wp(rust/'rustc'),b.wp(rust/'cargo'),'build','--release','--locked','-p','tad-compiler'],repo/'terrific-audio-driver')
b.wsl(['make','-j4','WERR=0','CXX=g++ -include cstdint'],repo/'tech-demo')
dest=ROOT/'testdata/m8-game';dest.mkdir(exist_ok=True)
shutil.copyfile(repo/'tech-demo/game.sfc',dest/'unnamed-snes-game.sfc')
for name in ['LICENSE','CREDITS.md','THIRD_PARTY_LICENSES.md','README.md']:shutil.copyfile(repo/name,dest/name)
shutil.copyfile(repo/'terrific-audio-driver/audio-driver/LICENSE',dest/'audio-driver-LICENSE')
m={'source':'https://github.com/undisbeliever/unnamed-snes-engine','commit':PIN,'unmodified':True,'sha256':b.sha(dest/'unnamed-snes-game.sfc'),'license':'MIT code; Zlib audio driver; CC BY-SA 4.0 art/music/SFX; CC0, public domain and Apache-2.0 samples; see retained CREDITS and THIRD_PARTY_LICENSES','submodules':subprocess.check_output(['git','submodule','status','--recursive'],cwd=repo,text=True),'cargoLockSha256':b.sha(repo/'terrific-audio-driver/Cargo.lock'),'build':'python tools/m8-game-build.py','rust':subprocess.check_output(['wsl','-d','Ubuntu','--',b.wp(rust/'rustc'),'--version'],text=True),'toolSha256':{str(p.relative_to(ROOT)):b.sha(p) for p in [rust/'rustc',rust/'cargo',repo/'wiz/bin/wiz',repo/'terrific-audio-driver/wiz/bin/wiz',repo/'terrific-audio-driver/target/release/tad-compiler']},'buildOptions':'Wiz WERR=0, CXX=g++ -include cstdint; unchanged source; Rust1.85.1 cargo --release --locked'}
(dest/'manifest.json').write_text(json.dumps(m,indent=2)+'\n',newline='\n')
print('Built unchanged external game',m['sha256'])
