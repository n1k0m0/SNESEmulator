# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See LICENSE and NOTICE for permissions and attribution.
#
# Generates the project-authored APU boot input without embedding original Nintendo firmware.
#

"""Own SPC boot source and tiny label assembler. Apache-2.0, SNESEmulator contributors.
Protocol implementation with page0 clear; does not reproduce Nintendo IPL timing.
Rebuild: python tools/own-ipl.py --write (otherwise checks checked-in bytes).
"""
from pathlib import Path
import re,sys,hashlib
out=bytearray();labels={};fixups=[]
def label(s):labels[s]=len(out)
def emit(*b):out.extend(b)
def branch(op,target):
 emit(op,0);fixups.append((len(out)-1,target))
# Reentry establishes stack and clears page0 even after an uploaded program dirtied it.
emit(0x20,0xcd,0xef,0xbd,0xe8,0x00) # CLRP / MOV X,#EF / MOV SP,X / MOV A,#0
label('clear');emit(0xc6,0x1d);branch(0xd0,'clear') # MOV (X),A / DEC X / BNE
emit(0x8f,0xaa,0xf4) # MOV $F4,#$AA
emit(0x8f,0xbb,0xf5) # MOV $F5,#$BB
label('ready');emit(0x78,0xcc,0xf4);branch(0xd0,'ready') # CMP $F4,#CC / BNE
label('command')
emit(0xba,0xf6,0xda,0x00) # MOVW YA,$F6 / MOVW $00,YA: destination
emit(0xba,0xf4,0xc4,0xf4) # MOVW YA,$F4 / MOV $F4,A: command acknowledgement
emit(0xdd,0x5d) # MOV A,Y / MOV X,A: mode
branch(0xf0,'execute')
label('first');emit(0xeb,0xf4);branch(0xd0,'first') # First byte index0
label('byte');emit(0x7e,0xf4);branch(0xd0,'changed') # CMP Y,$F4
emit(0xe4,0xf5,0xd7,0x00,0xcb,0xf4,0xfc) # read data; store before echo; INC Y
branch(0xd0,'byte');emit(0xab,0x01);branch(0x2f,'byte') # carry to high address
label('changed');branch(0x30,'command');branch(0x2f,'byte') # signed ring-counter difference: FF->00 is an ordinary next byte
label('execute');emit(0x1f,0x00,0x00) # JMP [$0000+X], X=0
for at,target in fixups:
 delta=labels[target]-(at+1);assert -128<=delta<=127;out[at]=delta&255
assert len(out)<=62
out.extend(bytes(62-len(out)));emit(0xc0,0xff)
p=Path(__file__).resolve().parents[1]/'SNESEmulator.Core/OwnIpl.cs'
hex=out.hex().upper();source=p.read_text()
if '--write' in sys.argv:p.write_text(re.sub(r'Hex="[0-9A-F]*"',f'Hex="{hex}"',source))
else:assert f'Hex="{hex}"' in source,'Run --write after reviewing own boot source'
print('Own IPL',len(out),'bytes SHA256',hashlib.sha256(out).hexdigest())
