"""Verify that compiled capabilities cannot drift from the packaged Gateway."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location("package_gateway", Path(__file__).with_name("package-gateway.py"))
gateway = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gateway)


class GatewayCapabilitiesTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.artifacts = self.root / "artifacts"
        self.descriptor = self.root / "src/FantasyTale.Launcher.Games.BubbleFighter/gateway-capabilities.json"
        self.descriptor.parent.mkdir(parents=True)
        content = b'{"schema_version":1,"bootstrap":["fixture"],"automatic":["fixture"]}'
        self.descriptor.write_bytes(content)
        self.pin = {
            "source_commit": "1" * 40,
            "capability": "fixture-native-center",
            "runtime_version": "10.0.12",
            "extensions_version": "10.0.2",
            "capabilities_sha256": hashlib.sha256(content).hexdigest(),
            "files": [],
        }
        for runtime in ("win-x64", "win-arm64"):
            relative = runtime + "/FantasyTale.Gateway.capabilities.json"
            path = self.artifacts / relative
            path.parent.mkdir(parents=True)
            path.write_bytes(content)
            self.pin["files"].append({"path": relative, "size": len(content), "sha256": hashlib.sha256(content).hexdigest()})
        (self.artifacts / "notices").mkdir()
        for relative in ("manifest.json", "notices/manifest.json"):
            (self.artifacts / relative).write_text(json.dumps(self.pin), encoding="utf-8")
        self.module_path = patch.object(gateway, "__file__", str(self.root / "tools/package-gateway.py"))
        self.module_path.start()
        self.addCleanup(self.module_path.stop)

    def test_matching_compiled_and_architecture_descriptors_are_accepted(self):
        gateway.verify_artifacts(self.artifacts, self.pin)

    def test_checkout_whitespace_does_not_change_capabilities(self):
        content = json.loads(self.descriptor.read_text(encoding="utf-8"))
        self.descriptor.write_text(json.dumps(content, indent=2) + "\n", encoding="utf-8")
        gateway.verify_artifacts(self.artifacts, self.pin)

    def test_stale_compiled_capabilities_are_rejected(self):
        self.descriptor.write_text('{"schema_version":1,"bootstrap":[],"automatic":[]}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Embedded Gateway capabilities"):
            gateway.verify_artifacts(self.artifacts, self.pin)

    def test_individually_pinned_architectures_must_advertise_the_same_capabilities(self):
        row = self.pin["files"][1]
        content = b'{"schema_version":1,"bootstrap":["other"],"automatic":[]}'
        (self.artifacts / row["path"]).write_bytes(content)
        row.update(size=len(content), sha256=hashlib.sha256(content).hexdigest())
        with self.assertRaisesRegex(ValueError, "capabilities differ between"):
            gateway.verify_artifacts(self.artifacts, self.pin)


if __name__ == "__main__":
    unittest.main()
