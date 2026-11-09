// /sessions 패널 그리기. 위젯을 안 띄웠을 때 앱 안에서 같은 목록을 본다.

import type { On } from 'claude-code'

import type { BoardMeta, BoardSession } from '../types'
import { accountLabel } from './account'
import { POLL_MS, REMIND_PRESETS_MIN, TOMORROW_HOUR } from './config'
import { ago, clock, timeOnly, tomorrowAt } from './format'
import { poll } from './poll'
import { byRank, heldNew, isDone, isHeld, isHidden, isUnread, needsNow } from './rank'
import {
  changeMeta,
  getAccount,
  getChecked,
  getError,
  getExpanded,
  getMeta,
  getSessions,
  getShowDone,
  getShowHeld,
  refreshStatus,
  toggleExpanded,
  toggleShowDone,
  toggleShowHeld,
} from './store'

export const PANE = 'session-board'

export function registerPane(on: On) {
  on('ui.render', { component: 'Pane', requestId: PANE }, async ($, e) => {
    const { Box, Text, Button } = $.ui.resolve(e)
    const allSessions = await getSessions($)
    const meta = await getMeta($)
    const account = await getAccount($)
    const expanded = await getExpanded($)
    const showDone = await getShowDone($)
    const showHeld = await getShowHeld($)
    const error = await getError($)
    const checkedAt = await getChecked($)
    const now = checkedAt || Date.now()

    const sessions = byRank(allSessions.filter(s => !isHidden(s, meta[s.id])), meta)
    const held = byRank(allSessions.filter(s => isHeld(s, meta[s.id])), meta)
    const done = byRank(allSessions.filter(s => isDone(s, meta[s.id])), meta)
    const heldNewCount = held.filter(s => heldNew(s, meta[s.id])).length

    const running = sessions.filter(s => s.isRunning).length
    const needsCount = sessions.filter(s => needsNow(s, meta[s.id])).length
    const unread = sessions.filter(s => isUnread(s, meta[s.id])).length

    const markAllRead = async () => {
      for (const s of sessions) {
        if (!s.isRunning) await changeMeta($, s.id, m => ({ ...m, seenAt: s.lastActivityAt }))
      }
      await refreshStatus($)
    }

    const elements = $.ui.resolve(e)
    const Input = 'Input' in elements ? elements.Input : undefined

    const row = (s: BoardSession, kind: 'main' | 'held' | 'done') => {
      const m: BoardMeta = meta[s.id] ?? {}
      const last = s.lastActivityAt ? Date.parse(s.lastActivityAt) : 0
      const unreadNow = isUnread(s, m)
      const needsOpen = needsNow(s, m)
      const dim = kind !== 'main'
      const isOpen = expanded === s.id

      let state = '✓ 읽음'
      let stateColor: 'warning' | 'claude' | 'subtle' | 'error' | 'suggestion' = 'subtle'
      if (s.isRunning) {
        state = '● 작업 중'
        stateColor = 'warning'
      } else if (needsOpen) {
        state = '🙋 확인 필요'
        stateColor = 'error'
      } else if (unreadNow) {
        state = '💬 답 왔음 · 안 읽음'
        stateColor = 'claude'
      }

      const remind = m.remindAt
        ? m.remindFired
          ? `⏰ 리마인드 지남 (${clock(m.remindAt)})`
          : `⏰ ${clock(m.remindAt)}`
        : ''

      const setRemind = (at: number | undefined) =>
        void changeMeta($, s.id, x => ({ ...x, remindAt: at, remindFired: false })).then(() =>
          refreshStatus($),
        )
      // 미루기와 확인 끝은 둘 중 하나만
      const setHold = (hold: boolean) =>
        void changeMeta($, s.id, x => ({
          ...x,
          heldAt: hold ? s.lastActivityAt : undefined,
          hiddenAt: undefined,
        })).then(() => refreshStatus($))
      const setDone = (finish: boolean) =>
        void changeMeta($, s.id, x => ({
          ...x,
          hiddenAt: finish ? s.lastActivityAt : undefined,
          heldAt: undefined,
        })).then(() => refreshStatus($))

      return (
        <Box key={`row-${kind}-${s.id}`} flexDirection="column">
          <Button
            key={`open-${s.id}`}
            plain
            onPress={() => void toggleExpanded($, s.id)}
          >
            {kind === 'held' ? '(미룸) ' : kind === 'done' ? '(확인 끝) ' : ''}
            {m.star ? '★ ' : ''}
            {s.title}
          </Button>
          <Text color={stateColor} bold={!dim && (unreadNow || needsOpen)} dimColor={dim}>
            {state} · {last ? `${ago(last, now)} (${clock(last)})` : '시간 모름'} ·{' '}
            {s.remote ? '📡 원격 켜짐' : '원격 꺼짐'}
          </Text>
          {kind === 'held' && heldNew(s, m) ? <Text color="claude">💬 미룬 뒤 새 답 있음</Text> : null}
          {needsOpen && s.ask ? <Text color="error" dimColor={dim}>→ {s.ask}</Text> : null}
          {remind ? <Text color={m.remindFired ? 'error' : 'suggestion'}>{remind}</Text> : null}
          {m.memo ? <Text dimColor>📝 {m.memo}</Text> : null}

          {isOpen ? (
            <Box flexDirection="column" paddingLeft={2} gap={1}>
              <Box flexDirection="row" flexWrap="wrap" gap={1}>
                <Button
                  key={`star-${s.id}`}
                  onPress={() => void changeMeta($, s.id, x => ({ ...x, star: !x.star }))}
                >
                  {m.star ? '강조 해제' : '★ 강조'}
                </Button>
                <Button
                  key={`read-${s.id}`}
                  onPress={() =>
                    void changeMeta($, s.id, x => ({
                      ...x,
                      seenAt: unreadNow ? s.lastActivityAt : '2000-01-01T00:00:00.000Z',
                    })).then(() => refreshStatus($))
                  }
                >
                  {unreadNow ? '읽음으로' : '안 읽음으로'}
                </Button>
                <Button key={`hold-${s.id}`} onPress={() => setHold(!m.heldAt)}>
                  {m.heldAt ? '⬆ 미루기 풀기' : '⏸ 미루기(일단 보류)'}
                </Button>
                <Button key={`done-${s.id}`} onPress={() => setDone(kind !== 'done')}>
                  {kind === 'done' ? '⬆ 다시 올리기' : '✓ 확인 끝'}
                </Button>
              </Box>
              <Text dimColor>리마인드 알람</Text>
              <Box flexDirection="row" flexWrap="wrap" gap={1}>
                {REMIND_PRESETS_MIN.map(min => (
                  <Button key={`r${min}-${s.id}`} onPress={() => setRemind(now + min * 60_000)}>
                    {min < 60 ? `${min}분 뒤` : `${min / 60}시간 뒤`}
                  </Button>
                ))}
                <Button key={`rtm-${s.id}`} onPress={() => setRemind(tomorrowAt(now))}>
                  내일 {TOMORROW_HOUR}시
                </Button>
                {m.remindAt ? (
                  <Button key={`roff-${s.id}`} onPress={() => setRemind(undefined)}>알람 끄기</Button>
                ) : null}
              </Box>
              {Input ? (
                <Input
                  key={`memo-${s.id}`}
                  label="메모 (알람에 같이 뜸)"
                  placeholder="예: 회신 확인, 내일 배포 전 점검"
                  value={m.memo ?? ''}
                  submitLabel="저장"
                  onSubmit={v => void changeMeta($, s.id, x => ({ ...x, memo: v.trim() || undefined }))}
                />
              ) : null}
            </Box>
          ) : null}
        </Box>
      )
    }

    const who = accountLabel(account)

    return (
      <Box flexDirection="column" gap={1}>
        <Box flexDirection="column">
          <Text bold>
            확인 필요 {needsCount} · 작업 중 {running} · 안 읽음 {unread}
            {who ? `   ${who}` : ''}
          </Text>
          <Text dimColor>
            {checkedAt
              ? `세션 ${sessions.length}개 · 갱신 ${timeOnly(checkedAt)} (${Math.round(POLL_MS / 1000)}초마다)`
              : '불러오는 중…'}
          </Text>
          <Box flexDirection="row" flexWrap="wrap" gap={1}>
            <Button key="refresh" onPress={() => void poll($)}>새로고침</Button>
            <Button key="all-read" onPress={() => void markAllRead()}>모두 읽음</Button>
            {held.length > 0 ? (
              <Button key="show-held" onPress={() => void toggleShowHeld($)}>
                {showHeld
                  ? '미룬 것 감추기'
                  : `⏸ 미룬 것 ${held.length}${heldNewCount ? ` (새 답 ${heldNewCount})` : ''}`}
              </Button>
            ) : null}
            {done.length > 0 ? (
              <Button key="show-done" onPress={() => void toggleShowDone($)}>
                {showDone ? '확인 끝 감추기' : `✓ 확인 끝 ${done.length}`}
              </Button>
            ) : null}
          </Box>
          {error ? <Text color="error">{error}</Text> : null}
        </Box>

        {sessions.map(s => row(s, 'main'))}

        {showHeld && held.length > 0 ? (
          <Box key="sec-held" flexDirection="column" gap={1}>
            <Text dimColor>⏸ 미룬 것 — 「미루기 풀기」를 누를 때까지 여기 · 알림 없음 · 리마인드는 울림</Text>
            {held.map(s => row(s, 'held'))}
          </Box>
        ) : null}

        {showDone && done.length > 0 ? (
          <Box key="sec-done" flexDirection="column" gap={1}>
            <Text dimColor>✓ 확인 끝 — 새 대화가 시작되거나 새 답이 오면 다시 올라옴</Text>
            {done.map(s => row(s, 'done'))}
          </Box>
        ) : null}
      </Box>
    )
  })
}
