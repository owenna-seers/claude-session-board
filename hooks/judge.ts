// 확인 필요 판단
//  1순위: 세션의 마지막 턴이 다음 단계 상자(wrap_up)에 남긴 질문(ask) — 앱 세션 파일 → 세션 기록 jsonl 끝에서 읽는다
//  2순위: 마지막 답 글을 모델(JUDGE_MODEL)에게 보여 판단
//  3순위: 낱말 어림(GUESS_RE)

import type { EngineInterface } from 'claude-code'

import type { BoardJudged, BoardJudgedMap, BoardSession } from '../types'
import { JUDGED_KEY, JUDGE_MODEL, JUDGE_PER_POLL, JUDGE_WITHIN_MS } from './config'
import { GUESS_ASK, GUESS_RE, JUDGE_MAX_CHARS, JUDGE_SYSTEM } from './judge-prompt'
import { getPaths, projectFolder } from './paths'
import { getJudged, setJudged } from './store'

const SERVER = 'ccd_session_mgmt'

export async function callText($: EngineInterface, tool: string, args: Record<string, unknown>): Promise<string> {
  const r = await $.mcp.call(SERVER, tool, args)
  const block = r.content.find(b => b.type === 'text') as { text?: string } | undefined
  const text = block?.text ?? ''
  if (r.isError) throw new Error(text || `${tool} 실패`)
  return text
}

export async function callJson($: EngineInterface, tool: string, args: Record<string, unknown>): Promise<unknown> {
  return JSON.parse(await callText($, tool, args))
}

function lastAssistantText(transcript: string): string {
  const parts = transcript.split(/\n?\[assistant\] /)
  if (parts.length < 2) return ''
  const last = parts[parts.length - 1] ?? ''
  return last.replace(/\n\[result\][^\n]*$/, '').trim()
}

const guess = (text: string) => GUESS_RE.test(text.slice(-400))

// 앱 세션 파일이 있는 폴더(뿌리 아래 계정 · 조직 폴더 두 겹)를 한 번 찾아 둔다
let appSessionsDir = ''
async function appSessionFile($: EngineInterface, id: string): Promise<Record<string, unknown> | undefined> {
  const name = `${id}.json`
  const tryRead = async (dir: string) => {
    try {
      return JSON.parse(await $.fs.read(`${dir}/${name}`)) as Record<string, unknown>
    } catch {
      return undefined
    }
  }
  if (appSessionsDir) {
    const hit = await tryRead(appSessionsDir)
    if (hit) return hit
  }
  const p = await getPaths($)
  for (const root of p.appSessionsRoots) {
    try {
      for (const a of await $.fs.list(root)) {
        if (a.kind !== 'dir') continue
        for (const b of await $.fs.list(`${root}/${a.name}`)) {
          if (b.kind !== 'dir') continue
          const dir = `${root}/${a.name}/${b.name}`
          const hit = await tryRead(dir)
          if (hit) {
            appSessionsDir = dir
            return hit
          }
        }
      }
    } catch {
      // 다음 뿌리로
    }
  }
  return undefined
}

/**
 * 세션의 마지막 턴이 다음 단계 상자(wrap_up)에 사용자에게 묻는 질문(ask)을 남겼으면 그 질문.
 * 마지막 턴에 wrap_up 이 없거나 질문이 없으면 undefined.
 * 기록은 수십 MB 라 $.fs.read(4 MiB 한도)로 못 읽어, tail.ps1 로 끝 부분만 읽는다.
 */
