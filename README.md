# EasyBlue Next

Windows용 EasyBlue 동글 제어 앱을 .NET10/WPF로 다시 작성한 프로젝트입니다. 현재 하드웨어 지원 대상은 VID1D54/PIDB002이며, 원본 EBTiny.exe/KMHook.dll 없이 실행합니다.

## 설치

GitHub Releases의 `EasyBlue-Next-0.3.0-Setup.exe`를 실행하세요. 관리자 권한 없이 현재 사용자에게 설치합니다. 시작 메뉴, 앱 제거, 선택적 바탕화면 바로 가기와 Windows 시작 시 트레이 실행을 지원합니다. 기존 버전은 트레이 메뉴에서 종료하고 설치하세요.

## 기능

- 다중 모니터와 폰·태블릿의 물리 크기 기준 배치, 붙은 경계에서 대기 후 자동 전환
- PC 화면 양방향 카운트다운/진행 막대와 브리딩 효과
- 기기별 사용자 단축키 및 PC↔마지막 기기 공유 단축키
- 페어링/PIN, 저장 기기 연결/삭제, 표시 이름/화면 크기 설정
- 실제 동글 Bluetooth 이름 읽기/변경, 원본 저장 페이지 백업 및 쓰기 후 검증
- LED, 마우스 속도/가속·스크롤 배율, 한/영 전송, 시작 시 실행
- 라이트·다크·시스템 테마, 전체 기기 관리 페이지 스크롤, 트레이 동작

## 검증 범위

코어 검사154개, Windows 크로스 빌드, NSIS 컴파일과 설치 아카이브 검증을 수행했습니다. 사용자에게서 기존 버전의 입력 전환 동작 확인을 받았으나, 새 페어링·동글 EEPROM 이름 변경과 Windows 설치·제거/시작 프로그램은 이 Linux 빌드 환경에서 실제로 실행하지 못했습니다. 출시물은 사전 검증용 준비본입니다.

원본 설정 전체를 이식한 것은 아닙니다. PC 클립보드 전송, 원본 특수키 그룹/키맵 편집 및 일부 OS별 예외는 남아 있습니다. 자세한 대조 목록은 [원본 설정 목록](Notes/Original-settings-inventory.md), [0.3.0 변경 사항](Notes/0.3.0-changes.md), [통신 분석](Notes/0.2.2-protocol-audit.md)에 있습니다.

## Windows에서 빌드

.NET10 SDK와 NSIS3 Unicode 컴파일러(`makensis`가 PATH에 필요)를 준비합니다.

```powershell
./Build-Windows.ps1
./Installer/Build-Installer.ps1
```

포터블 출력: `artifacts/win-x64`. 설치 EXE는 상위 폴더에 생성됩니다. 개발 중 코어 검사만 실행하려면:

```powershell
dotnet run --project EasyBlue.Checks -c Release
```

## 데이터

설정: `%LOCALAPPDATA%/EasyBlueNext/preferences.json`. 동글 이름 변경 전 백업: 같은 폴더의 `Backups`. 제거 시 사용자 설정과 백업은 남깁니다. 저장된 이름은 영문·숫자·기호 최대31자를 지원하며, 휴대폰에 캐시된 기존 Bluetooth 이름은 별도로 갱신해야 할 수 있습니다.

이 저장소에는 원본 EXE/DLL/펌웨어, 실제 사용자 설정 또는 동글 백업을 포함하지 않습니다. `Notes`에는 재현과 검토를 위한 정적 분석 근거와 이식 범위를 기록했습니다.
