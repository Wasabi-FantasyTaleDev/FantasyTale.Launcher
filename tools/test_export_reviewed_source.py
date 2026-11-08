"""Exercise export boundaries with in-memory Git metadata and synthetic content."""
import hashlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("export_reviewed", Path(__file__).with_name("export-reviewed-source.py"))
EXPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORTER)
REVISION = "1" * 40


class ReviewedExportTests(unittest.TestCase):
    def manifest(self, path="web/src/preview-levels.json", action="empty-preview"):
        return {"schema": 1, "revision": REVISION, "files": [{"path": path, "action": action,
                "sha256": hashlib.sha256(EXPORTER.EMPTY_PREVIEW).hexdigest()}]}

    def objects(self, path="web/src/preview-levels.json", mode="100644"):
        return patch.object(EXPORTER, "git", side_effect=[(REVISION + "\n").encode(),
                            f"{mode} blob {'2' * 40}\t{path}\0".encode()])

    def test_empty_catalog_export_contains_only_approved_files(self):
        with tempfile.TemporaryDirectory() as directory, self.objects():
            output = Path(directory) / "export"
            self.assertEqual(1, EXPORTER.export(Path(directory), self.manifest(), output))
            self.assertEqual(EXPORTER.EMPTY_PREVIEW, (output / "web/src/preview-levels.json").read_bytes())
            self.assertEqual(["web/src/preview-levels.json"], [p.relative_to(output).as_posix() for p in output.rglob("*") if p.is_file()])

    def test_paths_never_escape_or_include_private_metadata(self):
        for path in ("../outside", "/absolute", "C:/outside", "a\\b", "a//b", ".git/config", "docs/private/a",
                     "reference/a", "artifacts/a", "nul.txt", "a./b", "a:stream", "a\nfile"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                EXPORTER.safe_path(path)

    def test_movable_revisions_missing_files_and_links_are_rejected(self):
        manifest = self.manifest()
        manifest["revision"] = "HEAD"
        with self.assertRaises(ValueError):
            EXPORTER.validate_manifest(Path.cwd(), manifest)
        for mode in ("120000", "160000"):
            with self.subTest(mode=mode), self.objects(mode=mode), self.assertRaises(ValueError):
                EXPORTER.validate_manifest(Path.cwd(), self.manifest())
        with self.objects("different.txt"), self.assertRaises(ValueError):
            EXPORTER.validate_manifest(Path.cwd(), self.manifest())

    def test_raw_catalog_and_duplicate_entries_are_rejected(self):
        with self.objects(), self.assertRaises(ValueError):
            EXPORTER.validate_manifest(Path.cwd(), self.manifest(action="copy"))
        manifest = self.manifest()
        manifest["files"] *= 2
        with self.objects(), self.assertRaises(ValueError):
            EXPORTER.validate_manifest(Path.cwd(), manifest)

    def test_existing_output_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as directory, self.objects(), self.assertRaisesRegex(ValueError, "new directory"):
            EXPORTER.export(Path(directory), self.manifest(), Path(directory))

    def test_blob_copy_preserves_bytes_and_rejects_a_wrong_digest(self):
        class BlobProcess:
            def __enter__(self):
                self.stdout = io.BytesIO(b"reviewed\r\nbytes\x00")
                return self

            def __exit__(self, *_):
                self.stdout.close()

            def wait(self):
                return 0

        for correct in (True, False):
            with self.subTest(correct=correct), tempfile.TemporaryDirectory() as directory, self.objects("LICENSE"), \
                    patch.object(EXPORTER.subprocess, "Popen", return_value=BlobProcess()):
                manifest = self.manifest("LICENSE", "copy")
                if correct:
                    manifest["files"][0]["sha256"] = hashlib.sha256(b"reviewed\r\nbytes\x00").hexdigest()
                    EXPORTER.export(Path(directory), manifest, Path(directory) / "export")
                    self.assertEqual(b"reviewed\r\nbytes\x00", (Path(directory) / "export/LICENSE").read_bytes())
                else:
                    with self.assertRaisesRegex(ValueError, "hash mismatch"):
                        EXPORTER.export(Path(directory), manifest, Path(directory) / "export")

    def test_case_aliases_cannot_merge_distinct_source_directories(self):
        manifest = self.manifest("One/a", "copy")
        manifest["files"].append({**manifest["files"][0], "path": "one/b"})
        tree = f"100644 blob {'2' * 40}\tOne/a\0" + f"100644 blob {'3' * 40}\tone/b\0"
        with patch.object(EXPORTER, "git", side_effect=[(REVISION + "\n").encode(), tree.encode()]), \
                self.assertRaisesRegex(ValueError, "Case-alias"):
            EXPORTER.validate_manifest(Path.cwd(), manifest)


if __name__ == "__main__":
    unittest.main()
