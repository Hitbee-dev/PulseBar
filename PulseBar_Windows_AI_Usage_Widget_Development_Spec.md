# PulseBar 개발 설계서

> Windows 작업표시줄에서 시스템 자원과 AI 코딩 도구 사용량을 한눈에 확인하는 경량 모니터링 앱  
> 대상 환경: Windows 10/11 x64, Windows 네이티브 CLI 및 WSL2  
> 기준일: 2026-07-17

---

## 0. Claude/Codex에게 주는 최상위 지시

이 문서는 아이디어 문서가 아니라 **구현 명세**다.

다음 원칙을 지켜서 개발한다.

1. 먼저 저장소와 솔루션 구조를 만든다.
2. 각 단계가 끝날 때마다 빌드와 테스트를 실행한다.
3. 한 번에 모든 기능을 뒤엉켜 만들지 않는다.
4. Windows 시스템 자원 수집 → 작업표시줄 UI → Codex 연동 → Claude 연동 → Fable 토큰 집계 순으로 구현한다.
5. 공식 인터페이스가 있는 기능은 반드시 공식 인터페이스를 우선한다.
6. 브라우저 쿠키 탈취, OAuth 토큰 직접 복사, 자격 증명 파일 무단 파싱은 금지한다.
7. 공식 API가 없는 값은 UI에 `추정`, `이 PC에서만`, `마지막 갱신`을 명확하게 표시한다.
8. Explorer 프로세스에 DLL을 주입하거나 작업표시줄 프로세스의 자식 창으로 강제 삽입하지 않는다.
9. 관리자 권한 없이 실행되어야 한다.
10. Windows와 WSL에서 Claude Code/Codex가 실행되는 경우를 모두 지원한다.
11. 기존 Claude Code/Codex 설정을 수정하기 전에 백업하고, 기존 설정이 있으면 덮어쓰지 않는다.
12. 구현 중 확인되지 않은 값이나 동작을 사실처럼 가정하지 말고, 해당 버전의 공식 문서와 실제 CLI 출력으로 검증한다.

---

# 1. 제품 목표

## 1.1 핵심 목표

작업표시줄 오른쪽에 다음 정보를 실시간 또는 준실시간으로 표시한다.

### 시스템 자원

- CPU 전체 사용률
- 메모리 사용률
- GPU 전체 사용률
- 전용 GPU 메모리 사용량
- 디스크 활성도
- 디스크 읽기/쓰기 속도
- 네트워크 업로드/다운로드 속도
- 선택 사항: CPU/GPU 온도

### AI 도구 사용량

- Claude Code
  - 5시간 사용률
  - 7일 또는 주간 사용률
  - 각 한도 리셋 시각
  - 현재 로그인 상태와 플랜
  - Fable 5 로컬 토큰 사용량
    - 오늘
    - 최근 7일
    - 입력/출력/캐시 읽기/캐시 생성
- Codex
  - 단기 사용 한도
  - 주간 사용 한도
  - 각 한도 리셋 시각
  - 플랜
  - 크레딧 잔액이 제공되는 경우 잔액
  - 일별 토큰 활동이 제공되는 경우 일별/누적 토큰
- 추후 Provider Adapter를 통해 다른 도구 추가
  - Gemini CLI
  - GitHub Copilot
  - Cursor
  - 기타

## 1.2 비목표

MVP에서는 아래 기능을 만들지 않는다.

- 원격 서버 모니터링
- 모바일 앱
- 클라우드 동기화
- 웹 대시보드
- 팀 단위 사용량 수집
- 브라우저 쿠키나 세션 토큰을 훔쳐서 비공식 API 호출
- Explorer DLL 주입
- Windows 서비스
- Electron 기반 UI
- 무거운 시계열 DB
- Kubernetes, Docker, Redis 같은 쓸데없는 인프라

이 앱 하나 만들겠다고 분산 시스템을 세우는 병신 같은 과설계는 하지 않는다.

---

# 2. 현실적인 제약과 표시 규칙

## 2.1 Codex

Codex는 공식 `codex app-server`에서 JSON-RPC 방식으로 다음 값을 제공한다.

- `account/read`
- `account/rateLimits/read`
- `account/rateLimits/updated`
- `account/usage/read`

따라서 Codex 사용량은 브라우저 스크래핑 없이 구현 가능하다.

주의:

- Codex 버전에 따라 `primary`, `secondary`가 각각 5시간/주간이라고 고정해서는 안 된다.
- 반드시 `windowDurationMins` 값으로 버킷을 판별한다.
- 예:
  - 300분이면 5시간 버킷
  - 10,080분이면 7일 버킷
- 서버 정책 변경으로 특정 버킷이 사라질 수 있으므로 null-safe하게 구현한다.
- `rateLimitsByLimitId`가 있으면 단일 `rateLimits`보다 우선한다.
- Codex를 매번 새 프로세스로 실행하지 말고, Provider가 활성화된 동안 `codex app-server` 프로세스 하나를 유지한다.
- 연결이 끊기면 지수 백오프로 재시작한다.

## 2.2 Claude Code

Claude 구독 사용량을 외부 앱이 조회하는 공식 독립 API는 현재 보장되지 않는다.

대신 Claude Code 공식 statusline 입력 JSON에 아래 값이 들어온다.

- `rate_limits.five_hour.used_percentage`
- `rate_limits.five_hour.resets_at`
- `rate_limits.seven_day.used_percentage`
- `rate_limits.seven_day.resets_at`
- 모델 정보
- 세션 정보
- 현재 컨텍스트 토큰 정보

따라서 Claude 사용량은 다음 두 계층으로 나눈다.

### A. 구독 한도

Claude Code statusline bridge가 전달하는 값을 사용한다.

- Claude Code가 실행 중이고 statusline이 갱신될 때 최신화된다.
- Claude가 꺼져 있으면 마지막 값을 캐시한다.
- UI에 `마지막 갱신 3분 전`처럼 freshness를 표시한다.
- 10분 이상 오래된 값은 흐리게 표시하고 `오래됨` 상태로 바꾼다.

