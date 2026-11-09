// 이 PC 의 경로를 찾는다. 고정된 경로는 하나도 없다.
//  - 공유 폴더(위젯과 나눠 쓰는 sessions.json · meta.json · leader.json · view.json)
//  - Claude 앱이 세션마다 두는 파일(cliSessionId · cwd) 의 폴더들
//  - 세션 기록(jsonl) 폴더
//  - 로그인 계정 정보(~/.claude.json)

import type { EngineInterface } from 'claude-code'

import { SHARE_DIR } from './config'

export type Paths = {
  shareDir: string
  sessionsFile: string
  metaFile: string
  leaderFile: string
  /** 앱 창에서 보고 있는 세션(대표가 씀, 위젯이 읽음) */
  viewFile: string
  /** 세션 기록 jsonl: <projectsDir>/<cwd 를 비영숫자→- 로>/<cliSessionId>.jsonl */
  projectsDir: string
  /** 로그인 계정(oauthAccount) 이 든 파일 */
  claudeJson: string
  /** 앱 세션 파일 뿌리들(앱 설치마다 하나). 그 아래 계정 · 조직 폴더 두 겹은 judge.ts 가 찾는다 */
  appSessionsRoots: string[]
}

const slash = (p: string) => p.replace(/\\/g, '/').replace(/\/+$/, '')

let cached: Paths | undefined

export async function getPaths($: EngineInterface): Promise<Paths> {
  if (cached) return cached
  const home = slash((await $.env.get('USERPROFILE')) ?? (await $.env.get('HOME')) ?? '')
  const configDirEnv = await $.env.get('CLAUDE_CONFIG_DIR')
  const configDir = configDirEnv ? slash(configDirEnv) : `${home}/.claude`
  const shareDir = SHARE_DIR ? slash(SHARE_DIR) : `${home}/.claude/session-board`
  const localAppData = slash((await $.env.get('LOCALAPPDATA')) ?? `${home}/AppData/Local`)

  const appSessionsRoots: string[] = []
  try {
    for (const e of await $.fs.list(`${localAppData}/Claude-Data`)) {
      if (e.kind !== 'dir' || !e.name.startsWith('claude_')) continue
      const root = `${localAppData}/Claude-Data/${e.name}/claude-code-sessions`
      if (await $.fs.exists(root)) appSessionsRoots.push(root)
    }
  } catch {
    // 앱 폴더가 없으면(앱 밖에서 돌 때) 판단은 list_events 글로만
  }

  cached = {
    shareDir,
    sessionsFile: `${shareDir}/sessions.json`,
    metaFile: `${shareDir}/meta.json`,
    leaderFile: `${shareDir}/leader.json`,
    viewFile: `${shareDir}/view.json`,
    projectsDir: `${configDir}/projects`,
    claudeJson: configDirEnv ? `${configDir}/.claude.json` : `${home}/.claude.json`,
    appSessionsRoots,
  }
  return cached
}

/** 세션 기록 폴더 이름: cwd 의 영숫자가 아닌 글자를 - 로 */
export const projectFolder = (cwd: string) => cwd.replace(/[^A-Za-z0-9]/g, '-')
