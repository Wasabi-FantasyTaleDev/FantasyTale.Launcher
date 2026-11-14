/** Native introductions: kr-1256 .ui labels, cn-774 indexed ui.yaml fields and tw-454 StringTable translations. */
export const nativeModeDescriptions = {
  "Pvp": {
    "ko": "정면승부!",
    "zh-CN": "正面决胜负！",
    "zh-TW": "正面勝負!"
  },
  "Item": {
    "ko": "아이템으로 맞서자!",
    "zh-CN": "用道具迎战！",
    "zh-TW": "用道具對付吧!"
  },
  "PvpStarTeam": {
    "ko": "별을 모아 승리하자!",
    "zh-CN": "抢夺星星赢得比赛吧！"
  },
  "Zombie": {
    "ko": "좀비로부터 탈출하라!",
    "zh-CN": "先逃离僵尸！",
    "zh-TW": "從殭屍群中逃脫！"
  },
  "EventSoccer": {
    "ko": "축구대전!"
  },
  "Sniper": {
    "ko": "한 발에 모든 것을!",
    "zh-CN": "一发解决全部！",
    "zh-TW": "一發擊中！"
  },
  "SinglePvp": {
    "ko": "이젠 모두가 적이다!",
    "zh-CN": "所有人都是敌人！",
    "zh-TW": "現在開始大家都是敵人!"
  },
  "TeamSurvival": {
    "ko": "혼자가 아닌 팀!",
    "zh-CN": "不是一个人哦！",
    "zh-TW": "不是一個人的團隊!"
  },
  "ZombiePve": {
    "ko": "마을을 구출하라!",
    "zh-CN": "营救泡泡村吧！",
    "zh-TW": "守護村莊！"
  },
  "SniperTeamSurvival": {
    "ko": "최고의 긴장감!",
    "zh-CN": "最强烈的紧张感！"
  },
  "Bomb": {
    "ko": "폭탄을 설치하라!",
    "zh-CN": "安装炸弹！",
    "zh-TW": "安裝炸彈吧!"
  },
  "Captain": {
    "ko": "압도적인 힘을 느껴라!",
    "zh-CN": "感受压倒性的力量！"
  },
  "BfRunner": {
    "ko": "달려라 달려!"
  },
  "Area32": {
    "ko": "끝까지 살아남거라!",
    "zh-CN": "泡泡求生！大逃生"
  },
  "ExtremeRacing": {
    "ko": "Easy 익스트림 레이싱!",
    "zh-CN": "挑战极限"
  },
  "SuddenSurvival": {
    "ko": "끝까지 살아남아라!",
    "zh-CN": "就这样生存到最后吧！",
    "zh-TW": "生存到最後吧!"
  },
  "WeaponMaster": {
    "ko": "무기의 달인!",
    "zh-CN": "武器的达人"
  },
  "Snowfight": {
    "ko": "눈폭탄을 마구마구 던지자!"
  },
  "ZombieGhost": {
    "ko": "좀비킹과의 숨바꼭질!",
    "zh-CN": "黑暗中的惊险捉迷藏！"
  },
  "ControlPoint": {
    "ko": "거점을 차지하라!",
    "zh-CN": "占领据点取得胜利！",
    "zh-TW": "佔領據點取得勝利！"
  },
  "FinalZone": {
    "ko": "끝까지 살아남아라!",
    "zh-CN": "就这样生存到最后吧！",
    "zh-TW": "生存到最後吧!"
  },
  "Shotgun": {
    "ko": "한방한방 묵직한 손맛!"
  },
  "ZombieEscape": {
    "ko": "숨막히는 추격전!"
  },
  "AllPang": {
    "ko": "우리는 하나!",
    "zh-CN": "我们是一个！",
    "zh-TW": "我們是團隊！"
  },
  "NoItemPractice": {
    "ko": "NPC와 함께 노템전 연습!",
    "zh-CN": "与电脑一起进行无道具战的训练吧！",
    "zh-TW": "與NPC的經典模式練習！"
  },
  "JumpJump": {
    "ko": "제트팩으로 자유롭게!"
  },
  "NoItemTutorial": {
    "ko": "인공지능 강화 NPC와!",
    "zh-CN": "人工智能强化的电脑"
  },
  "Psionic": {
    "ko": "쏘고 피하고 팡하고!",
    "zh-CN": "激烈的超能力大战！"
  },
  "NoItemCustomAiWar": {
    "ko": "인공지능 강화 NPC와!",
    "zh-CN": "人工智能强化的电脑"
  },
  "TeamSurvivalCustomAiWar": {
    "ko": "인공지능 강화 NPC와!",
    "zh-CN": "　与更强大的电脑一起进行生存模式吧！"
  },
  "BnB": {
    "ko": "크레이지아케이드!"
  },
  "ZombieOrigin": {
    "ko": "좀비로부터 탈출하라!",
    "zh-TW": "從殭屍群中逃脫！"
  },
  "SimpleSuddenSurvival": {
    "ko": "끝까지 살아남아라!",
    "zh-TW": "生存到最後吧!"
  },
  "ZombieSurvival": {
    "ko": "끝까지 살아남아라!",
    "zh-TW": "生存到最後吧!"
  },
  "Horde": {
    "ko": "너굴단을 물리치자!",
    "zh-CN": "打败海盗团！",
    "zh-TW": "打倒狸貓團吧!"
  },
  "PirateAssaultSeason3": {
    "ko": "마을을 지키자!",
    "zh-CN": "守护村庄！",
    "zh-TW": "守護村莊！"
  }
};

/** Missing localized native introductions retain the shared hidden-mode label. */
export function describeModes(modes) {
  return modes.map(mode => ({...mode, descriptions:nativeModeDescriptions[mode.key]}));
}
