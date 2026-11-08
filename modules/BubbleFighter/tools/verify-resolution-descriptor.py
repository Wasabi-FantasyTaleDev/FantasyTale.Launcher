"""Read-only validation of a resolution descriptor against an unpacked PE image.

This checks image identity, original table data and exact instruction bytes. It
does not execute the client or establish display switching/rendering acceptance.
"""
import argparse
from pathlib import Path
import struct
import tomllib


class Image:
    def __init__(self, path):
        self.data = Path(path).read_bytes()
        if self.data[:2] != b"MZ":
            raise ValueError("not a PE image")
        pe = struct.unpack_from("<I", self.data, 0x3C)[0]
        if self.data[pe:pe + 4] != b"PE\0\0":
            raise ValueError("invalid PE signature")
        machine, sections, self.timestamp = struct.unpack_from("<HHI", self.data, pe + 4)
        if machine != 0x14C or struct.unpack_from("<H", self.data, pe + 24)[0] != 0x10B:
            raise ValueError("expected an x86 PE32 image")
        optional_size = struct.unpack_from("<H", self.data, pe + 20)[0]
        self.base = struct.unpack_from("<I", self.data, pe + 52)[0]
        self.size = struct.unpack_from("<I", self.data, pe + 80)[0]
        self.sections = []
        for i in range(sections):
            offset = pe + 24 + optional_size + i * 40
            vsize, rva, raw_size, raw = struct.unpack_from("<IIII", self.data, offset + 8)
            flags = struct.unpack_from("<I", self.data, offset + 36)[0]
            self.sections.append((rva, vsize, raw, raw_size, flags))

    def read(self, rva, count, executable=False):
        for start, span, raw, raw_size, flags in self.sections:
            if start <= rva and rva + count <= start + min(span, raw_size):
                if executable and not flags & 0x20000000:
                    raise ValueError(f"0x{rva:X} is not executable")
                offset = raw + rva - start
                result = self.data[offset:offset + count]
                if len(result) == count:
                    return result
        raise ValueError(f"0x{rva:X}+{count} is not backed by image bytes")


def verify(descriptor, image):
    if descriptor["schema"] != 1:
        raise ValueError("unsupported descriptor schema")
    if image.timestamp != descriptor["timestamp"] or image.size not in descriptor["image_sizes"]:
        raise ValueError("image timestamp or size does not match the descriptor")
    if image.base != descriptor["preferred_base"]:
        raise ValueError("preferred image base does not match the descriptor")
    table = b"".join(struct.pack("<II", *mode) for mode in descriptor["original_modes"])
    if image.read(descriptor["table_rva"], len(table)) != table:
        raise ValueError("original resolution table does not match")
    guard = bytes.fromhex(descriptor["guard"])
    if image.read(descriptor["guard_rva"], len(guard), executable=True) != guard:
        raise ValueError("native display-mode predicate guard does not match")
    if "menu_filter_call" in descriptor:
        call = descriptor["menu_filter_call"]
        rva, expected = call["rva"], bytes.fromhex(call["expect"])
        if len(expected) != 5 or expected[0] != 0xE8 or rva + 5 + struct.unpack_from("<i", expected, 1)[0] != descriptor["guard_rva"]:
            raise ValueError("menu filter call does not target the verified predicate")
        if image.read(rva, len(expected), executable=True) != expected:
            raise ValueError(f"menu filter call 0x{rva:X} does not match")
        print(f"Verified settings-only filter call at VA 0x{image.base + rva:08X}.")
    count = 0
    for site in descriptor["sites"]:
        expected = bytes.fromhex(site["expect"])
        current = image.read(site["rva"], len(expected), executable=True)
        if current != expected:
            raise ValueError(f"site 0x{site['rva']:X}: expected {expected.hex(' ')}, got {current.hex(' ')}")
        print(f"  {descriptor['build']} VA 0x{image.base + site['rva']:08X}: {current.hex(' ')} ({site['target']})")
        count += 1
    if "main_stage" in descriptor:
        stage = descriptor["main_stage"]
        for guard in [stage["loader"], stage["set_width"], stage["set_height"], *stage["calls"]]:
            expected = bytes.fromhex(guard["expect"])
            rva = guard["rva"] + guard.get("verify_offset", 0)
            if image.read(rva, len(expected), executable=True) != expected:
                raise ValueError(f"stage code guard 0x{rva:X} does not match")
        for call in stage["calls"]:
            expected = bytes.fromhex(call["expect"])
            if len(expected) != 5 or expected[0] != 0xE8 or (call["rva"] + 5 + struct.unpack_from("<i", expected, 1)[0]) != stage["loader"]["rva"]:
                raise ValueError("stage call does not target the verified loader")
        image.read(stage["screen_pointer_rva"], 4)
        print(f"Verified {len(stage['calls'])} stage-local loader calls and all three native function guards.")
    print(f"Verified {count} instruction sites, {len(table) // 8} original modes and the native display-mode guard.")
    print("Static validation only; in-game switching, persistence, layout and input remain unaccepted.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("descriptor", type=Path)
    parser.add_argument("image", type=Path)
    args = parser.parse_args()
    try:
        verify(tomllib.loads(args.descriptor.read_text(encoding="utf-8")), Image(args.image))
    except (ValueError, KeyError, OSError, struct.error) as error:
        parser.exit(1, f"Verification failed: {error}\n")


if __name__ == "__main__":
    main()
