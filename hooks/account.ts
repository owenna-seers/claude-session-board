// 로그인 계정 · 조직 이름 — 위젯 · 패널 머리줄 오른쪽에 보인다(계정을 여럿 쓸 때 구분용)

import type { EngineInterface } from 'claude-code'

import type { BoardAccount } from '../types'
import { getPaths } from './paths'

const REREAD_MS = 60_000

let cachedAt = 0
let cached: BoardAccount | undefined

/** ~/.claude.json 의 oauthAccount 에서 displayName · emailAddress · organizationName 세 값만 읽는다 */
export async function readAccount($: EngineInterface, now: number): Promise<BoardAccount | undefined> {
  if (now - cachedAt < REREAD_MS) return cached
  cachedAt = now
  try {
    const p = await getPaths($)
    const j = JSON.parse(await $.fs.read(p.claudeJson)) as { oauthAccount?: Record<string, unknown> }
    const a = j.oauthAccount
    if (!a) return (cached = undefined)
    const str = (k: string) => (typeof a[k] === 'string' ? (a[k] as string) : '')
    cached = { name: str('displayName'), email: str('emailAddress'), org: str('organizationName') }
  } catch {
    cached = undefined
  }
  return cached
}

/** 「이름 · 조직」 */
export function accountLabel(a: BoardAccount | undefined): string {
  if (!a) return ''
  const name = a.name || a.email
  return [name, a.org].filter(Boolean).join(' · ')
}
