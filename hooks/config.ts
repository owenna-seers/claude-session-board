// 세션 현황판 설정 — 플러그인 쪽에서 바꾸고 싶은 값은 전부 여기 (CUSTOMIZING.md 참고)
// 위젯(widget/src/SessionWidget.cs) 은 맨 위 Config 클래스에 같은 식으로 모여 있다.

/** 패널 제목 · 상태 줄 머리말 */
export const TITLE = '세션 현황판'

/** 세션 목록을 읽는 주기(ms). 위젯은 이 파일을 1초마다 읽는다 */
export const POLL_MS = 5_000

/** 앱에서 한 번에 가져오는 세션 수 */
export const LIST_LIMIT = 30

/**
 * 확인 필요 판단에 쓰는 모델. 'off' 면 모델을 부르지 않고
 * 다음 단계 상자(wrap_up)의 질문 + 낱말 어림만으로 판단한다(사용량 0).
 */
export const JUDGE_MODEL: 'haiku' | 'sonnet' | 'off' = 'haiku'

/** 최근 며칠 안에 움직인 세션만 판단한다 */
export const JUDGE_WITHIN_MS = 3 * 24 * 3600_000

/** 한 번 돌 때 몇 세션까지 판단하는가(모델 호출 수 상한) */
export const JUDGE_PER_POLL = 4

/** 판단 기준(judge-prompt.ts)을 바꾸면 이 이름을 올린다 → 전부 다시 판단 */
export const JUDGED_KEY = 'judged-4'

/**
 * 코드 판. 판단 · 대표 방식을 고칠 때마다 올린다.
 * 플러그인 코드는 세션이 시작될 때 실리므로, 고치기 전에 열린 세션은 옛 코드로 돈다.
 * 판이 높은 세션이 옛 판 대표의 자리를 넘겨받아, 고친 판단이 바로 위젯에 나온다.
 */
export const LEADER_V = 6

/** 대표 세션이 이 시간(ms) 넘게 소식이 없으면 다른 세션이 이어받는다 */
export const LEADER_STALE_MS = 15_000

/** 앱 창에서 지금 보고 있는 세션(view.json)을 살피는 주기(ms). 위젯이 그 카드에 테두리를 그린다 */
export const VIEW_MS = 1_500

/**
 * 시간 표기의 시간대(UTC 와의 차, 분). undefined 면 이 PC 의 시간대.
 * 패널 시각이 UTC 로 보이면 여기에 540(한국) 처럼 적는다.
 */
export const TZ_OFFSET_MIN: number | undefined = undefined

/**
 * 위젯과 나눠 쓰는 폴더. '' 면 <홈>/.claude/session-board.
 * 바꾸면 위젯(SessionWidget.cs 의 Config.ShareDir)도 같이 바꾼다.
 */
export const SHARE_DIR = ''

/** 세션 끝 알림(토스트)이 떠 있는 시간(ms) */
export const TOAST_MS = 15_000

/** 리마인드 알림이 떠 있는 시간(ms) */
export const REMIND_TOAST_MS = 60_000

/** 패널의 리마인드 버튼(분) */
export const REMIND_PRESETS_MIN = [30, 60, 180]

/** 「내일 n시」 리마인드의 n */
export const TOMORROW_HOUR = 9

/** $.store 키 — 바꿀 일 없음 */
export const STORE_KEY = 'meta'
