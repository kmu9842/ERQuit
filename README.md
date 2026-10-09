# ERQuit

이터널 리턴 창에서 직접 누른 **Alt+F4**로 게임 본체를 강제 종료하는 Windows 도구입니다.
Windows 10/11 x64, .NET Framework 4.8 이상이 필요합니다. 별도 런타임 설치가 필요 없는 Windows 기본 .NET Framework를 사용합니다.

## 설치 및 게임과 함께 실행

1. `ERQuit-Setup.exe`를 실행하고 **설치**를 누릅니다. 관리자 권한은 요청하지 않습니다.
2. 설치 화면에서 Steam 실행 옵션 문구를 **복사**합니다.
3. Steam 라이브러리 → **이터널 리턴** → **속성** → **일반** → **실행 옵션**에 붙여 넣습니다.
4. 이후 Steam에서 게임을 실행하면 ERQuit이 함께 시작되어 트레이에 표시됩니다. 게임이 끝나면 함께 종료됩니다.

설치 화면이 생성하는 문구는 다음 형태입니다. 경로는 해당 컴퓨터 사용자에게 맞게 생성됩니다.

```text
"C:\Users\사용자\AppData\Local\Programs\ERQuit\ERQuit.exe" --steam %command%
```

`%command%`는 그대로 입력합니다. Steam이 실제 게임 경로와 실행 인수로 치환합니다.
기존 게임 실행 옵션이 있었다면 `%command%` 뒤에 유지하세요. 기존 옵션도 다른 실행 도구를 감싸는 형태라면 중첩하지 마세요.
설치 프로그램은 실행 중인 Steam의 설정 파일을 직접 덮어쓰지 않습니다. 위 설정은 한 번만 필요합니다.
게임이 이미 켜져 있으면 아래의 연결 방법을 사용하고, 다음 Steam 실행부터 자동 실행 설정이 적용됩니다.

## 지금 실행 중인 게임에 연결

시작 메뉴의 **ERQuit** 또는 `ERQuit.exe`를 실행합니다. 연결 완료 표시가 나오면 게임 창으로 전환하세요.
게임이 아직 실행되지 않았으면 게임 이름으로 한정한 조회를 3초 간격으로 시도합니다.
이 모드에서는 게임을 종료해도 ERQuit이 남아 다음 게임 실행을 기다립니다.
휴대용 실행 파일도 지원합니다. 설치 없이 `ERQuit.exe`를 사용하고 그 파일의 실행 옵션 문구를 복사할 수 있습니다.

트레이 메뉴에서 빠른 종료 기능을 일시 중지하거나 **ERQuit 끝내기**를 선택할 수 있습니다.
상태 창의 X 버튼은 트레이로 숨깁니다. ERQuit을 끝내는 동작은 게임을 종료하지 않습니다.

## 대상과 권한 제한

- Steam 레지스트리와 `libraryfolders.vdf`, `appmanifest_1049590.acf`로 설치 경로를 찾습니다. 드라이브 문자나 사용자 이름을 고정하지 않습니다.
- 등록된 `EternalReturn.exe` 전체 경로, 게임 AppID 파일, 설치 파일 구성, **Nimbleneuron Corp.** Authenticode 서명을 검증합니다. 같은 이름의 다른 경로 파일은 거부합니다.
- Steam 동반 실행 모드는 `CreateProcessW`가 반환한 해당 게임의 핸들만 사용합니다. 실행 중인 프로세스 검색이 필요 없습니다. 보관할 핸들 권한은 종료와 종료 여부 확인으로 줄입니다.
- 기존 게임 연결 모드의 WMI 쿼리는 `WHERE Name = 'EternalReturn.exe'`로 고정됩니다. 프로세스 전체 목록을 요청하거나 다른 이름의 프로세스를 검색하지 않습니다.
- 기존 게임 연결 시 후보 핸들의 실행 시각과 전체 경로를 재검증합니다. PID가 재사용되거나 값이 바뀌면 연결을 거절합니다. 종료는 검증한 동일 핸들에만 요청하며 PID로 다시 열지 않습니다.
- 창 알림은 검증한 게임 PID로 제한한 `SetWinEventHook`으로 받습니다. 다른 앱의 이름, 창 제목, PID를 알아내지 않고 활성 창 핸들만 비교합니다. 게임 창을 한 번 활성화한 이후 동작합니다.
- 키보드 훅은 ERQuit 안에서 실행됩니다. DLL 주입, 게임 메모리 읽기/쓰기, 디버그 권한, 관리자 자동 상승, 안티치트 우회를 사용하지 않습니다. 키 입력은 저장하거나 전송하지 않습니다.
- 게임 창의 Alt+F4 외 입력은 전달합니다. 합성 입력과 Ctrl/Shift/Windows 키가 섞인 조합은 무시합니다. 처리한 F4를 계속 누를 때 새로 드러난 다른 창이 연속으로 닫히지 않도록 같은 누름의 반복과 키 해제를 소비합니다.
- 게임 자식 프로세스, 런처, Steam, 보안 서비스는 종료하지 않습니다. 프로세스 트리 종료 기능은 없습니다.

