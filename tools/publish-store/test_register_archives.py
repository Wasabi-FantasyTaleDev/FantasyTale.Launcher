"""Exercise reference registration without network access or game payloads."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from register_archives import plan, provider_identity, publish


class Response:
    def __enter__(self):
        return self

    def __exit__(self, *_):
        pass

    def raise_for_status(self):
        pass

    def iter_content(self, _):
        yield b"x"


class RegistrationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.payload = self.root / "local.7z"
        self.payload.write_bytes(b"fixture archive")
        self.digest = hashlib.sha256(self.payload.read_bytes()).hexdigest()
        self.md5 = hashlib.md5(self.payload.read_bytes(), usedforsecurity=False).hexdigest()
        self.old = 'schema=1\n[catalog]\nid="fixture"\nname={en="Fixture"}\n'
        self.baseline = self.root / "baseline.toml"
        self.baseline.write_text(self.old)
        self.manifest = self.root / "snapshot.toml"
        self.manifest.write_text('schema=1\n[release]\nid="client.test"\ngame="fixture"\nbuild="test-1"\nformat="archive"\narchive_format="7z"\n'
            f'[[files]]\npath="client.7z"\nsource_path="clients/client.7z"\nsize={self.payload.stat().st_size}\nsha256="{self.digest}"\nvolume=1\n')
        self.candidate = self.root / "candidate.toml"
        self.candidate.write_text(self.old.replace("schema=1", "schema=2") + '[[packages]]\nid="client.test"\nkind="client"\ngame="fixture"\nbuilds=["test-1"]\n'
            f'size={self.payload.stat().st_size}\nmanifest="snapshot.toml"\nmanifest_sha256="{hashlib.sha256(self.manifest.read_bytes()).hexdigest()}"\n')
        self.references = self.root / "references.toml"
        self.references.write_text('[[archives]]\nsource_path="clients/client.7z"\nlocal_path="local.7z"\n')
        outer = self

        class Storage:
            def __init__(self):
                self.remote = {"source.toml": outer.baseline.read_bytes()}
                self.writes = []
                self.accesses = []
                self.before_manifest = None
                self.before_catalog = None
                self.digest_override = None
                self.metadata_calls = 0

            def remote_hash(self, path):
                if path == "source.toml" and self.before_catalog:
                    callback, self.before_catalog = self.before_catalog, None
                    callback(self)
                data = self.remote.get(path)
                return hashlib.sha256(data).hexdigest() if data is not None else None

            def metadata(self, path, **options):
                self.metadata_calls += 1
                return {"size": outer.payload.stat().st_size, "hash_info": {"md5": self.digest_override or outer.md5}}

            def open_download(self, path, metadata, **options):
                self.accesses.append((path, options))
                return Response()

            def put(self, path, source):
                self.writes.append(path)
                self.remote[path] = source.read_bytes()
                if path == "snapshot.toml" and self.before_manifest:
                    self.before_manifest(self)

        self.storage = Storage()

    def tearDown(self):
        self.temporary.cleanup()

    def publish(self):
        return publish(self.storage, self.root, self.candidate, self.baseline, self.references, "source.toml")

    def test_only_small_metadata_is_uploaded_and_archive_evidence_is_explicit(self):
        receipt = self.publish()
        self.assertEqual(["snapshot.toml", "source.toml"], self.storage.writes)
        self.assertTrue(receipt["published"])
        self.assertEqual("provider-md5-and-anonymous-access", receipt["archives"][0]["remote_evidence"])
        self.assertEqual({"Range": "bytes=0-0"}, self.storage.accesses[0][1]["headers"])

    def test_preflight_never_uploads_metadata(self):
        receipt = publish(self.storage, self.root, self.candidate, self.baseline, self.references, "source.toml", True)
        self.assertFalse(receipt["published"])
        self.assertEqual([], self.storage.writes)
        self.assertEqual(1, len(self.storage.accesses))

    def test_local_hash_conflict_never_publishes(self):
        self.payload.write_bytes(b"wrong source")
        with self.assertRaisesRegex(ValueError, "candidate SHA-256"):
            self.publish()
        self.assertEqual([], self.storage.writes)

    def test_provider_digest_conflict_never_publishes(self):
        self.storage.digest_override = "0" * 32
        with self.assertRaisesRegex(ValueError, "differs from its local"):
            self.publish()
        self.assertEqual([], self.storage.writes)

    def test_remote_archive_change_during_manifest_upload_keeps_catalog_unchanged(self):
        self.storage.before_manifest = lambda storage: setattr(storage, "digest_override", "0" * 32)
        with self.assertRaisesRegex(ValueError, "changed before catalog"):
            self.publish()
        self.assertEqual(["snapshot.toml"], self.storage.writes)
        self.assertEqual(self.baseline.read_bytes(), self.storage.remote["source.toml"])

    def test_concurrent_catalog_change_is_not_overwritten(self):
        self.storage.before_manifest = lambda storage: storage.remote.update({"source.toml": b"changed"})
        with self.assertRaisesRegex(ValueError, "Catalog changed during"):
            self.publish()
        self.assertEqual(["snapshot.toml"], self.storage.writes)

    def test_immutable_manifest_conflict_is_not_overwritten(self):
        self.storage.remote["snapshot.toml"] = b"different manifest"
        with self.assertRaisesRegex(ValueError, "immutable manifest conflict"):
            self.publish()
        self.assertEqual([], self.storage.writes)

    def test_repeat_registration_does_not_write_again(self):
        self.publish()
        self.storage.writes.clear()
        self.publish()
        self.assertEqual([], self.storage.writes)

    def test_published_packages_cannot_be_removed(self):
        self.baseline.write_text(self.old + '[[packages]]\nid="preserved"\n')
        with self.assertRaisesRegex(ValueError, "alter or remove"):
            plan(self.root, self.candidate, self.baseline)

    def test_archive_references_must_cover_exactly_the_manifest(self):
        self.references.write_text(self.references.read_text().replace("clients/client.7z", "clients/other.7z"))
        with self.assertRaisesRegex(ValueError, "exactly cover"):
            self.publish()
        self.assertEqual([], self.storage.writes)

    def test_provider_must_supply_a_digest_and_size(self):
        self.assertEqual((3, "a" * 32), provider_identity({"size": 3, "hash_info": json.dumps({"md5": "A" * 32})}))
        for metadata in (None, {}, {"size": True}, {"size": 3}, {"size": 3, "is_dir": True}):
            with self.assertRaises(ValueError):
                provider_identity(metadata)


if __name__ == "__main__":
    unittest.main()
