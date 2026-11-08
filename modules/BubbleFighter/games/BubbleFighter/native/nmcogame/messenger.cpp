#include "messenger.h"

#include <cstdio>
#include <cstring>

namespace bubblefighter::nmco {

std::wstring Identity::passport() const {
    // Launcher tickets use the same application marker as the GameWidget bridge;
    // an unmarked random ticket would be interpreted as a new native account.
    if (!ticket.empty()) return L"bff-ticket:" + ticket;
    if (!typed.empty()) return typed;
    if (!launcher.empty()) return launcher;
    if (!environment.empty()) return environment;
    return L"guest";
}

bool decrypt_v1(const uint8_t* data, uint32_t size, std::vector<uint8_t>& plain) {
    if (size < 10) return false;
    const uint32_t body = size - 2;
    std::vector<uint8_t> reversed(body);
    for (uint32_t i = 0; i < body; ++i) reversed[i] = data[body - 1 - i];
    const uint8_t key = reversed[3];
    plain.resize(body - 8);
    uint8_t previous = 0;
    for (uint32_t i = 0; i < plain.size(); ++i) {
        const uint8_t cipher = reversed[8 + i];
        plain[i] = static_cast<uint8_t>(previous ^ key ^ cipher);
        previous = cipher;
    }
    return true;
}

bool Reader::u32(uint32_t& value) {
    if (pos + 4 > size) return false;
    std::memcpy(&value, data + pos, 4);
    pos += 4;
    return true;
}

// Length prefix mirrors the writer kr-812 sub_9024D0: the low two bits pick the width.
bool Reader::wstr(std::wstring& value) {
    if (pos >= size) return false;
    uint32_t count = 0;
    const uint8_t tag = data[pos] & 3;
    if (tag == 0) {
        count = data[pos] >> 2;
        pos += 1;
    } else if (tag == 1) {
        if (pos + 2 > size) return false;
        uint16_t raw = 0;
        std::memcpy(&raw, data + pos, 2);
        count = raw >> 2;
        pos += 2;
    } else if (tag == 2) {
        if (pos + 4 > size) return false;
        uint32_t raw = 0;
        std::memcpy(&raw, data + pos, 4);
        count = raw >> 2;
        pos += 4;
    } else {
        if (pos + 5 > size) return false;
        std::memcpy(&count, data + pos + 1, 4);
        pos += 5;
    }
    if (count > (size - pos) / 2) return false;
    value.assign(reinterpret_cast<const wchar_t*>(data + pos), count);
    pos += count * 2;
    return true;
}

void Writer::u32(uint32_t value) {
    const auto* bytes = reinterpret_cast<const uint8_t*>(&value);
    out.insert(out.end(), bytes, bytes + 4);
}

void Writer::wstr(const std::wstring& value) {
    const uint32_t count = static_cast<uint32_t>(value.size());
    if (count < 0x40) {
        out.push_back(static_cast<uint8_t>(count << 2));
    } else if (count < 0x4000) {
        const uint16_t raw = static_cast<uint16_t>((count << 2) | 1);
        out.insert(out.end(), reinterpret_cast<const uint8_t*>(&raw), reinterpret_cast<const uint8_t*>(&raw) + 2);
    } else {
        u32((count << 2) | 2);
    }
    const auto* bytes = reinterpret_cast<const uint8_t*>(value.data());
    out.insert(out.end(), bytes, bytes + count * 2);
}

std::vector<uint8_t> build_reply(const std::vector<uint8_t>& body) {
    Writer object;
    object.u32(kObjectHeader);
    object.u32(static_cast<uint32_t>(body.size()));
    object.out.insert(object.out.end(), body.begin(), body.end());
    Writer envelope;
    envelope.u32(kCryptorV0Id);
    envelope.u32(static_cast<uint32_t>(object.out.size()));
    envelope.out.insert(envelope.out.end(), object.out.begin(), object.out.end());
    envelope.u32(kCryptorV0Tag);
    return envelope.out;
}

bool open_request(const uint8_t* envelope, std::vector<uint8_t>& object) {
    if (envelope == nullptr) return false;
    uint32_t cryptor = 0, length = 0;
    std::memcpy(&cryptor, envelope, 4);
    std::memcpy(&length, envelope + 4, 4);
    if (length > kMaxRequestBytes) return false;
    if (cryptor == kCryptorV1Id) return decrypt_v1(envelope + 8, length, object);
    if (cryptor == kCryptorV0Id) {
        object.assign(envelope + 8, envelope + 8 + length);
        return true;
    }
    return false;
}

// Reply bodies follow each class's DeSerialize: the base (kr-812 sub_91D340) reads the call
// result into this[10]; the wrapper returns it, so 1 means the call succeeded.
bool answer(uint32_t code, const std::vector<uint8_t>& request, Identity& identity, std::vector<uint8_t>& body, std::string& note) {
    Writer reply;
    switch (code) {
    case kSetLocale:
    case kInitialize:
    case kRegisterCallback:
    case kGameLogInitialize:
        reply.u32(1);
        break;
    case kLoginMessenger:
        // tw-454 CNMLoginMessengerFunc: DeSerialize tw-454 sub_B87190 reads only the base call
        // result (tw-454 sub_B86990 -> this[10]); the caller tw-454 sub_A7FD30 ignores the return.
        reply.u32(1);
        break;
    case kLoginAuth: {
        // CNMLoginAuthFunc request (kr-812 sub_91CA60): header, u32 call id, u32 kind, wstr
        // passport, wstr id, wstr password. Kind 1 is the web-launch passport login (kr-812
        // sub_91C830, passport is argv[1]); kind 2 is the login dialog (kr-812 sub_91C8D0).
        // Reply (kr-812 sub_91CC50): u32 result, u32 auth result (0 = accepted), u32.
        Reader reader{request.data(), static_cast<uint32_t>(request.size()), 8};
        uint32_t call = 0, kind = 0;
        std::wstring passport, id;
        if (!reader.u32(call) || !reader.u32(kind) || !reader.wstr(passport) || !reader.wstr(id)) {
            note = "login-auth request unreadable";
            return false;
        }
        identity.typed = kind == 1 ? passport : id;
        reply.u32(1);
        reply.u32(0);
        reply.u32(0);
        char text[32];
        std::snprintf(text, sizeof text, "login-auth kind=%u", kind);
        note = text;
        break;
    }
    case kGetNexonPassport:
        // kr-812 sub_916960: u32 result, wstr passport. The client sends the passport as the
        // login id in gcpReqLogin (kr-812 sub_8FE2E0 -> sub_681500).
        reply.u32(1);
        reply.wstr(identity.passport());
        note = identity.ticket.empty() ? "passport provided" : "ticket provided";
        break;
    case kGetNxHwid:
        // kr-812 sub_916B10: u32 result, wstr hardware id (at most 64 characters).
        reply.u32(1);
        reply.wstr(L"STANDIN");
        break;
    default:
        return false;
    }
    body = reply.out;
    return true;
}

} // namespace bubblefighter::nmco