### B. Fable 5 실제 로컬 토큰 사용량

Claude Code의 공식 OpenTelemetry 로그/메트릭을 사용한다.

- 모델 ID가 `claude-fable-5`인 요청만 집계한다.
- 입력 토큰
- 출력 토큰
- 캐시 읽기 토큰
- 캐시 생성 토큰
- 비용 추정치가 제공되면 추정 비용
- 오늘/최근 7일/누적

중요:

- **Fable 로컬 토큰 수와 Claude 서버의 Fable 전용 잔여 쿼터는 같은 값이 아니다.**
- 서버가 모델별 잔여 쿼터를 공식 구조화 데이터로 제공하지 않는다면 `Fable 잔여 한도`라고 표시하지 않는다.
- 대신 `Fable 토큰 · 이 PC Claude Code 기준`이라고 표시한다.
- 다른 PC, claude.ai 웹, 모바일에서 사용한 Fable 토큰은 로컬 집계에 포함되지 않는다.
- Claude의 전체 5시간/7일 사용률은 서버가 statusline에 제공한 공식 값으로 표시한다.

## 2.3 데이터 신뢰도 배지

각 Provider 값에는 반드시 신뢰도 상태를 가진다.

```csharp
public enum DataFreshness
{
    Live,
    Fresh,
    Stale,
    Unavailable,
    AuthenticationRequired,
    Error
}

public enum DataScope
{
    ServerAccount,
    LocalMachine,
    LocalSession,
    Estimated
}
```

예시:

- Codex 5시간 사용률: `ServerAccount / Fresh`
- Claude 7일 사용률: `ServerAccount / Fresh`
- Fable 7일 토큰: `LocalMachine / Fresh`
- Claude가 꺼져 20분 지난 값: `ServerAccount / Stale`

---

# 3. 기술 스택

## 3.1 기본 스택

- 언어: C# 13 또는 현재 .NET 8에서 안정적으로 지원되는 문법
- 런타임: .NET 8 LTS
- UI: WPF
- 아키텍처: 단일 데스크톱 앱 + 필요한 경우 경량 bridge 실행 파일
- DI/Host: `Microsoft.Extensions.Hosting`
- 로깅: `Microsoft.Extensions.Logging`
- 설정: JSON
- 로컬 저장소: SQLite
- 테스트: xUnit
- Mock: NSubstitute 또는 Moq 중 하나만 선택
- 직렬화: `System.Text.Json`
- HTTP: `HttpClientFactory`
- 프로세스 통신:
  - Codex app-server: stdio JSON-RPC
  - Claude statusline: stdin JSON → cache file 또는 localhost bridge
  - Claude OTel: OTLP HTTP/protobuf

## 3.2 WPF를 선택하는 이유

- Windows 전용 앱이다.
- 작업표시줄/Win32 API 연동이 쉽다.
- WinUI 3보다 배포와 창 제어가 단순하다.
- Electron보다 메모리를 덜 먹는다.
- Avalonia의 크로스플랫폼 이점이 필요 없다.
- 한국어 UI와 DPI 대응이 안정적이다.

## 3.3 금지 기술

- Electron
- 웹뷰 기반 전체 UI
- Python 상시 프로세스
- Node.js 상시 백엔드
- Docker
- 별도 DB 서버
- 관리자 권한이 필요한 드라이버
- HWiNFO 의존
- 유료 센서 SDK
- Explorer injection

---

# 4. 저장소 구조

```text
PulseBar/
├─ PulseBar.sln
├─ Directory.Build.props
├─ README.md
├─ docs/
│  ├─ architecture.md
│  ├─ provider-contract.md
│  ├─ security.md
│  └─ troubleshooting.md
├─ src/
│  ├─ PulseBar.App/
│  │  ├─ App.xaml
│  │  ├─ Views/
│  │  ├─ ViewModels/
│  │  ├─ Controls/
│  │  ├─ Converters/
│  │  └─ Assets/
│  ├─ PulseBar.Core/
│  │  ├─ Models/
│  │  ├─ Interfaces/
│  │  ├─ Services/
│  │  └─ Configuration/
│  ├─ PulseBar.Windows/
│  │  ├─ Metrics/
│  │  ├─ Taskbar/
│  │  ├─ Interop/
│  │  ├─ Startup/
│  │  └─ Dpi/
│  ├─ PulseBar.Providers.Codex/
│  │  ├─ AppServer/
│  │  ├─ Models/
│  │  └─ CodexProvider.cs
│  ├─ PulseBar.Providers.Claude/
│  │  ├─ Statusline/
│  │  ├─ OpenTelemetry/
│  │  ├─ Models/
│  │  └─ ClaudeProvider.cs
│  ├─ PulseBar.Bridge/
│  │  ├─ Commands/
│  │  ├─ CacheWriter/
│  │  └─ Program.cs
│  └─ PulseBar.Storage/
│     ├─ Sqlite/
│     ├─ Repositories/
│     └─ Migrations/
├─ tests/
│  ├─ PulseBar.Core.Tests/
│  ├─ PulseBar.Windows.Tests/
│  ├─ PulseBar.Codex.Tests/
│  └─ PulseBar.Claude.Tests/
└─ packaging/
   ├─ installer/
   └─ portable/
```

프로젝트 수를 더 늘리지 않는다. 기능 하나마다 프로젝트 하나 만드는 건 관리가 아니라 장식이다.

---

# 5. UI/UX 명세

## 5.1 작업표시줄 표시 방식

Windows 작업표시줄에 공식적으로 임의 텍스트 위젯을 삽입하는 안정적인 API는 없다.

따라서 기본 구현은 다음과 같다.

### 기본 모드: Taskbar Overlay

