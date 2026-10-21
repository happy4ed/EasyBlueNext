# 원본 설정 대조 목록

원본 UTF-16 문자열표와 설정 접근 함수에서 확인한 키를 분류했다. 아래 고급 기능까지 모두 새 앱에 이식한 것은 아니다. 원본 바이너리는 새 소스 저장소/설치파일에 포함하지 않는다.

| 원본 항목 | 새 앱 처리 |
|---|---|
| SYSTEM/DeviceName | 실제 EEPROM 이름 조회·변경 UI, 메타데이터 검증/백업/쓰기 후 비교. 별도 PC 표시 이름과 구분 |
| LEDOnOff | 일반 설정 LED, 상태 전환/페어링에도 유지 |
| AutoPath | 일반 설정 및 설치 옵션 Windows 시작 시 트레이 실행 |
| FontName / FontSize | Segoe UI/Malgun Gothic 및 글자 크기, PerMonitorV2. 임의 폰트 선택은 미이식 |
| HOTKEY/Client1~4 / Bluetooth1~3 / DisConn / DiscUse | 기기 ID별 사용자 단축키와 PC/마지막 기기 전역 단축키. 공유 전역 키 토글 허용. 원본 키 문자열 직접 가져오기는 미이식 |
| BDAddr1~4 / Alias1~4 | 동글 등록 주소 읽기, PC 표시 이름·배치·단축키 저장 |
| LangSwitchKey | 한/영 Ctrl+Space 또는 기본 HID 한/영 전송 |
| MOUSE/Speed / Sensitivity / Threshold1 / Threshold2 / ThresLevel | 속도와 선택적 새 가속, 스크롤 배율. 원본 곡선과 임계값의 개별 숫자 편집 전체는 미이식 |
| KeyMapUse / KeyMapVer / KeyMapNum / KeyMapGrp1~4 | 버전/개수는 동글 키맵 메타데이터. 원본 저장 키맵 편집/변환은 미이식 |
| SPECIAL/Group1~4 / Active / GroupName1~4 / SpcKeyGroup1~4 | 원본 특수키 그룹 편집은 미이식. 기존 설정 앱 열기 제공 |
| PASTE/Locale / Target_OS / CtrlVOn | PC 클립보드 변환·폰 문자 입력 전송 전체 미이식. 기존 설정 앱 열기 제공. 기본 Ctrl+V 키 전달과 PC 클립보드 전송은 다른 기능 |
| ReconnUse1~4 / WinHotkey | 원본 재연결 정책/Windows 단축키별 예외 전체 미이식. 새 앱은 명시적 대상 전환과 실패 시 PC 복귀 |
| HelpMsgOn | 현재 도움말/상태 설명 제공. 원본 안내창 설정은 미이식 |
| Version / SIGNATURE / Boot / DeviceFunc / DeviceMac / PrevVersion / OpenDate | 앱·동글 메타데이터. 일반 사용자 토글로 노출하지 않음 |

새로 추가한 기능: 물리 크기 기준 다중 모니터·기기 배치, 경계 대기, 분리 기기 수동 전환, 양방향 PC 화면 카운트다운과 브리딩, 라이트/다크/시스템 테마, 전체 페이지 스크롤, 입력 큐/키 해제 복구, 설치 프로그램.

동글 이름 경로는 원본439C20에서419210을 거쳐 EEPROM read-modify-write로 이어지는 것을 확인했다. 예전 문서에서403740의 로컬 설정 쓰기만 보아 실제 이름 변경이 없다고 해석한 부분을 바로잡았다. 근거는 Original-Dongle-Name-evidence.json에 있다.
