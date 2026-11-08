#pragma once
// Every messenger function and event id the clients know, with the client's own class name, whether the
// stand-in answers it or not. Function ids are the codes passed to CallNMFunc; event ids are the codes in
// posted event buffers. The two are separate numeric spaces (40961 is a function and an event).
#include <cstdint>

namespace bubblefighter::nmco {

/// One named id.
struct NmName {
    uint32_t code;
    const char* name;
};

// Every CNM*Func class in the kr-812 RTTI, each with its construction site; kr-1256 uses the same ids.
inline constexpr NmName kFunctionNames[] = {
    {4098, "CNMRegisterCallbackFunc"},          // kr-812 sub_91E9E0
    {4099, "CNMResetCallbackFunc"},             // kr-812 sub_91EA70
    {4355, "CNMLoginVirtualFunc"},              // kr-812 sub_915870
    {4356, "CNMLogoutVirtualFunc"},             // kr-812 sub_91EFE0
    {4357, "CNMGetMyVirtualUserListFunc"},      // kr-812 sub_91F060
    {4360, "CNMChangeMyLevelFunc"},             // kr-812 sub_91F130
    {4362, "CNMLoginPassportFunc"},             // kr-812 sub_91C140
    {4365, "CNMLoginMessengerFunc"},            // kr-812 sub_91EE70
    {4366, "CNMLogoutMessengerFunc"},           // kr-812 sub_91EF60
    {4367, "CNMLoginAuthFunc"},                 // kr-812 sub_91C830, sub_91C8D0
    {4368, "CNMLogoutAuthFunc"},                // kr-812 sub_91EBC0
    {4369, "CNMInitializeFunc"},                // kr-812 sub_91E950
    {4371, "CNMGetGameTokenFunc"},              // kr-812 sub_91CDC0
    {8193, "CNMGetMyInfoFunc"},                 // kr-812 sub_916170
    {8202, "CNMChangeNicknameFunc"},            // kr-812 sub_9165D0
    {8452, "CNMSetLocaleFunc"},                 // kr-812 sub_91E800, sub_91E8A0
    {8453, "CNMGetNexonPassportFunc"},          // kr-812 sub_91EC40
    {8455, "CNMGetNxHWIDFunc"},                 // kr-812 sub_91ED20
    {16385, "CNMGetFriendListFunc"},            // kr-812 sub_91F5E0
    {16387, "CNMRequestNewFriendFunc"},         // kr-812 sub_91F3D0
    {16388, "CNMConfirmNewFriendFunc"},         // kr-812 sub_91F4C0
    {16389, "CNMBlockFriendFunc"},              // kr-812 sub_917840
    {16391, "CNMAddFriendToCategoryFunc"},      // kr-812 sub_917C00
    {16392, "CNMDeleteFriendFromCategoryFunc"}, // kr-812 sub_917CE0
    {16393, "CNMMoveFriendCategoryFunc"},       // kr-812 sub_918060
    {16641, "CNMAddCategoryFunc"},              // kr-812 sub_918490
    {16642, "CNMDeleteCategoryFunc"},           // kr-812 sub_91F550
    {16643, "CNMChangeCategoryNameFunc"},       // kr-812 sub_918A40
    {28675, "CNMGetMyGuildListExFunc"},         // kr-812 sub_91F930
    {28676, "CNMMonitorGuildOnlineInfoExFunc"}, // kr-812 sub_91FA30
    {32777, "CNMGetUrlExFunc"},                 // kr-812 sub_919AC0
    {32780, "CNMGetSupportGameListFunc"},       // kr-812 sub_91FE60
    {32782, "CNMGetGameFullNameFunc"},          // kr-812 sub_91FF40
    {32783, "CNMGetGameShortNameFunc"},         // kr-812 sub_920010
    {32789, "CNMExecuteNGMPatcherFunc"},        // kr-812 sub_919380
    {32791, "CNMIsNGMInstalledFunc"},           // kr-812 sub_91FCE0
    {40961, "CNMRequestChatSessionFunc"},       // kr-812 sub_91A680; kr-1256 sub_D75FA0
    {40968, "CNMRequestSessionToOtherFunc"},    // kr-812 sub_91AAB0
    {49153, "CNMGSRegisterCallbackFunc"},       // kr-812 sub_920660
    {49154, "CNMGSWantCloseFunc"},              // kr-812 sub_920700
    {49155, "CNMGSGetSessionInfoFunc"},         // kr-812 sub_91B4C0
    {49157, "CNMGSGetMemberListFunc"},          // kr-812 sub_9208E0
    {49409, "CNMCSSendChatMessageFunc"},        // kr-812 sub_9209C0; kr-1256 sub_575970
    {61456, "CNMGameLogInitializeFunc"},        // kr-812 sub_91D3A0
    {61457, "CNMGameLogFinalizeFunc"},          // kr-812 sub_920A70
    {61458, "CNMGameLogWriteStageLogFunc"},     // kr-812 sub_91D8C0
    {61459, "CNMGameLogWriteErrorLogFunc"},     // kr-812 sub_91D930
    {61600, "CNMPlayFeedWritePlayLogFunc"},     // kr-812 sub_91DBC0
};

// Every case of the kr-812 event factory (kr-812 sub_9147E0); kr-1256 names the same ids.
inline constexpr NmName kEventNames[] = {
    {4097, "CNMMessengerReplyEvent"},
    {4098, "CNMMsgConnectionClosedEvent"},
    {4099, "CNMRefreshEvent"},
    {4100, "CNMSpecialEvent"},
    {4101, "CNMRequestNewFriendEvent"},         // kr-812 sub_90D340
    {4102, "CNMServerMessageEvent"},
    {4103, "CNMCustomMessageEvent"},
    {4104, "CNMNoteInstantMsgEvent"},           // kr-812 sub_90D420
    {4105, "CNMRefreshMessageEvent"},
    {4106, "CNMFindUserReplyEvent"},
    {4107, "CNMInviteVirtualUserEvent"},
    {4108, "CNMUserInfoEvent"},                 // kr-812 sub_90DAB0
    {4109, "CNMGuildOnlineInfoEvent"},          // kr-812 sub_90DD40
    {4110, "CNMRealFriendInfoChangedEvent"},    // kr-812 sub_90E7F0
    {4111, "CNMVirtualFriendInfoChangedEvent"}, // kr-812 sub_90EBC0
    {4112, "CNMFriendInfoChangedEvent"},        // kr-812 sub_90EC40
    {4113, "CNMNotifyEvent"},
    {4114, "CNMGuildOnlineInfoExEvent"},
    {4115, "CNMGuildChatMessageEvent"},         // kr-812 sub_90DFF0
    {4116, "CNMCustomNotifyEvent"},             // kr-812 sub_90E480
    {4118, "CNMNoteInfoEvent"},                 // kr-812 sub_90E690
    {4119, "CNMAuthConnectionClosedEvent"},
    {36865, "CNMCRChatRoomCreatedEvent"},
    {36866, "CNMCRChatRoomEstablishedEvent"},
    {36867, "CNMCRChatRoomErrorEvent"},
    {36868, "CNMCRChatRoomMemberEvent"},
    {36869, "CNMCRChatRoomInfoEvent"},
    {36870, "CNMCRChatRoomMessageReceivedEvent"},
    {36871, "CNMCRChatRoomCreatedExEvent"},
    {36872, "CNMCRChatRoomEstablishedExEvent"},
    {36873, "CNMCRChatRoomInfoExEvent"},
    {40961, "CNMGSSessionCreatedEvent"},
    {40962, "CNMGSSessionEstablishedEvent"},
    {40963, "CNMGSSessionFailedEvent"},
    {40964, "CNMGSSessionClosedEvent"},
    {40965, "CNMGSSessionInfoChangedEvent"},
    {40966, "CNMGSNewMemberJoinedEvent"},       // kr-812 sub_9112B0
    {40967, "CNMGSMemberLeavedEvent"},          // kr-812 sub_911440
    {40968, "CNMGSMemberInfoChangedEvent"},     // kr-812 sub_911640
    {40969, "CNMGSSessionInfoEvent"},
    {40970, "CNMGSInviteRejectedEvent"},
    {41217, "CNMCSChatMessageReceivedEvent"},   // kr-812 sub_912060
    {41218, "CNMCSMultiChatCreatedEvent"},
    {41473, "CNMFUSFileUploadEventReceivedEvent"},
    {41729, "CNMFDSFileDownloadEventReceivedEvent"},
    {41985, "CNMWSWhiteBoardMsgReceivedEvent"},
    {41986, "CNMWSWhiteBoardAckReceivedEvent"},
    {41987, "CNMWSAssocSerialKeyEvent"},
    {45057, "CNMAttendanceEventEvent"},
    {49153, "CNMChannelErrorEvent"},
    {49154, "CNMChannelCreatedEvent"},          // kr-812 sub_9130C0
    {49155, "CNMChannelEstablishedEvent"},      // kr-812 sub_913230
    {49156, "CNMChannelInfoEvent"},             // kr-812 sub_9133D0
    {49157, "CNMChannelMemberInfoEvent"},
    {49158, "CNMChannelMemberInfoListEvent"},
    {49159, "CNMChannelMessageEvent"},
    {53249, "CNMLogReportSyncEvent"},
    {57345, "CNMP2PLoginReplyEvent"},
    {57346, "CNMP2PSendDataReplyEvent"},
    {57347, "CNMP2PMessageEvent"},
    {57348, "CNMP2PConnectionClosedEvent"},
};

namespace detail {
template <size_t N>
constexpr const char* find_name(const NmName (&table)[N], uint32_t code) {
    for (const NmName& entry : table)
        if (entry.code == code) return entry.name;
    return nullptr;
}
} // namespace detail

/// The client's class name for a function id, or null when no build defines it.
constexpr const char* function_name(uint32_t code) { return detail::find_name(kFunctionNames, code); }
/// The client's class name for an event id, or null when no build defines it.
constexpr const char* event_name(uint32_t code) { return detail::find_name(kEventNames, code); }

} // namespace bubblefighter::nmco