- 투명 배경의 borderless WPF window
- `Topmost = true`
- 작업표시줄 오른쪽 트레이 영역의 왼쪽에 정렬
- 실제 Explorer 자식 창으로 만들지 않는다.
- `SetParent`로 `Shell_TrayWnd`에 강제 결합하지 않는다.
- `FindWindow("Shell_TrayWnd")`와 `GetWindowRect`로 작업표시줄 위치를 읽는다.
- `TrayNotifyWnd`가 확인되면 해당 영역 왼쪽에 배치한다.
- 정확한 child hierarchy가 발견되지 않으면 작업표시줄 오른쪽 끝에서 안전 여백을 둔다.
- Explorer 재시작 시 등록되는 `TaskbarCreated` 메시지를 감지해서 재배치한다.
- 작업표시줄 자동 숨김, 상/하/좌/우 배치, DPI 변경을 처리한다.
- 기본적으로 주 모니터 작업표시줄만 지원하고, 설정에서 보조 모니터를 선택할 수 있게 한다.
- Windows 11의 가운데 정렬 여부와 무관하게 트레이 쪽 기준으로 배치한다.

### 실패 시 fallback

- 작업표시줄 바로 위에 붙는 compact floating mode
- 사용자가 직접 위치를 이동할 수 있다.
- 위치는 모니터별로 저장한다.
- taskbar overlay 실패가 앱 종료로 이어지면 안 된다.

### 절대 하지 말 것

- Explorer에 DLL 주입
- undocumented COM deskband 강제 등록
- Explorer 프로세스 메모리 패치
- 매 Explorer 업데이트마다 깨지는 후킹

## 5.2 Compact Bar 예시

### 한 줄 모드

```text
CPU 14  RAM 47  GPU 82  VRAM 9.8G  D 4  ↓32M ↑2M  |  Claude 5h 41 · W 68  |  Codex 5h 25 · W 18
```

### 초압축 모드

```text
C14 M47 G82 V9.8 D4 | CL 41/68 | CX 25/18
```

### 두 줄 모드

```text
CPU 14%  RAM 47%  GPU 82%  VRAM 9.8/16G  D 4%
Claude 5h 41% W 68%  |  Codex 5h 25% W 18%
```

## 5.3 상세 팝업

좌클릭 시 작업표시줄 위에 상세 패널을 연다.

### 시스템 탭

- CPU 사용률과 최근 60초 mini graph
- 메모리 사용률
- GPU 사용률
- VRAM 사용량
- 디스크 읽기/쓰기
- 네트워크 업/다운
- 현재 수집 주기
- 데이터 오류

### AI 사용량 탭

Provider별 카드:

#### Claude

- 로그인/연동 상태
- 플랜 정보가 있으면 플랜
- 5시간 사용률과 리셋 시각
- 7일 사용률과 리셋 시각
- Fable 오늘 토큰
- Fable 최근 7일 토큰
- 마지막 갱신 시각
- 데이터 범위 설명

#### Codex

- 로그인 상태
- 플랜
- 모든 rate-limit bucket
- 5시간/주간 버킷 우선 표시
- 리셋 시각
- 크레딧
- 오늘/누적 토큰 활동
- 마지막 갱신 시각

## 5.4 색상 기준

색만으로 상태를 전달하지 말고 숫자와 텍스트를 함께 표시한다.

- 0~69%: 정상
- 70~84%: 주의
- 85~94%: 높음
- 95~100%: 위험
- 오래된 데이터: 낮은 불투명도 + `오래됨`
- 인증 필요: `로그인 필요`
- 오류: `오류` + tooltip에 원인

색상 기준은 설정에서 변경 가능하게 만든다.

## 5.5 조작

- 좌클릭: 상세 팝업
- 우클릭:
  - 새로고침
  - 표시 항목
  - 설정
  - 시작 프로그램 등록/해제
  - 로그 폴더 열기
  - 종료
- 더블클릭:
  - 설정에서 지정한 동작
  - 기본값은 상세 팝업

---

# 6. Windows 시스템 자원 수집

## 6.1 수집 주기

| 항목 | 기본 주기 |
|---|---:|
| CPU | 1초 |
| RAM | 1초 |
| GPU | 1초 |
| VRAM | 1초 |
| Disk activity | 1초 |
| Disk throughput | 1초 |
| Network | 1초 |
| UI redraw | 1초 |
| History flush | 10초 |
| AI Provider poll | 60초 |
| Provider failure backoff | 1분 → 2분 → 5분, 최대 5분 |

1초 수집은 시스템 카운터 몇 개 읽는 수준이라 정상 구현이면 부담이 거의 없어야 한다.

## 6.2 CPU

가능하면 `GetSystemTimes` 기반으로 구현한다.

- 두 샘플의 idle/kernel/user delta로 전체 CPU 사용률 계산
- 첫 샘플은 값 없음 처리
- 0~100 범위 clamp
- 시스템 sleep/resume 후 baseline 재설정

## 6.3 Memory

`GlobalMemoryStatusEx` 사용.

표시:

- Physical total
- Physical used
- Physical available
- 사용률

## 6.4 Disk

PDH의 영문 카운터 API를 사용한다.

- `PdhAddEnglishCounterW`
- 로컬 Windows 언어와 무관하게 동작해야 한다.

후보 카운터:

```text
\PhysicalDisk(_Total)\% Disk Time
\PhysicalDisk(_Total)\Disk Read Bytes/sec
\PhysicalDisk(_Total)\Disk Write Bytes/sec
```

주의:

- `_Total` 인스턴스가 없는 환경을 처리한다.
- 첫 샘플은 버린다.
- removable drive와 offline disk 오류가 전체 수집기를 죽이지 않게 한다.
- 디스크 용량 사용률과 디스크 I/O 활성도를 혼동하지 않는다.
- compact bar의 `D`는 기본적으로 활성도다.
- 상세 팝업에는 각 볼륨의 용량 사용률도 별도 표시 가능하다.

## 6.5 Network

영문 PDH 카운터를 사용한다.

