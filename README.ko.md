# PulseBar

[English](README.md) | **한국어**

Windows 작업표시줄에서 **시스템 자원**과 **AI 코딩 도구 사용량**(Claude Code, Codex)을 트레이 바로 옆에서 실시간으로 보여주는 경량 위젯입니다.

![Windows 작업표시줄의 PulseBar 오버레이](docs/images/overlay.png)

개발자가 하루 종일 궁금해하는 세 가지를 한 눈에 답합니다:

1. *지금 내 PC가 힘들어하나?* — CPU · RAM · GPU · VRAM · 디스크 · 네트워크, 1초 갱신
2. *Claude 쿼터가 얼마나 남았지?* — 공식 5시간/주간 사용률과 리셋 시각
3. *Codex 쿼터는?* — 공식 rate-limit 버킷, 플랜, 크레딧

관리자 권한 불필요. Electron 아님. 자격 증명 접근 없음. 클라우드 없음. 작은 WPF 앱 하나입니다 (유휴 CPU < 0.5%).

---

## 바 읽는 법

```
CPU 11  RAM 62  GPU 39  VRAM 3.5G  D 0  ↓5K ↑14K
Claude 5h 69 · W 33  |  Codex W 12
```

| 토큰 | 의미 |
|---|---|
| `CPU 11` | 전체 CPU 사용률 (%) |
| `RAM 62` | 물리 메모리 사용률 (%) |
| `GPU 39` | 주 어댑터의 busiest-engine GPU 사용률 (%) — 작업 관리자와 같은 방식 |
| `VRAM 3.5G` | 전용 비디오 메모리 사용량 |
| `D 0` | 디스크 활성도 (%) |
| `↓5K ↑14K` | 네트워크 다운/업 (초당 바이트) |
| `Claude 5h 69 · W 33` | Claude 구독 사용량: 5시간 창 69%, 주간 창 33% |
| `Codex W 12` | Codex rate limit: 주간 창 12% 사용 |

수집할 수 없는 값은 가짜 `0` 대신 `—`로 표시합니다. 10분 이상 오래된 데이터는 명시적으로 `(오래됨)` 표시됩니다 — PulseBar는 옛 숫자를 현재값처럼 보여주지 않습니다.

바를 **좌클릭**하면 상세 팝업, **우클릭**하면 메뉴(새로고침·설정·로그인·연동·시작프로그램·로그·종료), **마우스 오버**하면 리셋 시각과 토큰 합계 툴팁이 나옵니다.

## 상세 팝업

| 시스템 탭 | AI 사용량 탭 |
|---|---|
| ![60초 CPU 그래프가 있는 시스템 탭](docs/images/detail-system.png) | ![프로바이더 카드가 있는 AI 사용량 탭](docs/images/detail-ai.png) |

- **시스템** — 최근 60초 CPU 미니 그래프 + RAM(사용/전체), GPU(어댑터명 포함), VRAM(사용/전체), 디스크 읽기/쓰기 속도, 네트워크 업/다운.
- **AI 사용량** — 프로바이더별 카드: 모든 rate-limit 창의 **사용률과 정확한 리셋 시각**, 계정, 플랜, 크레딧, 토큰 활동, 데이터 신선도("마지막 갱신 방금/N분 전"), 그리고 원클릭 액션: **Codex 로그인**, **Claude Code 열기**, **Claude 사용량 연동**, **Fable 토큰 수집 연동**.

## 기능 상세

### 시스템 모니터링
- CPU(`GetSystemTimes` 델타), RAM(`GlobalMemoryStatusEx`), 디스크, 네트워크, GPU, VRAM을 1초 주기로 샘플링.
- **영문 PDH 카운터 API**(`PdhAddEnglishCounterW`)를 사용해 Windows 표시 언어와 무관하게 동작.
- GPU 사용률은 어댑터별 busiest-engine 정책으로 집계(작업 관리자 방식) — 300% 같은 엉터리 숫자가 나오지 않습니다.
- VRAM 총량과 어댑터 이름은 DXGI에서 조회, 멀티 GPU는 전용 메모리가 가장 큰 어댑터를 자동 선택.
- 수집은 전부 UI 스레드 밖에서 수행되며, 카운터 하나가 실패해도 `—`로 강등될 뿐 앱은 죽지 않습니다.

