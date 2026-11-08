"""Export shared UI PCM samples and original event gains from a local client."""
import argparse
import hashlib
import importlib.util
import json
import struct
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SAMPLES = {'mouseIn': 'UI_mouseIn.wav', 'press': 'UI_press.wav',
           'toggleButtonOn': 'UI_toggleButtonOn.wav',
           'toggleButtonOff': 'UI_toggleButtonOff.wav', 'popup': 'UI_popup.wav',
           'cancel': 'UI_cancel.wav', 'fail': 'UI_fail.wav', 'notice': 'ui_notice.wav',
           'event-slidenotice': 'ui-slipNotice.wav'}


def prepare(client, target, key):
    """Keep game files external to the library and retain their provenance."""
    spec = importlib.util.spec_from_file_location('tutorial_audio', ROOT / 'prepare-tutorial.py')
    tutorial = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tutorial)
    encrypted = (client / 'Sounds/ui.fsb').read_bytes()
    reverse = bytes(int(f'{n:08b}'[::-1], 2) for n in range(256))
    bank = bytes(reverse[value] ^ key[i % len(key)] for i, value in enumerate(encrypted))
    if bank[:4] != b'FSB4':
        raise ValueError('The bank key did not produce an FSB4 header')
    count, header_size, payload_size = struct.unpack_from('<III', bank, 4)
    if len(bank) != 48 + header_size + payload_size:
        raise ValueError('Unexpected FSB4 size')
    events = (client / 'Sounds/bubbleFighter.fev').read_bytes()
    target.mkdir(parents=True, exist_ok=True)
    offset, data_offset, sounds = 48, 48 + header_size, {}
    for index in range(count):
        header_length = struct.unpack_from('<H', bank, offset)[0]
        name = bank[offset + 2:offset + 32].split(b'\0')[0].decode('ascii')
        frames, length = struct.unpack_from('<II', bank, offset + 32)
        mode, rate = struct.unpack_from('<II', bank, offset + 48)
        channels = struct.unpack_from('<H', bank, offset + 62)[0]
        if name in SAMPLES.values():
            if channels != 1 or not mode & 0x400000:
                raise ValueError(f'Expected mono IMA sample: {name}')
            pcm = tutorial.decode_mono_ima(bank[data_offset:data_offset + length], frames)
            event = next(event for event, sample in SAMPLES.items() if sample == name)
            # FEV event records use type 8, a length-prefixed ASCII name, then gain.
            marker = struct.pack('<II', 8, len(event) + 1) + event.encode('ascii') + b'\0'
            event_offset = events.find(marker)
            if event_offset < 0 or events.find(marker, event_offset + 1) >= 0:
                raise ValueError(f'Missing or ambiguous FEV event record: {event}')
            gain = struct.unpack_from('<f', events, event_offset + len(marker))[0]
            if not 0 < gain <= 1:
                raise ValueError(f'Unexpected original gain for {event}: {gain}')
            reference = b'ui/' + name.encode('ascii') + b'\0'
            reference_offset = events.find(reference)
            bank_reference = struct.pack('<I', 3) + b'ui\0' + struct.pack('<I', index)
            if reference_offset < 0 or not events.startswith(bank_reference, reference_offset + len(reference)):
                raise ValueError(f'FEV sample reference does not match FSB index: {name}')
            with wave.open(str(target / name), 'wb') as output:
                output.setparams((1, 2, rate, frames, 'NONE', 'not compressed'))
                output.writeframes(pcm)
            sounds[event] = {'file': name, 'gain': gain, 'bank': 'Sounds/ui.fsb',
                             'subsoundIndex': index, 'sampleRate': rate,
                             'sampleCount': frames, 'channels': channels,
                             'eventRecordOffset': event_offset,
                             'sampleReferenceOffset': reference_offset,
                             'pcmSha256': hashlib.sha256(pcm).hexdigest()}
        offset += header_length
        data_offset += length
    if len(sounds) != len(SAMPLES) or data_offset != len(bank) or offset != 48 + header_size:
        raise ValueError('Incomplete sound bank extraction')
    manifest = {'version': 1, 'bankSha256': hashlib.sha256(encrypted).hexdigest(),
                'eventFileSha256': hashlib.sha256(events).hexdigest(), 'sounds': sounds}
    (target / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(f'Prepared {len(sounds)} original UI samples with original event gains in {target}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--client-root', required=True, type=Path)
    parser.add_argument('--key', required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'local/sounds')
    args = parser.parse_args()
    if not args.key:
        parser.error('--key must not be empty')
    prepare(args.client_root, args.output, args.key.encode('ascii'))
