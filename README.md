# PulseBar

Windows 작업표시줄에서 시스템 자원과 AI 코딩 도구(Claude Code, Codex) 사용량을 한눈에 확인하는 경량 모니터링 앱.

A lightweight monitoring app that shows system resources and AI coding tool (Claude Code, Codex) usage at a glance on the Windows taskbar.

## 요구 사항 / Requirements

- Windows 10/11 x64
- .NET 8 Desktop Runtime (self-contained 배포 시 불필요 / not needed for self-contained builds)
- 관리자 권한 불필요 / No administrator rights required

## 빌드 / Build

```powershell
dotnet restore
dotnet build PulseBar.sln -c Release
dotnet test PulseBar.sln -c Release --no-build
```

## 구조 / Structure

- `src/PulseBar.App` — WPF 앱 (taskbar overlay UI)
- `src/PulseBar.Core` — 공통 모델/인터페이스/설정
- `src/PulseBar.Windows` — Windows 시스템 자원 수집 (CPU/RAM/GPU/Disk/Network)
- `src/PulseBar.Providers.Codex` — Codex app-server JSON-RPC provider
- `src/PulseBar.Providers.Claude` — Claude statusline/OTel provider
- `src/PulseBar.Bridge` — Claude Code statusline bridge 실행 파일
- `src/PulseBar.Storage` — SQLite 저장소
- `docs/` — 아키텍처/보안/트러블슈팅 문서

## 언어 / Language

UI는 한국어(ko-KR)와 영어(en-US)를 지원합니다. 설정에서 변경할 수 있습니다.

The UI supports Korean (ko-KR) and English (en-US), switchable in settings.
