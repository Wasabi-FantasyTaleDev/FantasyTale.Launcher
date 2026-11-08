"""Verify exit patch bytes and execute native keyboard branches with modeled inputs.

The clients JSON maps build IDs to unpacked PE files. Client functions outside the
keyboard consumer are replaced by explicit test results. Reaching dialog setup
does not establish rendering, real input delivery, or successful server departure.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import tomllib

import capstone
import pefile
import unicorn
from unicorn import x86_const


ROOT = Path(__file__).resolve().parents[1]
STAGE = 0x10000000
CONTEXT = 0x10004000
ROOM = 0x10008000
CONFIG = 0x10018000
VTABLE = 0x10019000
PROPERTY_STUB = 0x1001A000
UI = 0x1001B000
NODE = 0x1001C000
STACK = 0x1002F000


def require(condition: bool, message: str) -> None:
    """Keep verification failures active even when Python optimization is enabled."""
    if not condition:
        raise ValueError(message)


class KeyboardProbe:
    """Execute a client's actual keyboard instructions against a bounded fake scene."""

    def __init__(self, pe: pefile.PE, build: str, row: dict, patches: list[dict]):
        self.pe, self.build, self.row, self.patches = pe, build, row, patches
        self.base = pe.OPTIONAL_HEADER.ImageBase
        self.memory = pe.get_memory_mapped_image()
        boss = next(p for p in patches if p['id'] == build + '-exit-boss-presence')
        self.dialog_setup = self.base + boss['locate']['rva'] + len(bytes.fromhex(boss['expect']))
        self.decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.decoder.detail = True

    def run(self, *, patched=True, key=27, active=True, modal=False,
            ready=True, context=True, tutorial=False, boss=False) -> str:
        row = self.row
        machine = unicorn.Uc(unicorn.UC_ARCH_X86, unicorn.UC_MODE_32)
        machine.mem_map(0, 0x1000)
        machine.mem_map(self.base, (len(self.memory) + 4095) & ~4095)
        machine.mem_write(self.base, self.memory)
        machine.mem_map(STAGE, 0x30000)

        def write(address, value):
            machine.mem_write(address, struct.pack('<I', value))

        def read(address):
            return struct.unpack('<I', machine.mem_read(address, 4))[0]

        # Executable sections in several unpacked clients are also writable.
        for section in self.pe.sections:
            if section.IMAGE_SCN_MEM_WRITE and not section.IMAGE_SCN_MEM_EXECUTE:
                machine.mem_write(self.base + section.VirtualAddress, b'\0' * section.Misc_VirtualSize)
        if patched:
            for patch in self.patches:
                machine.mem_write(self.base + patch['locate']['rva'], bytes.fromhex(patch['write']))
        write(row['context_global'], CONTEXT if context else 0)
        write(row['room_global'], ROOM)
        write(STAGE + row['dialog_flag_offset'], int(modal))
        room = ROOM
        for _ in range(row['room_indirections']):
            write(room, room + 0x1000)
            room += 0x1000
        mode = 103 if tutorial else row['ordinary_mode']
        write(room + row['mode_offset'], NODE if row['mode_indirect'] else mode)
        if row['mode_indirect']:
            write(NODE, mode)
        if row['key_offset']:
            write(STAGE + row['key_offset'], 27)
        if row['loading_offset']:
            write(STAGE + row['loading_offset'], row['ready_value'] if ready else 0)
        if row['property_global']:
            write(row['property_global'], CONFIG)
            write(CONFIG, VTABLE)
            write(VTABLE + 8, PROPERTY_STUB)
        if row.get('ui_global'):
            write(row['ui_global'], UI)
        if row.get('key_map_global'):
            write(row['key_map_global'], UI + 0x100)
            machine.mem_write(NODE + 0x100 + 13, b'\x01')
        for offset, value in enumerate((0x1001FFFF, key, 0, 0)):
            write(STACK + offset * 4, value)
        machine.reg_write(x86_const.UC_X86_REG_ESP, STACK)
        machine.reg_write(x86_const.UC_X86_REG_ECX, STAGE)
        trace, outcome = [], []

        def observe(current, address, size, _):
            if address == self.dialog_setup:
                outcome.append('dialog_setup')
            elif address == row['tutorial_entry']:
                outcome.append('tutorial')
            elif address < row['entry'] or address > self.dialog_setup:
                outcome.append('rejected')
            if outcome:
                current.emu_stop()
                return
            instruction = next(self.decoder.disasm(bytes(current.mem_read(address, size)), address))
            trace.append(f'{address:08X} {instruction.mnemonic} {instruction.op_str}')
            if len(trace) > 20:
                del trace[0]
            if instruction.mnemonic == 'ret':
                outcome.append('rejected')
                current.emu_stop()
                return
            if instruction.mnemonic != 'call':
                return
            operand = instruction.operands[0]
            if operand.type == capstone.x86.X86_OP_IMM:
                target = operand.imm
            elif operand.type == capstone.x86.X86_OP_REG:
                name = 'UC_X86_REG_' + instruction.reg_name(operand.reg).upper()
                target = current.reg_read(getattr(x86_const, name))
            else:
                raise ValueError('Unexpected indirect call before dialog setup')
            allowed = {*row['zero_helpers'], row['match_helper'], row['input_toggle'], PROPERTY_STUB,
                       row.get('key_map_query'), row.get('tutorial_helper')}
            require(target in allowed, f'Unmodeled client call at {address:08X} to {target:08X}')
            value = int(target == row['match_helper'] and active)
            pop = 4 if target in (row['input_toggle'], PROPERTY_STUB) else 0
            if target == row.get('key_map_query'):
                output = read(current.reg_read(x86_const.UC_X86_REG_ESP))
                write(output + 8, NODE + 0x100)
                pop = 8
            if tutorial and target == row.get('tutorial_helper'):
                value = 1
            # The boss consumer is the direct call preceding the final patched JNE.
            boss_patch = next(p for p in self.patches if p['id'].endswith('-exit-boss-presence'))
            if address + size + 2 == self.base + boss_patch['locate']['rva']:
                value = int(boss)
            current.reg_write(x86_const.UC_X86_REG_EAX, value)
            current.reg_write(x86_const.UC_X86_REG_ESP, current.reg_read(x86_const.UC_X86_REG_ESP) + pop)
            current.reg_write(x86_const.UC_X86_REG_EIP, address + size)

        machine.hook_add(unicorn.UC_HOOK_CODE, observe)
        try:
            machine.emu_start(row['entry'], 0, count=1000)
        except Exception as error:
            raise ValueError(f'{self.build}: {error}\n' + '\n'.join(trace)) from error
        require(bool(outcome), f'{self.build}: keyboard execution exceeded 1000 instructions')
        return outcome[0]

    def verify(self) -> dict[str, str]:
        row = self.row
        cases = {
            'patched_escape': ({}, 'dialog_setup'),
            'unmodified_escape': ({'patched': False}, 'dialog_setup' if row['original_all_modes'] else 'rejected'),
            'other_key': ({'key': 65}, 'rejected'),
            'not_in_match': ({'active': False}, 'rejected'),
            'existing_dialog': ({'modal': True}, 'rejected'),
            'no_context': ({'context': False}, 'rejected'),
            'tutorial': ({'tutorial': True}, 'tutorial'),
            'boss_present': ({'boss': True}, 'dialog_setup'),
        }
        if row['loading_offset']:
            cases['loading'] = ({'ready': False}, 'rejected')
        result = {}
        for name, (arguments, expected) in cases.items():
            actual = self.run(**arguments)
            require(actual == expected, f'{self.build} {name}: expected {expected}, observed {actual}')
            result[name] = actual
        return result