### Claude Code 구독 사용량
- Claude Code가 statusline 명령에 전달하는 **공식 statusline JSON**을 읽습니다 — Claude가 보여주는 것과 동일한 `five_hour`/`seven_day` 사용률과 리셋 시각.
- 작은 브리지(`PulseBar.Bridge`)가 이 JSON을 로컬 캐시로 정규화합니다. Claude 자격 증명은 절대 건드리지 않고 항상 exit 0으로 종료해 Claude 세션을 방해할 수 없습니다.
- **이미 statusline HUD를 쓰고 계신가요?** PulseBar는 절대 덮어쓰지 않습니다. 동의 한 번이면 기존 명령을 *래핑*합니다: HUD는 이전과 똑같이 렌더링되고, PulseBar는 지나가는 동일한 JSON을 읽기만 합니다. 원본 명령은 사이드카 스크립트에 그대로 보존되고 `settings.json`은 먼저 백업됩니다.
- Claude Code가 꺼져 있으면 마지막 값을 오래됨 표시와 함께 보여줍니다.

### Fable 5 로컬 토큰 텔레메트리 (opt-in)
- Claude Code의 **공식 OpenTelemetry 내보내기**로 *이 PC*의 모델별 실제 토큰을 집계합니다: 입력·출력·캐시읽기·캐시생성 — 오늘/최근 7일.
- 트레이 메뉴에서 명시적으로 켜야만 동작합니다. 수신기는 **127.0.0.1 전용**, DPAPI로 보호된 bearer 시크릿 필수, 저장하는 것은 **토큰 수와 요청 메타데이터뿐 — 프롬프트·응답·대화 내용은 절대 수집하지 않습니다**.
- WSL에서 실행되는 Claude Code도 지원: 배포판 안의 헬퍼 프로세스가 정규화된 이벤트만 로컬 큐로 전달합니다.
- 표시는 항상 "**Fable 토큰 · 이 PC Claude Code 기준**" — 로컬 텔레메트리는 서버 쿼터가 *아니며*, PulseBar는 둘을 절대 섞지 않습니다.

### Codex 사용량
- **공식 `codex app-server` JSON-RPC 인터페이스**로 통신합니다 — 스크래핑 없음, 쿠키 접근 없음, `~/.codex` 파싱 없음.
- 서버가 보고하는 모든 rate-limit 버킷을 **창 길이 기준으로 분류**(300분→5h, 10080분→주간)하며 이름을 하드코딩하지 않아 새 버킷도 자동 표시됩니다.
- 서버가 제공하면 플랜, 크레딧, 누적/일별 토큰 활동도 표시.
- **브라우저 로그인 내장**: 공식 `account/login/start` 플로우로 브라우저가 열리고, 로그인 완료 즉시 갱신됩니다.
- app-server 프로세스 하나를 유지하며 1→2→5분 백오프로 재연결. 계정 전환을 감지하면 이전 스냅샷을 폐기합니다.

### Windows 네이티브 + WSL 동시 지원
- 첫 실행 시 Windows와 **모든 WSL 배포판**에서 `claude`/`codex`를 자동 탐지.
- **버전 인지 탐지**: snap, nvm, `~/.local/bin` 등에 여러 설치가 공존하면 각각의 `--version`을 조사해 최신을 선택 — 최신 서버 API와 안 맞는 구버전 바이너리를 걸러냅니다.
- 모든 WSL 프로세스는 따옴표 없는 인자와 PATH 보정으로 실행되어 nvm 셔뱅 CLI도 그냥 동작합니다.

### 편의 기능
- 시스템 트레이 왼쪽에 도킹; Explorer 재시작(`TaskbarCreated`), DPI 변경에 대응하고 전체화면 앱 이후 topmost를 재선언합니다.
- 도킹이 불가능하면(세로/자동숨김 작업표시줄) 드래그 가능한 플로팅 모드로 전환, 모니터별 위치 저장.
- 한국어/영어 UI, 설정에서 즉시 전환.
- 설정 내보내기/가져오기(가져온 파일은 검증), 사용자 단위 시작프로그램 토글(HKCU, 관리자 불필요), 자동 정리되는 롤링 로그.

## 시작하기

**요구 사항**: Windows 10/11 x64. Portable 빌드는 self-contained라 .NET 런타임 설치가 필요 없습니다. 관리자 권한은 어떤 경우에도 필요 없습니다.

### 1. PulseBar 받기

```powershell
# Portable: 직접 ZIP 빌드 (바이너리 릴리스 배포 전까지)
git clone https://github.com/Hitbee-dev/PulseBar.git
cd PulseBar
powershell -ExecutionPolicy Bypass -File packaging/portable/build-portable.ps1
# → artifacts/PulseBar-portable-win-x64.zip (+ SHA-256)
```