```text
\Network Interface(*)\Bytes Received/sec
\Network Interface(*)\Bytes Sent/sec
```

기본 동작:

- 활성 인터페이스 합산
- Loopback 제외
- 사용자가 Hyper-V, WSL vEthernet, Tailscale 등을 포함/제외 가능
- 어댑터 선택 모드 지원
- B/s, KB/s, MB/s 자동 단위 변환

## 6.6 GPU

### GPU 사용률

```text
\GPU Engine(*)\Utilization Percentage
```

- 인스턴스 이름에서 LUID와 engine type을 파싱한다.
- adapter별로 그룹화한다.
- 엔진 값을 무식하게 전부 합산해서 300% 같은 숫자를 만들지 않는다.
- Task Manager와 유사하게 adapter의 busiest engine 또는 검증된 aggregate 정책을 사용한다.
- 멀티 GPU 지원
- 기본 GPU 자동 선택
- 설정에서 GPU 선택

### VRAM

```text
\GPU Adapter Memory(*)\Dedicated Usage
```

- adapter LUID로 그룹화
- DXGI에서 adapter의 dedicated memory total 조회
- used/total 표시
- iGPU/shared memory는 별도 처리
- 값이 제공되지 않으면 unavailable

### 온도

MVP 필수 기능이 아니다.

Windows 네이티브 공통 API로 안정적인 GPU 온도 수집이 어렵기 때문에 다음 중 하나로만 확장한다.

1. LibreHardwareMonitorLib 선택 기능
2. 제조사별 API plugin
3. 미지원 표시

온도 하나 보겠다고 관리자 권한 드라이버를 깔지 않는다.

## 6.7 성능 목표

Idle 상태 기준:

- 앱 CPU 평균: 0.5% 미만
- 일반 상태 메모리: 100MB 미만 목표
- 디스크 쓰기: history flush 시에만 발생
- 네트워크: Provider당 기본 1분 1회 이하
- UI thread에서 PDH, 프로세스 I/O, SQLite 작업 금지

---

# 7. 공통 Provider 구조

```csharp
public interface IUsageProvider : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }

    Task<ProviderCapability> ProbeAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken);

    Task StartAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken);

    Task<UsageSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken);

    Task RefreshAsync(
        CancellationToken cancellationToken);

    event EventHandler<UsageSnapshot>? SnapshotUpdated;
    event EventHandler<ProviderStateChanged>? StateChanged;
}
```

```csharp
public sealed record UsageWindow(
    string Id,
    string DisplayName,
    double? UsedPercent,
    double? RemainingPercent,
    TimeSpan? Duration,
    DateTimeOffset? ResetsAt,
    DataFreshness Freshness,
    DataScope Scope);

public sealed record TokenUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheCreationTokens,
    long TotalTokens);

public sealed record UsageSnapshot(
    string ProviderId,
    string? AccountLabel,
    string? Plan,
    IReadOnlyList<UsageWindow> Windows,
    IReadOnlyDictionary<string, TokenUsage> ModelUsageToday,
    IReadOnlyDictionary<string, TokenUsage> ModelUsageSevenDays,
    decimal? CreditBalance,
    DateTimeOffset CollectedAt,
    DataFreshness Freshness,
    string? ErrorCode,
    string? ErrorMessage);
```

Provider는 UI 타입을 참조하지 않는다.

---

# 8. 실행 환경 Profile

Claude/Codex가 Windows에 설치되었는지 WSL에 설치되었는지 선택할 수 있어야 한다.

```csharp
public enum ExecutionEnvironmentType
{
    WindowsNative,
    Wsl
}

public sealed record ProviderProfile(
    string Id,
    string ProviderId,
    ExecutionEnvironmentType Environment,
    string? ExecutablePath,
    string? WslDistribution,
    string? LinuxHome,
    bool Enabled);
```

## 8.1 자동 탐지

### Windows Native

- `where.exe codex`
- `where.exe claude`
- PATH 직접 탐색
- 사용자가 executable path 지정 가능

### WSL

- `wsl.exe --list --quiet`
- 각 distro에 대해:
  - `wsl.exe -d <distro> -- sh -lc "command -v codex"`
  - `wsl.exe -d <distro> -- sh -lc "command -v claude"`
- 자동 탐지는 초기 설정 시에만 실행
- 매 UI refresh마다 WSL 프로세스를 띄우지 않는다.

## 8.2 WSL 프로세스 실행

Codex app-server 예시:

```text
wsl.exe -d Ubuntu-22.04 -- codex app-server
```

- stdin/stdout JSON lines를 그대로 중계한다.
- `UseShellExecute = false`
- stdout/stderr 비동기 읽기
- UTF-8
- cancellation 시 graceful shutdown 후 timeout이면 kill
- 전체 process tree 종료

---

# 9. Codex Provider 상세

## 9.1 연결 흐름

1. 설정된 환경에서 `codex app-server` 실행
2. JSON-RPC initialize 요청
3. initialized notification
4. `account/read`
5. 로그인되지 않았으면 UI에 로그인 버튼 표시
6. 로그인 시:
   - browser flow 또는 device-code flow
   - 가능하면 app-server 공식 로그인 메서드 사용
7. `account/rateLimits/read`
8. `account/usage/read`
9. `account/rateLimits/updated` notification 구독
10. 60초마다 보정 poll

## 9.2 JSON-RPC 클라이언트 요구사항

- 요청 ID monotonic 증가
- pending request dictionary
- timeout
- cancellation
- invalid JSON line 격리
- stderr 로그 별도 저장
- protocol version mismatch 처리
- app-server 버전에 맞춰 schema 생성 가능하면 빌드 과정에서 생성하지 말고 개발 시 pinned schema를 저장
- Codex 업그레이드 후 schema 변경을 검증할 integration test 제공

## 9.3 Rate-limit 매핑

```csharp
private static string ClassifyWindow(int? windowDurationMins)
{
    return windowDurationMins switch
    {
        300 => "five-hour",
        10080 => "seven-day",
        null => "unknown",
        _ => $"custom-{windowDurationMins}"
    };
}
```