def verify(clients: dict[str, str], package: Path | None = None) -> dict:
    source = ROOT / 'native/mid-match-exit'
    if package:
        manifest = tomllib.loads((package / 'plugin.toml').read_text(encoding='utf-8'))
        profiles = tomllib.loads((package / 'exit.toml').read_text(encoding='utf-8'))['clients']
    else:
        manifest = tomllib.loads((source / 'plugin.preview.toml').read_text(encoding='utf-8') + '\n'
                               + (source / 'native-patches.toml').read_text(encoding='utf-8'))
        profiles = tomllib.loads((source / 'exit.toml').read_text(encoding='utf-8'))['clients']
    routes = tomllib.loads((ROOT / 'native/tests/data/mid-match-exit-routes.toml').read_text(encoding='utf-8'))['clients']
    builds = set(manifest['compatibility']['builds'])
    require(set(clients) == builds == set(profiles), 'Client mapping must cover every published build exactly once')
    require(set(routes) == {b for b in builds if profiles[b]['route'] == 'native'},
            'Every native route must have a keyboard evidence fixture')
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    receipt = {'version': manifest['plugin']['version'], 'builds': {}, 'patches': 0, 'keyboard_cases': 0}
    for build in sorted(builds):
        path = Path(clients[build])
        pe = pefile.PE(str(path), fast_load=True)
        require(pe.FILE_HEADER.Machine == 0x14C, build + ': expected an x86 image')
        base = pe.OPTIONAL_HEADER.ImageBase
        rows = [p for p in manifest['patches'] if build in p['builds']]
        require(profiles[build]['route'] != 'companion' or not rows,
                build + ': passive companion route must not declare memory patches')
        checked = []
        for patch in rows:
            before = bytes.fromhex(patch['expect'])
            rva = patch['locate'].get('rva', profiles[build].get('handler_rva'))
            require(rva is not None and pe.get_data(rva, len(before)) == before,
                    build + ': original bytes differ for ' + patch['id'])
            if profiles[build]['route'] == 'native':
                instructions = list(decoder.disasm(before, base + rva))
                require(len(instructions) == 1 and instructions[0].mnemonic in ('je', 'jne')
                        and instructions[0].size == len(before), patch['id'] + ': expected one full conditional branch')
                require(bytes.fromhex(patch['write']) == b'\x90' * len(before),
                        patch['id'] + ': must preserve the native instruction width')
            checked.append(patch['id'])
        with path.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        result = {'sha256': digest, 'patches': checked}
        if profiles[build]['route'] == 'native':
            result['keyboard_cases'] = KeyboardProbe(pe, build, routes[build], rows).verify()
            receipt['keyboard_cases'] += len(result['keyboard_cases'])
        else:
            profile = profiles[build]
            require(pe.FILE_HEADER.TimeDateStamp == profile['pe_timestamp']
                    and pe.OPTIONAL_HEADER.SizeOfImage in profile['image_sizes'], build + ': image identity differs')
            pointer = struct.unpack('<I', pe.get_data(profile['key_slot_rva'], 4))[0]
            require(pointer == base + profile['handler_rva'], build + ': keyboard vtable pointer differs')
            signature = bytes.fromhex(profile['handler_signature'])
            require(pe.get_data(profile['handler_rva'], len(signature)) == signature, build + ': handler signature differs')
            result['companion_entry'] = 'image identity, vtable pointer and handler signature verified'
        receipt['builds'][build] = result
        receipt['patches'] += len(checked)
        print(f'{build}: {len(checked)} patch checks; '
              + (f'{len(result["keyboard_cases"])} keyboard cases' if 'keyboard_cases' in result else result['companion_entry']))
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--clients', required=True, type=Path, help='JSON object mapping all build IDs to unpacked PE paths')
    parser.add_argument('--package', type=Path, help='Verify an unpacked release package instead of source definitions')
    parser.add_argument('--output', required=True, type=Path, help='Write the verification receipt here')
    options = parser.parse_args()
    result = verify(json.loads(options.clients.read_text(encoding='utf-8')), options.package)
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'Verified {len(result["builds"])} builds, {result["patches"]} patches and '
          f'{result["keyboard_cases"]} modeled keyboard cases. Live input and rendering remain unverified.')
