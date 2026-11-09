# 고치는 법

바꾸고 싶은 것이 어느 파일의 무엇인지. 세션에서 「~ 바꿔 줘」 하면 에이전트가 `/session-board:customize` 로 이 문서를 따른다.
손으로 고칠 때도 같은 자리다.

## 1. 위젯 (widget/src/SessionWidget.cs)

맨 위 `static class Config` 에 모여 있다.

1. 창 제목 · 트레이 글 → `Title`
2. 플러그인과 나눠 쓰는 폴더 → `ShareDirUnderHome` (플러그인 `SHARE_DIR` 과 같은 자리여야 함)
3. 파일을 다시 읽는 주기 → `PollSeconds` · 「갱신 없음」 판정 → `StaleSeconds`
4. 시간대 → `TzOffsetHours` (null = PC 시간대, 9 = 한국) · 시각 양식 → `ClockFormat` · `TimeFormat`
5. 「내일 n시」 → `TomorrowHour`
6. 카드 색 → `Color*` (작업 중 · 확인 필요 · 답 왔음 · 읽음 · 리마인드 지남 · 강조 · 원격)
7. 머리줄 문구 → `MsgWaiting` · `MsgStale`
8. 창 바탕 · 테두리 · 버튼 모양 · 글꼴 · 기본 크기 → `const string Xaml` 안(`Background='#EE1E1F24'` · `FontFamily='Malgun Gothic'` · `Width='340'` 등)
9. 카드 한 장의 구성 → `MakeCard`, 오른쪽 클릭 메뉴 → 같은 함수 아래쪽 `AddItem(...)`
10. 리마인드 시간 종류 → `MakeCard` 의 `⏰` 항목들과 `DoAction` 의 `remind*`

고친 뒤: 떠 있는 위젯을 끄고 다시 빌드.

```
powershell -NoProfile -ExecutionPolicy Bypass -File widget\src\build.ps1 -Stop -Run
```

C# 5 문법만 된다(윈도우 기본 csc). `$"..."` 보간 · `?.` · `nameof` 는 안 된다.

## 2. 플러그인 (hooks/)

설정값은 `config.ts` 한 파일.

1. 목록 읽는 주기 → `POLL_MS` · 한 번에 가져오는 세션 수 → `LIST_LIMIT`
2. 확인 필요 판단 모델 → `JUDGE_MODEL` (`'haiku'` · `'sonnet'` · `'off'`), 최근 며칠 → `JUDGE_WITHIN_MS`, 한 번에 몇 개 → `JUDGE_PER_POLL`
3. 시간대 → `TZ_OFFSET_MIN` (undefined = PC 시간대, 540 = 한국)
4. 나눠 쓰는 폴더 → `SHARE_DIR`
5. 알림이 떠 있는 시간 → `TOAST_MS` · `REMIND_TOAST_MS`, 패널 리마인드 버튼 → `REMIND_PRESETS_MIN` · `TOMORROW_HOUR`

기준과 그림은 파일별로.

6. **확인 필요 기준** → `judge-prompt.ts` (`JUDGE_SYSTEM` 글 · 어림 낱말 `GUESS_RE`). 고치면 `config.ts` 의 `JUDGED_KEY` 와 `LEADER_V` 를 올린다.
7. 판단 흐름(wrap_up 질문 → 모델 → 어림) → `judge.ts`
8. 카드 상태 판별 · 정렬 → `rank.ts` (위젯의 「상태 판단」과 같은 규칙으로 맞춘다)
9. 시간 표기 → `format.ts`
10. 경로 찾기 → `paths.ts`
11. 계정 · 조직 표시 → `account.ts`
12. 패널(`/sessions`) 그림 → `pane.tsx`
13. 주기 · 대표 세션 · 위젯용 파일 쓰기 · 알림 → `register.tsx`

고친 뒤: `claude plugin validate <폴더>` → 세션에서 `/reload-plugins`. 다른 세션은 새로 열 때부터.

## 3. 규칙

1. `LEADER_V` · `JUDGED_KEY`: 판단 · 대표 방식을 고칠 때마다 올린다. 플러그인 코드는 세션이 시작될 때 실리므로, 고치기 전에 열린 세션이 대표면 옛 판단이 위젯에 남는다. 판이 높은 세션이 자리를 넘겨받는다.
2. `sessions.json` 에 값을 더하면 `types/index.d.ts` 의 `BoardSession` · `BoardFile` 과 위젯 `Session` 클래스 · `Poll()` 에 같이 더한다.
3. `meta.json` 의 키(`star` · `seenAt` · `heldAt` · `hiddenAt` · `remindAt` · `remindFired` · `memo` · `board`)는 플러그인과 위젯이 같이 쓴다. 바꾸면 둘 다. `view.json`({focused, visible}) 은 플러그인이 쓰고 위젯이 읽는다.
4. 옛 `claude` 명령(2.1.2xx)은 hooks 모듈을 모른다. 앱에 든 것을 쓴다 — `CLAUDE.md` 1번.
5. 검사기(`claude plugin validate`)가 거는 두 가지: ① atom 의 `read` · `update` 는 atom 을 선언한 파일(`store.ts`) 안에서만 — 다른 파일은 `store.ts` 의 `get*` · `set*` 함수로. ② `$` 를 다른 파일의 함수에 넘길 때 그 함수는 `function` 선언이어야 한다(`const f = async ($) => …` 는 거부).
6. 줄 끝은 LF. `.ps1` 은 UTF-8 BOM(PowerShell 5.1 이 한글을 읽는 조건).

## 4. 타입 검사(선택)

플러그인이 한 번 실리면 엔진이 `.claude-plugin/types/` 에 API 선언과 `tsconfig.json` 을 써 준다. 그 뒤 저장소 뿌리에서 `tsc -p .` 가 된다(`tsconfig.json` 이 그것을 extends).
