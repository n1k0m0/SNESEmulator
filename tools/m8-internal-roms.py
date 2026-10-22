"""Original Apache-2.0 diagnostic inputs; never external-title evidence."""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def build():
 dest=ROOT/'artifacts/m8-internal';dest.mkdir(parents=True,exist_ok=True)
 rom=bytearray([0xea]*32768)
 # Explicitly disable both screens and color math; power-on PPU register
 # entropy is model-specific and is not an oracle for this latch diagnostic.
 code=bytearray.fromhex('78 a9 80 8d 00 21 9c 2c 21 9c 2d 21 9c 30 21 9c 31 21 a9 00 8d 21 21 a9 1f 8d 22 21 a9 00 8d 22 21 a9 0f 8d 00 21')
 # Poll the actual PPU vertical counter, reset read flips with STAT78.
 # SETINI changes at V64, after display capture but before counter V128.
 code+=bytes.fromhex('ad 3f 21 ad 37 21 ad 3d 21 c9 40 d0 f3 a9 01 8d 33 21 80 fe')
 rom[:len(code)]=code;rom[0x7fc0:0x8000]=bytes(64)
 rom[0x7fc0:0x7fd5]=b'M8 MIDFIELD SETINI    '
 rom[0x7fd5:0x7fdc]=bytes([0x20,0,5,0,0,0x33,0]);rom[0x7ffc:0x7ffe]=bytes([0,0x80])
 rom[0x7fdc:0x7fde]=bytes([255,255]);checksum=sum(rom)&65535
 rom[0x7fdc:0x7fe0]=((~checksum)&65535).to_bytes(2,'little')+checksum.to_bytes(2,'little')
 (dest/'midfield-setini.sfc').write_bytes(rom)
if __name__=='__main__':build()
