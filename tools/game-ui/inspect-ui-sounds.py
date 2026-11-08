"""Compare original FSB4 sample metadata and payloads across client builds."""
import argparse
import hashlib
import json
import struct
from pathlib import Path


def inspect(path, key):
    """Decrypt a bank and retain sample hashes without exporting game audio."""
    encrypted = path.read_bytes()
    reverse = bytes(int(f'{n:08b}'[::-1], 2) for n in range(256))
    bank = bytes(reverse[value] ^ key[i % len(key)] for i, value in enumerate(encrypted))
    if bank[:4] != b'FSB4':
        raise ValueError(f'{path}: key did not produce an FSB4 header')
    count, header_size, payload_size = struct.unpack_from('<III', bank, 4)
    header_offset, payload_offset = 48, 48 + header_size
    if payload_offset + payload_size != len(bank):
        raise ValueError(f'{path}: unexpected bank size')
    samples = []
    for index in range(count):
        length = struct.unpack_from('<H', bank, header_offset)[0]
        if length < 64 or header_offset + length > 48 + header_size:
            raise ValueError(f'{path}: invalid sample header {index}')
        name = bank[header_offset + 2:header_offset + 32].split(b'\0')[0].decode('ascii')
        frames, size = struct.unpack_from('<II', bank, header_offset + 32)
        mode, rate = struct.unpack_from('<II', bank, header_offset + 48)
        channels = struct.unpack_from('<H', bank, header_offset + 62)[0]
        if payload_offset + size > len(bank):
            raise ValueError(f'{path}: truncated sample {index}')
        samples.append({'index': index, 'name': name, 'frames': frames,
                        'bytes': size, 'mode': hex(mode), 'rate': rate,
                        'channels': channels,
                        'payloadSha256': hashlib.sha256(bank[payload_offset:payload_offset + size]).hexdigest()})
        header_offset += length
        payload_offset += size
    if header_offset != 48 + header_size or payload_offset != len(bank):
        raise ValueError(f'{path}: sample boundaries do not cover the bank')
    return {'path': str(path), 'bytes': len(bank),
            'fileSha256': hashlib.sha256(encrypted).hexdigest(),
            'payloadSha256': hashlib.sha256(bank[48 + header_size:]).hexdigest(),
            'samples': samples}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--key', required=True, help='Original client bank key (ASCII)')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('banks', nargs='+', type=Path)
    args = parser.parse_args()
    key = args.key.encode('ascii')
    if not key:
        parser.error('--key must not be empty')
    results = [inspect(path, key) for path in args.banks]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(results, indent=2), encoding='utf-8')
    baseline = results[0]
    for result in results:
        same_samples = ([sample['payloadSha256'] for sample in result['samples']] ==
                        [sample['payloadSha256'] for sample in baseline['samples']])
        print(f"{result['path']}: {len(result['samples'])} samples; "
              f"same ordered sample payloads as baseline: {same_samples}")
