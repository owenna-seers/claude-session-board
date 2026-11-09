---
name: customize
description: 세션 현황판(session-board) 플러그인 · 위젯을 사용자 말대로 고친다 — 확인 필요 기준 · 색 · 양식 · 주기 · 문구 · 기능 추가. 「현황판 ~ 바꿔 줘」 「위젯 ~ 해 줘」 「확인 필요 기준 ~」 같은 요청에 쓴다.
---

# 세션 현황판 고치기

사용자가 세션 현황판(플러그인 session-board 또는 윈도우 위젯)을 바꾸고 싶어 할 때 따른다. 어느 프로젝트 세션에서든 된다.

## 1. 폴더 찾기

플러그인은 설치 사본이 아니라 **등록한 폴더에서 바로 읽힌다**(폴더 마켓). 그 폴더가 정본이다.

1. `~/.claude/plugins/known_marketplaces.json` 을 읽어 이름이 `session-board` 인 항목의 `installLocation`(또는 `source.path`)을 찾는다. 그 폴더에 `.claude-plugin/plugin.json` 과 `CUSTOMIZING.md` 가 있어야 한다.
2. 없으면 사용자에게 저장소를 어디에 받았는지 묻는다.

## 2. 무엇을 고칠지 정하기

그 폴더의 `CUSTOMIZING.md` 를 읽는다. 「바꾸고 싶은 것 → 파일 · 상수」 대조표와 규칙이 있다. 사용자 말이 대조표의 어느 줄인지 정하고, 고칠 파일을 읽은 뒤 고친다. 대조표에 없는 기능 추가는 플러그인(`sessions.json` 에 싣기)과 위젯(그리기) 두 쪽을 함께 본다.

## 3. 규칙

1. 판단 기준(`hooks/judge-prompt.ts`)이나 대표 선출 · 판단 흐름(`hooks/judge.ts` · `hooks/register.tsx`)을 고쳤으면 `hooks/config.ts` 의 `JUDGED_KEY` 이름과 `LEADER_V` 숫자를 하나 올린다. 안 올리면 옛 코드로 도는 세션이 대표 자리를 쥐고 있어 고친 것이 위젯에 안 나온다.
2. 플러그인을 고친 뒤 `claude plugin validate <폴더>` 로 검사한다(옛 판 `claude` 면 앱에 든 `claude.exe` 를 쓴다 — `CLAUDE.md` 1번).
3. 위젯(`widget/src/SessionWidget.cs`)을 고쳤으면 `powershell -NoProfile -ExecutionPolicy Bypass -File <폴더>\widget\src\build.ps1 -Stop -Run` 으로 떠 있는 위젯을 끄고 다시 빌드해 띄운다. C# 5 문법만 된다(`$""` 보간 · `?.` 안 됨).
4. 끝나면 사용자에게 「`/reload-plugins` 를 치면 이 세션에 반영됩니다. 다른 세션은 새로 열 때부터입니다」라고 안내한다.
5. 이 폴더 밖은 고치지 않는다. `sessions.json` · `meta.json`(사용자의 표시 상태)은 건드리지 않는다.
