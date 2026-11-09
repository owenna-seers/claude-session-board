# session-board — 에이전트 안내

이 폴더는 Claude Code 플러그인 「세션 현황판」의 저장소이자 플러그인 마켓이다. 윈도우 + Claude 데스크톱 앱(Code 탭) 전용.
사용자가 「설치해 줘」 「세팅해 줘」 「init 해 줘」 라고 하면 아래 **설치**를, 「지워 줘」 라고 하면 **지우기**를 따른다.
고치는 요청은 `CUSTOMIZING.md`(또는 설치 뒤 `/session-board:customize` 스킬).

## 설치

이 폴더를 플러그인 마켓으로 등록하고(설치 사본이 아니라 이 폴더에서 바로 읽힌다), 플러그인을 user 범위로 설치하고, 위젯을 빌드해 띄운다.
사용자에게 묻는 것은 5번 하나뿐이다. 명령은 PowerShell 로 돌린다.

1. **claude 명령 고르기.** `claude --version` 이 2.1.300 이상이면 그대로 `claude`. 그보다 낮거나 없으면(옛 판은 hooks 모듈을 모른다) 앱에 든 것을 찾는다:
   `Get-ChildItem "$env:LOCALAPPDATA\Claude-Data\claude_*\claude-code\*\*\claude.exe" | Sort-Object FullName -Descending | Select-Object -First 1`
   아래 `<claude>` 는 그 경로다. 둘 다 없으면 사용자에게 Claude 데스크톱 앱이 설치돼 있는지 묻고 멈춘다.
2. **마켓 등록.** `<claude> plugin marketplace add "<이 폴더의 절대 경로>"`. 「already exists」 류의 말이 나오면 그대로 간다.
3. **플러그인 설치.** `<claude> plugin install session-board@session-board --scope user`. 「Installed」 또는 이미 설치돼 있다는 말이면 성공.
4. **위젯 빌드 · 실행.** `powershell -NoProfile -ExecutionPolicy Bypass -File "<이 폴더>\widget\src\build.ps1" -Stop -Run`
   결과 exe 는 `%USERPROFILE%\.claude\session-board\app\SessionWidget.exe`. 화면 구석에 위젯이 뜬다.
5. **묻기(한 번에).** 「바탕화면에 바로가기를 만들고, 윈도우가 시작할 때 위젯을 같이 켤까요?」
   - 바로가기: `$s = (New-Object -ComObject WScript.Shell).CreateShortcut("$env:USERPROFILE\Desktop\세션 위젯.lnk"); $s.TargetPath = "<exe>"; $s.Save()`
   - 시작 프로그램: `Set-ItemProperty -Path HKCU:\Software\Microsoft\Windows\CurrentVersion\Run -Name SessionWidget -Value '"<exe>"'` (위젯 트레이 메뉴에서도 켜고 끌 수 있다)
6. **마지막 안내.** 이렇게 말한다:
   - 지금 위젯은 「현황 기다리는 중」이다. 플러그인은 새로 시작한 세션부터 실리니, **Claude 앱에서 새 세션을 하나 열면** 1초 안에 채워진다.
   - 앱 안에서는 `/sessions` 로 같은 목록을 패널로 볼 수 있다.
   - 바꾸고 싶으면 어느 세션에서든 「현황판 ~ 바꿔 줘」(`/session-board:customize`).

## 확인

설치가 됐는지 보려면 `<claude> plugin list` 에 `session-board@session-board` 가 있고 `Read from:` 이 이 폴더인지, 새 세션을 연 뒤 `%USERPROFILE%\.claude\session-board\sessions.json` 이 5초마다 바뀌는지 본다.

## 지우기

1. 위젯 끄기: `Get-Process SessionWidget -ErrorAction SilentlyContinue | Stop-Process`
2. 시작 프로그램: `Remove-ItemProperty -Path HKCU:\Software\Microsoft\Windows\CurrentVersion\Run -Name SessionWidget -ErrorAction SilentlyContinue`, 바탕화면 `세션 위젯.lnk` 삭제
3. 플러그인: `<claude> plugin uninstall session-board@session-board` → `<claude> plugin marketplace remove session-board`
4. 사용자에게 묻고 `%USERPROFILE%\.claude\session-board\` 삭제(표시 상태 meta.json 이 들어 있다)

## 하지 말 것

- 이 폴더 밖의 파일을 직접 고치지 않는다(`settings.json` 은 `claude plugin` 명령이 고친다).
- `SessionWidget.exe` · `app.ico` · `widget-settings.json` 을 git 에 넣지 않는다(.gitignore).
- `sessions.json` · `meta.json` · `leader.json` 을 손으로 고치지 않는다.
