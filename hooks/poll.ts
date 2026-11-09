// 주기 작업: 대표 세션 선출 → 앱에서 세션 목록 읽기 → 확인 필요 판단 → 위젯용 sessions.json 쓰기 → 알림
//
// 모든 세션에 실린다. 그중 대표 세션 하나(leader.json)만 목록을 읽고 판단하고 파일을 쓴다.
// 나머지 세션은 그 파일만 읽어 패널 · 상태 줄을 맞춘다.

import type { EngineInterface } from 'claude-code'

import type { BoardFile, BoardMetaMap, BoardSession } from '../types'
import { readAccount } from './account'
import { LEADER_STALE_MS, LEADER_V, LIST_LIMIT, REMIND_TOAST_MS, TOAST_MS } from './config'
import { callJson, judgeSome, withJudgement } from './judge'
import { getPaths } from './paths'
import {
  changeMeta,
  getAccount,
  getMeta,
  getSessions,
  readMetaFile,
  refreshStatus,
  saveMeta,
  setAccount,
  setChecked,
  setError,
  setMeta,
  setSessions,
} from './store'

export const PLUGIN_VERSION = '0.2.0'

type RawSession = {
  sessionId?: string
  title?: string
  isRunning?: boolean
  lastActivityAt?: string
  link?: string
  remoteControlActive?: boolean
}

function toRow(raw: RawSession, isSelf: boolean): BoardSession | undefined {
  if (!raw.sessionId) return undefined
  return {
    id: raw.sessionId,
    title: raw.title || '(제목 없음)',
    isRunning: !!raw.isRunning,
    lastActivityAt: raw.lastActivityAt ?? '',
    isSelf,
    remote: !!raw.remoteControlActive,
    ...(raw.link ? { link: raw.link } : {}),
  }
}

// 겹쳐 돌지 않게 (다시 불러오면 처음부터)
let polling = false

export async function poll($: EngineInterface) {
  if (polling) return
  polling = true
  try {
    await pollOnce($)
  } finally {
    polling = false
  }
}

/**
 * 대표 세션인가: 대표가 없거나 LEADER_STALE_MS 넘게 소식이 없으면 이 세션이 맡는다.
 * 코드 판(LEADER_V)이 더 높은 세션이 옛 판 대표의 자리를 넘겨받는다
 * (옛 판은 남의 id 가 그 시간 안에 쓰여 있으면 물러나므로 따로 고칠 것이 없다).
 */
async function amLeader($: EngineInterface, now: number): Promise<boolean> {
  const p = await getPaths($)
  const me = await $.session.id()
  let cur: { id?: unknown; at?: unknown; v?: unknown } = {}
  try {
    cur = JSON.parse(await $.fs.read(p.leaderFile)) as { id?: unknown; at?: unknown; v?: unknown }
  } catch {
    cur = {}
  }
  const isOther = typeof cur.id === 'string' && cur.id !== me
  const isFresh = typeof cur.at === 'number' && now - cur.at < LEADER_STALE_MS
  const curV = typeof cur.v === 'number' ? cur.v : 0
  if (isOther && isFresh && curV >= LEADER_V) return false
  await $.fs.write(p.leaderFile, JSON.stringify({ id: me, at: now, v: LEADER_V }))
  return true
}

/** 대표가 아닌 세션: 대표가 남긴 파일만 읽어 패널 · 상태 줄을 맞춘다 */
async function followFiles($: EngineInterface) {
  const p = await getPaths($)
  try {
    const d = JSON.parse(await $.fs.read(p.sessionsFile)) as Partial<BoardFile>
    if (Array.isArray(d.sessions)) await setSessions($, d.sessions)
    if (typeof d.updatedAtMs === 'number') await setChecked($, d.updatedAtMs)
    await setError($, typeof d.error === 'string' ? d.error : '')
    await setAccount($, d.account)
  } catch {
    // 아직 파일이 없으면 다음 차례에
  }
  const fileMeta = await readMetaFile($)
  if (fileMeta) await setMeta($, fileMeta)
}

/** 위젯이 읽는 sessions.json */
async function exportSessions($: EngineInterface, now: number, error: string) {
  const p = await getPaths($)
  const sessions = await getSessions($)
  const account = await getAccount($)
  const body: BoardFile = {
    updatedAtMs: now,
    updatedAt: new Date(now).toISOString(),
    error,
    pluginVersion: PLUGIN_VERSION,
    ...(account ? { account } : {}),
    sessions,
  }
  try {
    await $.fs.write(p.sessionsFile, JSON.stringify(body, null, 1))
  } catch {
    // 위젯용 파일을 못 써도 패널은 계속 돈다
  }
}

async function pollOnce($: EngineInterface) {
  const now = await $.clock.now()
  if (!(await amLeader($, now))) {
    await followFiles($)
    await refreshStatus($)
    return
  }
  try {
    await setAccount($, await readAccount($, now))

    const list = (await callJson($, 'list_sessions', { limit: LIST_LIMIT })) as RawSession[]
    let selfRow: BoardSession | undefined
    try {
      selfRow = toRow((await callJson($, 'get_session', { session_id: 'self' })) as RawSession, true)
    } catch {
      selfRow = undefined
    }
    const raw = [
      ...(selfRow ? [selfRow] : []),
      ...list.map(r => toRow(r, false)).filter((r): r is BoardSession => !!r),
    ]
    const judged = await judgeSome($, raw, now)
    const fresh = raw.map(s => withJudgement(s, judged))

    const before = await getSessions($)
    const wasRunning = new Map(before.map(s => [s.id, s.isRunning]))
    const firstPoll = before.length === 0

    // 처음 보는 세션은 읽은 것으로 시작, 작업이 끝난 세션은 알림
    const fileMeta = await readMetaFile($)
    const meta = fileMeta ?? (await getMeta($))
    const nextMeta: BoardMetaMap = { ...meta }
    for (const s of fresh) {
      const m = nextMeta[s.id]
      if (!m?.seenAt) nextMeta[s.id] = { ...m, seenAt: s.lastActivityAt }
      else if (!firstPoll && wasRunning.get(s.id) === true && !s.isRunning && !s.isSelf && !m?.heldAt) {
        // 미룬 세션은 알리지 않는다(접힌 칸에 「새 답 있음」만)
        $.ui.toast(
          s.needs ? `🙋 확인 필요 — ${s.title}${s.ask ? ` · ${s.ask}` : ''}` : `💬 답 왔음 — ${s.title}`,
          { timeoutMs: TOAST_MS },
        )
      }
    }
    if (!fileMeta || JSON.stringify(nextMeta) !== JSON.stringify(fileMeta)) await saveMeta($, nextMeta)
    else await setMeta($, nextMeta)
    await setSessions($, fresh)
    await setError($, '')
    await exportSessions($, now, '')
  } catch (err) {
    const msg = `세션 목록을 못 읽음: ${String((err as Error)?.message ?? err)}`
    await setError($, msg)
    await exportSessions($, now, msg)
  }

  // 리마인드 알람
  const sessions = await getSessions($)
  const meta = await getMeta($)
  for (const s of sessions) {
    const m = meta[s.id]
    if (m?.remindAt && !m.remindFired && m.remindAt <= now) {
      $.ui.toast(`⏰ 리마인드 — ${s.title}${m.memo ? ` · ${m.memo}` : ''}`, { timeoutMs: REMIND_TOAST_MS })
      await changeMeta($, s.id, x => ({ ...x, remindFired: true }))
    }
  }

  await setChecked($, now)
  await refreshStatus($)
}
