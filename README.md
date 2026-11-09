# 세션 현황판 (session-board)

Claude 데스크톱 앱(Code 탭)에서 세션을 여러 개 돌리다 보면 어느 세션이 답을 기다리는지 잊는다.
이 도구는 그 현황을 **늘 위에 떠 있는 윈도우 위젯**으로 보여 준다 — 작업 중 · 답 왔음 · 🙋 확인 필요(무엇을 답해야 하는지 한 줄) · 미루기 · 확인 끝 · 리마인드 · 메모.
위젯은 Claude 앱이 다른 창 뒤에 있어도 보이고, 카드를 누르면 앱이 그 세션으로 넘어간다.

두 부분으로 되어 있다.

1. **플러그인**(이 저장소) — Claude Code 플러그인. 앱 안의 세션에 실려 세션 목록을 읽고, 「확인 필요」를 판단해 `~\.claude\session-board\sessions.json` 에 쓴다. `/sessions` 패널도 준다.
2. **위젯**(`widget/`) — C# WPF 창. 그 파일을 1초마다 읽어 보여 준다. 위젯 혼자서는 현황이 돌지 않는다 — 플러그인이 있어야 한다.

> 윈도우 + Claude 데스크톱 앱(Code 탭) 전용. 앱 내부 파일에 기대므로 앱 업데이트로 깨질 수 있다. 확인한 앱의 Claude Code 판: 2.1.317.

## 설치

### 1. 프롬프트 한 줄 (권함)

1. 이 저장소를 받는다. `git clone https://github.com/owenna-seers/claude-session-board` 또는 「Code → Download ZIP」을 받아 **둘 자리**에 푼다(이 폴더가 그대로 정본이 된다 — `Downloads` 말고).
2. Claude 앱 → Code 탭에서 그 폴더를 열고 **「설치해 줘」**.
3. 에이전트가 `CLAUDE.md` 를 따라 플러그인을 설치하고 위젯을 빌드해 띄운다. 바탕화면 바로가기 · 윈도우 시작 때 자동 실행은 물어본다.
4. 설치 직후 위젯은 「현황 기다리는 중」이다. **새 세션을 하나 열면** 채워진다(플러그인은 새로 시작한 세션부터 실린다).

### 2. 손으로

터미널에서(옛 판 `claude` 면 앱에 든 `claude.exe` — `CLAUDE.md` 1번):

```
claude plugin marketplace add <받은 폴더의 절대 경로>
```

```
claude plugin install session-board@session-board
```

```
powershell -NoProfile -ExecutionPolicy Bypass -File <받은 폴더>\widget\src\build.ps1 -Run
```

앱에서 하려면 + → Plugins → Add marketplace 에 폴더 경로.

### 3. exe 만

Releases 의 `SessionWidget.exe`. 서명이 없어 SmartScreen 이 막으면 「추가 정보 → 실행」. 플러그인은 1 또는 2 로 따로 설치해야 한다.

## 쓰기

- 윈도우가 켜지면 위젯이 같이 뜬다(트레이 메뉴 「윈도우 시작할 때 같이 켜기」를 켰다면). Claude 앱에서 평소처럼 세션을 돌린다.
- 세션이 끝나면 카드가 「💬 답 왔음」 또는 「🙋 확인 필요 → <질문>」으로 바뀌고 알림이 뜬다.
- 카드 클릭 → 앱이 그 세션으로. 오른쪽 클릭 → ⏸ 미루기 · ✓ 확인 끝 · ⏰ 리마인드 · 📝 메모 · ★ 강조.
- 여러 장은 Ctrl · Shift 클릭으로 골라 한꺼번에.
- 머리줄 오른쪽에 로그인한 계정 · 조직.
- 앱 안에서는 `/sessions` 로 같은 목록을 패널로 본다.

자세한 것은 [widget/README.md](widget/README.md).

## 바꾸기

어느 세션에서든 말로 한다 — 「확인 필요 기준을 더 엄격하게」 「위젯 글씨 키워」. 에이전트가 `/session-board:customize` 스킬로 이 폴더를 찾아 고친다.
손으로 고치려면 [CUSTOMIZING.md](CUSTOMIZING.md). 플러그인은 설치 사본이 아니라 **이 폴더에서 바로 읽히므로** 고치고 `/reload-plugins` 하면 된다. 위젯은 `build.ps1 -Stop -Run`.

## 업데이트 · 지우기

- 업데이트: `git pull`(ZIP 이면 새 ZIP 을 같은 자리에 덮어쓰기) → 세션에서 `/reload-plugins` → 위젯은 「위젯 다시 빌드해 줘」 또는 Releases 의 새 exe.
- 지우기: 세션에서 「session-board 지워 줘」, 또는 손으로 — `claude plugin uninstall session-board@session-board` · `claude plugin marketplace remove session-board` · 위젯 끄기 · 트레이 메뉴에서 시작 프로그램 끄기 · `~\.claude\session-board\` 삭제.

## 어떻게 도는가

1. 플러그인은 앱의 모든 세션에 실린다. `~\.claude\session-board\leader.json` 으로 **대표 세션 하나**만 세션 목록을 읽고 판단하고 `sessions.json` 을 쓴다. 대표가 15초 넘게 조용하면 다른 세션이 이어받는다.
2. 「확인 필요」 판단: 그 세션의 마지막 턴이 다음 단계 상자에 남긴 질문이 있으면 그대로 → 없으면 마지막 답을 haiku 에게 보여 「사용자 답 없이는 못 나아가는가」만 묻는다 → 모델이 없으면 낱말 어림. 답 시각마다 한 번만, 최근 3일 · 한 번에 4개.
3. 표시(강조 · 읽음 · 미루기 · 확인 끝 · 리마인드 · 메모)는 `meta.json` 을 플러그인과 위젯이 같이 읽고 쓴다.

## 한계

1. 윈도우 + Claude 데스크톱 앱 전용. 위젯 빌드는 윈도우에 든 `csc.exe`(.NET Framework 4.x)를 쓴다.
2. 앱 내부 파일(세션 파일 · 세션 기록 · `ccd_session_mgmt` 도구)에 기댄다. 앱이 바뀌어 「확인 필요」가 안 잡히면 「답 왔음」까지만 보인다.
3. haiku 판단은 각자의 사용량을 쓴다. `hooks/config.ts` 의 `JUDGE_MODEL = 'off'` 로 끌 수 있다.
4. 열린 세션이 하나도 없으면 현황이 멈춘다. 앱을 끄면 위젯은 마지막 상태와 「갱신 없음」만 보인다.
5. 앱 사이드바의 안 읽음 점과는 연결되지 않는다.
