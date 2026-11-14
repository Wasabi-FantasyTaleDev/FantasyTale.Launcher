const weaponSubCategories = {"zh-CN": {"Rifles": "步枪", "Shotguns": "散弹枪", "Snipers": "狙击枪", "SubWeapon": "手枪", "Weapon_Rifle": "步枪", "Weapon_ShotGun": "散弹枪", "Weapon_Sniper": "狙击枪", "Pants": "下衣", "MeleeWeapon": "近距离武器", "SpecialWeapon": "特殊武器"}, "zh-TW": {"Rifles": "步槍", "Shotguns": "散彈槍", "Snipers": "狙擊槍", "SubWeapon": "手槍", "Weapon_Rifle": "步槍", "Weapon_ShotGun": "散彈槍", "Weapon_Sniper": "狙擊槍", "Pants": "下衣", "MeleeWeapon": "近距離武器", "SpecialWeapon": "特殊武器"}, "ko": {"Rifles": "라이플", "Shotguns": "샷건", "Snipers": "스나이퍼", "SubWeapon": "피스톨", "Weapon_Rifle": "라이플", "Weapon_ShotGun": "샷건", "Weapon_Sniper": "스나이퍼", "Pants": "하의", "MeleeWeapon": "근접무기", "SpecialWeapon": "특수무기"}, "en": {"Rifles": "Rifles", "Shotguns": "Shotguns", "Snipers": "Snipers", "SubWeapon": "Pistols", "Weapon_Rifle": "Rifles", "Weapon_ShotGun": "Shotguns", "Weapon_Sniper": "Snipers", "Pants": "Pants", "MeleeWeapon": "Melee", "SpecialWeapon": "Special"}, "id": {"Rifles": "Senapan", "Shotguns": "Shotgun", "Snipers": "Sniper", "SubWeapon": "Pistol", "Weapon_Rifle": "Senapan", "Weapon_ShotGun": "Shotgun", "Weapon_Sniper": "Sniper", "Pants": "Celana", "MeleeWeapon": "Jarak dekat", "SpecialWeapon": "Khusus"}, "vi": {"Rifles": "Súng trường", "Shotguns": "Shotgun", "Snipers": "Bắn tỉa", "SubWeapon": "Súng lục", "Weapon_Rifle": "Súng trường", "Weapon_ShotGun": "Shotgun", "Weapon_Sniper": "Bắn tỉa", "Pants": "Quần", "MeleeWeapon": "Cận chiến", "SpecialWeapon": "Đặc biệt"}, "th": {"Rifles": "ไรเฟิล", "Shotguns": "ลูกซอง", "Snipers": "สไนเปอร์", "SubWeapon": "ปืนพก", "Weapon_Rifle": "ไรเฟิล", "Weapon_ShotGun": "ลูกซอง", "Weapon_Sniper": "สไนเปอร์", "Pants": "กางเกง", "MeleeWeapon": "ระยะประชิด", "SpecialWeapon": "พิเศษ"}};
const subCategories = {
  en: { primary: 'Primary', secondary: 'Secondary', character: 'Characters', SetClothes: 'Outfits', Costumes: 'One-piece', Hats: 'Hats', Shirts: 'Shirts', Shoes: 'Shoes', Gloves: 'Gloves', HeadAccesory: 'Head', FrontAccesory: 'Waist', FaceAccesory: 'Face', BackAccesory: 'Back' },
  'zh-CN': { primary: '主武器', secondary: '副武器', character: '角色', SetClothes: '套装', Costumes: '连体衣', Hats: '帽子', Shirts: '上衣', Shoes: '鞋子', Gloves: '手套', HeadAccesory: '头饰', FrontAccesory: '配饰', FaceAccesory: '美容', BackAccesory: '背装' },
  'zh-TW': { primary: '主武器', secondary: '副武器', character: '角色', SetClothes: '套裝', Costumes: '連體衣', Hats: '帽子', Shirts: '上衣', Shoes: '鞋子', Gloves: '手套', HeadAccesory: '頭飾', FrontAccesory: '配飾', FaceAccesory: '美容', BackAccesory: '背裝' },
  ko: { primary: '주무기', secondary: '보조무기', character: '캐릭터', SetClothes: '세트', Costumes: '코스튬', Hats: '모자', Shirts: '상의', Shoes: '신발', Gloves: '장갑', HeadAccesory: '머리', FrontAccesory: '허리', FaceAccesory: '얼굴', BackAccesory: '등' },
  id: { primary: 'Utama', secondary: 'Sekunder', character: 'Karakter', SetClothes: 'Set', Costumes: 'Kostum', Hats: 'Topi', Shirts: 'Baju', Shoes: 'Sepatu', Gloves: 'Sarung tangan', HeadAccesory: 'Kepala', FrontAccesory: 'Pinggang', FaceAccesory: 'Wajah', BackAccesory: 'Punggung' },
  vi: { primary: 'Vũ khí chính', secondary: 'Vũ khí phụ', character: 'Nhân vật', SetClothes: 'Bộ đồ', Costumes: 'Liền thân', Hats: 'Mũ', Shirts: 'Áo', Shoes: 'Giày', Gloves: 'Găng tay', HeadAccesory: 'Đầu', FrontAccesory: 'Eo', FaceAccesory: 'Mặt', BackAccesory: 'Lưng' },
  th: { primary: 'อาวุธหลัก', secondary: 'อาวุธรอง', character: 'ตัวละคร', SetClothes: 'ชุด', Costumes: 'ชุดชิ้นเดียว', Hats: 'หมวก', Shirts: 'เสื้อ', Shoes: 'รองเท้า', Gloves: 'ถุงมือ', HeadAccesory: 'ศีรษะ', FrontAccesory: 'เอว', FaceAccesory: 'ใบหน้า', BackAccesory: 'หลัง' },
};

