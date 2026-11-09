// 카드의 상태 판별과 정렬. 위젯(SessionWidget.cs 「상태 판단」) 과 같은 규칙이어야 한다.

import type { BoardMeta, BoardSession } from '../types'

/** 💬 답 왔음 · 안 읽음: 멈춘 세션의 마지막 활동이 읽은 시각보다 뒤 */
export const isUnread = (s: BoardSession, m: BoardMeta | undefined) =>
  !s.isRunning && !!m?.seenAt && s.lastActivityAt > m.seenAt

/** ⏸ 미루기(일단 보류): 사용자가 풀 때까지. 단, 리마인드가 울리면 목록에 나온다 */
export const isHeld = (_s: BoardSession, m: BoardMeta | undefined) => !!m?.heldAt && !m.remindFired

/** 미룬 뒤로 새 답이 왔는가(접힌 칸에 표시만) */
export const heldNew = (s: BoardSession, m: BoardMeta | undefined) =>
  !!m?.heldAt && !s.isRunning && s.lastActivityAt > m.heldAt

/** ✓ 확인 끝: 그 뒤로 새 활동(새 대화 · 새 답)이 없는 동안만 */
export const isDone = (s: BoardSession, m: BoardMeta | undefined) =>
  !m?.heldAt && !!m?.hiddenAt && s.lastActivityAt <= m.hiddenAt

/** 목록 본문에서 빠지는가 */
export const isHidden = (s: BoardSession, m: BoardMeta | undefined) => isHeld(s, m) || isDone(s, m)

/** 🙋 확인 필요: 읽어도 그대로, 사용자가 현황판에서 내릴 때만 사라진다 */
export const needsNow = (s: BoardSession, _m: BoardMeta | undefined) => !!s.needs

/** 작을수록 위. 리마인드 지남 → 확인 필요 → 강조 → 안 읽음 → 작업 중 → 나머지 */
export function rank(s: BoardSession, m: BoardMeta | undefined): number {
  if (m?.remindFired) return 0
  if (needsNow(s, m)) return 1
  if (m?.star) return 2
  if (isUnread(s, m)) return 3
  if (s.isRunning) return 4
  return 5
}

/** 순위, 같으면 최근 활동이 위 */
export function byRank(list: BoardSession[], meta: Record<string, BoardMeta>): BoardSession[] {
  return [...list].sort((a, b) => {
    const r = rank(a, meta[a.id]) - rank(b, meta[b.id])
    return r !== 0 ? r : b.lastActivityAt.localeCompare(a.lastActivityAt)
  })
}
