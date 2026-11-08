"""Verify frontend-only publication retains its backend and replaces hashed assets."""
import hashlib
import json
from pathlib import Path
import tempfile
import tomllib
import unittest
from publish_frontend import publish


class FrontendPublicationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.base = self.root / "base"
        self.frontend = self.root / "frontend"
        self.frontend.mkdir()
        (self.frontend / "assets").mkdir()
        (self.frontend / "index.html").write_text('assets/new.js')
        (self.frontend / "assets/new.js").write_text('new frontend')
        self.feed = "FantasyTale/launcher/win-x64/current.toml"
        index = self.base / self.feed
        index.parent.mkdir(parents=True)
        text = 'schema=1\nproduct="fantasytale-launcher"\nplatform="win-x64"\nversion="0.1.19"\nrevision=20\n'
        self.backend_hash = None
        for destination, payload in (("FantasyTale.Launcher.exe", b"backend"), ("updater/FantasyTale.Launcher.Updater.exe", b"helper"), ("wwwroot/index.html", b"old index"), ("wwwroot/assets/old.js", b"old frontend")):
            sha = hashlib.sha256(payload).hexdigest()
            if destination == "FantasyTale.Launcher.exe":
                self.backend_hash = sha
            path = "FantasyTale/launcher/win-x64/objects/" + sha
            source = self.base / path
            source.parent.mkdir(exist_ok=True)
            source.write_bytes(payload)
            text += f'\n[[files]]\npath="{destination}"\nsource_path="{path}"\nsha256="{sha}"\nsize={len(payload)}\n'
        index.write_text(text)

    def tearDown(self):
        self.temp.cleanup()

    def test_complete_frontend_replaces_old_assets_and_retains_exact_backend(self):
        index = publish(self.base, self.frontend, self.root / "output", self.feed, "0.1.19", 21)
        release = tomllib.loads(index.read_text())
        files = {row["path"]: row for row in release["files"]}
        self.assertNotIn("wwwroot/assets/old.js", files)
        self.assertIn("wwwroot/assets/new.js", files)
        self.assertEqual(self.backend_hash, files["FantasyTale.Launcher.exe"]["sha256"])
        for row in files.values():
            self.assertEqual(row["sha256"], hashlib.sha256((self.root / "output" / row["source_path"]).read_bytes()).hexdigest())

    def test_wrong_backend_or_nonadvancing_revision_is_rejected(self):
        for version, revision in (("0.1.18", 21), ("0.1.19", 20)):
            with self.assertRaises(ValueError):
                publish(self.base, self.frontend, self.root / "output", self.feed, version, revision)
        self.assertFalse((self.root / "output").exists())

    def test_index_digest_matches_written_bytes_and_aliases(self):
        alias = "preview/launcher.toml"
        (self.base / ".fantasytale-feed-aliases.json").write_text(json.dumps({"feed_path": self.feed, "aliases": [alias]}))
        output = self.root / "output"
        index = publish(self.base, self.frontend, output, self.feed, "0.1.19", 21)
        content = index.read_bytes()
        metadata = json.loads((output / ".fantasytale-feed-aliases.json").read_text())
        self.assertEqual(hashlib.sha256(content).hexdigest(), metadata["sha256"])
        self.assertEqual(content, (output / alias).read_bytes())
        self.assertNotIn(b"\r", content)

    def test_large_readable_inventory_is_published_without_omitting_files(self):
        index = self.base / self.feed
        source_path = "FantasyTale/launcher/win-x64/objects/" + self.backend_hash
        with index.open("a", encoding="utf-8", newline="\n") as stream:
            for number in range(986):
                stream.write(f'[[files]]\npath="runtime/resources/translations/{number:04}.resources"\nsource_path="{source_path}"\nsize=7\nsha256="{self.backend_hash}"\n')
        published = publish(self.base, self.frontend, self.root / "output", self.feed, "0.1.19", 21)
        content = published.read_bytes()
        self.assertEqual(990, len(tomllib.loads(content.decode())["files"]))
        self.assertGreater(len(content), 256 * 1024)
        self.assertIn(b'\n\n[[files]]\npath = "', content)
        self.assertIn(b'\nsource_path = "', content)

    def test_invalid_aliases_never_leave_a_publishable_output(self):
        for aliases in ([self.feed], ["objects/current.toml"], ["preview/current.toml", "PREVIEW/current.toml"]):
            (self.base / ".fantasytale-feed-aliases.json").write_text(json.dumps({"feed_path": self.feed, "aliases": aliases}))
            with self.assertRaises(ValueError):
                publish(self.base, self.frontend, self.root / "output", self.feed, "0.1.19", 21)
            self.assertFalse((self.root / "output").exists())

    def test_corrupt_baseline_and_incomplete_frontend_are_rejected(self):
        (self.frontend / "index.html").unlink()
        with self.assertRaisesRegex(ValueError, "incomplete"):
            publish(self.base, self.frontend, self.root / "output", self.feed, "0.1.19", 21)
        object_path = next((self.base / "FantasyTale/launcher/win-x64/objects").iterdir())
        object_path.write_bytes(b"corrupt")
        with self.assertRaisesRegex(ValueError, "identity"):
            publish(self.base, self.frontend, self.root / "output", self.feed, "0.1.19", 21)


if __name__ == "__main__":
    unittest.main()