const stats = {
  'zh-CN': ['攻击力', '命中率', '重量', '连射速度', '自动瞄准'],
  'zh-TW': ['攻擊力', '命中率', '重量', '連射速度', '自動瞄準'],
  ko: ['공격력', '명중률', '무게', '연사속도', '자동조준'],
  en: ['Damage', 'Accuracy', 'Weight', 'Fire rate', 'Auto aim'],
  id: ['Serangan', 'Akurasi', 'Berat', 'Laju tembak', 'Bidik otomatis'],
  vi: ['Sát thương', 'Chính xác', 'Trọng lượng', 'Tốc độ bắn', 'Tự ngắm'],
  th: ['พลังโจมตี', 'ความแม่นยำ', 'น้ำหนัก', 'อัตรายิง', 'เล็งอัตโนมัติ'],
};

const popupTexts = {
  'zh-CN': ['可装备角色', '装备限制仅适用于服装', ['防御力', '装填速度', '移动速度']],
  'zh-TW': ['可裝備角色', '裝備限制僅適用於服裝', ['防禦力', '裝填速度', '移動速度']],
  ko: ['장착 가능 캐릭터', '장착 제한은 의상만 해당됩니다.', ['방어력', '장전속도', '이동속도']],
  en: ['Wearable by', 'Restrictions apply to clothing only.', ['Armor', 'Reload speed', 'Move speed']],
  id: ['Dapat dipakai oleh', 'Batasan hanya untuk pakaian.', ['Pertahanan', 'Kecepatan isi ulang', 'Kecepatan gerak']],
  vi: ['Nhân vật dùng được', 'Giới hạn chỉ áp dụng cho trang phục.', ['Phòng thủ', 'Tốc độ nạp đạn', 'Tốc độ di chuyển']],
  th: ['ตัวละครที่สวมได้', 'ข้อจำกัดใช้กับชุดเท่านั้น', ['พลังป้องกัน', 'ความเร็วรีโหลด', 'ความเร็วเคลื่อนที่']],
};

