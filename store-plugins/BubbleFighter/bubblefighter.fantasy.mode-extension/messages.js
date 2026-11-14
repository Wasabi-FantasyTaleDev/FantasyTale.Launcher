/** The mode picker and its native entry button share the same localized title. */
export const messages = {
  'zh-CN':{restartRequired:"无法确认模式应用结果或恢复连接。请重新启动游戏后再试。",selectedCaption:'（已选择{mode}）',hidden:'隐藏模式扩展',title:'模式扩展',confirm:'确认',cancel:'取消',close:'关闭',scroll:'模式列表',previous:'向上',next:'向下',failed:'未能应用模式，请重新打开创建房间窗口后重试。',unavailable:'当前客户端暂不支持原生模式扩展入口。'},
  'zh-TW':{restartRequired:"無法確認模式套用結果或恢復連線。請重新啟動遊戲後再試。",selectedCaption:'（已選擇{mode}）',hidden:'隱藏模式擴充',title:'模式擴充',confirm:'確認',cancel:'取消',close:'關閉',scroll:'模式列表',previous:'向上',next:'向下',failed:'無法套用模式，請重新開啟建立房間視窗後再試。',unavailable:'目前用戶端暫不支援原生模式擴充入口。'},
  ko:{restartRequired:"모드 적용 결과를 확인하거나 연결을 복구할 수 없습니다. 게임을 다시 시작한 뒤 시도해 주세요.",selectedCaption:' ({mode} 선택됨)',hidden:'숨겨진 모드 확장',title:'모드 확장',confirm:'확인',cancel:'취소',close:'닫기',scroll:'모드 목록',previous:'위로',next:'아래로',failed:'모드를 적용하지 못했습니다. 방 만들기 창을 다시 열고 시도해 주세요.',unavailable:'현재 클라이언트는 기본 모드 확장 버튼을 지원하지 않습니다.'},
  en:{restartRequired:"The mode change could not be confirmed or the connection could not be restored. Restart the game before trying again.",selectedCaption:' (Selected: {mode})',hidden:'Hidden mode extension',title:'Mode Extension',confirm:'Confirm',cancel:'Cancel',close:'Close',scroll:'Modes',previous:'Scroll up',next:'Scroll down',failed:'The mode could not be applied. Reopen the room creation window and try again.',unavailable:'This client does not yet support the native Mode Extension button.'},
  id:{restartRequired:"Perubahan mode tidak dapat dipastikan atau koneksi tidak dapat dipulihkan. Mulai ulang game sebelum mencoba lagi.",selectedCaption:' (Dipilih: {mode})',hidden:'Ekstensi mode tersembunyi',title:'Ekstensi Mode',confirm:'Konfirmasi',cancel:'Batal',close:'Tutup',scroll:'Daftar mode',previous:'Gulir atas',next:'Gulir bawah',failed:'Mode tidak dapat diterapkan. Buka kembali jendela pembuatan ruangan dan coba lagi.',unavailable:'Klien ini belum mendukung tombol Ekstensi Mode bawaan.'},
  vi:{restartRequired:"Không thể xác nhận thay đổi chế độ hoặc khôi phục kết nối. Hãy khởi động lại trò chơi rồi thử lại.",selectedCaption:' (Đã chọn: {mode})',hidden:'Mở rộng chế độ ẩn',title:'Mở rộng chế độ',confirm:'Xác nhận',cancel:'Hủy',close:'Đóng',scroll:'Danh sách chế độ',previous:'Cuộn lên',next:'Cuộn xuống',failed:'Không thể áp dụng chế độ. Hãy mở lại cửa sổ tạo phòng rồi thử lại.',unavailable:'Máy khách này chưa hỗ trợ nút Mở rộng chế độ trong giao diện gốc.'},
  th:{restartRequired:"ไม่สามารถยืนยันการเปลี่ยนโหมดหรือกู้คืนการเชื่อมต่อได้ โปรดเริ่มเกมใหม่ก่อนลองอีกครั้ง",selectedCaption:' (เลือก: {mode})',hidden:'ส่วนขยายโหมดที่ซ่อนอยู่',title:'โหมดเพิ่มเติม',confirm:'ยืนยัน',cancel:'ยกเลิก',close:'ปิด',scroll:'รายการโหมด',previous:'เลื่อนขึ้น',next:'เลื่อนลง',failed:'ไม่สามารถใช้โหมดนี้ได้ โปรดเปิดหน้าต่างสร้างห้องใหม่แล้วลองอีกครั้ง',unavailable:'ไคลเอนต์นี้ยังไม่รองรับปุ่มโหมดเพิ่มเติมใน UI ดั้งเดิม'},
};

/** Resolve the exact catalogue language, then a supported base language. */
export function languageFor(locale) {
  const requested = String(locale ?? '').toLowerCase();
  return Object.keys(messages).find(key => key.toLowerCase() === requested)
    ?? Object.keys(messages).find(key => key === requested.split('-')[0])
    ?? 'en';
}

export function labelsFor(locale) { return messages[languageFor(locale)]; }
