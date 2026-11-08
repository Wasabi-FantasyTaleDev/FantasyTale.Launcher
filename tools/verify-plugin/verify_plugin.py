"""Check hook plugins against client images without running the client.

For every pattern or rva patch of a plugin that applies to a build, the image (an unpacked
dump or an unpacked executable) is mapped by its section table and the tool reports how
often the pattern matches, whether the expected bytes are at the site, and, for rewritten
relative jumps, the branch target before and after the rewrite. Named locators run only
inside the game and are reported as not checked. If a native plugin carries
client-versions.toml, its build descriptors are validated before image checks;
supported native descriptors also check their recorded image bytes and targets.

Usage:
  python verify_plugin.py <plugin-folder> --image <build-id>=<path> [--image ...] [--branch <build-id>=<branch>]

Exit code 0 when at least one patch was checked and all applicable patches passed.
Exit code 1 for a failed byte/pattern check; 2 for incomplete runtime-only checks or
no applicable patches. Runtime locators cannot be accepted from an offline image.
"""
from __future__ import annotations

import argparse
import struct
import sys
import tomllib
from dataclasses import dataclass
from pathlib import Path


SUPPORTED_DESCRIPTOR_FIELDS = {
    "preferred_base", "render_call_rva", "render_call_bytes", "original_draw_rva",
    "current_health_rva", "health_accessor_abi", "maximum_health_source", "hud_anchor_offset",
    "player_capture", "hud_register", "resource_manager_rva", "construct_options_rva", "destroy_options_rva",
    "load_sprite_rva", "measure_number_rva", "resource_signatures",
    "resource_string_inline_capacity", "resource_options_size",
    "native_style_slot", "native_style_sprite_index", "sprite_frames_offset", "sprite_texture_offset", "texture_resident_offset",
}

RESOURCE_LAYOUT_FIELDS = {"native_style_slot", "native_style_sprite_index", "sprite_frames_offset", "sprite_texture_offset", "texture_resident_offset"}


def validate_resource_layout(row: dict, prefix: str) -> list[str]:
    """Bound plugin-owned font tables and reject overlapping sprite members."""
    errors = []
    slot = row.get("native_style_slot")
    index = row.get("native_style_sprite_index")
    if type(slot) is not int or not 18 <= slot <= 30:
        errors.append(f"{prefix}.native_style_slot: must be in 18..30")
    if type(index) is not int or not 0 <= index < 4096 or (type(slot) is int and index < slot * 128 + 130):
        errors.append(f"{prefix}.native_style_sprite_index: must be within the private table after its character map")
    for key in ("sprite_frames_offset", "sprite_texture_offset", "texture_resident_offset"):
        offset = row.get(key)
        if type(offset) is not int or not 4 <= offset <= 4096 or offset % 4:
            errors.append(f"{prefix}.{key}: must be a four-byte-aligned offset in 4..4096")
    frames, texture = row.get("sprite_frames_offset"), row.get("sprite_texture_offset")
    if type(frames) is int and type(texture) is int and frames <= texture < frames + 8:
        errors.append(f"{prefix}.sprite_texture_offset: must not overlap the frame begin/end pair")
    return errors


