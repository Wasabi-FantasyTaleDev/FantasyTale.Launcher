#pragma once
// Nexon messenger (NMCO) request/reply codec and the stand-in's answers.
// Codes, envelope layout, stream cipher and field order come from the kr-812 corpus:
// wrapper kr-812 sub_91DE10, envelope kr-812 sub_902140 / sub_902020, cryptors
// CStreamCryptor_v0/_v1 (kr-812 sub_901C10 / sub_901D10), object header kr-812 sub_902B50 /
// sub_902BF0, string codec kr-812 sub_9025E0.
#include <cstdint>
#include <string>
#include <vector>

namespace bubblefighter::nmco {

// Function codes (kr-812 constructor sites).
constexpr uint32_t kRegisterCallback = 0x1002;  // kr-812 sub_91E9E0
constexpr uint32_t kLoginMessenger = 0x110D;    // tw-454 sub_B86580, called from tw-454 sub_A7FD30
constexpr uint32_t kLoginAuth = 0x110F;         // kr-812 sub_91C8D0
constexpr uint32_t kInitialize = 0x1111;        // kr-812 sub_91E950
constexpr uint32_t kSetLocale = 0x2104;         // kr-812 sub_91E800, sub_91E8A0
constexpr uint32_t kGetNexonPassport = 0x2105;  // kr-812 sub_91EC40
constexpr uint32_t kGetNxHwid = 0x2107;         // kr-812 sub_91ED20
constexpr uint32_t kGameLogInitialize = 0xF010; // kr-812 sub_91D3A0

// Object header: u16 0x666F at +4 and version bytes 1,1 (kr-812 sub_902B50).
constexpr uint32_t kObjectHeader = 0x666F0101;
// CStreamCryptor_v0 (identity) and _v1 ids.
constexpr uint32_t kCryptorV0Id = 2083380;
constexpr uint32_t kCryptorV0Tag = 9048478;
constexpr uint32_t kCryptorV1Id = 177783594;
constexpr uint32_t kMaxRequestBytes = 0x100000;

/// Little-endian reader over a decrypted request object.
struct Reader {
    const uint8_t* data;
    uint32_t size;
    uint32_t pos;
    bool u32(uint32_t& value);
    bool wstr(std::wstring& value);
};

/// Little-endian writer for reply bodies.
struct Writer {
    std::vector<uint8_t> out;
    void u32(uint32_t value);
    void wstr(const std::wstring& value);
};

/// Where the passport the client sends in its login request comes from.
struct Identity {
    std::wstring ticket;          ///< Launcher login ticket; wins when present.
    std::wstring launcher;        ///< Launcher account name.
    std::wstring typed;           ///< Account typed into the client's own login dialog.
    std::wstring environment;     ///< BFF_LAUNCHER_ACCOUNT, for the previous launcher scripts.

    /// The passport: marked launcher ticket, then the typed account, then the launcher account, then the
    /// environment, then "guest".
    std::wstring passport() const;
};

/// Removes the 2-byte checksum, reverses the rest and undoes the chained XOR keyed by the
/// low byte of the u32 at offset 3 (kr-812 sub_901D10).
bool decrypt_v1(const uint8_t* data, uint32_t size, std::vector<uint8_t>& plain);
/// Opens a request envelope: [u32 cryptor id][u32 length][object]; lengths over
/// kMaxRequestBytes are refused.
bool open_request(const uint8_t* envelope, std::vector<uint8_t>& object);
/// Wraps a reply body in an object header and a v0 (identity) envelope (kr-812 sub_902020).
std::vector<uint8_t> build_reply(const std::vector<uint8_t>& body);
/// Builds the stand-in's reply body for `code`. Returns false for unhandled codes.
/// `note` receives a short log description without secrets.
bool answer(uint32_t code, const std::vector<uint8_t>& request, Identity& identity, std::vector<uint8_t>& body, std::string& note);

} // namespace bubblefighter::nmco
