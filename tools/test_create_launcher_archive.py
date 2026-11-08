"""Verify portable archives contain only selected bytes and their source policy."""
import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile


class PortableArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.package = self.root / "package"
        self.package.mkdir()
        self.payload = self.package / "launcher.exe"
        self.payload.write_bytes(b"fixture")
        (self.package / "launcher-update.toml").write_text("kind='http'\nbase_url='https://updates.example/'")
        self.index = self.root / "current.toml"
        self.index.write_text('schema=1\nproduct="fantasytale-launcher"\n[[files]]\npath="launcher.exe"\nsize=7\nsha256="' + hashlib.sha256(b"fixture").hexdigest() + '"\n')
        self.archive = self.root / "portable.zip"

    def tearDown(self):
        self.temporary.cleanup()

    def run_archive(self):
        return subprocess.run([sys.executable, str(Path(__file__).with_name("create-launcher-archive.py")), str(self.package), str(self.index), str(self.archive)], capture_output=True, text=True, timeout=30)

    def test_unselected_local_data_is_not_in_the_portable_archive(self):
        (self.package / "account-sessions.bin").write_bytes(b"private fixture")
        result = self.run_archive()
        self.assertEqual(0, result.returncode, result.stderr)
        with zipfile.ZipFile(self.archive) as archive:
            self.assertEqual({"launcher.exe", "launcher-update.toml"}, set(archive.namelist()))
            self.assertEqual(b"fixture", archive.read("launcher.exe"))

    def test_changed_input_does_not_leave_a_publishable_archive(self):
        self.payload.write_bytes(b"changed")
        self.assertNotEqual(0, self.run_archive().returncode)
        self.assertFalse(self.archive.exists())
        self.assertEqual([], list(self.root.glob("*.tmp")))

    def test_an_existing_archive_is_preserved(self):
        self.archive.write_bytes(b"existing")
        self.assertNotEqual(0, self.run_archive().returncode)
        self.assertEqual(b"existing", self.archive.read_bytes())

    def test_a_large_valid_index_is_accepted(self):
        with self.index.open("a", encoding="utf-8") as stream:
            stream.write("\n# " + "index documentation " * 14000 + "\n")
        self.assertGreater(self.index.stat().st_size, 256 * 1024)
        result = self.run_archive()
        self.assertEqual(0, result.returncode, result.stderr)
        with zipfile.ZipFile(self.archive) as archive:
            self.assertEqual(b"fixture", archive.read("launcher.exe"))
            self.assertIsNone(archive.testzip())

    def create_many_files(self):
        rows = ['schema=1\nproduct="fantasytale-launcher"\n']
        for number in range(1025):
            name = f"assets/{number:04}.txt"
            content = f"asset {number}".encode("utf-8")
            path = self.package / name
            path.parent.mkdir(exist_ok=True)
            path.write_bytes(content)
            rows.append(f'[[files]]\npath="{name}"\nsize={len(content)}\nsha256="{hashlib.sha256(content).hexdigest()}"\n')
        self.index.write_text("".join(rows), encoding="utf-8")

    def test_more_than_1024_files_are_verified_and_archived(self):
        self.create_many_files()
        self.assertLess(self.index.stat().st_size, 256 * 1024)
        result = self.run_archive()
        self.assertEqual(0, result.returncode, result.stderr)
        with zipfile.ZipFile(self.archive) as archive:
            self.assertEqual(1026, len(archive.infolist()))
            self.assertEqual(b"asset 0", archive.read("assets/0000.txt"))
            self.assertEqual(b"asset 1024", archive.read("assets/1024.txt"))
            self.assertIsNone(archive.testzip())

    def test_a_changed_late_file_in_a_large_index_cannot_publish(self):
        self.create_many_files()
        (self.package / "assets/1024.txt").write_bytes(b"changed!!!")
        result = self.run_archive()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("differs from the selected index", result.stderr)
        self.assertFalse(self.archive.exists())
        self.assertEqual([], list(self.root.glob("*.tmp")))


if __name__ == "__main__":
    unittest.main()