def validate_client_descriptors(folder: Path, plugin: dict) -> list[str]:
    """Validate optional external native-build descriptors before image checks."""
    path = folder / "client-versions.toml"
    if not path.exists():
        return []
    try:
        document = tomllib.loads(path.read_text(encoding="utf-8"))
    except (OSError, tomllib.TOMLDecodeError) as error:
        return [f"client-versions.toml: {error}"]
    errors: list[str] = []
    if document.get("schema") != 1:
        errors.append("client-versions.toml.schema: must be 1")
    rows = document.get("builds")
    if not isinstance(rows, list):
        return errors + ["client-versions.toml.builds: must be an array"]
    compatibility = set(plugin.get("compatibility", {}).get("builds", []))
    seen: set[str] = set()
    supported: set[str] = set()
    for index, row in enumerate(rows):
        prefix = f"client-versions.toml.builds[{index}]"
        if not isinstance(row, dict):
            errors.append(f"{prefix}: must be a table")
            continue
        build = row.get("id")
        status = row.get("status")
        if not isinstance(build, str) or not build:
            errors.append(f"{prefix}.id: must be a non-empty string")
            continue
        if build in seen:
            errors.append(f"{prefix}.id: duplicate build '{build}'")
        seen.add(build)
        if status not in ("supported", "research"):
            errors.append(f"{prefix}.status: must be supported or research")
            continue
        timestamp = row.get("pe_timestamp")
        sizes = row.get("image_sizes")
        if not isinstance(timestamp, int) or not 0 <= timestamp <= 0xFFFFFFFF:
            errors.append(f"{prefix}.pe_timestamp: must be an unsigned 32-bit integer")
        if not isinstance(sizes, list) or not sizes or any(not isinstance(value, int) or not 0 <= value <= 0xFFFFFFFF for value in sizes):
            errors.append(f"{prefix}.image_sizes: must be a non-empty array of unsigned 32-bit integers")
        string_capacity = row.get("resource_string_inline_capacity")
        if "resource_string_inline_capacity" in row and (type(string_capacity) is not int or not 1 <= string_capacity <= 64):
            errors.append(f"{prefix}.resource_string_inline_capacity: must be in 1..64")
        options_size = row.get("resource_options_size")
        if "resource_options_size" in row and (type(options_size) is not int or not 4 <= options_size <= 4096):
            errors.append(f"{prefix}.resource_options_size: must be in 4..4096")
        if status != "supported":
            if RESOURCE_LAYOUT_FIELDS & row.keys():
                errors.extend(validate_resource_layout(row, prefix))
            continue
        supported.add(build)
        if build not in compatibility:
            errors.append(f"{prefix}.id: supported build is missing from plugin compatibility")
        for field in SUPPORTED_DESCRIPTOR_FIELDS:
            if field not in row:
                errors.append(f"{prefix}.{field}: missing for supported build")
        for field in ("preferred_base", "render_call_rva", "original_draw_rva", "current_health_rva",
                      "resource_manager_rva", "construct_options_rva", "destroy_options_rva",
                      "load_sprite_rva", "measure_number_rva"):
            value = row.get(field)
            if type(value) is not int or not 0 < value <= 0xFFFFFFFF:
                errors.append(f"{prefix}.{field}: must be a positive unsigned 32-bit address")
        if row.get("maximum_health_source") == "vtable_f64":
            for field in ("maximum_entry_rva", "maximum_health_rva"):
                value = row.get(field)
                if type(value) is not int or not 0 < value <= 0xFFFFFFFF:
                    errors.append(f"{prefix}.{field}: must be a positive unsigned 32-bit address")
        errors.extend(validate_resource_layout(row, prefix))
        if row.get("player_capture") not in ("caller_edi", "hud_owner"):
            errors.append(f"{prefix}.player_capture: must be caller_edi or hud_owner")
        if row.get("hud_register") not in ("ebp", "ebx"):
            errors.append(f"{prefix}.hud_register: must be ebp or ebx")
        abi = row.get("health_accessor_abi")
        if abi not in ("thiscall_f64", "edi_field_f64"):
            errors.append(f"{prefix}.health_accessor_abi: must be thiscall_f64 or edi_field_f64")
        if abi == "edi_field_f64":
            offset = row.get("health_field_offset")
            if type(offset) is not int or not 1 <= offset <= 65536:
                errors.append(f"{prefix}.health_field_offset: must be in 1..65536 for edi_field_f64")
        source = row.get("maximum_health_source")
        vtable_fields = ("maximum_entry_rva", "maximum_health_rva", "maximum_vtable_slot")
        if source == "vtable_f64":
            for field in vtable_fields:
                if field not in row:
                    errors.append(f"{prefix}.{field}: missing for vtable_f64")
            slot = row.get("maximum_vtable_slot")
            if type(slot) is not int or not 4 <= slot <= 4096 or slot % 4:
                errors.append(f"{prefix}.maximum_vtable_slot: must be a four-byte-aligned offset in 4..4096")
        elif source == "player_field_f32":
            offset = row.get("maximum_health_field_offset")
            if type(offset) is not int or not 1 <= offset <= 65536:
                errors.append(f"{prefix}.maximum_health_field_offset: must be in 1..65536 for player_field_f32")
            if any(field in row for field in vtable_fields):
                errors.append(f"{prefix}.maximum_health_source: player_field_f32 cannot also declare vtable access")
        else:
            errors.append(f"{prefix}.maximum_health_source: must be vtable_f64 or player_field_f32")
        render_bytes = row.get("render_call_bytes")
        valid_render_bytes = False
        if isinstance(render_bytes, str):
            try:
                valid_render_bytes = len(parse_pattern(render_bytes)[0]) >= 5
            except ValueError:
                valid_render_bytes = False
        if not valid_render_bytes:
            errors.append(f"{prefix}.render_call_bytes: must contain at least five bytes")
        signatures = row.get("resource_signatures")
        if not isinstance(signatures, list) or not signatures:
            errors.append(f"{prefix}.resource_signatures: must be a non-empty array")
        else:
            for sig_index, signature in enumerate(signatures):
                sig_prefix = f"{prefix}.resource_signatures[{sig_index}]"
                if not isinstance(signature, dict) or type(signature.get("rva")) is not int or not 0 < signature["rva"] <= 0xFFFFFFFF or not isinstance(signature.get("bytes"), str):
                    errors.append(f"{sig_prefix}: needs integer rva and hexadecimal bytes")
                    continue
                try:
                    signature_bytes = parse_pattern(signature["bytes"])[0]
                except ValueError:
                    signature_bytes = b""
                if len(signature_bytes) != 8:
                    errors.append(f"{sig_prefix}.bytes: must contain exactly eight bytes")
    for build in compatibility:
        if build not in supported:
            errors.append(f"compatibility build '{build}' has no supported descriptor")
    return errors


