"""Check native UI descriptors against local, unpacked PE images without running a game."""
import argparse
import struct
import tomllib
from pathlib import Path


class PeImage:
    """Read file-backed x86 image ranges using their PE section mappings."""

    def __init__(self, path):
        self.data = Path(path).read_bytes()
        if self.data[:2] != b'MZ':
            raise ValueError('Missing DOS header')
        pe = struct.unpack_from('<I', self.data, 60)[0]
        if self.data[pe:pe + 4] != b'PE\0\0':
            raise ValueError('Missing PE header')
        machine, count, self.timestamp = struct.unpack_from('<HHI', self.data, pe + 4)
        optional_size = struct.unpack_from('<H', self.data, pe + 20)[0]
        optional = pe + 24
        if machine != 0x14c or struct.unpack_from('<H', self.data, optional)[0] != 0x10b:
            raise ValueError('Expected an x86 PE32 image')
        self.base = struct.unpack_from('<I', self.data, optional + 28)[0]
        self.size = struct.unpack_from('<I', self.data, optional + 56)[0]
        self.sections = []
        for index in range(count):
            section = optional + optional_size + index * 40
            virtual_size, rva, raw_size, raw_offset = struct.unpack_from('<IIII', self.data, section + 8)
            flags = struct.unpack_from('<I', self.data, section + 36)[0]
            self.sections.append((rva, virtual_size, raw_offset, raw_size, flags))

    def read(self, rva, size, executable=False):
        for start, _, raw, length, flags in self.sections:
            if start <= rva and rva + size <= start + length:
                if executable and not flags & 0x20000000:
                    raise ValueError(f'Function RVA {rva:#x} is not executable')
                result = self.data[raw + rva - start:raw + rva - start + size]
                if len(result) == size:
                    return result
        raise ValueError(f'RVA range {rva:#x}+{size:#x} has no file-backed image bytes')


def verify(descriptor_path, client_path):
    """Verify identity, code fingerprints, modal vtable binding and layout bounds."""
    definition = tomllib.loads(Path(descriptor_path).read_text(encoding='utf-8'))
    image = PeImage(client_path)
    if definition['schema'] != 1 or definition['build'] != Path(descriptor_path).stem:
        raise ValueError('Descriptor identity does not match its filename')
    if definition['timestamp'] != image.timestamp:
        raise ValueError(f'PE timestamp mismatch: descriptor={definition["timestamp"]:#x}, image={image.timestamp:#x}')
    addresses, values = definition['addresses'], definition['values']
    for name, rva in addresses.items():
        if not isinstance(rva, int) or not 0 < rva < image.size:
            raise ValueError(f'Invalid address {name}')
    legacy = values['message_abi'] in (4, 5)
    conventions = [('message_abi', 5), ('modal_abi', 3)]
    if not legacy:
        conventions.extend((('notify_abi', 4), ('chat_abi', 5)))
    for name, maximum in conventions:
        if not 1 <= values[name] <= maximum:
            raise ValueError(f'Unknown {name}: {values[name]}')
    if legacy and (values['modal_abi'] != 3 or not {'allocate', 'position_message', 'detach_message', 'string_create', 'string_release', 'set_text'} <= addresses.keys()):
        raise ValueError('Legacy message boxes need the three-flag modal and legacy string functions')
    if legacy:
        for name in ('ui_root_offset', 'active_form_offset', 'pending_form_offset', 'exit_flag_offset'):
            if not isinstance(values.get(name), int) or not 0 <= values[name] <= 0xffffffff or (name != 'exit_flag_offset' and values[name] % 4):
                raise ValueError(f'Invalid legacy readiness offset: {name}')
    if not 1 <= values.get('allocate_abi', 1) <= 2:
        raise ValueError('Unknown allocate_abi')
    if values.get('allocate_abi', 1) == 2 and not {'allocate', 'allocator'} <= addresses.keys():
        raise ValueError('Contextual allocation needs allocate and allocator addresses')
    if 'append_announcement' in addresses:
        for name in ('announcement_abi', 'announcement_layout', 'clock_abi'):
            if not 1 <= values.get(name, 1) <= 2:
                raise ValueError(f'Unknown {name}')
        if values.get('clock_abi', 1) == 1 and 'clock' not in addresses:
            raise ValueError('Member announcement clock needs its owner address')
    for name, width in (('reference_offset', 4), ('secondary_offset', 4), ('ok_offset', 4), ('cancel_offset', 4), ('without_icon_offset', 1)):
        if name in values and not 0 <= values[name] <= values['message_size'] - width:
            raise ValueError(f'{name} extends beyond the dialog allocation')
    checked = set()
    for signature in definition['signatures']:
        name = signature['address']
        expected = bytes.fromhex(signature['bytes'])
        if not expected or image.read(addresses[name], len(expected), executable=True) != expected:
            raise ValueError(f'Code signature mismatch: {name}')
        checked.add(name)
    functions = {'allocate', 'construct_message', 'layout_message', 'modal_message', 'position_message', 'detach_message',
                 'string_create', 'string_release', 'notify', 'chat', 'append_announcement', 'clock_now', 'set_text'}
    missing = functions.intersection(addresses) - checked
    if missing:
        raise ValueError(f'Missing function signatures: {sorted(missing)}')
    if 'modal_slot' in values:
        target = struct.unpack('<I', image.read(addresses['message_vtable'] + values['modal_slot'], 4))[0]
        if target != image.base + addresses['modal_message']:
            raise ValueError('Modal vtable slot does not point to the declared modal method')
    return len(checked)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--client', action='append', required=True, metavar='BUILD=EXE')
    parser.add_argument('--descriptors', type=Path, help='Override the native UI descriptor directory')
    args = parser.parse_args()
    root = args.descriptors or Path(__file__).resolve().parents[2] / 'games/BubbleFighter/plugins/bubblefighter.fantasy.native-ui-bridge/native-ui'
    for item in args.client:
        build, image = item.split('=', 1)
        if build not in {path.stem for path in root.glob('*.toml')}:
            raise ValueError(f'Unknown descriptor: {build}')
        count = verify(root / f'{build}.toml', image)
        print(f'{build}: {count} code signatures, PE identity, modal binding and layout bounds verified')


if __name__ == '__main__':
    main()