정확한 정책:

- `usedPercent`만 오면 `remaining = 100 - used`
- 0~100 clamp는 UI에서만 하지 말고 parser 단계에서도 비정상 값 기록
- `resetsAt`은 Unix seconds
- 로컬 timezone으로 표시
- primary/secondary 이름을 하드코딩하지 않는다.
- `rateLimitsByLimitId`의 모든 bucket을 저장한다.
- compact bar에는 설정으로 선택한 두 bucket만 표시한다.

## 9.4 Codex usage/read

지원되는 경우:

- lifetimeTokens
- peakDailyTokens
- currentStreakDays
- longestStreakDays
- dailyUsageBuckets

이 데이터는 quota와 별개다.

- quota 카드와 token activity 카드를 구분한다.
- daily bucket은 SQLite에 중복 저장하지 않고 provider snapshot으로 upsert한다.

## 9.5 로그인

Codex app-server 공식 로그인 흐름만 사용한다.

금지:

- `~/.codex` 또는 `%USERPROFILE%\.codex`의 OAuth token 직접 파싱
- 브라우저 쿠키 복사
- Windows Credential Manager에서 Codex secret 빼오기

## 9.6 장애 처리

- executable 없음
- WSL distro 종료/삭제
- 로그인 만료
- app-server protocol 오류
- 네트워크 없음
- rate-limit endpoint null
- 5시간 bucket이 일시적으로 사라짐
- 계정 전환 후 이전 snapshot이 남는 문제

계정 ID나 account label이 바뀌면 기존 snapshot을 즉시 stale 처리하고 새로 조회한다.

---

# 10. Claude Provider 상세

## 10.1 Claude quota bridge

`PulseBar.Bridge`에 다음 명령을 만든다.

```text
PulseBar.Bridge claude-statusline --output <cache-path>
```

동작:

1. stdin에서 Claude Code statusline JSON 전체 읽기
2. 필요한 필드만 추출
3. 임시 파일에 JSON 쓰기
4. flush
5. atomic replace
6. stdout에는 설정에 따라 짧은 statusline 또는 빈 문자열 출력
7. 오류가 발생해도 Claude Code 사용을 방해하지 않고 exit code 0을 기본으로 한다.
8. 오류 로그에는 prompt, 응답 본문, transcript 내용을 절대 기록하지 않는다.

정규화 cache 예시:

```json
{
  "schemaVersion": 1,
  "provider": "claude",
  "account": null,
  "model": {
    "id": "claude-fable-5",
    "displayName": "Fable 5"
  },
  "rateLimits": {
    "fiveHour": {
      "usedPercent": 41.0,
      "resetsAt": 1784269200
    },
    "sevenDay": {
      "usedPercent": 68.0,
      "resetsAt": 1784697600
    }
  },
  "sessionId": "redacted-or-optional",
  "updatedAt": "2026-07-17T06:20:00Z"
}
```

세션 ID는 기본 저장하지 않아도 된다.

## 10.2 Claude settings 설치

Windows 네이티브:

```text
%USERPROFILE%\.claude\settings.json
```

WSL:

```text
~/.claude/settings.json
```

설치 절차:

1. 기존 settings 파일 읽기
2. JSON parse
3. 원본 백업
4. `statusLine`이 없는 경우에만 자동 등록
5. 기존 `statusLine`이 있으면 절대 덮어쓰지 않는다.
6. 기존 statusline과 병합하는 수동 가이드를 표시한다.

예시 개념:

```json
{
  "statusLine": {
    "type": "command",
    "command": "\"C:\\Program Files\\PulseBar\\PulseBar.Bridge.exe\" claude-statusline --output \"%LOCALAPPDATA%\\PulseBar\\bridge\\claude-status.json\"",
    "padding": 1
  }
}
```

WSL에서는 Linux용 bridge binary 또는 shell wrapper를 사용한다.

```json
{
  "statusLine": {
    "type": "command",
    "command": "~/.local/bin/pulsebar-bridge claude-statusline --output /mnt/c/Users/<WINDOWS_USER>/AppData/Local/PulseBar/bridge/claude-status.json"
  }
}
```

경로와 quoting을 하드코딩하지 말고 installer가 실제 경로를 생성한다.

## 10.3 기존 statusline 병합

기존 statusline이 있을 때 자동 덮어쓰지 않는다.

선택지:

1. 사용자가 PulseBar bridge만 사용
2. wrapper가 기존 statusline command를 호출하고 출력을 passthrough
3. 수동 설정

wrapper 구현 시:

- stdin JSON을 메모리에 한 번 읽는다.
- bridge 처리 후 동일 JSON을 기존 command stdin에 전달한다.
- 기존 stdout을 그대로 Claude에 반환한다.
- command quoting과 timeout을 처리한다.
- 기존 command가 500ms 이상 걸리면 경고한다.
- 무한 재귀 방지 marker를 둔다.

MVP에서는 안전을 위해 기존 statusline이 있으면 수동 병합 안내까지만 해도 된다.

## 10.4 Claude OpenTelemetry 수집

목적:

- Fable 5 실제 토큰 사용량
- 모델별 토큰 사용량
- 오늘/7일 집계

Claude Code에서 공식적으로 제공하는 OTel 데이터 중 다음만 수집한다.

- `claude_code.token.usage`
- `claude_code.cost.usage`
- `claude_code.api_request`

관심 속성:

- model
- type
- input_tokens
- output_tokens
- cache_read_tokens
- cache_creation_tokens
- cost_usd
- query_source
- timestamp
- session.id
- request_id

프롬프트와 응답 텍스트는 수집하지 않는다.

명시적으로 다음 옵션을 켜지 않는다.

- raw API bodies
- user prompt logging
- assistant response logging
- tool detail logging

## 10.5 Windows Native OTel