def parse_pattern(text: str) -> tuple[bytes, bytes]:
    values, mask = bytearray(), bytearray()
    for token in text.split():
        if token in ("?", "??"):
            values.append(0)
            mask.append(0)
        else:
            values.append(int(token, 16))
            mask.append(0xFF)
    return bytes(values), bytes(mask)


class Image:
    """A PE file mapped by its section table."""

    def __init__(self, path: Path):
        data = path.read_bytes()
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        count = struct.unpack_from("<H", data, pe + 6)[0]
        opt = pe + 24
        self.base = struct.unpack_from("<I", data, opt + 28)[0]
        self.timestamp = struct.unpack_from("<I", data, pe + 8)[0]
        self.size = struct.unpack_from("<I", data, opt + 56)[0]
        table = opt + struct.unpack_from("<H", data, pe + 20)[0]
        self.sections = []
        for i in range(count):
            _, vsize, va, rsize, raw, *_rest, flags = struct.unpack_from("<8sIIIIIIHHI", data, table + i * 40)
            self.sections.append((va, max(vsize, rsize), data[raw:raw + rsize], bool(flags & 0x20000000)))

    def code(self):
        return [(va, blob) for va, _, blob, executable in self.sections if executable]

    def read(self, rva: int, size: int) -> bytes | None:
        for va, span, blob, _ in self.sections:
            if va <= rva and rva + size <= va + len(blob):
                return blob[rva - va:rva - va + size]
        return None


def find(image: Image, values: bytes, mask: bytes, limit: int) -> list[int]:
    hits: list[int] = []
    exact_prefix = 0
    while exact_prefix < len(mask) and mask[exact_prefix] == 0xFF:
        exact_prefix += 1
    for va, blob in image.code():
        start = 0
        while len(hits) < limit:
            if exact_prefix:
                start = blob.find(values[:exact_prefix], start)
                if start < 0:
                    break
            if start + len(values) > len(blob):
                break
            if all((blob[start + k] & mask[k]) == values[k] for k in range(len(values))):
                hits.append(va + start)
            start += 1
    return hits


def selector(sel: str, build: str, branch: str) -> bool:
    return sel == "*" or sel == build or (sel.startswith("branch:") and sel[7:] == branch)


def jump_target(code: bytes, rva: int) -> int | None:
    if len(code) >= 2 and 0x70 <= code[0] <= 0x7F or (code and code[0] == 0xEB):
        return rva + 2 + struct.unpack("b", code[1:2])[0]
    if len(code) >= 6 and code[0] == 0x0F and 0x80 <= code[1] <= 0x8F:
        return rva + 6 + struct.unpack("<i", code[2:6])[0]
    if len(code) >= 5 and code[0] in (0xE9, 0xE8):
        return rva + 5 + struct.unpack("<i", code[1:5])[0]
    return None


@dataclass
class CheckResult:
    """Separate proven byte checks from deferred runtime checks and failures."""

    checked: int = 0
    deferred: int = 0
    failed: bool = False


