"""Read bounded PE and Authenticode identification metadata without executing clients.

This is an offline identification probe, not Authenticode verification. Certificate
presence and signed timestamps are reported separately from PE linker timestamps.
"""

import argparse
import hashlib
import json
import struct
from pathlib import Path

from asn1crypto import cms, tsp

MAX_CERTIFICATE_BYTES = 16 * 1024 * 1024
MAX_SIGNATURES = 16


def read_signatures(table):
    """Decode certificate records, accepting only zero padding after DER payloads."""
    signers = []
    cursor = 0
    signature_count = 0
    while cursor < len(table):
        if len(table) - cursor < 8:
            raise ValueError("Truncated WIN_CERTIFICATE")
        size, revision, kind = struct.unpack_from("<IHH", table, cursor)
        if size < 8 or cursor + size > len(table) or revision != 0x200 or kind != 2:
            raise ValueError("Invalid or unsupported WIN_CERTIFICATE")
        signature_count += 1
        if signature_count > MAX_SIGNATURES:
            raise ValueError("Too many signatures")
        payload = table[cursor + 8:cursor + size]
        signature = cms.ContentInfo.load(payload)
        if any(payload[len(signature.dump()):]):
            raise ValueError("Nonzero data follows the signature")
        if signature["content_type"].native != "signed_data":
            raise ValueError("Certificate table is not SignedData")
        data = signature["content"]
        if len(data["signer_infos"]) > MAX_SIGNATURES:
            raise ValueError("Too many signers")
        signers.extend(signer_metadata(signer, data["certificates"]) for signer in data["signer_infos"])
        cursor += (size + 7) & ~7
    return signers


def signer_metadata(signer, certificates):
    """Match a signer to its certificate and preserve distinct timestamp kinds."""
    result = {"certificate_sha256": None, "subject": None, "signing_time": [], "timestamps": []}
    sid = signer["sid"]
    for choice in certificates:
        if choice.name != "certificate":
            continue
        certificate = choice.chosen
        if sid.name != "issuer_and_serial_number":
            continue
        identity = sid.chosen
        if identity["serial_number"].native == certificate.serial_number and identity["issuer"].dump() == certificate.issuer.dump():
            result["certificate_sha256"] = hashlib.sha256(certificate.dump()).hexdigest()
            result["subject"] = certificate.subject.human_friendly
            break
    for attribute in signer["signed_attrs"]:
        if attribute["type"].native == "signing_time":
            result["signing_time"].extend(value.native.isoformat() for value in attribute["values"])
    for attribute in signer["unsigned_attrs"]:
        if attribute["type"].native == "counter_signature":
            for counter in attribute["values"]:
                for signed_attribute in counter["signed_attrs"]:
                    if signed_attribute["type"].native == "signing_time":
                        result["timestamps"].extend({"kind": "counter_signature", "time": value.native.isoformat()} for value in signed_attribute["values"])
        elif attribute["type"].dotted == "1.3.6.1.4.1.311.3.3.1":
            for value in attribute["values"]:
                token = cms.ContentInfo.load(value.dump())
                content = token["content"]["encap_content_info"]["content"]
                info = tsp.TSTInfo.load(content.contents)
                result["timestamps"].append({"kind": "rfc3161", "time": info["gen_time"].native.isoformat()})
    return result


def inspect(path):
    """Read the candidate's headers and certificate table with explicit bounds."""
    path = Path(path)
    length = path.stat().st_size
    with path.open("rb") as stream:
        def read_at(offset, count):
            if offset < 0 or count < 0 or offset + count > length:
                raise ValueError("PE range is outside the file")
            stream.seek(offset)
            data = stream.read(count)
            if len(data) != count:
                raise ValueError("Client changed during inspection")
            return data

        dos = read_at(0, 64)
        if dos[:2] != b"MZ":
            raise ValueError("Not an MZ executable")
        pe_offset = struct.unpack_from("<I", dos, 60)[0]
        if pe_offset > 1024 * 1024:
            raise ValueError("PE header exceeds inspection bound")
        coff = read_at(pe_offset, 24)
        if coff[:4] != b"PE\0\0":
            raise ValueError("Not a PE executable")
        machine, section_count, timestamp = struct.unpack_from("<HHI", coff, 4)
        optional_size, characteristics = struct.unpack_from("<HH", coff, 20)
        optional = read_at(pe_offset + 24, optional_size)
        if len(optional) < 2:
            raise ValueError("Missing optional PE header")
        magic = struct.unpack_from("<H", optional)[0]
        directory_offset = {0x10B: 96, 0x20B: 112}.get(magic)
        if directory_offset is None or len(optional) < directory_offset:
            raise ValueError("Unsupported or truncated optional PE header")
        directory_count = struct.unpack_from("<I", optional, directory_offset - 4)[0]
        result = {"path": str(path.resolve()), "executable": path.name, "machine": f"0x{machine:04X}", "section_count": section_count,
                  "pe_timestamp": f"0x{timestamp:08X}", "is_dll": bool(characteristics & 0x2000),
                  "signature_verification": "not_performed", "signers": []}
        if directory_count <= 4:
            return result
        if len(optional) < directory_offset + 40:
            raise ValueError("Truncated certificate directory")
        certificate_offset, certificate_size = struct.unpack_from("<II", optional, directory_offset + 32)
        if certificate_offset == 0 and certificate_size == 0:
            return result
        try:
            if certificate_offset == 0 or certificate_size == 0 or certificate_size > MAX_CERTIFICATE_BYTES:
                raise ValueError("Invalid or oversized certificate table")
            result["signers"] = read_signatures(read_at(certificate_offset, certificate_size))
        except (ValueError, KeyError, TypeError) as error:
            # Unpacking can leave a stale certificate directory; retain header evidence.
            result["signature_metadata_error"] = str(error)
        return result


def main():
    """Inspect explicit files; errors are reported per candidate and fail the run."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="+")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    results = []
    for path in args.paths:
        try:
            results.append(inspect(path))
        except (OSError, ValueError, KeyError, TypeError) as error:
            results.append({"path": str(path), "error": str(error)})
    output = json.dumps(results, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(output + "\n", encoding="utf-8")
    else:
        print(output)
    return int(any("error" in result for result in results))


if __name__ == "__main__":
    raise SystemExit(main())