PulseBar 앱 내부에 loopback OTLP receiver를 호스팅한다.

- Bind: `127.0.0.1`
- 기본 포트: 4318 또는 충돌 시 동적 포트
- Protocol: HTTP/protobuf
- Endpoint:
  - `/v1/logs`
  - 필요하면 `/v1/metrics`
- 임의 bearer secret을 생성하고 DPAPI로 보호
- Claude 설정의 `OTEL_EXPORTER_OTLP_HEADERS`에 secret 설정
- payload 크기 제한
- localhost 외 연결 거부

Claude user settings 개념:

```json
{
  "env": {
    "CLAUDE_CODE_ENABLE_TELEMETRY": "1",
    "OTEL_LOGS_EXPORTER": "otlp",
    "OTEL_EXPORTER_OTLP_PROTOCOL": "http/protobuf",
    "OTEL_EXPORTER_OTLP_ENDPOINT": "http://127.0.0.1:4318",
    "OTEL_EXPORTER_OTLP_HEADERS": "Authorization=Bearer <secret>"
  }
}
```

현재 설치된 Claude Code 버전의 정확한 환경 변수와 endpoint 동작은 공식 문서와 실제 실행으로 검증한다.

## 10.6 WSL OTel

Windows 앱이 WSL의 localhost를 억지로 직접 받게 하지 않는다.

권장 구조:

- Linux용 `pulsebar-bridge` helper를 WSL 안에서 실행
- helper가 WSL localhost의 OTLP endpoint를 연다.
- Claude Code는 WSL의 `127.0.0.1`로 export한다.
- helper는 정규화된 token event만 Windows 공유 cache에 atomic append/upsert한다.
- 공유 경로 예:
  - `/mnt/c/Users/<user>/AppData/Local/PulseBar/bridge/claude-otel.sqlite`
  - 또는 compact JSONL queue
- Windows 앱은 1~5초마다 새 normalized event만 가져온다.

보안:

- WSL bridge도 loopback only
- bearer secret
- raw prompt/response 저장 금지
- 이벤트 처리 후 queue rotation
- 파일 크기 상한

MVP 단순화가 필요하면:

- WSL Claude quota bridge는 먼저 구현
- WSL Fable OTel bridge는 다음 단계로 구현
- UI에는 미구현 값을 0으로 표시하지 말고 `연동 필요`로 표시

## 10.7 Fable 집계

모델 ID exact match:

```text
claude-fable-5
```

future alias 대응을 위해 설정 가능한 matcher를 둔다.

집계 키:

```text
provider + environmentProfile + model + date + tokenType
```

중복 방지:

- request_id가 있으면 request_id + token type
- 없으면 session.id + event.sequence
- 둘 다 없으면 timestamp/model/token counts hash
- 중복 이벤트 재수신 시 idempotent upsert

표시:

```text
Fable 오늘 1.82M
입력 640K · 출력 91K · 캐시읽기 1.05M · 캐시생성 39K
```

`TotalTokens`의 정의는 UI에서 명확히 한다.

- 단순 합계
- 또는 billable-equivalent를 따로 계산
- 캐시 토큰까지 포함한 총량과 input/output만 합한 양을 혼동하지 않는다.

---

# 11. 저장소와 데이터 보존

## 11.1 경로

```text
%LOCALAPPDATA%\PulseBar\
├─ config.json
├─ data\
│  └─ pulsebar.db
├─ bridge\
│  ├─ claude-status.json
│  └─ claude-events\
├─ logs\
└─ backups\
```

## 11.2 SQLite 테이블

```sql
CREATE TABLE provider_snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    profile_id TEXT NOT NULL,
    collected_at_utc TEXT NOT NULL,
    payload_json TEXT NOT NULL
);

CREATE TABLE token_usage_events (
    event_key TEXT PRIMARY KEY,
    provider_id TEXT NOT NULL,
    profile_id TEXT NOT NULL,
    model_id TEXT NOT NULL,
    occurred_at_utc TEXT NOT NULL,
    input_tokens INTEGER NOT NULL DEFAULT 0,
    output_tokens INTEGER NOT NULL DEFAULT 0,
    cache_read_tokens INTEGER NOT NULL DEFAULT 0,
    cache_creation_tokens INTEGER NOT NULL DEFAULT 0,
    estimated_cost_usd REAL NULL
);

CREATE INDEX ix_token_usage_model_time
ON token_usage_events(provider_id, profile_id, model_id, occurred_at_utc);
```

## 11.3 보존 정책

기본값:

- raw normalized token events: 30일
- 일별 aggregate: 1년
- 시스템 1초 샘플: 메모리에 최근 5분만 유지
- 시스템 history 저장은 기본 비활성
- 로그: 7일, 최대 50MB

사용자가 기록 저장을 끄면:

- 현재 snapshot만 메모리에 유지
- 앱 종료 시 token history를 제외한 시스템 history 폐기

---

# 12. 보안

## 12.1 자격 증명

- Codex OAuth는 codex app-server가 소유한다.
- Claude OAuth token을 PulseBar가 읽지 않는다.
- Windows Credential Manager나 Claude/Codex credential 파일을 직접 파싱하지 않는다.
- 로컬 bridge secret은 DPAPI CurrentUser로 보호한다.
- 로그에 token, cookie, authorization header를 남기지 않는다.

## 12.2 네트워크

- 기본적으로 외부 listen 금지
- OTLP receiver는 loopback
- WSL bridge도 WSL loopback
- Windows Firewall rule 자동 생성 금지
- Provider 공식 서비스 외 임의 서버 통신 금지
- telemetry 업로드 금지
- crash report는 opt-in

## 12.3 설정 변경

Claude 설정 변경 전:

- timestamp가 붙은 backup 생성
- JSON parse 실패 시 수정 중단
- atomic write
- 앱에서 원복 기능 제공

## 12.4 공급망