def check_research_descriptor(row: dict, image: Image) -> CheckResult:
    """Verify recorded research call sites without accepting native execution."""
    result = CheckResult(deferred=1)
    build = row["id"]
    identity = image.timestamp == row.get("pe_timestamp") and image.size in row.get("image_sizes", [])
    result.checked += 1
    result.failed = not identity
    print(f"  {build}: research image identity={'yes' if identity else 'NO'}")
    for prefix, site_key, bytes_key, target_key in (
        ("health", "health_accessor_call_va", "health_accessor_call_bytes", "health_accessor_va"),
        ("life", "life_accessor_call_va", "life_accessor_call_bytes", "life_accessor_va"),
        ("wrapper", "wrapper_call_va", "wrapper_call_bytes", "number_wrapper_va"),
    ):
        values = (row.get(site_key), row.get(bytes_key), row.get(target_key))
        if all(value is None for value in values):
            continue
        result.checked += 1
        site, signature, target = values
        if (type(site) is not int or not image.base <= site < image.base + image.size or
                type(target) is not int or not image.base <= target < image.base + image.size or
                not isinstance(signature, str)):
            print(f"  {build} {prefix}: incomplete or invalid call descriptor")
            result.failed = True
            continue
        try:
            expected, mask = parse_pattern(signature)
        except (ValueError, OverflowError):
            expected, mask = b"", b""
        current = image.read(site - image.base, 5)
        good = (len(expected) == 5 and mask == b"\xff" * 5 and expected[0] == 0xE8 and
                current == expected and jump_target(current or b"", site) == target)
        if prefix == "health":
            # The measured CN HUD consumes HP from x87 ST(0); Life uses EAX instead.
            following = image.read(site - image.base + 5, 2)
            abi = row.get("health_accessor_abi")
            good = (good and abi in ("thiscall_f64", "edi_field_f64") and following is not None and
                    following[0] in (0xD9, 0xDD) and (following[1] >> 3) & 7 == 3 and
                    following[1] >> 6 != 3)
            if abi == "edi_field_f64":
                good = good and type(row.get("health_field_offset")) is int and row["health_field_offset"] > 0
        if prefix == "wrapper":
            # All measured wrappers here return to caller cleanup of seven slots.
            good = good and image.read(site - image.base + 5, 3) == b"\x83\xc4\x1c"
        print(f"  {build} {prefix}: va=0x{site:08X} target=0x{target:08X} verified={'yes' if good else 'NO'}")
        result.failed |= not good
    setup = row.get("health_argument_setup_va")
    signature = row.get("health_argument_setup_bytes")
    if setup is not None or signature is not None:
        result.checked += 1
        try:
            expected, mask = parse_pattern(signature) if isinstance(signature, str) else (b"", b"")
        except (ValueError, OverflowError):
            expected, mask = b"", b""
        call = row.get("health_accessor_call_va")
        good = (type(setup) is int and type(call) is int and image.base <= setup < call and
                1 <= len(expected) <= 64 and setup + len(expected) == call and
                mask == b"\xff" * len(expected) and image.read(setup - image.base, len(expected)) == expected)
        print(f"  {build} HP argument setup: verified={'yes' if good else 'NO'}")
        result.failed |= not good
    print(f"  {build}: research only; native ABI and controlled rendering remain deferred")
    return result


def check_supported_descriptor(row: dict, image: Image) -> CheckResult:
    """Check native descriptor bytes and targets while leaving execution deferred."""
    result = CheckResult(deferred=1)
    build = row["id"]
    identity = (image.timestamp == row["pe_timestamp"] and image.size in row["image_sizes"]
                and image.base == row["preferred_base"])
    result.checked += 1
    result.failed = not identity
    print(f"  {build}: supported descriptor image identity={'yes' if identity else 'NO'}")
    site = row["render_call_rva"]
    expected, mask = parse_pattern(row["render_call_bytes"])
    current = image.read(site, len(expected))
    # The hook replaces this call; the original target and caller cleanup are part of its ABI.
    good = (len(expected) == 8 and mask == b"\xff" * 8 and current == expected
            and expected[0] == 0xE8 and expected[5:] == b"\x83\xc4\x1c"
            and jump_target(current or b"", site) == row["original_draw_rva"])
    result.checked += 1
    result.failed |= not good
    print(f"  {build} render call and seven-slot cleanup: verified={'yes' if good else 'NO'}")
    for signature in row["resource_signatures"]:
        expected, mask = parse_pattern(signature["bytes"])
        good = (bool(expected) and mask == b"\xff" * len(expected)
                and image.read(signature["rva"], len(expected)) == expected)
        result.checked += 1
        result.failed |= not good
        print(f"  {build} resource rva=0x{signature['rva']:X}: verified={'yes' if good else 'NO'}")
    if row["maximum_health_source"] == "vtable_f64":
        pointer = image.read(row["maximum_entry_rva"], 4)
        good = (pointer is not None and len(pointer) == 4
                and struct.unpack("<I", pointer)[0] == image.base + row["maximum_health_rva"])
        result.checked += 1
        result.failed |= not good
        print(f"  {build} maximum-health vtable entry: verified={'yes' if good else 'NO'}")
    print(f"  {build}: native loading, player provenance and rendered behavior remain deferred")
    return result


