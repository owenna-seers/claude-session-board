/** 앱이 알려 주는 세션 한 줄 */
export type BoardSession = {
  id: string
  title: string
  isRunning: boolean
  lastActivityAt: string
  isSelf: boolean
  /** claude:// 주소: 윈도우 위젯이 이 세션을 앱에서 열 때 쓴다 */
  link?: string
  /** 원격 제어(Remote Control) 켜짐 여부 */
  remote?: boolean
  /** 마지막 답이 사용자의 결정 · 답을 기다리는가 */
  needs?: boolean
  /** 무엇을 확인해야 하는지 한 줄 */
  ask?: string
}

/** 로그인 계정 · 조직 (머리줄 오른쪽) */
export type BoardAccount = { name: string; email: string; org: string }

/** 세션의 마지막 답을 읽고 내린 판단 (그 답의 시각 기준) */
export type BoardJudged = { at: string; needs: boolean; ask: string }
export type BoardJudgedMap = Record<string, BoardJudged>

/** 사용자가 세션마다 붙여 두는 표시 ($.store 와 meta.json 에 저장, 위젯과 나눠 씀) */
export type BoardMeta = {
  star?: boolean
  seenAt?: string
  remindAt?: number
  remindFired?: boolean
  memo?: string
  /** ✓ 확인 끝: 이 시각(그때의 마지막 활동) 뒤에 새 활동(새 대화 · 새 답)이 생기면 다시 올라온다 */
  hiddenAt?: string
  /** ⏸ 미루기(일단 보류): 사용자가 풀 때까지 접힌 칸에 둔다. 값은 미룰 때의 마지막 활동 시각 */
  heldAt?: string
  /** (옛 판이 남긴 값, 지금은 쓰지 않음) */
  openedAt?: string
}

export type BoardMetaMap = Record<string, BoardMeta>

/** 위젯이 읽는 sessions.json 의 모양 */
export type BoardFile = {
  updatedAtMs: number
  updatedAt: string
  error: string
  pluginVersion: string
  account?: BoardAccount
  sessions: BoardSession[]
}

declare module 'claude-code' {
  interface PluginState {
    'session-board': {
      sessions: Shaped<BoardSession[]>
      meta: Shaped<BoardMetaMap>
      judged: Shaped<BoardJudgedMap>
      account: Shaped<BoardAccount | undefined>
      expanded: string
      showHidden: boolean
      showHeld: boolean
      error: string
      checkedAt: number
    }
  }
}
