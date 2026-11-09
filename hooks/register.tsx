// 세션 현황판 — 등록
//  session.start: 저장된 표시 · 판단을 되살리고, /sessions 명령을 두고, 주기 작업(poll.ts)을 돌린다
//  command.run /sessions: 패널(pane.tsx)을 연다
// 바꾸고 싶은 값은 config.ts, 판단 기준은 judge-prompt.ts.

import type { Register } from 'claude-code'

import type { BoardJudgedMap, BoardMetaMap } from '../types'
import { JUDGED_KEY, POLL_MS, STORE_KEY, TITLE, VIEW_MS } from './config'
import { PANE, registerPane } from './pane'
import { poll, pollView } from './poll'
import { setJudged, setMeta } from './store'

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const saved = (await $.store.get(STORE_KEY)) as BoardMetaMap | undefined
    if (saved && typeof saved === 'object') await setMeta($, saved)
    const judged = (await $.store.get(JUDGED_KEY)) as BoardJudgedMap | undefined
    if (judged && typeof judged === 'object') await setJudged($, judged)

    await $.command.register({
      name: 'sessions',
      description: `${TITLE} 열기`,
    })
    // 모든 세션에 실리므로 패널은 /sessions 로 열 때만
    void poll($)
    $.clock.every(POLL_MS, () => void poll($))
    $.clock.every(VIEW_MS, () => void pollView($))
    return next(e)
  })

  on('command.run', { command: 'sessions' }, async $ => {
    await $.ui.open({ id: PANE, title: TITLE })
    void poll($)
    return { text: `${TITLE}을 열었습니다.` }
  })

  registerPane(on)
}
