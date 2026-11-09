# 세션 위젯 (윈도우)

플러그인이 `~\.claude\session-board\sessions.json` 에 쓰는 현황을 1초마다 읽어, 늘 위에 떠 있는 작은 창으로 보여 준다.
Claude 앱이 다른 창 뒤에 있어도 보인다. 앱을 끄면 세션이 없으니 마지막 상태만 남는다.

## 빌드

윈도우 10/11 에 든 C# 컴파일러(.NET Framework 4.x `csc.exe`)만 쓴다. 따로 설치할 것 없음.

```
powershell -NoProfile -ExecutionPolicy Bypass -File widget\src\build.ps1 -Stop -Run
```

- 결과: `%USERPROFILE%\.claude\session-board\app\SessionWidget.exe` (`-Out <경로>` 로 바꿀 수 있음)
- `-Stop`: 떠 있는 위젯을 먼저 끈다(exe 가 잠겨 있으면 빌드가 안 됨)
- `-Run`: 빌드 뒤 바로 띄운다
- 아이콘 `app.ico` 가 없으면 `make-icon.ps1` 이 만든다

빌드 없이 쓰려면 GitHub Releases 의 `SessionWidget.exe` 를 받는다. 서명이 없어 처음 실행 때 SmartScreen 이 막으면 「추가 정보 → 실행」.

## 쓰기

- 카드 클릭: Claude 앱에서 그 세션 열기(+ 읽음 표시). 확인 필요 표시는 그대로 — 사용자가 내릴 때만 사라진다.
- 오른쪽 클릭: ★ 강조 · 읽음 / 안 읽음 · ⏸ 미루기 · ✓ 확인 끝 · － 위젯에서 빼기 · ⏰ 리마인드 · 📝 메모
- 본문은 앱 사이드바 섹션별로 묶인다(섹션 없는 세션은 맨 아래). 섹션 줄 클릭 = 접기 / 펼치기
- 앱 메인 창에서 포커스가 있는 세션 = 하늘색 테두리 + 「👁 보는 중」. 머리줄 「👁 지금 보는 세션 올리기 / 가기」 = 그 카드로(올리지 않았으면 올리고 미루기 · 확인 끝도 풀고)
- 위젯에 올린 세션만 본문에 나온다. 처음 보는 세션은 저절로 올림, 처음 켤 때는 그때 있는 세션 전부 올림(화면 그대로)
- Ctrl 클릭 / Shift 클릭 / 「☐ 여러 개 고르기」: 여러 장 골라 한꺼번에. Ctrl+A 전부, Esc 풀기
- 머리줄: 📌 항상 위, — 트레이로, ✕ 끄기
- 맨 아래 칸 탭: 「✓ 확인 끝 n」 「⏸ 미룬 것 n」 「＋ 위젯에 없는 것 n」 — 누르면 목록 맨 아래에 그 칸이 펼쳐지고 그리로 스크롤(다 0이면 줄 숨김)
- 머리줄 오른쪽: 로그인한 계정 · 조직
- 트레이 아이콘: 왼쪽 클릭 보이기 / 숨기기, 오른쪽 클릭 「윈도우 시작할 때 같이 켜기」(HKCU Run `SessionWidget`)

## 파일

- `widget-settings.json`(exe 옆): 창 자리 · 크기 · 항상 위 · 펼침 상태 · 본 세션 id(`knownIds`) · 접은 섹션
- `~\.claude\session-board\meta.json`: 강조 · 읽음 · 미루기 · 확인 끝 · 리마인드 · 메모 · 위젯에 올림(`board`) — 플러그인과 같이 쓴다
- `~\.claude\session-boardiew.json`: 앱 창에서 보고 있는 세션(플러그인 대표가 1.5초마다, 바뀔 때만 씀) — 읽기만

## 고치기

`src/SessionWidget.cs` 맨 위 `Config` → `CUSTOMIZING.md` 1절.