function editor(language: keyof typeof subCategories, values: string[], categories: string[]) {
  const keys = ['title', 'hint', 'selected', 'all', 'search', 'banned', 'allowed', 'reset', 'apply', 'cancel', 'close', 'previous', 'next', 'empty'];
  const extraKeys = ['restrictedOnly', 'presets', 'presetName', 'choosePreset', 'savePreset', 'loadPreset', 'deletePreset', 'noDescription', 'page', 'noPreview', 'bannedHint'];
  const extras = {
    'zh-CN': ['只看已禁用', '预设方案', '输入方案名称', '选择方案', '保存方案', '载入', '删除', '暂无道具说明', '页码', '暂无预览', '本房间禁止使用以下物品'],
    'zh-TW': ['只看已禁用', '預設方案', '輸入方案名稱', '選擇方案', '儲存方案', '載入', '刪除', '暫無道具說明', '頁碼', '暫無預覽', '本房間禁止使用以下物品'],
    ko: ['제한만 보기', '프리셋', '이름 입력', '프리셋 선택', '프리셋 저장', '불러오기', '삭제', '아이템 설명이 없습니다', '페이지', '미리보기 없음', '이 방에서 사용할 수 없는 아이템입니다'],
    en: ['Restricted only', 'Presets', 'Preset name', 'Choose preset', 'Save preset', 'Load', 'Delete', 'No item description', 'Page', 'No preview', 'These items cannot be used in this room'],
    id: ['Dibatasi saja', 'Preset', 'Nama preset', 'Pilih preset', 'Simpan preset', 'Muat', 'Hapus', 'Tidak ada deskripsi item', 'Halaman', 'Tidak ada pratinjau', 'Item ini tidak boleh dipakai di ruang ini'],
    vi: ['Chỉ vật phẩm cấm', 'Mẫu luật', 'Tên mẫu luật', 'Chọn mẫu luật', 'Lưu mẫu', 'Tải', 'Xóa', 'Chưa có mô tả vật phẩm', 'Trang', 'Chưa có ảnh xem trước', 'Các vật phẩm này bị cấm trong phòng'],
    th: ['เฉพาะที่ห้ามใช้', 'ชุดกฎ', 'ชื่อชุดกฎ', 'เลือกชุดกฎ', 'บันทึกชุดกฎ', 'โหลด', 'ลบ', 'ไม่มีคำอธิบายไอเทม', 'หน้า', 'ไม่มีภาพตัวอย่าง', 'ไอเทมเหล่านี้ห้ามใช้ในห้องนี้'],
  };
  return { ...Object.fromEntries(keys.map((key, index) => [key, values[index]])),
    ...Object.fromEntries(extraKeys.map((key, index) => [key, extras[language][index]])),
    categories: Object.fromEntries(['weapons', 'characters', 'clothes', 'accessories'].map((key, index) => [key, categories[index]])),
    subCategories: {...subCategories[language], ...weaponSubCategories[language]}, stats: stats[language],
    wearable: popupTexts[language][0], wearNotice: popupTexts[language][1], armorStats: popupTexts[language][2] };
}

