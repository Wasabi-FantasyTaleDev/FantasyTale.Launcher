"""Ensure publication uploads only the verified package graph."""
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import contextlib
import io
import threading
import tomllib
from urllib.parse import quote, unquote
from upload_alist import AList, graph, main, retry_network, feed_aliases, manual_download, verify_parallel, published_filelist, network_failure_summary, signed_metadata
from server_profiles import server_profiles
import requests


class UploadGraphTests(unittest.TestCase):
    def test_fast_worker_continues_while_another_object_is_slow(self):
        release = threading.Event()
        progressed = threading.Event()

        def operation(item):
            if item == 0:
                if not release.wait(3):
                    raise TimeoutError("The second worker did not make progress")
            elif item == 2:
                progressed.set()
                release.set()

        try:
            self.assertEqual(4, len(list(verify_parallel(range(4), operation, 2))))
            self.assertTrue(progressed.is_set())
        finally:
            release.set()

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        (self.root / "FantasyTale/files").mkdir(parents=True)
        (self.root / "FantasyTale/snapshots").mkdir()
        self.digest = hashlib.sha256(b"payload").hexdigest()
        self.object = self.root / "FantasyTale/files" / self.digest
        self.object.write_bytes(b"payload")
        self.write_manifest("FantasyTale/files/" + self.digest)

    def tearDown(self):
        self.directory.cleanup()

    def write_manifest(self, path):
        content = f'[[files]]\nsource_path="{path}"\nsha256="{self.digest}"\n'.encode()
        digest = hashlib.sha256(content).hexdigest()
        (self.root / "FantasyTale/snapshots" / (digest + ".toml")).write_bytes(content)
        self.manifest = "FantasyTale/snapshots/" + digest + ".toml"
        (self.root / "FantasyTale/source.toml").write_text(f'[[packages]]\nmanifest="{self.manifest}"\nmanifest_sha256="{digest}"\n', encoding="utf-8")

    def test_graph_excludes_unreferenced_secrets_and_orders_manifest_after_content(self):
        (self.root / ".env").write_text("secret")
        paths = [path for path, _ in graph(self.root, "FantasyTale/source.toml")]
        self.assertEqual(["FantasyTale/files/" + self.digest, self.manifest], paths)

    def test_github_subscription_needs_no_local_payload_upload(self):
        (self.root / "FantasyTale/source.toml").write_text('schema=2\n[catalog]\nid="fixture"\n[[github_releases]]\nid="fixture.server"\nrepository="fixture/server"\nasset="server.zip"\nexecutable="server.exe"\n', encoding="utf-8")
        self.assertEqual([], graph(self.root, "FantasyTale/source.toml"))
        catalog = self.root / "FantasyTale/source.toml"
        catalog.write_text(catalog.read_text() + '\n[[packages]]\nid="fixture.server"\ngithub_asset={ repository="fixture/server", tag="v1.0.0", asset="server.zip", executable="server.exe" }\n', encoding="utf-8")
        self.assertEqual([], graph(self.root, "FantasyTale/source.toml"))

    def test_changed_content_is_refused(self):
        self.object.write_bytes(b"tampered")
        with self.assertRaisesRegex(ValueError, "content digest"):
            graph(self.root, "FantasyTale/source.toml")

    def test_catalogs_do_not_have_a_fixed_metadata_size_limit(self):
        feed = self.root / "FantasyTale/source.toml"
        packages = feed.read_text()
        padding = "\n#" + "x" * (256 * 1024)
        feed.write_text('schema=2\n[catalog]\nid="fixture"\n' + packages + padding)
        self.assertEqual(2, len(graph(self.root, "FantasyTale/source.toml")))
        feed.write_text('schema=1\n[catalog]\nid="fixture"\n' + packages + padding)
        self.assertEqual(2, len(graph(self.root, "FantasyTale/source.toml")))

    def test_launcher_feed_reuses_the_same_verified_content_graph(self):
        feed = self.root / "FantasyTale/source.toml"
        feed.write_text(f'product="fantasytale-launcher"\n[[files]]\nsource_path="FantasyTale/files/{self.digest}"\nsha256="{self.digest}"\n', encoding="utf-8")
        self.assertEqual([("FantasyTale/files/" + self.digest, self.digest)], graph(self.root, "FantasyTale/source.toml"))

    def test_launcher_feed_with_conflicting_object_identity_is_refused(self):
        row = f'[[files]]\nsource_path="FantasyTale/files/{self.digest}"\nsha256="{self.digest}"\n'
        (self.root / "FantasyTale/source.toml").write_text('product="fantasytale-launcher"\n' + row + row.replace(f'sha256="{self.digest}"', 'sha256="' + '0' * 64 + '"'), encoding="utf-8")
        with self.assertRaises(ValueError):
            graph(self.root, "FantasyTale/source.toml")

    def test_changed_manifest_is_refused(self):
        (self.root / self.manifest).write_bytes(b"tampered")
        with self.assertRaisesRegex(ValueError, "manifest digest"):
            graph(self.root, "FantasyTale/source.toml")

    def test_escape_path_is_refused(self):
        self.write_manifest("../outside")
        with self.assertRaises(ValueError):
            graph(self.root, "FantasyTale/source.toml")

    def publish_fixture(self, fail=False, changed=False, direct=False, changed_alias=False, changed_portable=False, same_portable=False, corrupt_portable=False, phase="all", published=False, inventory=None, remote_archives=None, mutate_after_write=None, committed_timeout=False, local_mutation=None):
        outer = self
        class FakeAList:
            def __init__(self, *_args):
                self.remote = {"FantasyTale/source.toml": "previous", "FantasyTale/preview/current.toml": "previous-alias", "FantasyTale/portable/latest.zip": "previous-portable"}
                self.remote.update(remote_archives or {})
                self.writes = []
                if published:
                    self.remote["FantasyTale/source.toml"] = hashlib.sha256((outer.root / "FantasyTale/source.toml").read_bytes()).hexdigest()
                    self.remote["FantasyTale/preview/current.toml"] = self.remote["FantasyTale/source.toml"]
                if same_portable:
                    archive = outer.root / "FantasyTale/portable/latest.zip"
                    self.remote["FantasyTale/portable/latest.zip"] = "corrupt" if corrupt_portable else hashlib.sha256(archive.read_bytes()).hexdigest()
                self.proxies = requests.utils.get_environ_proxies("https://files.example/")
                outer.api = self

            def remote_hash(self, path, *, expected_size=None):
                if inventory is not None and "/objects/" in path:
                    raise AssertionError("Unchanged content must not be downloaded")
                if expected_size is not None:
                    outer.assertEqual((outer.root / path).stat().st_size, expected_size)
                if changed and path.endswith("source.toml") and self.writes:
                    return "changed"
                if changed_alias and path.endswith("preview/current.toml") and self.writes:
                    return "changed-alias"
                if changed_portable and path.endswith("latest.zip") and self.writes:
                    return "changed-portable"
                return self.remote.get(path)

            def read_filelist(self, path, expected_digest):
                return inventory or b'product="legacy"\n'

            def put(self, path, source):
                if fail:
                    raise ValueError("Fixture rejected upload")
                self.writes.append(path)
                self.remote[path] = hashlib.sha256(source.read_bytes()).hexdigest()
                if committed_timeout and path.endswith("latest.zip"):
                    raise requests.Timeout("Storage task deadline expired after committing bytes")
                if mutate_after_write and path == mutate_after_write[0]:
                    self.remote[mutate_after_write[1]] = "concurrent-volume-write"
                if local_mutation and path == local_mutation[0]:
                    (outer.root / local_mutation[1]).write_bytes(b"changed local artifact")

            def metadata(self, path, *, authenticated=False):
                return {"path": path} if path in self.remote else None

            def change_stamp(self, path):
                if same_portable and path.endswith("latest.zip"):
                    archive = outer.root / path
                    return "provider-md5", archive.stat().st_size, hashlib.md5(archive.read_bytes()).hexdigest()
                return self.remote_hash(path)

        arguments = ["upload", "--input", str(self.root), "--base-url", "https://files.example/", "--workers", "2", "--phase", phase]
        if direct:
            arguments.append("--no-proxy")
        with patch("upload_alist.AList", FakeAList), patch.dict("os.environ", {"FT_ALIST_USERNAME": "fixture", "FT_ALIST_PASSWORD": "fixture"}), patch("sys.argv", arguments), contextlib.redirect_stdout(io.StringIO()):
            main()

    def test_published_inventory_skips_unchanged_object_downloads_and_uploads(self):
        self.write_aliases()
        old = self.root / ("FantasyTale/files/" + self.digest)
        target = self.root / ("FantasyTale/objects/" + self.digest)
        target.parent.mkdir()
        target.write_bytes(old.read_bytes())
        feed = self.root / "FantasyTale/source.toml"
        feed.write_text('product="fantasytale-launcher"\n[[files]]\n'
                        f'source_path="FantasyTale/objects/{self.digest}"\n'
                        f'sha256="{self.digest}"\nsize={target.stat().st_size}\n', encoding="utf-8")
        (self.root / "FantasyTale/preview/current.toml").write_bytes(feed.read_bytes())
        metadata = self.root / ".fantasytale-feed-aliases.json"
        record = json.loads(metadata.read_text())
        record["sha256"] = hashlib.sha256(feed.read_bytes()).hexdigest()
        metadata.write_text(json.dumps(record))
        self.publish_fixture(inventory=feed.read_bytes())
        self.assertEqual(["FantasyTale/source.toml", "FantasyTale/preview/current.toml"], self.api.writes)

    def test_published_inventory_rejects_conflicts_and_ignores_mutable_paths(self):
        row = f'[[files]]\nsource_path="FantasyTale/objects/{self.digest}"\nsha256="{self.digest}"\nsize=1\n'
        with self.assertRaisesRegex(ValueError, "Conflicting"):
            published_filelist(('product="fantasytale-launcher"\n' + row + row.replace('size=1', 'size=2')).encode())
        self.assertEqual({}, published_filelist(('product="fantasytale-launcher"\n' + row.replace('/objects/', '/files/')).encode()))

    def write_aliases(self, aliases=None):
        feed = self.root / "FantasyTale/source.toml"
        feed.write_text(f'product="fantasytale-launcher"\nschema=1\n[[files]]\nsource_path="FantasyTale/files/{self.digest}"\nsha256="{self.digest}"\n', encoding="utf-8")
        alias = self.root / "FantasyTale/preview/current.toml"
        alias.parent.mkdir(exist_ok=True)
        alias.write_bytes(feed.read_bytes())
        (self.root / ".fantasytale-feed-aliases.json").write_text(json.dumps({
            "feed_path": "FantasyTale/source.toml", "aliases": aliases if aliases is not None else ["FantasyTale/preview/current.toml"],
            "sha256": hashlib.sha256(feed.read_bytes()).hexdigest(),
        }), encoding="utf-8")

    def test_legacy_feed_is_published_after_all_objects_and_canonical_index(self):
        self.write_aliases()
        self.publish_fixture()
        self.assertEqual(["FantasyTale/files/" + self.digest, "FantasyTale/source.toml", "FantasyTale/preview/current.toml"], self.api.writes)
        self.assertEqual(self.api.remote["FantasyTale/source.toml"], self.api.remote["FantasyTale/preview/current.toml"])

    def write_signed_record(self):
        self.write_aliases()
        record = {"feed_path": "FantasyTale/source.toml", "sha256": hashlib.sha256((self.root / "FantasyTale/source.toml").read_bytes()).hexdigest()}
        for name, content in (("signature", b"signature transport fixture"), ("public_key", b"public key transport fixture")):
            path = f"FantasyTale/{name}.bin"
            (self.root / path).write_bytes(content)
            record[name + "_path"] = path
            record[name + "_sha256"] = hashlib.sha256(content).hexdigest()
        (self.root / ".fantasytale-signature.json").write_text(json.dumps(record), encoding="utf-8")
        return record

    def test_signature_artifacts_are_verified_before_index_and_alias_publication(self):
        record = self.write_signed_record()
        self.publish_fixture()
        self.assertEqual(["FantasyTale/files/" + self.digest, record["signature_path"], record["public_key_path"], "FantasyTale/source.toml", "FantasyTale/preview/current.toml"], self.api.writes)

    def test_existing_matching_signature_artifacts_are_reused_without_writes(self):
        record = self.write_signed_record()
        existing = {record[field + "_path"]: record[field + "_sha256"] for field in ("signature", "public_key")}
        self.publish_fixture(remote_archives=existing)
        self.assertNotIn(record["signature_path"], self.api.writes)
        self.assertNotIn(record["public_key_path"], self.api.writes)
        self.assertIn("FantasyTale/source.toml", self.api.writes)

    def test_signature_artifact_mutation_is_rejected_before_authentication(self):
        for field in ("signature", "public_key"):
            with self.subTest(field=field):
                record = self.write_signed_record()
                (self.root / record[field + "_path"]).write_bytes(b"tampered")
                with patch("upload_alist.AList") as connect, self.assertRaisesRegex(ValueError, "declared identity"):
                    main_arguments = ["upload", "--input", str(self.root), "--base-url", "https://files.example/"]
                    with patch("sys.argv", main_arguments):
                        main()
                connect.assert_not_called()

    def test_signature_record_cannot_target_aliases_content_or_another_index(self):
        for path in ("../outside", "FantasyTale/source.toml", "FantasyTale/preview/current.toml", "FantasyTale/source.toml.archive.toml", "FantasyTale/objects/signature", "FantasyTale/files/" + self.digest, ".fantasytale-publish.json"):
            with self.subTest(path=path):
                record = self.write_signed_record()
                record["signature_path"] = path
                (self.root / ".fantasytale-signature.json").write_text(json.dumps(record))
                with self.assertRaises(ValueError):
                    signed_metadata(self.root, "FantasyTale/source.toml", graph(self.root, "FantasyTale/source.toml"), ["FantasyTale/preview/current.toml"])
        record = self.write_signed_record()
        record["sha256"] = "0" * 64
        (self.root / ".fantasytale-signature.json").write_text(json.dumps(record))
        with self.assertRaisesRegex(ValueError, "another launcher index"):
            signed_metadata(self.root, "FantasyTale/source.toml", [], [])

    def test_remote_signature_conflict_preserves_feed_and_conflicting_signature(self):
        record = self.write_signed_record()
        with self.assertRaisesRegex(ValueError, "Signature artifact changed"):
            self.publish_fixture(mutate_after_write=("FantasyTale/files/" + self.digest, record["signature_path"]))
        self.assertNotIn(record["signature_path"], self.api.writes)
        self.assertNotIn("FantasyTale/source.toml", self.api.writes)

    def test_local_signature_change_during_payload_upload_preserves_feed(self):
        record = self.write_signed_record()
        with self.assertRaisesRegex(ValueError, "Local signature artifact changed"):
            self.publish_fixture(local_mutation=("FantasyTale/files/" + self.digest, record["signature_path"]))
        self.assertNotIn("FantasyTale/source.toml", self.api.writes)

    def test_concurrent_feed_change_blocks_signature_publication(self):
        record = self.write_signed_record()
        with self.assertRaisesRegex(ValueError, "Catalog changed before signature"):
            self.publish_fixture(changed=True)
        self.assertNotIn(record["signature_path"], self.api.writes)
        self.assertNotIn("FantasyTale/source.toml", self.api.writes)

    def test_archive_phase_requires_the_matching_public_signature(self):
        self.write_signed_record()
        self.write_portable()
        with self.assertRaisesRegex(ValueError, "matching signed incremental"):
            self.publish_fixture(phase="archive", published=True)
        self.assertEqual([], self.api.writes)

    def test_signature_path_cannot_overwrite_the_manual_archive(self):
        record = self.write_signed_record()
        self.write_portable()
        record["signature_path"] = "FantasyTale/portable/latest.zip"
        (self.root / ".fantasytale-signature.json").write_text(json.dumps(record))
        with patch("upload_alist.AList") as connect, self.assertRaisesRegex(ValueError, "conflicts with release content"):
            with patch("sys.argv", ["upload", "--input", str(self.root), "--base-url", "https://files.example/"]):
                main()
        connect.assert_not_called()

    def test_signature_upload_failure_never_publishes_the_feed(self):
        record = self.write_signed_record()
        original = self.publish_fixture
        with patch("upload_alist.verify_parallel", lambda items, operation, workers: ()):
            with self.assertRaisesRegex(ValueError, "Fixture rejected upload"):
                original(fail=True)
        self.assertNotIn("FantasyTale/source.toml", self.api.writes)
        self.assertNotIn(record["signature_path"], self.api.writes)

    def test_signed_artifact_publication_over_real_http_keeps_authentication_off_downloads(self):
        record = self.write_signed_record()
        writes, storage = self.publish_over_real_http()
        self.assertEqual(["FantasyTale/files/" + self.digest, record["signature_path"], record["public_key_path"], "FantasyTale/source.toml", "FantasyTale/preview/current.toml"], writes)

    def publish_over_real_http(self):
        storage, directories, writes, public_headers = {}, set(), [], []
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def reply(self, content):
                self.send_response(200)
                self.send_header("Content-Length", str(len(content)))
                self.end_headers()
                self.wfile.write(content)

            def json_reply(self, data):
                self.reply(json.dumps(data).encode())

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                if self.path == "/api/auth/login":
                    self.json_reply({"code": 200, "data": {"token": "fixture-admin"}})
                elif self.path == "/api/fs/mkdir":
                    directories.add(body["path"].lstrip("/"))
                    self.json_reply({"code": 200})
                elif self.path == "/api/fs/get":
                    path = body["path"].lstrip("/")
                    if path in storage:
                        self.json_reply({"code": 200, "data": {"size": len(storage[path]), "raw_url": f"http://127.0.0.1:{self.server.server_port}/raw/" + quote(path)}})
                    elif path in directories:
                        self.json_reply({"code": 200, "data": {"is_dir": True}})
                    else:
                        self.json_reply({"code": 404})
                else:
                    self.send_error(404)

            def do_PUT(self):
                if self.headers.get("Authorization") != "fixture-admin":
                    self.send_error(403)
                    return
                path = unquote(self.headers["File-Path"]).lstrip("/")
                storage[path] = self.rfile.read(int(self.headers["Content-Length"]))
                writes.append(path)
                self.json_reply({"code": 200})

            def do_GET(self):
                public_headers.append(self.headers.get("Authorization"))
                self.reply(storage[unquote(self.path.removeprefix("/raw/"))])

        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            arguments = ["upload", "--input", str(self.root), "--base-url", f"http://127.0.0.1:{server.server_port}"]
            with patch("sys.argv", arguments), patch.dict("os.environ", {"FT_ALIST_USERNAME": "fixture", "FT_ALIST_PASSWORD": "fixture", "NO_PROXY": "127.0.0.1", "no_proxy": "127.0.0.1"}), contextlib.redirect_stdout(io.StringIO()):
                main()
            for path in writes:
                self.assertEqual((self.root / path).read_bytes(), storage[path])
            self.assertTrue(public_headers)
            self.assertTrue(all(value is None for value in public_headers))
            return writes, storage
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)

    def write_server_profile(self, profile_only=False, inline=False):
        advertisement = '\n[[servers]]\nid="Community_Server"\ngame="fixture"\nname="Community"\napi_url="https://game.example/"\nbuilds=["kr-1"]\n'
        path = "servers/Community_Server.toml"
        if not inline:
            advertisement += f'profile="{path}"\n'
            (self.root / "servers").mkdir(exist_ok=True)
            (self.root / path).write_text('schema=1\n[server]\nid="Community_Server"\ngame="fixture"\nname="Complete introduction"\napi_url="https://game.example/"\nbuilds=["kr-1"]\n', encoding="utf-8")
        feed = self.root / "FantasyTale/source.toml"
        packages = '' if profile_only else feed.read_text()
        feed.write_text('schema=2\n[catalog]\nid="fixture"\nname={en="Fixture"}\n' + packages + advertisement, encoding="utf-8")
        return path

    def test_server_profile_is_published_after_packages_and_before_catalog(self):
        path = self.write_server_profile()
        self.publish_fixture()
        self.assertEqual(["FantasyTale/files/" + self.digest, self.manifest, path, "FantasyTale/source.toml"], self.api.writes)

    def test_profile_only_and_inline_only_directories_publish_without_dummy_packages(self):
        path = self.write_server_profile(profile_only=True)
        self.publish_fixture()
        self.assertEqual([path, "FantasyTale/source.toml"], self.api.writes)
        self.write_server_profile(profile_only=True, inline=True)
        self.publish_fixture()
        self.assertEqual(["FantasyTale/source.toml"], self.api.writes)

    def test_profile_can_update_an_existing_profile_under_its_observed_baseline(self):
        path = self.write_server_profile(profile_only=True)
        self.publish_fixture(remote_archives={path: "previous-profile"})
        self.assertEqual([path, "FantasyTale/source.toml"], self.api.writes)
        self.assertEqual(hashlib.sha256((self.root / path).read_bytes()).hexdigest(), self.api.remote[path])

    def test_concurrent_profile_change_does_not_overwrite_profile_or_catalog(self):
        path = self.write_server_profile()
        with self.assertRaisesRegex(ValueError, "Server profile artifact changed"):
            self.publish_fixture(mutate_after_write=("FantasyTale/files/" + self.digest, path))
        self.assertNotIn(path, self.api.writes)
        self.assertNotIn("FantasyTale/source.toml", self.api.writes)

    def test_local_profile_change_during_payload_upload_preserves_published_catalog(self):
        path = self.write_server_profile()
        with self.assertRaisesRegex(ValueError, "Local server profile artifact changed"):
            self.publish_fixture(local_mutation=("FantasyTale/files/" + self.digest, path))
        self.assertNotIn(path, self.api.writes)
        self.assertNotIn("FantasyTale/source.toml", self.api.writes)

    def test_earlier_profile_change_during_later_profile_upload_preserves_catalog(self):
        first = self.write_server_profile(profile_only=True)
        second = "servers/Second_Server.toml"
        (self.root / second).write_text((self.root / first).read_text().replace("Community_Server", "Second_Server").replace("game.example", "second.example"))
        feed = self.root / "FantasyTale/source.toml"
        advertisement = feed.read_text().split("[[servers]]", 1)[1]
        feed.write_text(feed.read_text() + "\n[[servers]]" + advertisement.replace("Community_Server", "Second_Server").replace("game.example", "second.example"))
        with self.assertRaisesRegex(ValueError, "metadata changed before catalog"):
            self.publish_fixture(mutate_after_write=(second, first))
        self.assertEqual([first, second], self.api.writes)

    def test_missing_server_profile_is_rejected_before_authentication(self):
        path = self.write_server_profile()
        (self.root / path).unlink()
        with patch("upload_alist.AList") as connect, self.assertRaises(FileNotFoundError):
            with patch("sys.argv", ["upload", "--input", str(self.root), "--base-url", "https://files.example/"]):
                main()
        connect.assert_not_called()

    def test_catalog_change_during_profile_preflight_is_rejected_before_authentication(self):
        self.write_server_profile(profile_only=True)
        def mutate(*args):
            profiles = server_profiles(*args)
            feed = self.root / "FantasyTale/source.toml"
            feed.write_text(feed.read_text().replace("game.example", "other.example"))
            return profiles
        with patch("upload_alist.server_profiles", side_effect=mutate), patch("upload_alist.AList") as connect:
            with patch("sys.argv", ["upload", "--input", str(self.root), "--base-url", "https://files.example/"]):
                with self.assertRaisesRegex(ValueError, "catalog changed during preflight"):
                    main()
        connect.assert_not_called()

    def test_server_profile_publication_over_real_http_serves_exact_toml_before_catalog(self):
        path = self.write_server_profile(profile_only=True)
        writes, storage = self.publish_over_real_http()
        self.assertEqual([path, "FantasyTale/source.toml"], writes)
        self.assertEqual((self.root / path).read_bytes(), storage[path])

    def test_local_feed_mutation_during_upload_is_rejected_before_feed_write(self):
        self.write_aliases()
        with self.assertRaisesRegex(ValueError, "Local catalog changed"):
            self.publish_fixture(local_mutation=("FantasyTale/files/" + self.digest, "FantasyTale/source.toml"))
        self.assertNotIn("FantasyTale/source.toml", self.api.writes)

    def test_changed_alias_is_refused_before_any_remote_write(self):
        self.write_aliases()
        (self.root / "FantasyTale/preview/current.toml").write_bytes(b"tampered")
        with patch("upload_alist.AList") as connect, self.assertRaisesRegex(ValueError, "exact canonical"):
            self.publish_fixture()
        connect.assert_not_called()

    def test_concurrent_legacy_feed_change_is_not_overwritten(self):
        self.write_aliases()
        with self.assertRaisesRegex(ValueError, "changed during upload"):
            self.publish_fixture(changed_alias=True)
        self.assertNotIn("FantasyTale/preview/current.toml", self.api.writes)

    def test_alias_escape_duplicates_and_content_conflicts_are_rejected(self):
        for aliases in (["../outside"], ["FantasyTale/preview/current.toml"] * 2,
                        ["FantasyTale/files/" + self.digest], ["FantasyTale/objects/index.toml"]):
            with self.subTest(aliases=aliases):
                self.write_aliases(aliases)
                with self.assertRaises(ValueError):
                    feed_aliases(self.root, "FantasyTale/source.toml", graph(self.root, "FantasyTale/source.toml"))

    def test_alias_metadata_for_changed_index_is_rejected(self):
        self.write_aliases()
        with (self.root / "FantasyTale/source.toml").open("a") as stream:
            stream.write("\n# changed\n")
        with self.assertRaisesRegex(ValueError, "another index"):
            feed_aliases(self.root, "FantasyTale/source.toml", graph(self.root, "FantasyTale/source.toml"))

    def write_portable(self):
        self.write_aliases()
        archive = self.root / "FantasyTale/portable/latest.zip"
        archive.parent.mkdir(exist_ok=True)
        archive.write_bytes(b"portable fixture")
        metadata = self.root / ".fantasytale-feed-aliases.json"
        record = json.loads(metadata.read_text())
        record["manual_download"] = {"path": "FantasyTale/portable/latest.zip", "sha256": hashlib.sha256(archive.read_bytes()).hexdigest(), "size": archive.stat().st_size}
        metadata.write_text(json.dumps(record))

    def test_incremental_discovery_and_aliases_are_published_before_manual_archive(self):
        self.write_portable()
        self.publish_fixture()
        self.assertEqual(["FantasyTale/files/" + self.digest, "FantasyTale/source.toml", "FantasyTale/preview/current.toml", "FantasyTale/portable/latest.zip",
                          "FantasyTale/source.toml.archive.toml", "FantasyTale/preview/current.toml.archive.toml"], self.api.writes)
        companion = tomllib.loads((self.root / "FantasyTale/source.toml.archive.toml").read_text())
        self.assertEqual(self.api.remote["FantasyTale/source.toml"], companion["manifest_sha256"])
        self.assertEqual(self.api.remote["FantasyTale/portable/latest.zip"], companion["files"][0]["sha256"])
        self.assertEqual("zip", companion["format"])

    def test_incremental_phase_never_uploads_manual_archive(self):
        self.write_portable()
        self.publish_fixture(phase="incremental")
        self.assertEqual(["FantasyTale/files/" + self.digest, "FantasyTale/source.toml", "FantasyTale/preview/current.toml"], self.api.writes)

    def test_other_archive_formats_publish_all_volumes_before_fallback_metadata(self):
        for format_name in ("zip", "7z", "rar"):
            with self.subTest(format=format_name):
                self.write_portable()
                record_path = self.root / ".fantasytale-feed-aliases.json"
                record = json.loads(record_path.read_text())
                volumes = []
                for number in (1, 2):
                    path = f"FantasyTale/portable/launcher.{format_name}.{number:03}"
                    content = f"volume {number}".encode()
                    (self.root / path).write_bytes(content)
                    volumes.append({"path": path, "size": len(content), "sha256": hashlib.sha256(content).hexdigest()})
                record["manual_download"] = {"format": format_name, "volumes": volumes}
                record_path.write_text(json.dumps(record))
                self.publish_fixture()
                companion_path = "FantasyTale/source.toml.archive.toml"
                companion = tomllib.loads((self.root / companion_path).read_text())
                self.assertEqual(format_name, companion["format"])
                self.assertEqual([1, 2], [row["volume"] for row in companion["files"]])
                self.assertEqual([row["path"] for row in volumes], [row["source_path"] for row in companion["files"]])
                for row in volumes:
                    self.assertLess(self.api.writes.index(row["path"]), self.api.writes.index(companion_path))

    def test_archive_volumes_keep_distinct_baselines_and_reject_later_mutation(self):
        self.write_portable()
        record_path = self.root / ".fantasytale-feed-aliases.json"
        record = json.loads(record_path.read_text())
        volumes = []
        for number in (1, 2):
            path = f"FantasyTale/portable/launcher.zip.{number:03}"
            content = f"volume {number}".encode()
            (self.root / path).write_bytes(content)
            volumes.append({"path": path, "size": len(content), "sha256": hashlib.sha256(content).hexdigest()})
        record["manual_download"] = {"format": "zip", "volumes": volumes}
        record_path.write_text(json.dumps(record))
        first, second = [row["path"] for row in volumes]
        previous = {first: "previous-first", second: "previous-second"}
        self.publish_fixture(phase="archive", published=True, remote_archives=previous)
        self.assertIn(second, self.api.writes)
        with self.assertRaisesRegex(ValueError, "changed during upload"):
            self.publish_fixture(phase="archive", published=True, remote_archives=previous,
                                 mutate_after_write=(first, second))
        self.assertNotIn(second, self.api.writes)
        self.assertNotIn("FantasyTale/source.toml.archive.toml", self.api.writes)

    def test_archive_phase_rejects_feed_or_fallback_changes_during_archive_upload(self):
        for target, message in (("FantasyTale/preview/current.toml", "Launcher feed changed"),
                                ("FantasyTale/source.toml.archive.toml", "Archive fallback changed")):
            with self.subTest(target=target):
                self.write_portable()
                feed = (self.root / "FantasyTale/source.toml").read_bytes()
                previous = {"FantasyTale/preview/current.toml": hashlib.sha256(feed).hexdigest()}
                with self.assertRaisesRegex(ValueError, message):
                    self.publish_fixture(phase="archive", published=True, remote_archives=previous,
                                         mutate_after_write=("FantasyTale/portable/latest.zip", target))
                self.assertNotIn("FantasyTale/preview/current.toml.archive.toml", self.api.writes)

    def test_archive_phase_requires_matching_live_feed_and_never_reuploads_objects(self):
        self.write_portable()
        with self.assertRaisesRegex(ValueError, "matching incremental feed"):
            self.publish_fixture(phase="archive")
        self.assertEqual([], self.api.writes)
        self.publish_fixture(phase="archive", published=True)
        self.assertEqual(["FantasyTale/portable/latest.zip", "FantasyTale/source.toml.archive.toml", "FantasyTale/preview/current.toml.archive.toml"], self.api.writes)

    def test_committed_archive_is_verified_after_task_timeout_without_retransmission(self):
        self.write_portable()
        with patch("upload_alist.time.sleep"):
            self.publish_fixture(phase="archive", published=True, committed_timeout=True)
        self.assertEqual(["FantasyTale/portable/latest.zip", "FantasyTale/source.toml.archive.toml", "FantasyTale/preview/current.toml.archive.toml"], self.api.writes)

    def test_changed_manual_archive_is_rejected_before_connection(self):
        self.write_portable()
        (self.root / "FantasyTale/portable/latest.zip").write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "declared identity"):
            self.publish_fixture()

    def test_manual_archive_failure_preserves_verified_incremental_feed(self):
        self.write_portable()
        with self.assertRaisesRegex(ValueError, "public verification"):
            self.publish_fixture(changed_portable=True)
        self.assertIn("FantasyTale/source.toml", self.api.writes)
        self.assertIn("FantasyTale/preview/current.toml", self.api.writes)
        self.assertNotIn("FantasyTale/source.toml.archive.toml", self.api.writes)

    def test_direct_upload_bypasses_inherited_proxy(self):
        with patch.dict("os.environ", {"HTTPS_PROXY": "http://proxy.example:7890", "NO_PROXY": ""}, clear=True):
            self.publish_fixture()
            self.assertEqual("http://proxy.example:7890", self.api.proxies["https"])
            self.publish_fixture(direct=True)
            self.assertEqual({}, self.api.proxies)

    def test_catalog_is_written_after_verified_contents_and_manifest(self):
        self.publish_fixture()
        self.assertEqual(["FantasyTale/files/" + self.digest, self.manifest, "FantasyTale/source.toml"], self.api.writes)

    def test_object_failure_does_not_replace_catalog(self):
        with self.assertRaisesRegex(ValueError, "rejected upload"):
            self.publish_fixture(fail=True)
        self.assertEqual("previous", self.api.remote["FantasyTale/source.toml"])

    def test_early_failure_does_not_queue_the_entire_release(self):
        rows = []
        for index in range(12):
            content = f"object {index}".encode()
            digest = hashlib.sha256(content).hexdigest()
            (self.root / "FantasyTale/files" / digest).write_bytes(content)
            rows.append(f'[[files]]\nsource_path="FantasyTale/files/{digest}"\nsha256="{digest}"\n')
        (self.root / "FantasyTale/source.toml").write_text('product="fantasytale-launcher"\n' + ''.join(rows), encoding="utf-8")
        writes = []
        lock = threading.Lock()
        class FailingAList:
            def __init__(self, *_args):
                pass

            def read_filelist(self, path, expected_digest):
                return b'product="legacy"\n'

            def remote_hash(self, path, *, expected_size=None):
                return "previous" if path.endswith("source.toml") else None

            def put(self, path, source):
                with lock:
                    writes.append(path)
                raise ValueError("Fixture rejected upload")

        with patch("upload_alist.AList", FailingAList), patch.dict("os.environ", {"FT_ALIST_USERNAME": "fixture", "FT_ALIST_PASSWORD": "fixture"}), patch("sys.argv", ["upload", "--input", str(self.root), "--base-url", "https://files.example/", "--workers", "2"]), contextlib.redirect_stdout(io.StringIO()), self.assertRaisesRegex(ValueError, "rejected upload"):
            main()
        self.assertLessEqual(len(writes), 2)
        self.assertNotIn("FantasyTale/source.toml", writes)

    def test_concurrent_catalog_change_refuses_mutable_publication(self):
        with self.assertRaisesRegex(ValueError, "changed during upload"):
            self.publish_fixture(changed=True)
        self.assertNotIn("FantasyTale/source.toml", self.api.writes)

    def test_transient_gateway_failure_retries_with_a_fresh_operation(self):
        response = requests.Response()
        response.status_code = 502
        operation = unittest.mock.Mock(side_effect=[requests.HTTPError(response=response), "verified"])
        with patch("upload_alist.time.sleep"), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual("verified", retry_network(operation))
        self.assertEqual(2, operation.call_count)

    def test_permanent_access_failure_does_not_retry(self):
        response = requests.Response()
        response.status_code = 403
        operation = unittest.mock.Mock(side_effect=requests.HTTPError(response=response))
        with self.assertRaises(requests.HTTPError):
            retry_network(operation)
        self.assertEqual(1, operation.call_count)

    def test_gateway_non_json_api_response_is_retried(self):
        operation = unittest.mock.Mock(side_effect=[requests.exceptions.JSONDecodeError("Invalid JSON", "<html>", 0), "verified"])
        with patch("upload_alist.time.sleep"), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual("verified", retry_network(operation))
        self.assertEqual(2, operation.call_count)

    def test_transport_retry_is_bounded(self):
        operation = unittest.mock.Mock(side_effect=requests.Timeout())
        with patch("upload_alist.time.sleep"), contextlib.redirect_stdout(io.StringIO()), self.assertRaises(requests.Timeout):
            retry_network(operation)
        self.assertEqual(4, operation.call_count)

    def test_interrupted_response_retries_without_disclosing_download_url(self):
        operation = unittest.mock.Mock(side_effect=[requests.exceptions.ChunkedEncodingError("https://download.example/object?secret=fixture"), "verified"])
        output = io.StringIO()
        with patch("upload_alist.time.sleep"), contextlib.redirect_stdout(output):
            self.assertEqual("verified", retry_network(operation))
        self.assertEqual(2, operation.call_count)
        self.assertNotIn("secret", output.getvalue())
        self.assertNotIn("download.example", output.getvalue())

    def test_nested_transport_diagnostics_retain_os_code_without_request_secrets(self):
        cause = ConnectionResetError(10054, "https://download.example/object?secret=fixture")
        error = requests.ConnectionError("Bearer fixture-token", cause)
        operation = unittest.mock.Mock(side_effect=[error, "verified"])
        output = io.StringIO()
        with patch("upload_alist.time.sleep"), contextlib.redirect_stdout(output):
            self.assertEqual("verified", retry_network(operation))
        self.assertIn("ConnectionResetError errno=10054", output.getvalue())
        for secret in ("download.example", "secret", "Bearer", "fixture-token"):
            self.assertNotIn(secret, output.getvalue())

    def test_transport_diagnostic_cycles_do_not_repeat_or_hang(self):
        error = requests.ConnectionError("private URL")
        error.__cause__ = error
        self.assertEqual("ConnectionError", network_failure_summary(error))

    def test_repeated_interrupted_responses_exhaust_the_retry_limit(self):
        operation = unittest.mock.Mock(side_effect=requests.exceptions.ChunkedEncodingError())
        with patch("upload_alist.time.sleep"), contextlib.redirect_stdout(io.StringIO()), self.assertRaises(requests.exceptions.ChunkedEncodingError):
            retry_network(operation)
        self.assertEqual(4, operation.call_count)

    def test_public_connections_are_reused_per_worker_without_admin_headers(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.api = requests.Session()
        api.api.headers["Authorization"] = "fixture-admin-token"
        first = api.public_session()
        self.addCleanup(first.close)
        self.addCleanup(api.api.close)
        self.assertIs(first, api.public_session())
        self.assertNotIn("Authorization", first.headers)
        other = []
        def worker():
            session = api.public_session()
            other.append((session, api.public_session()))
        thread = threading.Thread(target=worker)
        thread.start()
        thread.join(timeout=3)
        self.assertFalse(thread.is_alive())
        self.assertEqual(1, len(other))
        self.addCleanup(other[0][0].close)
        self.assertIs(other[0][0], other[0][1])
        self.assertIsNot(first, other[0][0])
        self.assertNotIn("Authorization", other[0][0].headers)

    def test_metadata_and_verified_bytes_reuse_a_real_http_connection(self):
        ports, authorization = set(), []
        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"
            def log_message(self, *args):
                pass
            def reply(self, content):
                ports.add(self.client_address[1])
                authorization.append(self.headers.get("Authorization"))
                self.send_response(200)
                self.send_header("Content-Length", str(len(content)))
                self.end_headers()
                self.wfile.write(content)
            def do_POST(self):
                self.rfile.read(int(self.headers["Content-Length"]))
                content = json.dumps({"code": 200, "data": {"size": 7,
                    "raw_url": f"http://127.0.0.1:{self.server.server_port}/payload"}}).encode()
                self.reply(content)
            def do_GET(self):
                self.reply(b"payload")
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.verify_timeout_seconds = 900
        api.base = f"http://127.0.0.1:{server.server_port}"
        session = api.public_session()
        session.trust_env = False
        try:
            for _ in range(2):
                self.assertEqual(hashlib.sha256(b"payload").hexdigest(), api.remote_hash("object"))
            self.assertEqual(1, len(ports))
            self.assertEqual([None] * 4, authorization)
        finally:
            session.close()
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)

    def metadata_reply(self, message):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.base = "https://files.example"
        response = unittest.mock.Mock()
        response.json.return_value = {"code": 500, "message": message}
        with patch("upload_alist.requests.Session.post", return_value=response):
            return api.metadata("FantasyTale/files/fixture")

    def test_known_missing_object_is_not_a_network_failure(self):
        self.assertIsNone(self.metadata_reply("object not found"))

    def test_missing_parent_directories_allow_first_publication(self):
        self.assertIsNone(self.metadata_reply("failed get parent list: failed get dir: failed get parent list: failed get dir: object not found"))

    def test_unrecognized_wrapped_error_is_not_treated_as_absent(self):
        with self.assertRaises(requests.HTTPError):
            self.metadata_reply("authentication failed: object not found")

    def test_provider_failure_is_not_mistaken_for_a_missing_object(self):
        with self.assertRaises(requests.HTTPError):
            self.metadata_reply("provider date parsing failed")

    def test_empty_success_body_is_accepted_for_stream_upload(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.verify_timeout_seconds = 900
        api.base = "https://files.example"
        api.api = unittest.mock.Mock()
        api.api.headers = {}
        api.directories = {"FantasyTale", "FantasyTale/files"}
        api.directory_lock = threading.Lock()
        response = unittest.mock.Mock(status_code=200, text="")
        response.raise_for_status.return_value = None
        source = self.root / "FantasyTale" / "files" / "fixture"
        source.parent.mkdir(parents=True, exist_ok=True)
        source.write_bytes(b"fixture")
        with patch("upload_alist.requests.put", return_value=response):
            api.put("FantasyTale/files/fixture", source)

    def task_upload_fixture(self, size=8 * 1024 * 1024):
        api = AList.__new__(AList)
        api.verify_timeout_seconds = 900
        api.background_uploads = True
        api.base = "https://files.example"
        api.api = unittest.mock.Mock()
        api.api.headers = {}
        api.directory_lock = threading.Lock()
        api.ensure_directories = unittest.mock.Mock()
        source = self.root / "background.zip"
        with source.open("wb") as stream:
            stream.truncate(size)
        response = unittest.mock.Mock(status_code=200, text="task")
        response.json.return_value = {"code": 200, "data": {"task": {"id": "fixture-task"}}}
        return api, source, response

    @staticmethod
    def task_reply(state, identity="fixture-task"):
        response = unittest.mock.Mock()
        response.json.return_value = {"code": 200, "data": {"id": identity, "state": state, "progress": 100,
            "error": "https://private.example/?secret=token", "status": "Bearer private"}}
        return response

    def test_background_upload_waits_for_storage_success_instead_of_progress_percent(self):
        api, source, response = self.task_upload_fixture()
        api.api.post.side_effect = [self.task_reply(0), self.task_reply(1), self.task_reply(2)]
        output = io.StringIO()
        with patch("upload_alist.requests.put", return_value=response) as put, patch("upload_alist.time.sleep"), contextlib.redirect_stdout(output):
            api.put("portable/background.zip", source)
        self.assertEqual("true", put.call_args.kwargs["headers"]["As-Task"])
        self.assertEqual(1, put.call_count)
        self.assertEqual(3, api.api.post.call_count)
        self.assertEqual({}, api.upload_tasks)
        self.assertIn("running", output.getvalue())
        for secret in ("private.example", "secret", "token", "Bearer"):
            self.assertNotIn(secret, output.getvalue())

    def test_polling_failure_resumes_the_same_task_without_resending_the_archive(self):
        api, source, response = self.task_upload_fixture()
        api.api.post.side_effect = requests.ConnectionError("private URL")
        with patch("upload_alist.requests.put", return_value=response) as put, patch("upload_alist.time.sleep"), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(requests.ConnectionError):
                api.put("portable/background.zip", source)
            api.api.post.side_effect = None
            api.api.post.return_value = self.task_reply(2)
            api.put("portable/background.zip", source)
        self.assertEqual(1, put.call_count)
        self.assertEqual({}, api.upload_tasks)

    def test_failed_or_canceled_storage_tasks_cannot_count_as_a_successful_upload(self):
        for state in (3, 4, 7):
            with self.subTest(state=state):
                api, source, response = self.task_upload_fixture()
                api.api.post.return_value = self.task_reply(state)
                with patch("upload_alist.requests.put", return_value=response), contextlib.redirect_stdout(io.StringIO()), self.assertRaisesRegex(ValueError, "failed or was canceled"):
                    api.put("portable/background.zip", source)

    def test_background_task_identity_and_state_are_validated_before_acceptance(self):
        for state, identity in ((2, "another-task"), (True, "fixture-task"), (10, "fixture-task"), ("2", "fixture-task")):
            with self.subTest(state=state, identity=identity):
                api, source, response = self.task_upload_fixture()
                api.api.post.return_value = self.task_reply(state, identity)
                with patch("upload_alist.requests.put", return_value=response), contextlib.redirect_stdout(io.StringIO()), self.assertRaisesRegex(ValueError, "invalid or unavailable"):
                    api.put("portable/background.zip", source)

    def test_background_uploads_leave_small_metadata_requests_synchronous(self):
        api, source, response = self.task_upload_fixture(size=1024)
        response.text = ""
        with patch("upload_alist.requests.put", return_value=response) as put, contextlib.redirect_stdout(io.StringIO()):
            api.put("metadata.toml", source)
        self.assertEqual("false", put.call_args.kwargs["headers"]["As-Task"])
        api.api.post.assert_not_called()

    def test_expired_background_task_is_not_submitted_again_or_accepted_late(self):
        api, source, response = self.task_upload_fixture()
        identity = (str(source.resolve()), source.stat().st_size, source.stat().st_mtime_ns)
        api.upload_tasks = {"portable/background.zip": ("fixture-task", identity, 0)}
        with patch("upload_alist.requests.put") as put, patch("upload_alist.time.monotonic", return_value=901), patch("upload_alist.time.sleep"), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(requests.Timeout):
                api.put("portable/background.zip", source)
            put.assert_not_called()
            api.api.post.assert_not_called()
        api.api.post.return_value = self.task_reply(2)
        with patch("upload_alist.requests.put") as put, patch("upload_alist.time.monotonic", side_effect=[899, 901]), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(requests.Timeout):
                api.put("portable/background.zip", source)
            put.assert_not_called()
        self.assertIn("portable/background.zip", api.upload_tasks)

    def test_resuming_upload_refuses_a_changed_local_archive_before_network_access(self):
        api, source, response = self.task_upload_fixture()
        identity = (str(source.resolve()), source.stat().st_size, source.stat().st_mtime_ns)
        api.upload_tasks = {"portable/background.zip": ("fixture-task", identity, 0)}
        with source.open("ab") as stream:
            stream.write(b"changed")
        with patch("upload_alist.requests.put") as put, self.assertRaisesRegex(ValueError, "another local object"):
            api.put("portable/background.zip", source)
        put.assert_not_called()
        api.api.post.assert_not_called()

    def test_real_background_upload_tracks_task_after_disconnected_poll_without_resubmission(self):
        api, source, _ = self.task_upload_fixture()
        submitted, polls, digests = [], [], []
        payload_digest = hashlib.sha256(source.read_bytes()).hexdigest()
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass
            def reply(self, content):
                body = json.dumps(content).encode()
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            def do_PUT(self):
                submitted.append((self.path, self.headers.get("As-Task"), self.headers.get("Authorization")))
                remaining = int(self.headers["Content-Length"])
                digest = hashlib.sha256()
                while remaining:
                    chunk = self.rfile.read(min(128 * 1024, remaining))
                    if not chunk:
                        return
                    digest.update(chunk)
                    remaining -= len(chunk)
                digests.append(digest.hexdigest())
                self.reply({"code": 200, "data": {"task": {"id": "fixture-task"}}})
            def do_POST(self):
                self.rfile.read(int(self.headers.get("Content-Length", 0)))
                polls.append((self.path, self.headers.get("Authorization")))
                if len(polls) == 1:
                    self.close_connection = True
                    return
                self.reply({"code": 200, "data": {"id": "fixture-task", "state": 1 if len(polls) == 2 else 2, "progress": 100}})
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        api.base = f"http://127.0.0.1:{server.server_port}"
        api.api = requests.Session()
        api.api.trust_env = False
        api.api.headers["Authorization"] = "fixture-token"
        output = io.StringIO()
        try:
            with patch("upload_alist.time.sleep"), patch.dict("os.environ", {"NO_PROXY": "127.0.0.1"}), contextlib.redirect_stdout(output):
                api.put("portable/background.zip", source)
            self.assertEqual([("/api/fs/put", "true", "fixture-token")], submitted)
            self.assertEqual([payload_digest], digests)
            self.assertEqual([("/api/task/upload/info?tid=fixture-task", "fixture-token")] * 3, polls)
            self.assertEqual({}, api.upload_tasks)
            self.assertIn("running", output.getvalue())
            self.assertIn("succeeded", output.getvalue())
            self.assertNotIn("fixture-token", output.getvalue())
        finally:
            api.api.close()
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)

    def verify_remote(self, size, chunks, clock=None):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.verify_timeout_seconds = 900
        api.metadata = unittest.mock.Mock(return_value={"size": size, "raw_url": "https://download.example/object?secret=fixture"})
        response = unittest.mock.MagicMock()
        response.__enter__.return_value = response
        response.iter_content.return_value = iter(chunks)
        with patch("upload_alist.requests.Session.get", return_value=response) as get:
            if clock is None:
                result = api.remote_hash("FantasyTale/files/fixture")
            else:
                with patch("upload_alist.time.monotonic", side_effect=clock):
                    result = api.remote_hash("FantasyTale/files/fixture")
        return result, get.call_count

    def test_invalid_remote_size_is_refused_before_download(self):
        for size in (None, True, -1, "7"):
            with self.subTest(size=size), patch("upload_alist.requests.Session.get") as get:
                api = AList.__new__(AList)
                api.public_transport = threading.local()
                api.metadata = unittest.mock.Mock(return_value={"size": size})
                with self.assertRaisesRegex(ValueError, "invalid size"):
                    api.remote_hash("FantasyTale/files/fixture")
                get.assert_not_called()

    def test_stream_exceeding_advertised_size_is_stopped_before_next_chunk(self):
        def oversized():
            yield b"too much"
            self.fail("Oversized response must not continue reading")
        with self.assertRaisesRegex(ValueError, "advertised size"):
            self.verify_remote(1, oversized())

    def test_truncated_stream_is_refused(self):
        with self.assertRaisesRegex(ValueError, "size mismatch"):
            self.verify_remote(8, [b"short"])

    def test_trickling_stream_has_a_total_time_budget_without_url_disclosure(self):
        with self.assertRaises(requests.Timeout) as error:
            self.verify_remote(2, [b"a", b"b"], [0, 1, 901])
        self.assertNotIn("secret", str(error.exception))
        self.assertNotIn("download.example", str(error.exception))

    def test_complete_remote_stream_is_hashed(self):
        digest, calls = self.verify_remote(7, [b"pay", b"load"])
        self.assertEqual(hashlib.sha256(b"payload").hexdigest(), digest)
        self.assertEqual(1, calls)

    def test_large_archive_verification_uses_the_local_size_without_large_allocations(self):
        size = 101 * 1024**3
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.verify_timeout_seconds = 900
        api.metadata = unittest.mock.Mock(return_value={"size": size})
        response = unittest.mock.MagicMock()
        response.__enter__.return_value = response
        chunk = unittest.mock.MagicMock()
        chunk.__len__.return_value = size
        response.iter_content.return_value = iter([chunk])
        digest = unittest.mock.Mock()
        digest.hexdigest.return_value = "fixture-digest"
        with patch.object(api, "open_download", return_value=response), patch("upload_alist.hashlib.sha256", return_value=digest):
            self.assertEqual("fixture-digest", api.remote_hash("archive-part", expected_size=size))
        digest.update.assert_called_once_with(chunk)
        response.iter_content.assert_called_once_with(128 * 1024)

    def test_invalid_archive_verification_size_is_rejected_before_network_access(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.metadata = unittest.mock.Mock()
        for size in (True, -1, "7"):
            with self.subTest(size=size), self.assertRaisesRegex(ValueError, "verification size"):
                api.remote_hash("archive-part", expected_size=size)
        api.metadata.assert_not_called()

    def test_changed_remote_size_is_rejected_before_streaming(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.open_download = unittest.mock.Mock()
        for actual in (6, 8, 4 * 1024**3):
            api.metadata = unittest.mock.Mock(return_value={"size": actual})
            with self.subTest(actual=actual), self.assertRaisesRegex(ValueError, "size"):
                api.remote_hash("object", expected_size=7)
        api.open_download.assert_not_called()

    def test_expired_public_url_renews_once_without_api_authentication(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        old = {"size": 7, "raw_url": "https://download.example/old"}
        renewed = {"size": 7, "raw_url": "https://download.example/new"}
        api.metadata = unittest.mock.Mock(return_value=renewed)
        rejected = requests.Response(); rejected.status_code = 403; rejected.raw = io.BytesIO()
        accepted = requests.Response(); accepted.status_code = 200; accepted.raw = io.BytesIO()
        with patch("upload_alist.requests.Session.get", side_effect=[rejected, accepted]) as get:
            self.assertIs(accepted, api.open_download("object", old, stream=True))
        api.metadata.assert_called_once_with("object", refresh=True)
        self.assertEqual([old["raw_url"], renewed["raw_url"]], [call.args[0] for call in get.call_args_list])
        self.assertTrue(all("Authorization" not in call.kwargs.get("headers", {}) for call in get.call_args_list))

    def test_public_url_renewal_rejects_changed_size_and_does_not_loop(self):
        for scenario in ("changed", "unchanged", "second-rejection"):
            with self.subTest(scenario=scenario):
                api = AList.__new__(AList)
                api.public_transport = threading.local()
                old = {"size": 7, "raw_url": "https://download.example/old"}
                renewed = {"size": 8 if scenario == "changed" else 7,
                           "raw_url": old["raw_url"] if scenario == "unchanged" else "https://download.example/new"}
                api.metadata = unittest.mock.Mock(return_value=renewed)
                rejected = unittest.mock.Mock(status_code=403)
                rejected.raise_for_status.side_effect = requests.HTTPError("Forbidden")
                with patch("upload_alist.requests.Session.get", return_value=rejected) as get:
                    with self.assertRaises(ValueError if scenario == "changed" else requests.HTTPError):
                        api.open_download("object", old, stream=True)
                self.assertEqual(2 if scenario == "second-rejection" else 1, get.call_count)
                api.metadata.assert_called_once_with("object", refresh=True)

    def test_verification_budget_is_validated_before_authentication(self):
        for budget in (True, 0, 29, 3601):
            with self.subTest(budget=budget), patch("upload_alist.requests.Session") as session:
                with self.assertRaisesRegex(ValueError, "Verification timeout"):
                    AList("https://files.example", "fixture", "fixture", budget)
                session.assert_not_called()

    def test_invalid_provider_urls_are_refused_before_download(self):
        for url in (None, 1, "file:///secret", "https://user:password@download.example/object", "https:///object"):
            with self.subTest(url=url), patch("upload_alist.requests.Session.get") as get:
                api = AList.__new__(AList)
                api.public_transport = threading.local()
                with self.assertRaisesRegex(ValueError, "invalid raw download URL"):
                    api.open_download("object", {"size": 7, "raw_url": url})
                get.assert_not_called()

    def test_mutable_archive_metadata_stamp_never_reads_provider_bytes(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.metadata = unittest.mock.Mock(return_value={"size": 7, "hash_info": {"md5": "A" * 32}})
        api.remote_hash = unittest.mock.Mock()
        self.assertEqual(("provider-md5", 7, "a" * 32), api.change_stamp("archive"))
        api.remote_hash.assert_not_called()
        api.metadata.assert_called_once_with("archive", refresh=True)

    def test_completed_archive_upload_retry_requires_sha256_before_reusing_bytes(self):
        self.write_aliases()
        self.write_portable()
        self.publish_fixture(same_portable=True)
        self.assertNotIn("FantasyTale/portable/latest.zip", self.api.writes)

    def test_matching_provider_md5_cannot_accept_corrupt_archive_bytes(self):
        self.write_aliases()
        self.write_portable()
        with self.assertRaisesRegex(ValueError, "public verification"):
            self.publish_fixture(same_portable=True, corrupt_portable=True)
        self.assertIn("FantasyTale/source.toml", self.api.writes)
        self.assertNotIn("FantasyTale/portable/latest.zip", self.api.writes)

    def test_mutable_archive_without_provider_digest_falls_back_to_verified_bytes(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.metadata = unittest.mock.Mock(return_value={"size": 7, "hash_info": {"md5": "invalid"}})
        api.remote_hash = unittest.mock.Mock(return_value="b" * 64)
        self.assertEqual(("sha256", "b" * 64), api.change_stamp("archive"))
        api.remote_hash.assert_called_once_with("archive")

    def test_missing_mutable_archive_needs_no_download(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.metadata = unittest.mock.Mock(return_value=None)
        api.remote_hash = unittest.mock.Mock()
        self.assertIsNone(api.change_stamp("archive"))
        api.remote_hash.assert_not_called()

    def test_range_headers_survive_one_expired_url_renewal(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.metadata = unittest.mock.Mock(return_value={"size": 7, "raw_url": "https://download.example/new"})
        rejected = requests.Response(); rejected.status_code = 403; rejected.raw = io.BytesIO()
        accepted = requests.Response(); accepted.status_code = 206; accepted.raw = io.BytesIO()
        options = {"headers": {"Range": "bytes=0-6"}, "stream": True, "timeout": (20, 60)}
        with patch("upload_alist.requests.Session.get", side_effect=[rejected, accepted]) as get:
            self.assertIs(accepted, api.open_download("object", {"size": 7, "raw_url": "https://download.example/old"}, **options))
        self.assertTrue(all(call.kwargs == options for call in get.call_args_list))

    def test_malformed_renewed_provider_url_is_not_requested(self):
        api = AList.__new__(AList)
        api.public_transport = threading.local()
        api.metadata = unittest.mock.Mock(return_value={"size": 7, "raw_url": "file:///secret"})
        rejected = requests.Response(); rejected.status_code = 403; rejected.raw = io.BytesIO()
        with patch("upload_alist.requests.Session.get", return_value=rejected) as get:
            with self.assertRaisesRegex(ValueError, "invalid raw download URL"):
                api.open_download("object", {"size": 7, "raw_url": "https://download.example/old"})
        self.assertEqual(1, get.call_count)


if __name__ == "__main__":
    unittest.main()
