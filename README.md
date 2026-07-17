# PulseBar

Windows 작업표시줄에서 시스템 자원과 AI 코딩 도구(Claude Code, Codex) 사용량을 한눈에 확인하는 경량 모니터링 앱.

A lightweight monitoring app that shows system resources and AI coding tool (Claude Code, Codex) usage at a glance on the Windows taskbar.

```text
CPU 14  RAM 47  GPU 82  VRAM 9.8G  D 4  ↓32M ↑2M
Claude 5h 41 · W 68  |  Codex 5h 25 · W 18
```

## 기능 / Features

- **시스템 자원 (1초 갱신)** — CPU(GetSystemTimes), RAM, GPU/VRAM(PDH+DXGI, 어댑터별 busiest-engine 집계), 디스크 활성도/속도, 네트워크 업다운 (영문 PDH 카운터 — OS 언어 무관)
- **Claude Code 구독 사용량** — 공식 statusline JSON을 브리지로 수신: 5시간/7일 사용률, 리셋 시각. 10분 이상 오래된 값은 `오래됨` 표시
- **Fable 토큰 (이 PC 기준)** — 공식 OpenTelemetry(OTLP/JSON, 127.0.0.1 전용)로 모델별 토큰 집계. 서버 쿼터와 절대 혼동하지 않도록 별도 표기
- **Codex 사용량** — 공식 `codex app-server` JSON-RPC: rate limit 버킷(기간 기반 분류), 리셋 시각, 플랜, 크레딧, 토큰 활동
- **Windows/WSL 모두 지원** — CLI 자동 탐지 (`where.exe` / `wsl.exe -d <distro> -- sh -lc "command -v ..."`)
- **한국어/영어 UI** — 설정에서 즉시 전환
- 관리자 권한 불필요, Explorer 주입 없음, 자격 증명 접근 없음, 외부 텔레메트리 없음

## 요구 사항 / Requirements

- Windows 10/11 x64 (portable ZIP은 self-contained — 런타임 설치 불필요)
- 소스 빌드 시: .NET 8 SDK 이상

## 빌드 / Build

```powershell
dotnet restore
dotnet build PulseBar.sln -c Release
dotnet test PulseBar.sln -c Release --no-build
```

### Portable 패키지 / Portable package

```powershell
powershell -ExecutionPolicy Bypass -File packaging/portable/build-portable.ps1
# → artifacts/PulseBar-portable-win-x64.zip (+ .sha256)
```

## 사용 / Usage

1. `PulseBar.exe` 실행 → 시스템 자원은 즉시 작업표시줄 트레이 왼쪽에 표시됩니다.
2. Claude/Codex CLI는 첫 실행 시 자동 탐지되어 프로필이 생성됩니다.
3. **Claude 구독 사용량**: statusline이 없으면 자동 등록(원본은 백업), 이미 있으면 로그에 안내되는
   `--passthrough` 래퍼 명령으로 수동 병합합니다. `docs/troubleshooting.md` 참고.
4. **Fable 토큰**: 트레이 우클릭 → *Fable 토큰 수집 연동 (OTel)* — 명시적 opt-in입니다.
   프롬프트/응답 내용은 절대 수집하지 않습니다(토큰 수와 요청 메타데이터만).
5. 트레이/오버레이 우클릭: 새로고침, 설정(언어·레이아웃), 시작 프로그램 등록, 로그 폴더, 종료.
6. 오버레이에 마우스를 올리면 리셋 시각·플랜·Fable 토큰 상세 툴팁이 표시됩니다.

## 데이터 범위 주의 / Data-scope caveat

```text
Claude 7일 사용률 68%        # 서버 계정 quota (statusline 제공 공식 값)
Fable 최근 7일 12.4M tokens  # 이 PC의 Claude Code telemetry (다른 기기/웹 미포함)
```

두 수치는 다른 것이며 PulseBar는 이를 절대 하나로 합쳐 표시하지 않습니다.

## 구조 / Structure

- `src/PulseBar.App` — WPF 앱 (taskbar overlay UI, DI host)
- `src/PulseBar.Core` — 모델/설정/로컬라이제이션/포매터
- `src/PulseBar.Windows` — 시스템 메트릭 수집, taskbar interop, CLI 탐지, DPAPI
- `src/PulseBar.Providers.Codex` — codex app-server JSON-RPC provider
- `src/PulseBar.Providers.Claude` — statusline/OTel 파서, settings 설치기, provider
- `src/PulseBar.Bridge` — statusline 브리지 + WSL OTel 헬퍼 실행 파일
- `src/PulseBar.Storage` — SQLite 토큰 이벤트 저장소
- `docs/` — 아키텍처/프로바이더 계약/보안/트러블슈팅

## 언어 / Language

UI는 한국어(ko-KR)와 영어(en-US)를 지원합니다. 트레이 → 설정 → 언어에서 변경합니다.

The UI supports Korean (ko-KR) and English (en-US); switch it under tray → Settings → Language.