export const messages = {
  'zh-CN': { language: '语言', open: '打开禁用设置', saved: '房间规则已保存', demo: '界面预览', editor: editor('zh-CN', ['房间禁用规则', '点击物品，选择本房间禁止使用的内容', '已禁用', '全部', '搜索名称或编号', '已禁用', '允许使用', '全选', '应用规则', '取消', '关闭', '上一页', '下一页', '没有找到匹配的物品'], ['武器', '角色', '服装', '饰品']) },
  'zh-TW': { language: '語言', open: '開啟禁用設定', saved: '房間規則已儲存', demo: '介面預覽', editor: editor('zh-TW', ['房間禁用規則', '點選物品，選擇本房間禁止使用的內容', '已禁用', '全部', '搜尋名稱或編號', '已禁用', '允許使用', '全選', '套用規則', '取消', '關閉', '上一頁', '下一頁', '找不到符合的物品'], ['武器', '角色', '服裝', '配飾']) },
  en: { language: 'Language', open: 'Open restrictions', saved: 'Room rules saved', demo: 'Interface preview', editor: editor('en', ['Room restrictions', 'Select the items that cannot be used in this room', 'Restricted', 'All', 'Search name or ID', 'Restricted', 'Allowed', 'Select all', 'Apply rules', 'Cancel', 'Close', 'Previous page', 'Next page', 'No matching items'], ['Weapons', 'Characters', 'Clothing', 'Accessories']) },
  ko: { language: '언어', open: '제한 설정 열기', saved: '방 규칙을 저장했습니다', demo: '화면 미리보기', editor: editor('ko', ['방 아이템 제한', '이 방에서 사용할 수 없는 아이템을 선택하세요', '제한', '전체', '이름 또는 번호 검색', '사용 제한', '사용 가능', '모두 선택', '규칙 적용', '취소', '닫기', '이전 페이지', '다음 페이지', '검색 결과가 없습니다'], ['무기', '캐릭터', '의상', '액세서리']) },
  id: { language: 'Bahasa', open: 'Buka pembatasan', saved: 'Aturan ruang disimpan', demo: 'Pratinjau antarmuka', editor: editor('id', ['Pembatasan ruang', 'Pilih item yang tidak boleh dipakai di ruang ini', 'Dibatasi', 'Semua', 'Cari nama atau ID', 'Dibatasi', 'Diizinkan', 'Pilih semua', 'Terapkan', 'Batal', 'Tutup', 'Halaman sebelumnya', 'Halaman berikutnya', 'Tidak ada item yang cocok'], ['Senjata', 'Karakter', 'Pakaian', 'Aksesori']) },
  vi: { language: 'Ngôn ngữ', open: 'Mở giới hạn', saved: 'Đã lưu luật phòng', demo: 'Xem trước giao diện', editor: editor('vi', ['Giới hạn trong phòng', 'Chọn vật phẩm không được sử dụng trong phòng', 'Đã cấm', 'Tất cả', 'Tìm tên hoặc mã', 'Đã cấm', 'Được dùng', 'Chọn tất cả', 'Áp dụng', 'Hủy', 'Đóng', 'Trang trước', 'Trang sau', 'Không tìm thấy vật phẩm'], ['Vũ khí', 'Nhân vật', 'Trang phục', 'Phụ kiện']) },
  th: { language: 'ภาษา', open: 'เปิดการจำกัด', saved: 'บันทึกกฎห้องแล้ว', demo: 'ตัวอย่างหน้าจอ', editor: editor('th', ['ข้อจำกัดของห้อง', 'เลือกไอเทมที่ไม่อนุญาตให้ใช้ในห้องนี้', 'จำกัดแล้ว', 'ทั้งหมด', 'ค้นหาชื่อหรือรหัส', 'ห้ามใช้', 'ใช้ได้', 'เลือกทั้งหมด', 'ใช้กฎ', 'ยกเลิก', 'ปิด', 'หน้าก่อนหน้า', 'หน้าถัดไป', 'ไม่พบไอเทมที่ตรงกัน'], ['อาวุธ', 'ตัวละคร', 'เสื้อผ้า', 'เครื่องประดับ']) },
};

const notices = {
  'zh-CN': ['方案已保存，刷新页面后仍可载入', '方案已删除', '无法读取或保存本地方案'],
  'zh-TW': ['方案已儲存，重新整理後仍可載入', '方案已刪除', '無法讀取或儲存本機方案'],
  ko: ['프리셋을 저장했습니다', '프리셋을 삭제했습니다', '로컬 프리셋을 읽거나 저장할 수 없습니다'],
  en: ['Preset saved and available after reload', 'Preset deleted', 'Cannot read or save local presets'],
  id: ['Preset tersimpan untuk digunakan kembali', 'Preset dihapus', 'Tidak dapat membaca atau menyimpan preset lokal'],
  vi: ['Đã lưu mẫu luật để dùng lại', 'Đã xóa mẫu luật', 'Không thể đọc hoặc lưu mẫu luật cục bộ'],
  th: ['บันทึกชุดกฎแล้วและโหลดใหม่ได้', 'ลบชุดกฎแล้ว', 'ไม่สามารถอ่านหรือบันทึกชุดกฎในเครื่อง'],
};

export const localizedMessages = Object.fromEntries(Object.entries(messages).map(([language, value]) => {
  const notice = notices[language as keyof typeof notices];
  const themeLabels = {'zh-CN':'样式', 'zh-TW':'樣式', ko:'스타일', en:'Style', id:'Gaya', vi:'Kiểu', th:'รูปแบบ'};
  return [language, { ...value, theme: themeLabels[language as keyof typeof themeLabels], themes: {v1:'v1', v2:'v2'}, presetSaved: notice[0], presetDeleted: notice[1], presetStorageError: notice[2] }];
}));
