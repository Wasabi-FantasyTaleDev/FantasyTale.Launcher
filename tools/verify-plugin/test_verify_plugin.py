"""Acceptance classification for offline patch verification."""
import contextlib
import io
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import verify_plugin


class FixtureImage:
    """A single mapped code section with deterministic matching bytes."""

    base = 0x400000

    def __init__(self, _path=None):
        self.blob = bytes.fromhex("75 03 90 90")

    def code(self):
        return [(0x1000, self.blob)]

    def read(self, rva, size):
        offset = rva - 0x1000
        return self.blob[offset:offset + size] if 0 <= offset <= len(self.blob) - size else None


class VerificationTests(unittest.TestCase):
    def supported_fixture(self):
        image = FixtureImage()
        image.timestamp, image.size = 123, 0x2000
        blob = bytearray(b"\x90" * 80)
        blob[:8] = bytes.fromhex("E8 2B 00 00 00 83 C4 1C")
        blob[16:24] = bytes.fromhex("55 8B EC 83 EC 10 53 56")
        blob[24:28] = struct.pack("<I", image.base + 0x1040)
        image.blob = bytes(blob)
        row = {"id": "test-1", "pe_timestamp": 123, "image_sizes": [0x2000],
               "preferred_base": image.base, "render_call_rva": 0x1000,
               "render_call_bytes": "E8 2B 00 00 00 83 C4 1C", "original_draw_rva": 0x1030,
               "resource_signatures": [{"rva": 0x1010, "bytes": "55 8B EC 83 EC 10 53 56"}],
               "maximum_health_source": "vtable_f64", "maximum_entry_rva": 0x1018, "maximum_health_rva": 0x1040}
        return row, image

    def check_supported(self, row, image):
        with contextlib.redirect_stdout(io.StringIO()):
            return verify_plugin.check_supported_descriptor(row, image)

    def test_supported_native_bytes_keep_runtime_acceptance_deferred(self):
        row, image = self.supported_fixture()
        result = self.check_supported(row, image)
        self.assertFalse(result.failed)
        self.assertEqual(4, result.checked)
        self.assertEqual(1, result.deferred)

    def test_supported_native_rejects_wrong_image_target_cleanup_and_pointer(self):
        for key, value in (("pe_timestamp", 124), ("image_sizes", [0x3000]), ("preferred_base", 0x500000),
                           ("original_draw_rva", 0x1031), ("maximum_health_rva", 0x1041),
                           ("render_call_bytes", "E8 2B 00 00 00 83 C4 18"),
                           ("render_call_bytes", "E8 ?? 00 00 00 83 C4 1C"), ("maximum_entry_rva", 0x2000)):
            with self.subTest(key=key, value=value):
                row, image = self.supported_fixture()
                row[key] = value
                self.assertTrue(self.check_supported(row, image).failed)

    def test_supported_native_rejects_corrupted_render_resource_and_vtable_bytes(self):
        for offset in (0, 5, 7, 16, 23, 24, 27):
            with self.subTest(offset=offset):
                row, image = self.supported_fixture()
                blob = bytearray(image.blob)
                blob[offset] ^= 1
                image.blob = bytes(blob)
                self.assertTrue(self.check_supported(row, image).failed)
        row, image = self.supported_fixture()
        row["resource_signatures"][0]["bytes"] = "55 ?? EC 83 EC 10 53 56"
        self.assertTrue(self.check_supported(row, image).failed)

    def test_supported_native_field_maximum_does_not_require_a_vtable(self):
        row, image = self.supported_fixture()
        row["maximum_health_source"] = "player_field_f32"
        del row["maximum_entry_rva"], row["maximum_health_rva"]
        result = self.check_supported(row, image)
        self.assertFalse(result.failed)
        self.assertEqual(3, result.checked)
        self.assertEqual(1, result.deferred)

    def research_fixture(self):
        image = FixtureImage()
        image.timestamp = 123
        image.size = 0x2000
        image.blob = bytes.fromhex("E8 2B 00 00 00 DD 5C 24 00 E8 22 00 00 00 8B C8 E8 1B 00 00 00 83 C4 1C") + b"\x90" * 28
        row = {
            "id": "test-1", "status": "research", "pe_timestamp": 123, "image_sizes": [0x2000],
            "health_accessor_call_va": 0x401000, "health_accessor_call_bytes": "E8 2B 00 00 00", "health_accessor_va": 0x401030,
            "health_accessor_abi": "thiscall_f64",
            "life_accessor_call_va": 0x401009, "life_accessor_call_bytes": "E8 22 00 00 00", "life_accessor_va": 0x401030,
            "wrapper_call_va": 0x401010, "wrapper_call_bytes": "E8 1B 00 00 00", "number_wrapper_va": 0x401030,
        }
        return row, image

    def check_research(self, row, image):
        with contextlib.redirect_stdout(io.StringIO()):
            return verify_plugin.check_research_descriptor(row, image)

    def test_research_call_checks_never_accept_runtime(self):
        row, image = self.research_fixture()
        result = self.check_research(row, image)
        self.assertFalse(result.failed)
        self.assertEqual(4, result.checked)
        self.assertEqual(1, result.deferred)

    def test_research_rejects_wrong_call_target(self):
        row, image = self.research_fixture()
        row["health_accessor_va"] += 1
        self.assertTrue(self.check_research(row, image).failed)

    def test_research_rejects_wrong_image_and_cleanup(self):
        row, image = self.research_fixture()
        image.timestamp += 1
        self.assertTrue(self.check_research(row, image).failed)
        image.timestamp -= 1
        image.blob = image.blob[:21] + bytes.fromhex("83 C4 18") + image.blob[24:]
        self.assertTrue(self.check_research(row, image).failed)

    def test_research_rejects_missing_or_wildcard_evidence(self):
        row, image = self.research_fixture()
        del row["health_accessor_call_va"]
        self.assertTrue(self.check_research(row, image).failed)
        row, image = self.research_fixture()
        row["wrapper_call_bytes"] = "E8 ?? ?? ?? ??"
        self.assertTrue(self.check_research(row, image).failed)

    def test_research_rejects_life_call_as_health(self):
        row, image = self.research_fixture()
        for suffix in ("va", "call_va", "call_bytes"):
            row[f"health_accessor_{suffix}"] = row[f"life_accessor_{suffix}"]
        self.assertTrue(self.check_research(row, image).failed)

    def test_research_requires_float_return_contract(self):
        for abi in (None, "thiscall_i32", True):
            with self.subTest(abi=abi):
                row, image = self.research_fixture()
                row["health_accessor_abi"] = abi
                self.assertTrue(self.check_research(row, image).failed)
        for postfix in ("8B C8", "DD D8", "D9 14"):
            with self.subTest(postfix=postfix):
                row, image = self.research_fixture()
                image.blob = image.blob[:5] + bytes.fromhex(postfix) + image.blob[7:]
                self.assertTrue(self.check_research(row, image).failed)

    def test_research_field_float_requires_field_offset(self):
        row, image = self.research_fixture()
        row["health_accessor_abi"] = "edi_field_f64"
        self.assertTrue(self.check_research(row, image).failed)
        row["health_field_offset"] = 372
        self.assertFalse(self.check_research(row, image).failed)

    def test_delayed_floating_store_requires_exact_nonfloating_prefix(self):
        row, image = self.research_fixture()
        row = {key: value for key, value in row.items() if not key.startswith(("life_", "wrapper_", "number_"))}
        consumer = bytes.fromhex("8B 4D DC D9 5D E4")
        image.blob = image.blob[:5] + consumer + b"\x90" * 45
        row["health_result_bytes"] = consumer.hex(" ")
        self.assertFalse(self.check_research(row, image).failed)
        self.assertTrue(self.check_research({key: value for key, value in row.items() if key != "health_result_bytes"}, image).failed)
        for invalid in ("8B ?? DC D9 5D E4", "8B 4D DC D9 55 E4", "8B 4D DC D9 DD E4", "E8 4D DC D9 5D E4"):
            with self.subTest(consumer=invalid):
                self.assertTrue(self.check_research(row | {"health_result_bytes": invalid}, image).failed)
        image.blob = image.blob[:7] + b"\xdb" + image.blob[8:]
        self.assertTrue(self.check_research(row, image).failed)

    def test_separate_native_hp_getter_needs_complete_exact_float_return(self):
        row, image = self.research_fixture()
        row.pop("health_accessor_call_va")
        row.pop("health_accessor_call_bytes")
        getter = bytes.fromhex("51 D9 04 24 59 C3")
        image.blob = image.blob[:48] + getter
        row["health_accessor_bytes"] = getter.hex(" ")
        result = self.check_research(row, image)
        self.assertFalse(result.failed)
        self.assertEqual(1, result.deferred)
        for invalid in ("51 D9 04 24 59 ??", "51 8B 04 24 59 C3", "51 D9 04 24 58 C3", ""):
            self.assertTrue(self.check_research(row | {"health_accessor_bytes": invalid}, image).failed)
        self.assertTrue(self.check_research(row | {"health_accessor_abi": "thiscall_i32"}, image).failed)
        self.assertTrue(self.check_research({key: value for key, value in row.items() if key != "health_accessor_va"}, image).failed)
        image.blob = image.blob[:-1] + b"\x90"
        self.assertTrue(self.check_research(row, image).failed)

    def test_research_checks_life_target_independently(self):
        row, image = self.research_fixture()
        row["life_accessor_va"] += 1
        self.assertTrue(self.check_research(row, image).failed)

    def setup_fixture(self):
        row, image = self.research_fixture()
        setup = bytes.fromhex("8B 03 8D B8 74 01 00 00")
        image.blob = setup + image.blob
        for key in list(row):
            if key.endswith("_va"):
                row[key] += len(setup)
        row.update(health_argument_setup_va=0x401000, health_argument_setup_bytes=setup.hex(" "))
        row.update(health_accessor_abi="edi_field_f64", health_field_offset=372)
        return row, image

    def test_research_checks_argument_setup_and_keeps_runtime_deferred(self):
        row, image = self.setup_fixture()
        result = self.check_research(row, image)
        self.assertFalse(result.failed)

    def test_research_rejects_argument_setup_mismatch(self):
        for key, value in (("health_argument_setup_va", None), ("health_argument_setup_va", 0x401001),
                           ("health_argument_setup_bytes", None), ("health_argument_setup_bytes", "8B 03"),
                           ("health_argument_setup_bytes", "8B ?? 8D B8 74 01 00 00")):
            with self.subTest(key=key, value=value):
                row, image = self.setup_fixture()
                row[key] = value
                self.assertTrue(self.check_research(row, image).failed)
        row, image = self.setup_fixture()
        image.blob = b"\x90" + image.blob[1:]
        self.assertTrue(self.check_research(row, image).failed)

    def run_tool(self, content, images=True):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "plugin.toml").write_text('[plugin]\nid="fixture"\nversion="1.0.0"\n' + content, encoding="utf-8")
            arguments = ["verify_plugin", directory]
            if images:
                arguments += ["--image", "test-1=fixture.exe"]
            with patch("sys.argv", arguments), patch.object(verify_plugin, "Image", FixtureImage), contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                return verify_plugin.main()

    def test_unique_byte_match_is_accepted(self):
        self.assertEqual(0, self.run_tool('[[patches]]\nid="gate"\nlocate={pattern="75 03"}\nexpect="75 03"\nwrite="90 90"\n'))

    def test_byte_mismatch_fails(self):
        self.assertEqual(1, self.run_tool('[[patches]]\nid="gate"\nlocate={rva=4096}\nexpect="74 03"\nwrite="90 90"\n'))

    def test_duplicate_match_fails(self):
        self.assertEqual(1, self.run_tool('[[patches]]\nid="gate"\nlocate={pattern="90"}\nexpect="90"\nwrite="90"\n'))

    def test_runtime_locator_is_incomplete(self):
        self.assertEqual(2, self.run_tool('[[patches]]\nid="gate"\nlocate={locator="fixture.runtime"}\nexpect="75 03"\nwrite="90 90"\n'))

    def test_native_only_plugin_is_incomplete(self):
        self.assertEqual(2, self.run_tool('[native]\nlibrary="fixture.dll"\n'))

    def test_native_entry_with_passing_patch_is_still_incomplete(self):
        self.assertEqual(2, self.run_tool('[native]\nlibrary="fixture.dll"\n[[patches]]\nid="gate"\nlocate={pattern="75 03"}\nexpect="75 03"\nwrite="90 90"\n'))

    def test_unavailable_build_is_incomplete(self):
        self.assertEqual(2, self.run_tool('[compatibility]\nbuilds=["different-1"]\n'))

    def test_no_image_is_usage_error(self):
        with self.assertRaises(SystemExit) as error:
            self.run_tool("", images=False)
        self.assertEqual(2, error.exception.code)

    def test_mixed_offline_and_runtime_checks_are_incomplete(self):
        self.assertEqual(2, self.run_tool('[[patches]]\nid="gate"\nlocate={pattern="75 03"}\nexpect="75 03"\nwrite="90 90"\n[[patches]]\nid="runtime"\nlocate={locator="fixture.runtime"}\nexpect="75 03"\nwrite="90 90"\n'))

    def test_supported_external_descriptor_requires_compatibility(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            plugin = {"compatibility": {"builds": ["test-1"]}}
            folder.joinpath("client-versions.toml").write_text(
                '''schema = 1
[[builds]]
id = "test-1"
status = "supported"
pe_timestamp = 1
image_sizes = [2]
preferred_base = 4194304
render_call_rva = 1
render_call_bytes = "E8 00 00 00 00"
original_draw_rva = 1
current_health_rva = 1
health_accessor_abi = "thiscall_f64"
maximum_health_source = "vtable_f64"
maximum_entry_rva = 1
maximum_health_rva = 1
maximum_vtable_slot = 356
hud_anchor_offset = 1
player_capture = "caller_edi"
hud_register = "ebp"
white_style_slot = 5
resource_manager_rva = 1
resource_string_abi = "msvc_wstring24"
resource_string_inline_capacity = 7
resource_options_size = 88
construct_options_rva = 1
destroy_options_rva = 1
load_sprite_rva = 1
measure_number_rva = 1
native_style_slot = 22
native_style_sprite_index = 3079
sprite_frames_offset = 120
sprite_texture_offset = 136
texture_resident_offset = 144
[[builds.resource_signatures]]
rva = 1
bytes = "00 00 00 00 00 00 00 00"
''',
                encoding="utf-8")
            self.assertEqual([], verify_plugin.validate_client_descriptors(folder, plugin))
            descriptor = folder.joinpath("client-versions.toml")
            valid = descriptor.read_text(encoding="utf-8")
            for before, after, key in (
                ('resource_string_abi = "msvc_wstring24"', 'resource_string_abi = "cow_wstring4"', 'resource_string_inline_capacity'),
                ('resource_string_abi = "msvc_wstring24"', '', 'resource_string_abi'),
                ('white_style_slot = 5', 'white_style_slot = 31', 'white_style_slot'),
                ('player_capture = "caller_edi"', 'player_capture = "caller_stack"', 'player_stack_offset'),
                ('player_capture = "caller_edi"', 'player_capture = "caller_stack"\nplayer_stack_offset = 24', 'player_stack_offset'),
                ('player_capture = "caller_edi"', 'player_capture = "caller_esi"\nplayer_stack_offset = 76', 'player_stack_offset'),
            ):
                descriptor.write_text(valid.replace(before, after), encoding="utf-8")
                self.assertTrue(any(key in error for error in verify_plugin.validate_client_descriptors(folder, plugin)))
            for value in ('"1"', "true", "-1", "4294967296"):
                with self.subTest(address=value):
                    descriptor.write_text(valid.replace("render_call_rva = 1", f"render_call_rva = {value}"), encoding="utf-8")
                    self.assertTrue(any("render_call_rva" in error for error in verify_plugin.validate_client_descriptors(folder, plugin)))
            cow = valid.replace('resource_string_abi = "msvc_wstring24"', 'resource_string_abi = "cow_wstring4"')
            cow = cow.replace('resource_string_inline_capacity = 7', 'construct_string_rva = 1\nrelease_string_rva = 1\ndestroy_options_offset = 32')
            cow = cow.replace('resource_options_size = 88', 'resource_options_size = 36')
            descriptor.write_text(cow, encoding="utf-8")
            self.assertEqual([], verify_plugin.validate_client_descriptors(folder, plugin))
            for before, after, key in (
                    ('construct_string_rva = 1', '', 'construct_string_rva'),
                    ('release_string_rva = 1', 'release_string_rva = 0', 'release_string_rva'),
                    ('release_string_rva = 1', 'release_string_rva = 2', 'resource_signatures'),
                    ('destroy_options_offset = 32', 'destroy_options_offset = 36', 'destroy_options_offset'),
                    ('destroy_options_offset = 32', 'destroy_options_offset = 31', 'destroy_options_offset')):
                descriptor.write_text(cow.replace(before, after), encoding="utf-8")
                self.assertTrue(any(key in error for error in verify_plugin.validate_client_descriptors(folder, plugin)))
            for value in ("", "maximum_vtable_slot = 0", "maximum_vtable_slot = 355", "maximum_vtable_slot = 4100", "maximum_vtable_slot = true"):
                with self.subTest(slot=value):
                    descriptor.write_text(valid.replace("maximum_vtable_slot = 356", value), encoding="utf-8")
                    errors = verify_plugin.validate_client_descriptors(folder, plugin)
                    self.assertTrue(any("maximum_vtable_slot" in error for error in errors))
            for value in ("", 'hud_register = "eax"', 'hud_register = 0'):
                with self.subTest(register=value):
                    descriptor.write_text(valid.replace('hud_register = "ebp"', value), encoding="utf-8")
                    errors = verify_plugin.validate_client_descriptors(folder, plugin)
                    self.assertTrue(any("hud_register" in error for error in errors))
            for key, value in (("resource_string_inline_capacity", "0"), ("resource_string_inline_capacity", "65"),
                               ("resource_options_size", "3"), ("resource_options_size", "4097"),
                               ("resource_options_size", "true")):
                with self.subTest(resource_field=key, value=value):
                    descriptor.write_text(valid.replace(f"{key} = " + ("7" if key == "resource_string_inline_capacity" else "88"), f"{key} = {value}"), encoding="utf-8")
                    errors = verify_plugin.validate_client_descriptors(folder, plugin)
                    self.assertTrue(any(key in error for error in errors))
            for key, value in (("health_accessor_abi", '"thiscall_i32"'), ("maximum_health_source", '"invalid"')):
                descriptor.write_text(valid.replace(f'{key} = ' + ('"thiscall_f64"' if key == "health_accessor_abi" else '"vtable_f64"'), f'{key} = {value}'), encoding="utf-8")
                self.assertTrue(any(key in error for error in verify_plugin.validate_client_descriptors(folder, plugin)))
            field = valid.replace('health_accessor_abi = "thiscall_f64"', 'health_accessor_abi = "edi_field_f64"\nhealth_field_offset = 372')
            field = field.replace('maximum_health_source = "vtable_f64"', 'maximum_health_source = "player_field_f32"\nmaximum_health_field_offset = 5184')
            for key in ("maximum_entry_rva", "maximum_health_rva", "maximum_vtable_slot"):
                field = "\n".join(line for line in field.split("\n") if not line.startswith(f"{key} ="))
            descriptor.write_text(field, encoding="utf-8")
            self.assertEqual([], verify_plugin.validate_client_descriptors(folder, plugin))
            for key, value in (("health_field_offset", "372"), ("maximum_health_field_offset", "5184")):
                for invalid in ("0", "65537", "true"):
                    descriptor.write_text(field.replace(f"{key} = {value}", f"{key} = {invalid}"), encoding="utf-8")
                    self.assertTrue(any(key in error for error in verify_plugin.validate_client_descriptors(folder, plugin)))
            descriptor.write_text(field.replace('maximum_health_field_offset = 5184', 'maximum_health_field_offset = 5184\nmaximum_vtable_slot = 356'), encoding="utf-8")
            self.assertTrue(any("maximum_health_source" in error for error in verify_plugin.validate_client_descriptors(folder, plugin)))

    def test_supported_external_descriptor_must_be_complete(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            folder.joinpath("client-versions.toml").write_text(
                'schema = 1\n[[builds]]\nid = "test-1"\nstatus = "supported"\npe_timestamp = 1\nimage_sizes = [2]\n',
                encoding="utf-8")
            errors = verify_plugin.validate_client_descriptors(folder, {"compatibility": {"builds": ["test-1"]}})
            self.assertTrue(any("render_call_rva" in error for error in errors))

    def test_resource_layout_rejects_invalid_tables_and_offsets(self):
        row = {"native_style_slot": 21, "native_style_sprite_index": 2822,
               "sprite_frames_offset": 48, "sprite_texture_offset": 60, "texture_resident_offset": 72}
        self.assertEqual([], verify_plugin.validate_resource_layout(row, "fixture"))
        for key, value in (("native_style_slot", 17), ("native_style_slot", 31), ("native_style_slot", True),
                           ("native_style_sprite_index", 2817), ("native_style_sprite_index", 4096),
                           ("sprite_frames_offset", 0), ("sprite_frames_offset", 4097), ("sprite_frames_offset", 49),
                           ("sprite_texture_offset", 48), ("sprite_texture_offset", 52),
                           ("texture_resident_offset", 0), ("texture_resident_offset", True)):
            with self.subTest(key=key, value=value):
                changed = row | {key: value}
                self.assertTrue(any(key in error for error in verify_plugin.validate_resource_layout(changed, "fixture")))
        for key in row:
            changed = dict(row)
            del changed[key]
            self.assertTrue(any(key in error for error in verify_plugin.validate_resource_layout(changed, "fixture")))


if __name__ == "__main__":
    unittest.main()
