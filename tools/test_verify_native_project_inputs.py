"""Exercise native source inventory boundaries with temporary project trees."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('native_inputs', Path(__file__).with_name('verify-native-project-inputs.py'))
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class NativeInputTests(unittest.TestCase):
    """Distinguish missing source, external dependencies, templates and MSBuild expressions."""

    def test_missing_compile_header_and_local_import_are_reported(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'sample.vcxproj').write_text('<Project><ClCompile Include="missing.cpp" />'
                '<ClInclude Include="missing.h" /><Import Project="missing.props" /></Project>', encoding='utf-8')
            result = checker.inspect(root, [])
            self.assertEqual(3, len(result['missing']))

    def test_existing_input_outside_snapshot_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            outer = Path(folder)
            root = outer / 'snapshot'
            root.mkdir()
            (outer / 'private.cpp').write_text('void sample() {}', encoding='utf-8')
            (root / 'sample.vcxproj').write_text('<Project><ClCompile Include="../private.cpp" /></Project>', encoding='utf-8')
            self.assertEqual(1, len(checker.inspect(root, [])['missing']))

    def test_only_declared_existing_template_is_skipped(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'template.vcxproj').write_text('<Project><ClCompile Include="sample.c" /></Project>', encoding='utf-8')
            self.assertEqual([], checker.inspect(root, ['template.vcxproj'])['missing'])
            self.assertEqual(1, len(checker.inspect(root, [])['missing']))
            with self.assertRaises(ValueError):
                checker.inspect(root, ['absent.vcxproj'])

    def test_local_sources_and_system_msbuild_imports_remain_distinct(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'sample.c').write_text('void sample(void) {}', encoding='utf-8')
            (root / 'sample.vcxproj').write_text('<Project><ClCompile Include="$(ProjectDir)sample.c" />'
                '<Import Project="$(VCTargetsPath)/Microsoft.Cpp.props" /></Project>', encoding='utf-8')
            result = checker.inspect(root, [])
            self.assertEqual([], result['missing'])
            self.assertEqual(1, len(result['requires_msbuild_evaluation']))


if __name__ == '__main__':
    unittest.main()