원하는 곳에 압축을 풀고 `PulseBar.exe`를 실행하세요. `packaging/installer/`에 Inno Setup 사용자 단위 인스톨러 스크립트도 있습니다.

### 2. 첫 실행
1. 시스템 자원은 즉시 작업표시줄에 표시됩니다.
2. Claude/Codex CLI가 자동 탐지되고(Windows + WSL) 프로필이 `%LOCALAPPDATA%\PulseBar\config.json`에 저장됩니다.
3. 트레이 우클릭 → **Codex 로그인** — 브라우저에서 완료하면 몇 초 안에 숫자가 나타납니다.
4. 트레이 우클릭 → **Claude 사용량 연동** — statusline 브리지를 자동 설치하거나, 기존 HUD가 있으면 래핑 동의를 묻습니다. Claude Code를 한 번 재시작하세요.
5. 선택: **Fable 토큰 수집 연동 (OTel)** — 로컬 모델별 토큰 집계. Claude Code를 한 번 재시작하세요.

## 두 숫자는 서로 다른 것입니다

```
Claude 주간 사용률 33%        ← 서버 계정 쿼터 (공식 statusline 값)
Fable 최근 7일 12.4M tokens   ← 이 PC의 로컬 텔레메트리 (다른 기기/웹 미포함)
```

PulseBar는 이 둘을 항상 시각적으로도 의미적으로도 분리합니다. 둘을 하나의 "퍼센트"로 섞는 도구는 거짓말을 하는 것이고, PulseBar는 그렇게 하지 않습니다.

## 보안과 프라이버시

- 관리자 권한 없음, Explorer/DLL 주입 없음, 비공식 작업표시줄 해킹 없음.
- 자격 증명을 읽지 않습니다: Codex 인증은 `codex app-server`가, Claude 인증은 Claude Code가 소유합니다.
- 브라우저 쿠키, Windows 자격 증명 관리자 접근 없음.
- 네트워크 리스너는 전부 루프백 전용 + bearer 시크릿(DPAPI로 보호).
- 프롬프트·응답·대화 내용은 절대 수집/기록/저장하지 않습니다.
- `settings.json` 수정 시: 타임스탬프 백업 → 없을 때만 추가 → 원자적 쓰기, 기존 설정은 명시적 동의 없이 덮어쓰지 않음.
- PulseBar 자체의 외부 텔레메트리 전송 없음.

자세한 내용: [docs/security.md](docs/security.md)

## 소스에서 빌드

```powershell
dotnet restore
dotnet build PulseBar.sln -c Release
dotnet test PulseBar.sln -c Release --no-build   # 테스트 180개
```

| 프로젝트 | 역할 |
|---|---|
| `src/PulseBar.App` | WPF 앱 — DI 호스트, 오버레이, 상세 팝업, 트레이 |
| `src/PulseBar.Core` | 모델, 프로바이더 계약, 설정, 로컬라이제이션, 포매터 |
| `src/PulseBar.Windows` | 메트릭(PDH/DXGI), taskbar interop, CLI 탐지, DPAPI |
| `src/PulseBar.Providers.Codex` | codex app-server JSON-RPC 클라이언트 + 로그인 |
| `src/PulseBar.Providers.Claude` | statusline 파서/설치기, OTel 파서, 프로바이더 |
| `src/PulseBar.Bridge` | statusline 브리지 + WSL OTel 헬퍼 실행 파일 |
| `src/PulseBar.Storage` | SQLite 토큰 이벤트 저장소 (멱등, 30일 보존) |

스택: .NET 8 LTS · WPF · SQLite · xUnit. 의도적으로 **안 쓰는 것**: Electron, 웹뷰, 상시 Node/Python 데몬, Docker, 관리자 드라이버.

## 문제 해결

[docs/troubleshooting.md](docs/troubleshooting.md)를 참고하세요 — 기존 HUD와의 statusline 병합 명령, 오래됨 표시의 의미, 카운터 가용성 등을 다룹니다. 로그는 `%LOCALAPPDATA%\PulseBar\logs\`에 있습니다.

## 알려진 제약

- 가로 작업표시줄에 도킹; 세로/자동숨김 작업표시줄은 플로팅 모드를 사용합니다.
- 주 모니터 작업표시줄만 지원 (보조 모니터 선택은 예정).
- Fable 텔레메트리는 이 PC의 Claude Code 트래픽만 집계합니다 — 설계상 의도입니다.
- GPU 온도는 의도적으로 범위 밖입니다 (관리자 드라이버 꼼수를 쓰지 않기 위해).
