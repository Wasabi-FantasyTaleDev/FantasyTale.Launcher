"""Exercise PE bounds and signature metadata without trusting synthetic signatures."""

import struct
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

from asn1crypto import cms, tsp, x509

from inspect_client import MAX_CERTIFICATE_BYTES, inspect, read_signatures


def signature(counter=False, rfc3161=False):
    signer = {"version": "v1", "sid": {"issuer_and_serial_number": {"issuer": x509.Name.build({"common_name": "Fixture"}), "serial_number": 1}},
              "digest_algorithm": {"algorithm": "sha256"}, "signature_algorithm": {"algorithm": "rsassa_pkcs1v15"}, "signature": b"not-a-valid-signature",
              "signed_attrs": [{"type": "signing_time", "values": [cms.Time({"utc_time": datetime(2015, 1, 1, tzinfo=timezone.utc)})]}], "unsigned_attrs": []}
    if counter:
        counter_signer = dict(signer)
        counter_signer["signed_attrs"] = [{"type": "signing_time", "values": [cms.Time({"utc_time": datetime(2015, 1, 2, tzinfo=timezone.utc)})]}]
        signer["unsigned_attrs"] = [{"type": "counter_signature", "values": [counter_signer]}]
    if rfc3161:
        info = tsp.TSTInfo({"version": "v1", "policy": "1.2.3", "message_imprint": {"hash_algorithm": {"algorithm": "sha256"}, "hashed_message": b"x" * 32},
                           "serial_number": 1, "gen_time": datetime(2026, 1, 1, tzinfo=timezone.utc)})
        token = cms.ContentInfo({"content_type": "signed_data", "content": {"version": "v3", "digest_algorithms": [{"algorithm": "sha256"}],
                               "encap_content_info": {"content_type": "tst_info", "content": info}, "signer_infos": []}})
        signer["unsigned_attrs"] = [{"type": "1.3.6.1.4.1.311.3.3.1", "values": [token]}]
    return cms.ContentInfo({"content_type": "signed_data", "content": {"version": "v1", "digest_algorithms": [{"algorithm": "sha256"}],
        "encap_content_info": {"content_type": "data", "content": b"fixture"}, "certificates": [], "signer_infos": [signer]}}).dump()


def certificate(payload):
    data = struct.pack("<IHH", len(payload) + 8, 0x200, 2) + payload
    return data + b"\0" * (-len(data) % 8)


def executable(table=b"", advertised_size=None):
    data = bytearray(512)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 60, 64)
    data[64:68] = b"PE\0\0"
    struct.pack_into("<HHI", data, 68, 0x14C, 1, 0x55376C47)
    struct.pack_into("<HH", data, 84, 224, 2)
    struct.pack_into("<H", data, 88, 0x10B)
    struct.pack_into("<I", data, 88 + 92, 16)
    struct.pack_into("<II", data, 88 + 96 + 32, 512 if table or advertised_size else 0, len(table) if advertised_size is None else advertised_size)
    return bytes(data) + table


class InspectionTests(unittest.TestCase):
    def probe(self, data):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "BubbleFighterU.exe"
            path.write_bytes(data)
            return inspect(path)

    def test_unsigned_unpacked_preserves_timestamp(self):
        result = self.probe(executable())
        self.assertEqual("0x55376C47", result["pe_timestamp"])
        self.assertEqual([], result["signers"])
        self.assertEqual("not_performed", result["signature_verification"])

    def test_signing_time_is_metadata_not_signature_verification(self):
        result = self.probe(executable(certificate(signature())))
        self.assertEqual(["2015-01-01T00:00:00+00:00"], result["signers"][0]["signing_time"])
        self.assertIsNone(result["signers"][0]["certificate_sha256"])
        self.assertEqual("not_performed", result["signature_verification"])

    def test_accepts_zero_padding_inside_original_certificate_record(self):
        self.assertEqual(1, len(read_signatures(certificate(signature() + b"\0" * 7))))

    def test_countersignature_time_is_distinct_from_signer_claimed_time(self):
        signer = read_signatures(certificate(signature(counter=True)))[0]
        self.assertEqual(["2015-01-01T00:00:00+00:00"], signer["signing_time"])
        self.assertEqual([{"kind": "counter_signature", "time": "2015-01-02T00:00:00+00:00"}], signer["timestamps"])

    def test_reads_rfc3161_token_time(self):
        signer = read_signatures(certificate(signature(rfc3161=True)))[0]
        self.assertEqual([{"kind": "rfc3161", "time": "2026-01-01T00:00:00+00:00"}], signer["timestamps"])

    def test_rejects_nonzero_trailing_signature_data(self):
        with self.assertRaisesRegex(ValueError, "Nonzero"):
            read_signatures(certificate(signature() + b"x"))

    def test_stale_certificate_directory_retains_header_evidence(self):
        result = self.probe(executable(b"invalid certificate bytes"))
        self.assertEqual("0x55376C47", result["pe_timestamp"])
        self.assertIn("signature_metadata_error", result)
        self.assertEqual([], result["signers"])

    def test_oversized_certificate_table_is_not_read(self):
        result = self.probe(executable(advertised_size=MAX_CERTIFICATE_BYTES + 1))
        self.assertIn("oversized", result["signature_metadata_error"])

    def test_rejects_truncated_executable(self):
        with self.assertRaisesRegex(ValueError, "outside"):
            self.probe(b"MZ")


if __name__ == "__main__":
    unittest.main()