**기존 프로세스 연결의 엄밀한 한계:** 이름으로 필터링한 WMI 조회라도 Windows 공급자가 내부에서 어떻게 열거하는지까지 ERQuit이 통제하지는 못합니다. 조회 결과를 받은 직후 PID가 재사용되면 최소 식별 확인을 위해 연 후보가 다른 프로세스일 수 있습니다. 실행 시각/경로 불일치 시 즉시 핸들을 닫아 종료를 막습니다. 다른 프로세스에 대한 후보 접근 가능성까지 배제해야 한다면 **Steam 동반 실행 모드만 사용하세요.** 이 모드에는 `OpenProcess`나 WMI 조회 경로가 없습니다.

## 동작 한계

강제 종료이므로 저장 중인 로컬 설정은 유실될 수 있습니다. Windows의 `TerminateProcess`로 즉시 종료를 요청하지만 드라이버나 미완료 I/O까지 포함한 종료 완료 시간을 보장하지는 않습니다.
보안 프로그램이 종료/훅을 막는 경우 자동으로 권한을 올리거나 우회하지 않습니다. 검증 실패 시 동작을 멈추고 이유를 표시합니다.
게임이 Steam이나 보안 프로그램을 통해 새 프로세스로 다시 실행되는 경우, 동반 실행 모드는 새 프로세스를 추적하지 않습니다. 이때 직접 ERQuit을 실행해 기존 게임 연결 모드를 사용할 수 있습니다.
Steam 오버레이가 게임과 같은 프로세스 안에서 그려지는 상황은 별도 프로세스/창으로 구분할 수 없으므로 게임이 활성 상태로 취급될 수 있습니다.
서명 검증은 로컬 인증서 캐시만 사용합니다. 실시간 인증서 폐기 여부는 조회하지 않으며, 서명자 변경이나 신뢰 체인 부족 시에는 실행을 거부합니다.
심볼릭 링크/정션을 통한 설치 경로는 보안을 위해 지원하지 않습니다.

## 제거

먼저 Steam 실행 옵션에서 ERQuit 부분을 제거하고 기존 옵션으로 복원하세요.
트레이에서 ERQuit을 끝낸 다음 `%LOCALAPPDATA%\Programs\ERQuit` 폴더와 시작 메뉴의 ERQuit 바로가기를 삭제하면 됩니다.
자동 시작 레지스트리, 서비스, 예약 작업은 만들지 않습니다.

## 빌드와 검증

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\artifacts\ERQuit.Tests.exe --verify-installed --verify-running --render
```

기본 빌드는 입력/포커스/프로세스 식별/인수 인코딩/다중 Steam 라이브러리/API 제한 테스트를 실행합니다.
추가 검증은 설치 파일과 현재 실행 중인 이터널 리턴에 대한 읽기 전용 확인입니다. 실제 게임이나 다른 프로그램을 실행하거나 종료하지 않습니다.
UI 렌더링과 설치 파일 안의 앱 바이너리 일치 여부도 검사합니다. 실게임 강제 종료, 다른 PC, 안티치트 호환성은 자동 테스트로 검증하지 않습니다.

API 근거: [CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw), [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook), [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc), [TerminateProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-terminateprocess).