async function lastTurnAsk($: EngineInterface, id: string): Promise<string | undefined> {
  const app = await appSessionFile($, id)
  if (!app) return undefined
  const cli = String(app.lastAssistantCliSessionId ?? app.cliSessionId ?? '')
  if (!cli) return undefined
  const p = await getPaths($)
  const cwds = [app.cwd, app.originCwd].filter((c): c is string => typeof c === 'string' && c !== '')
  let path = ''
  for (const c of cwds) {
    const candidate = `${p.projectsDir}/${projectFolder(c)}/${cli}.jsonl`
    if (await $.fs.exists(candidate)) {
      path = candidate
      break
    }
  }
  if (!path) return undefined
  const r = await $.process.run(
    ['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', `${$.plugin.root}/hooks/tail.ps1`, path],
    { timeoutMs: 20_000 },
  )
  if (r.exitCode !== 0) return undefined
  const lines = r.stdout.split('\n').slice(1) // 첫 줄은 잘린 줄일 수 있음
  let ask: string | undefined
  for (const line of lines) {
    if (!line.trim()) continue
    let e: { type?: string; isMeta?: boolean; message?: { content?: unknown } }
    try {
      e = JSON.parse(line)
    } catch {
      continue
    }
    const content = e.message?.content
    if (e.type === 'user' && !e.isMeta) {
      // 사용자(또는 다른 세션 알림)가 보낸 새 말 = 새 턴: 그 전 질문은 지난 것
      const isPrompt = typeof content === 'string' ||
        (Array.isArray(content) &&
          content.some(b => (b as { type?: string }).type === 'text') &&
          !content.some(b => (b as { type?: string }).type === 'tool_result'))
      if (isPrompt) ask = undefined
      continue
    }
    if (e.type !== 'assistant' || !Array.isArray(content)) continue
    for (const b of content as { type?: string; name?: string; input?: { ask?: unknown } }[]) {
      if (b.type === 'tool_use' && b.name === 'mcp__ccd_turn__wrap_up') {
        ask = typeof b.input?.ask === 'string' && b.input.ask.trim() ? b.input.ask.trim() : undefined
      }
    }
  }
  return ask
}

async function judge($: EngineInterface, s: BoardSession): Promise<BoardJudged> {
  // 1) 다음 단계 상자에 질문이 있으면 그대로 확인 필요
  try {
    const ask = await lastTurnAsk($, s.id)
    if (ask) return { at: s.lastActivityAt, needs: true, ask: ask.slice(0, 80) }
  } catch {
    // 아래 글 판단으로
  }
  // 2) 없으면 마지막 답 글을 읽고 판단
  const transcript = await callText($, 'list_events', { session_id: s.id, limit: 12 })
  const text = lastAssistantText(transcript)
  if (!text) return { at: s.lastActivityAt, needs: false, ask: '' }
  if (JUDGE_MODEL !== 'off') {
    const clipped = text.length > JUDGE_MAX_CHARS ? text.slice(-JUDGE_MAX_CHARS) : text
    const r = await $.model.complete({
      model: JUDGE_MODEL,
      system: JUDGE_SYSTEM,
      prompt: `세션 제목: ${s.title}\n\n마지막 답:\n<<<\n${clipped}\n>>>`,
      maxTokens: 200,
      timeoutMs: 30_000,
    })
    if (r.isAnswered) {
      const m = r.text.match(/\{[\s\S]*\}/)
      if (m) {
        try {
          const j = JSON.parse(m[0]) as { needs?: unknown; ask?: unknown }
          const needs = j.needs === true
          return { at: s.lastActivityAt, needs, ask: needs ? String(j.ask ?? '').slice(0, 80) : '' }
        } catch {
          // 아래 어림으로
        }
      }
    }
  }
  // 3) 낱말 어림
  const needs = guess(text)
  return { at: s.lastActivityAt, needs, ask: needs ? GUESS_ASK : '' }
}

/** 최근에 움직인 · 아직 판단 안 한 세션을 몇 개만 판단하고, 사라진 세션의 판단은 버린다 */
export async function judgeSome($: EngineInterface, sessions: BoardSession[], now: number): Promise<BoardJudgedMap> {
  let judged = await getJudged($)
  const todo = sessions
    .filter(s => !s.isSelf && !s.isRunning && s.lastActivityAt)
    .filter(s => now - Date.parse(s.lastActivityAt) < JUDGE_WITHIN_MS)
    .filter(s => judged[s.id]?.at !== s.lastActivityAt)
    .slice(0, JUDGE_PER_POLL)
  if (todo.length === 0) return judged
  for (const s of todo) {
    try {
      const j = await judge($, s)
      judged = { ...judged, [s.id]: j }
    } catch {
      // 이번엔 건너뛰고 다음 갱신 때 다시
    }
  }
  const alive = new Set(sessions.map(s => s.id))
  const kept: BoardJudgedMap = {}
  for (const [id, j] of Object.entries(judged)) if (alive.has(id)) kept[id] = j
  await setJudged($, kept)
  await $.store.set(JUDGED_KEY, kept)
  return kept
}

export function withJudgement(s: BoardSession, judged: BoardJudgedMap): BoardSession {
  const j = judged[s.id]
  if (s.isRunning || !j || j.at !== s.lastActivityAt || !j.needs) return s
  return { ...s, needs: true, ask: j.ask }
}
