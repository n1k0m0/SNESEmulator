from pathlib import Path
import urllib.request,hashlib,subprocess,importlib.util,json
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('refs',ROOT/'tools/m8-reference-build.py');b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
cache=ROOT/'artifacts/m8-rust';cache.mkdir(exist_ok=True)
url='https://static.rust-lang.org/rustup/archive/1.28.2/x86_64-unknown-linux-gnu/rustup-init'
data=urllib.request.urlopen(url).read();expected=urllib.request.urlopen(url+'.sha256').read().decode().split()[0];assert hashlib.sha256(data).hexdigest()==expected
(cache/'rustup-init').write_bytes(data)
env=['env','RUSTUP_INIT_SKIP_PATH_CHECK=yes','RUSTUP_HOME='+b.wp(cache/'rustup'),'CARGO_HOME='+b.wp(cache/'cargo')]
b.wsl(['chmod','+x',b.wp(cache/'rustup-init')],cache)
b.wsl([*env,b.wp(cache/'rustup-init'),'-y','--profile','minimal','--default-toolchain','1.85.1','--no-modify-path'],cache)
(cache/'bootstrap.json').write_text(json.dumps({'url':url,'sha256':expected,'toolchain':'1.85.1'},indent=2)+'\n')