def check(plugin: dict, build: str, branch: str, image: Image) -> CheckResult:
    compat = plugin.get("compatibility", {})
    if not any(selector(s, build, branch) for s in compat.get("builds", ["*"])) or any(
            selector(s, build, branch) for s in compat.get("exclude", [])):
        print(f"  {build}: not available (compatibility)")
        return CheckResult()
    result = CheckResult()
    if plugin.get("native"):
        print(f"  {build}: native entry requires in-game verification")
        result.deferred += 1
    for patch in plugin.get("patches", []):
        if patch.get("builds") and not any(selector(s, build, branch) for s in patch["builds"]):
            continue
        locate = patch["locate"]
        expect, expect_mask = parse_pattern(patch["expect"])
        write, _ = parse_pattern(patch["write"])
        offset = locate.get("offset", 0)
        label = f"  {build} {patch['id']}:"
        if "locator" in locate:
            print(f"{label} locator {locate['locator']} (checked in game only)")
            result.deferred += 1
            continue
        result.checked += 1
        if "rva" in locate:
            sites = [locate["rva"]]
        else:
            values, mask = parse_pattern(locate["pattern"])
            sites = find(image, values, mask, 3)
            unique = locate.get("match", "unique") == "unique"
            if not sites or (unique and len(sites) > 1):
                print(f"{label} pattern matches {len(sites)}{'+' if len(sites) == 3 else ''} times")
                result.failed = True
                continue
            sites = sites[:1]
        site = sites[0] + offset
        current = image.read(site, len(expect))
        good = current is not None and all((current[k] & expect_mask[k]) == expect[k] for k in range(len(expect)))
        text = f"{label} va=0x{image.base + site:08X} bytes={current.hex(' ') if current else '?'} expected={'yes' if good else 'NO'}"
        before, after = jump_target(current or b"", site), jump_target(write, site)
        if before is not None or after is not None:
            fmt = lambda t: "none" if t is None else f"0x{image.base + t:08X}"
            text += f" branch {fmt(before)} -> {fmt(after)}"
        print(text)
        result.failed = result.failed or not good
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("plugin", type=Path)
    parser.add_argument("--image", action="append", required=True, help="<build-id>=<path to unpacked image>")
    parser.add_argument("--branch", action="append", default=[], help="<build-id>=<branch>, default: prefix of the id")
    parser.add_argument("--research-descriptors", action="store_true", help="Check recorded research call sites; never reports full native acceptance")
    args = parser.parse_args()
    plugin = tomllib.loads((args.plugin / "plugin.toml").read_text(encoding="utf-8"))
    descriptor_errors = validate_client_descriptors(args.plugin, plugin)
    if descriptor_errors:
        for error in descriptor_errors:
            print(f"Descriptor error: {error}")
        return 1
    branches = dict(item.split("=", 1) for item in args.branch)
    descriptors = {}
    path = args.plugin / "client-versions.toml"
    if path.exists():
        descriptors = {row["id"]: row for row in tomllib.loads(path.read_text(encoding="utf-8"))["builds"]}
    research = {build: row for build, row in descriptors.items() if args.research_descriptors and row["status"] == "research"}
    print(f"{plugin['plugin']['id']} {plugin['plugin']['version']}")
    result = CheckResult()
    for item in args.image:
        build, path = item.split("=", 1)
        image = Image(Path(path))
        checked = (check_research_descriptor(research[build], image) if build in research else
                   check(plugin, build, branches.get(build, build.split("-")[0]), image))
        if build in descriptors and descriptors[build]["status"] == "supported":
            native = check_supported_descriptor(descriptors[build], image)
            checked.checked += native.checked
            checked.deferred = max(checked.deferred, native.deferred)
            checked.failed |= native.failed
        result.checked += checked.checked
        result.deferred += checked.deferred
        result.failed = result.failed or checked.failed
    print(f"Summary: {result.checked} offline checks, {result.deferred} deferred runtime checks")
    if result.failed:
        return 1
    if result.checked == 0 or result.deferred:
        print("Incomplete verification: no full offline acceptance is claimed.")
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
