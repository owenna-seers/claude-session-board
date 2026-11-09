# 바뀐 것

## 0.2.0 — 공유판 (2026-11)

- 어느 PC 에서나 돌도록 경로를 전부 자동 탐색(홈 폴더 · 앱 설치 폴더 · 세션 기록 폴더).
- 시간대는 PC 시간대(플러그인 `TZ_OFFSET_MIN` · 위젯 `Config.TzOffsetHours` 로 고정 가능).
- 로그인 계정 · 조직을 위젯 · 패널 머리줄 오른쪽에 표시.
- 설정을 한곳에: 플러그인 `hooks/config.ts`, 위젯 `SessionWidget.cs` 맨 위 `Config`.
- 판단 기준을 `hooks/judge-prompt.ts` 로 분리. 모델 끄기(`JUDGE_MODEL = 'off'`).
- `/session-board:customize` 스킬과 `CUSTOMIZING.md`.
- 위젯 빌드 스크립트에 `-Out` · `-Stop` · `-Run`.
- 설치 직후 「갱신 없음」 문구를 「새 세션을 열면 채워집니다」로.

## 0.1.0 — 처음 (2026-11-05, 한 PC 용)

- Claude 앱 mod 패널(`/sessions`) · 윈도우 WPF 위젯 · 확인 필요 판단(wrap_up 질문 → haiku → 어림) ·
  대표 세션 하나만 읽기 · 미루기 / 확인 끝 · 리마인드 · 메모 · 여러 개 고르기 · 트레이 · 시작 프로그램.
