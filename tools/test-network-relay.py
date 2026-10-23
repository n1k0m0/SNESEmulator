#!/usr/bin/env python3
"""Portable relay parser/auth budgets, with local in-memory fixtures only."""
import asyncio
import datetime
import importlib.util
import json
import logging
import os
from pathlib import Path
import struct
import subprocess
import sys
import time
from unittest.mock import patch
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('m9d_relay', ROOT/'relay_server/snes_relay_server.py')
relay = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = relay
spec.loader.exec_module(relay)
logging.disable(logging.CRITICAL)
results = []


def test(name, body):
    start = time.monotonic()
    try:
        body()
        result = {'Name': name, 'Status': 'pass', 'Detail': None}
    except Exception as error:
        import traceback
        result = {'Name': name, 'Status': 'fail', 'Detail': traceback.format_exc()}
    result.update(Seconds=time.monotonic()-start, Evidence=None)
    results.append(result)
    print(result['Status'].upper(), name, result['Detail'] or '')


def rejects(body):
    try:
        body()
    except (ValueError, UnicodeDecodeError):
        return
    raise AssertionError('malformed payload accepted')


def registration():
    valid = struct.pack('<iB', 1, 1)+relay.write_string('test')+relay.write_string('p')
    assert relay.parse_register(valid) == (1, 1, 'test', 'p')
    for invalid in (valid+b'\0', valid[:-1], valid[:4]+b'\xff'+valid[5:], valid[:9]+b'\xff'+valid[10:], struct.pack('<iB', 1, 1)+relay.write_string('x'*129)):
        rejects(lambda: relay.parse_register(invalid))


def auth_budget():
    async def run():
        relay.relay_auth_failures.clear()
        with patch.object(relay.time, 'monotonic', return_value=100):
            for i in range(relay.AUTH_ENTRY_BUDGET):
                assert not await relay.record_bad_relay_password(f'local-fixture-{i}', 'fixture', 1, 'id')
            assert len(relay.relay_auth_failures) == relay.AUTH_ENTRY_BUDGET
            assert await relay.record_bad_relay_password('unknown-fixture', 'fixture', 1, 'id')
            assert len(relay.relay_auth_failures) == relay.AUTH_ENTRY_BUDGET
            assert not await relay.record_bad_relay_password('local-fixture-0', 'fixture', 1, 'id')
            assert await relay.record_bad_relay_password('local-fixture-0', 'fixture', 1, 'id')
            assert await relay.get_relay_auth_block_seconds('local-fixture-0') > 0
        with patch.object(relay.time, 'monotonic', return_value=100+relay.AUTH_ENTRY_TTL_SECONDS+1):
            assert await relay.get_relay_auth_block_seconds('unknown-fixture') == 0
            assert len(relay.relay_auth_failures) == 0
    asyncio.run(run())


def frame_budget():
    async def run():
        payload=b'opaque local bytes'
        packet=relay.HEADER.pack(relay.MAGIC, 1, relay.CHANNEL_DATA, 0, 1, len(payload))+payload
        reader=asyncio.StreamReader()
        task=asyncio.create_task(relay.read_frame(reader))
        for part in packet:
            reader.feed_data(bytes([part]))
            await asyncio.sleep(0)
        decoded=await task
        assert decoded.payload == payload and decoded.channel_id == 1
        for length, flags, kind in [(relay.MAX_PAYLOAD+1,0,5),(0,1,5),(0,0,255)]:
            reader=asyncio.StreamReader();reader.feed_data(relay.HEADER.pack(relay.MAGIC,1,kind,flags,1,length))
            try:
                await relay.read_frame(reader)
            except ValueError:
                pass
            else:
                raise AssertionError('invalid relay header accepted')
        assert relay.MAX_CONNECTIONS == 64 and relay.MAX_SESSIONS == 16 and relay.MAX_CLIENTS_PER_SESSION == 16
    asyncio.run(run())


test('M9d relay strict role UTF8 text length and trailing payload', registration)
test('M9d relay auth tracker bounded fail-closed overflow and global TTL cleanup', auth_budget)
test('M9d relay fragmented frames flags and before-allocation payload budgets', frame_budget)

def git(*args):
    return subprocess.check_output(['git',*args],cwd=ROOT,text=True).strip()

exit_code=int(any(row['Status'] != 'pass' for row in results))
report={'Suite':'network-relay-contract','Schema':'snes-lab-results-v1','Commit':git('rev-parse','HEAD'),
        'WorkingTreeStatus':git('status','--porcelain'),'StartedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'Metadata':{'limits':'In-memory parser/auth fixtures; actual TLS/Relay sockets are separate Windows tests'},'Tests':results,'ExitCode':exit_code}
prefix=Path(os.environ.get('SNES_LAB_CHILD_REPORT',str(ROOT/'artifacts/m9d-relay-contract')))
prefix.parent.mkdir(parents=True,exist_ok=True)
prefix.with_suffix('.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
suite=ET.Element('testsuite',name=report['Suite'],tests=str(len(results)),failures=str(exit_code),errors='0',skipped='0')
for row in results:
    case=ET.SubElement(suite,'testcase',name=row['Name'],classname=report['Suite'],time=str(row['Seconds']))
    if row['Status'] != 'pass':
        ET.SubElement(case,'failure',type='fail').text=row['Detail']
ET.ElementTree(suite).write(str(prefix)+'.junit.xml',encoding='utf-8',xml_declaration=True)
sys.exit(exit_code)
