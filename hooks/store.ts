// 패널이 드는 값(atom) 과 위젯과 나눠 쓰는 표시 파일(meta.json)
//
// atom 은 이 파일 안에서만 read · update 한다(검사기가 atom 참조를 같은 파일에서만 따라간다).
// 다른 파일은 아래 get* · set* 함수로 읽고 쓴다.

import { atom, read, update } from 'claude-code'
import type { EngineInterface } from 'claude-code'

import type { BoardAccount, BoardJudgedMap, BoardMeta, BoardMetaMap, BoardSession } from '../types'
import { STORE_KEY, TITLE } from './config'
import { getPaths } from './paths'
import { isHidden, isUnread, needsNow } from './rank'

const sessionsAtom = atom({ plugin: 'session-board', key: 'sessions' } as const, [], {
  shape: 'sessions-2',
})
const metaAtom = atom({ plugin: 'session-board', key: 'meta' } as const, {}, {
  shape: 'meta-1',
})
const judgedAtom = atom({ plugin: 'session-board', key: 'judged' } as const, {}, {
  shape: 'judged-4',
})
const accountAtom = atom({ plugin: 'session-board', key: 'account' } as const, undefined, {
  shape: 'account-1',
})
const expandedAtom = atom({ plugin: 'session-board', key: 'expanded' } as const, '')
const showHiddenAtom = atom({ plugin: 'session-board', key: 'showHidden' } as const, false)
const showHeldAtom = atom({ plugin: 'session-board', key: 'showHeld' } as const, false)
const errorAtom = atom({ plugin: 'session-board', key: 'error' } as const, '')
const checkedAtom = atom({ plugin: 'session-board', key: 'checkedAt' } as const, 0)

// ---------- 읽기 · 쓰기 ----------

export async function getSessions($: EngineInterface): Promise<BoardSession[]> {
  return read($, sessionsAtom)
}
export async function setSessions($: EngineInterface, v: BoardSession[]) {
  await update($, sessionsAtom, () => v)
}
export async function getMeta($: EngineInterface): Promise<BoardMetaMap> {
  return read($, metaAtom)
}
export async function setMeta($: EngineInterface, v: BoardMetaMap) {
  await update($, metaAtom, () => v)
}
export async function getJudged($: EngineInterface): Promise<BoardJudgedMap> {
  return read($, judgedAtom)
}
export async function setJudged($: EngineInterface, v: BoardJudgedMap) {
  await update($, judgedAtom, () => v)
}
export async function getAccount($: EngineInterface): Promise<BoardAccount | undefined> {
  return read($, accountAtom)
}
export async function setAccount($: EngineInterface, v: BoardAccount | undefined) {
  await update($, accountAtom, () => v)
}
export async function getExpanded($: EngineInterface): Promise<string> {
  return read($, expandedAtom)
}
export async function toggleExpanded($: EngineInterface, id: string) {
  await update($, expandedAtom, cur => (cur === id ? '' : id))
}
export async function getShowDone($: EngineInterface): Promise<boolean> {
  return read($, showHiddenAtom)
}
export async function toggleShowDone($: EngineInterface) {
  await update($, showHiddenAtom, v => !v)
}
export async function getShowHeld($: EngineInterface): Promise<boolean> {
  return read($, showHeldAtom)
}
export async function toggleShowHeld($: EngineInterface) {
  await update($, showHeldAtom, v => !v)
}
export async function getError($: EngineInterface): Promise<string> {
  return read($, errorAtom)
}
export async function setError($: EngineInterface, v: string) {
  await update($, errorAtom, () => v)
}
export async function getChecked($: EngineInterface): Promise<number> {
  return read($, checkedAtom)
}
export async function setChecked($: EngineInterface, v: number) {
  await update($, checkedAtom, () => v)
}

// ---------- 표시 파일(meta.json) ----------

export async function readMetaFile($: EngineInterface): Promise<BoardMetaMap | undefined> {
  try {
    const p = await getPaths($)
    const parsed: unknown = JSON.parse(await $.fs.read(p.metaFile))
    return parsed && typeof parsed === 'object' ? (parsed as BoardMetaMap) : undefined
  } catch {
    return undefined
  }
}

export async function saveMeta($: EngineInterface, next: BoardMetaMap) {
  const p = await getPaths($)
  await update($, metaAtom, () => next)
  await $.store.set(STORE_KEY, next)
  await $.fs.write(p.metaFile, JSON.stringify(next, null, 1))
}

/** 위젯이 그사이 바꾼 것을 먼저 읽고, 그 위에 이 세션의 변경을 얹는다 */
export async function changeMeta($: EngineInterface, id: string, fn: (m: BoardMeta) => BoardMeta) {
  const base = (await readMetaFile($)) ?? (await read($, metaAtom))
  await saveMeta($, { ...base, [id]: fn(base[id] ?? {}) })
}

/** 상태 줄: 「세션 현황판 확인 필요 n · 작업 중 n · 안 읽음 n」 */
export async function refreshStatus($: EngineInterface) {
  const meta = await read($, metaAtom)
  const sessions = (await read($, sessionsAtom)).filter((s: BoardSession) => !isHidden(s, meta[s.id]))
  const running = sessions.filter((s: BoardSession) => s.isRunning).length
  const needs = sessions.filter((s: BoardSession) => needsNow(s, meta[s.id])).length
  const unread = sessions.filter((s: BoardSession) => isUnread(s, meta[s.id])).length
  const due = sessions.filter((s: BoardSession) => meta[s.id]?.remindFired).length
  const parts = [`확인 필요 ${needs}`, `작업 중 ${running}`, `안 읽음 ${unread}`]
  if (due) parts.push(`⏰ ${due}`)
  $.ui.status(`${TITLE} ${parts.join(' · ')}`)
}