- NuGet package 수 최소화
- package version pin
- lock file 사용
- GitHub Actions에서 dependency audit
- unsigned 임의 binary 포함 금지
- release binary SHA-256 제공
- 가능하면 code signing은 배포 단계에서 적용

---

# 13. 시작 프로그램과 배포

## 13.1 시작 프로그램

관리자 권한 없이 현재 사용자 기준으로 등록한다.

우선순위:

1. HKCU Run
2. 사용자가 요청한 경우 Startup 폴더 shortcut

Windows Task Scheduler는 필요 없다.

## 13.2 배포 형식

두 가지 제공:

### Portable ZIP

- 압축 해제 후 실행
- 설정은 LocalAppData
- 자동 업데이트 없음

### Installer

- 현재 사용자 설치
- 기본 경로:
  - `%LOCALAPPDATA%\Programs\PulseBar`
- 관리자 권한 불필요
- 시작 프로그램 옵션
- 제거 시 사용자 데이터 보존 여부 선택

MSIX 때문에 불필요하게 AppContainer 제약을 만들지 않는다. Inno Setup, WiX 또는 검증된 경량 installer 중 하나를 사용한다.

## 13.3 빌드

```powershell
dotnet restore
dotnet build PulseBar.sln -c Release
dotnet test PulseBar.sln -c Release --no-build
dotnet publish src/PulseBar.App/PulseBar.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true
```

WSL bridge가 필요하면:

```powershell
dotnet publish src/PulseBar.Bridge/PulseBar.Bridge.csproj `
  -c Release `
  -r linux-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

---

# 14. 설정 파일 예시

```json
{
  "schemaVersion": 1,
  "appearance": {
    "language": "ko-KR",
    "mode": "taskbarOverlay",
    "layout": "twoLine",
    "fontFamily": "Segoe UI",
    "fontSize": 11,
    "opacity": 0.96,
    "targetMonitor": "primary"
  },
  "metrics": {
    "pollIntervalMs": 1000,
    "showCpu": true,
    "showMemory": true,
    "showGpu": true,
    "showVram": true,
    "showDiskActivity": true,
    "showNetwork": true,
    "networkMode": "activeAdapters"
  },
  "providers": [
    {
      "id": "claude-wsl",
      "providerId": "claude",
      "environment": "wsl",
      "wslDistribution": "Ubuntu-22.04",
      "enabled": true,
      "refreshIntervalSeconds": 60
    },
    {
      "id": "codex-wsl",
      "providerId": "codex",
      "environment": "wsl",
      "wslDistribution": "Ubuntu-22.04",
      "enabled": true,
      "refreshIntervalSeconds": 60
    }
  ],
  "thresholds": {
    "warningPercent": 70,
    "highPercent": 85,
    "criticalPercent": 95
  },
  "storage": {
    "tokenEventRetentionDays": 30,
    "logRetentionDays": 7,
    "storeSystemHistory": false
  }
}
```

---

# 15. 상태 모델

```csharp
public enum ProviderConnectionState
{
    Disabled,
    Detecting,
    ExecutableMissing,
    AuthenticationRequired,
    Connecting,
    Connected,
    Stale,
    Disconnected,
    Error
}
```

각 상태에는 사용자 조치가 따라야 한다.

| 상태 | UI 동작 |
|---|---|
| ExecutableMissing | 경로 선택 또는 WSL distro 선택 |
| AuthenticationRequired | 로그인 버튼 |
| Connecting | spinner, 이전 값 유지 |
| Connected | 정상 표시 |
| Stale | 이전 값 흐리게 표시 |
| Disconnected | 재연결 버튼 |
| Error | 짧은 원인 + 로그 열기 |

오류 메시지에 stack trace를 그대로 처박지 않는다. 상세 로그에만 기록한다.

---

# 16. 테스트 전략

## 16.1 단위 테스트

### Windows metrics

- CPU delta 계산
- counter missing
- negative/overflow
- sleep/resume baseline
- unit formatting
- GPU engine aggregate
- multi adapter LUID grouping

### Codex

- JSON-RPC response matching
- notification parsing
- primary only
- secondary only
- multiple rateLimitsByLimitId
- 5시간 버킷 없음
- weekly 버킷 없음
- usedPercent null
- account switch
- stale snapshot
- malformed line
- process crash

### Claude

- statusline JSON 정상 파싱
- rate_limits 누락
- five_hour만 존재
- seven_day만 존재
- resets_at 변환
- atomic cache write
- 기존 settings backup
- 기존 statusLine 보호
- OTel event model filter
- Fable exact match
- duplicate event
- WSL path quoting

## 16.2 Integration test

환경 변수를 통해 fake CLI를 사용한다.

```text
PULSEBAR_CODEX_EXECUTABLE=<fake-app-server>
PULSEBAR_CLAUDE_CACHE=<fixture>
```

Fake Codex app-server가 JSON-RPC request를 받고 fixture response를 돌려주게 한다.

실제 계정이 필요한 테스트는 기본 test suite에서 제외하고 명시적 opt-in으로만 실행한다.

## 16.3 수동 검증

- Windows 10 19044
- Windows 11 최신 안정 버전
- 100%, 125%, 150%, 200% DPI
- 작업표시줄 상/하/좌/우
- 작업표시줄 자동 숨김
- Explorer 재시작
- sleep/resume
- multi-monitor
- NVIDIA 단일 GPU
- iGPU + dGPU
- WSL Ubuntu-22.04
- Windows native CLI
- 네트워크 끊김
- 계정 로그아웃/로그인
- Claude/Codex 미설치

---

# 17. 완료 기준

## 17.1 MVP 완료

다음이 모두 되어야 MVP 완료다.

- [ ] 관리자 권한 없이 설치/실행
- [ ] CPU/RAM/GPU/VRAM/Disk/Network 1초 갱신
- [ ] 작업표시줄 overlay 또는 floating fallback
- [ ] 한국어 UI
- [ ] 시작 프로그램 등록/해제
- [ ] Windows Native/WSL profile 선택
- [ ] Codex app-server 연결
- [ ] Codex rate limit 및 reset 표시
- [ ] Claude statusline bridge
- [ ] Claude 5시간/7일 사용률 및 reset 표시
- [ ] stale 상태 표시
- [ ] 기존 Claude settings 백업
- [ ] provider 오류가 시스템 모니터링을 죽이지 않음
- [ ] portable release
- [ ] 주요 parser 단위 테스트

## 17.2 v1 완료

- [ ] Claude OTel 수집
- [ ] Fable 오늘/7일 토큰 집계
- [ ] Windows native Claude OTel
- [ ] WSL Claude OTel bridge
- [ ] 상세 popup과 mini graph
- [ ] installer
- [ ] 자동 로그 정리
- [ ] 설정 export/import
- [ ] 계정/환경 profile 다중 지원

---

# 18. 구현 순서

## Phase 1: Skeleton

- 솔루션 생성
- DI/Host
- logging
- config
- single-instance mutex
- tray icon
- settings window
- 테스트 프로젝트

검증:

```powershell
dotnet build
dotnet test
```

## Phase 2: Windows Metrics

- CPU
- RAM
- disk
- network
- GPU
- VRAM
- formatter
- in-memory ring buffer

검증:

- Task Manager와 수치 비교
- GPU가 100%를 넘지 않는지 확인
- 30분 idle CPU 측정

## Phase 3: Taskbar Overlay

- taskbar bounds
- tray bounds
- DPI
- auto hide
- Explorer restart
- fallback mode
- click/right-click

검증:

- Windows 10/11
- multi-monitor
- taskbar 위치 변경

## Phase 4: Codex

- process runner
- JSON-RPC
- initialize
- account/read
- login
- rateLimits/read
- rateLimits/updated
- usage/read
- Windows/WSL

검증:

- app-server fixture
- 실제 계정 opt-in
- 계정 변경
- 프로세스 crash

## Phase 5: Claude quota

- bridge command
- statusline parser
- atomic cache
- Windows/WSL installer
- settings backup
- stale policy

검증:

- Claude 실행 중 갱신
- Claude 종료 후 stale
- 기존 statusline 보호

## Phase 6: Fable token telemetry

- local OTLP receiver
- token event normalization
- dedup
- SQLite
- daily/weekly aggregation
- WSL helper

검증:

- Fable 요청
- Sonnet 요청
- 모델별 분리
- duplicate export
- 앱 재시작 후 집계 유지

## Phase 7: Packaging

- portable
- installer
- uninstall
- startup
- docs
- release checklist

---

# 19. Claude/Codex에게 요구할 작업 방식

각 Phase마다 아래 형식으로 진행한다.

1. 변경할 파일 목록
2. 설계 결정
3. 구현
4. 테스트
5. 실행 결과
6. 남은 위험
7. 다음 Phase

코드를 작성한 뒤 `빌드할 수 있을 것 같다`고 끝내지 말고 실제로 실행한다.

실패하면:

- 오류 로그를 읽는다.
- 원인을 설명한다.
- 수정한다.
- 다시 빌드한다.

사용자에게 확인을 요청해야 하는 경우는 아래뿐이다.

- 제품명 변경
- 시각 디자인 선택
- 보안상 기존 설정을 덮어써야 하는 상황
- 공식 인터페이스 부재로 비공식 방법을 사용할지 결정해야 하는 상황

나머지는 합리적인 기본값으로 진행한다.

---

# 20. 공식 참고 자료

구현 전에 현재 설치된 CLI 버전과 문서를 다시 확인한다.

- Codex App Server  
  https://learn.chatgpt.com/docs/app-server

- Codex developer commands  
  https://learn.chatgpt.com/docs/developer-commands

- Claude Code statusline  
  https://code.claude.com/docs/en/statusline

- Claude Code monitoring / OpenTelemetry  
  https://code.claude.com/docs/en/monitoring-usage

- Claude Code IDE usage dialog  
  https://code.claude.com/docs/en/ide-integrations

- Claude Code costs and subscription limits  
  https://code.claude.com/docs/en/costs

문서에 나온 예제 payload를 맹신하지 말고 실제 설치 버전에서 schema와 null 가능성을 확인한다.

---

# 21. 첫 실행 시 사용자 경험

1. 앱 실행
2. 시스템 자원은 즉시 표시
3. Claude/Codex 자동 탐지
4. 발견된 실행 환경 표시

```text
Claude Code
- Windows: 없음
- WSL Ubuntu-22.04: 발견

Codex
- Windows: 없음
- WSL Ubuntu-22.04: 발견
```

5. 사용자가 profile 활성화
6. Codex:
   - 기존 로그인 감지
   - 미로그인 시 로그인
7. Claude:
   - statusline bridge 설치 안내
   - 기존 설정 백업
8. Fable token:
   - OTel 연동은 별도 opt-in
   - 수집 항목과 수집하지 않는 항목을 명시
9. 작업표시줄 compact bar 표시

기능을 쓰려면 HWiNFO나 다른 모니터링 앱을 설치하라고 요구하지 않는다.

---

# 22. 최종 판단 기준

이 앱의 성공 기준은 기능 개수가 아니다.

- 수치가 믿을 만한가
- 수치의 범위가 명확한가
- 작업표시줄에서 바로 읽히는가
- 자원 사용량이 낮은가
- Claude/Codex 업데이트로 한 Provider가 깨져도 앱 전체가 살아 있는가
- 자격 증명을 훔치지 않는가
- WSL 사용자가 귀찮게 Windows에 CLI를 다시 설치하지 않아도 되는가

특히 다음 두 문구를 혼동하지 않는다.

```text
Claude 7일 사용률 68%       # 서버 계정 quota
Fable 최근 7일 12.4M tokens # 이 PC의 Claude Code telemetry
```

둘을 하나의 `Fable 주간 한도 68%` 같은 애매한 숫자로 합치면 제품 신뢰도가 바로 좆망한다.
