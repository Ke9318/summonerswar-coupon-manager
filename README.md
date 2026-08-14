# Summoners War Coupon Manager

Windows용 Summoners War 쿠폰 스캔·자동 등록 GUI 프로그램입니다.

## 주요 기능

- SWGT, SW-Teams, SWQ 쿠폰 소스를 독립적으로 스캔
- 여러 소스에서 수집한 쿠폰 코드 병합 및 중복 제거
- 후보 쿠폰도 공식 Hive 쿠폰 페이지에서 한 번 시도
- GUI에서 계정 추가, 수정, 삭제 및 사용 계정 선택
- `새 쿠폰 등록`: 계정별 완료 기록을 확인해 미처리 쿠폰만 실행
- `전체 검사`: 현재 발견된 모든 쿠폰을 다시 검사
- 성공, 이미 사용, 만료, 무효, 오류 결과를 계정별로 기록
- 숨겨진 WebView2에서 한국 서버 선택과 쿠폰 등록 자동화
- GitHub Releases 기반 자동 업데이트 및 자동 재시작

## 개인정보와 사용자 데이터

배포 파일과 저장소에는 기본 계정, Hive ID, 닉네임이 포함되지 않습니다. 사용자가 프로그램을 처음 실행한 뒤 직접 계정을 등록합니다.

사용자 데이터는 프로그램 설치 폴더가 아닌 다음 위치에 저장됩니다.

`%LOCALAPPDATA%\SWCouponManager\`

- `state.json`: 계정, 선택 상태, 쿠폰 처리 기록, 마지막 스캔 결과, 창 위치와 크기
- `state.backup.json`: 이전 정상 상태의 자동 백업

프로그램 업데이트는 이 폴더를 건드리지 않으므로 계정과 쿠폰 기록이 유지됩니다. 기본 상태 파일이 손상되면 백업 파일을 사용해 복구합니다.

## 자동 업데이트

프로그램 시작 시 `Ke9318/summonerswar-coupon-manager`의 최신 GitHub Release를 확인합니다. 더 높은 버전이 있으면 화면에 업데이트 버튼이 표시됩니다.

업데이트를 실행하면 다음 순서로 처리됩니다.

1. `SWCouponManager-win-x64.zip`을 임시 폴더에 백그라운드 다운로드
2. ZIP을 별도 준비 폴더에 풀고 `SWCouponManager.exe` 포함 여부 확인
3. 현재 프로그램 자동 종료
4. 최대 5회 재시도하며 프로그램 파일 교체
5. 새 버전 자동 실행

사용자 데이터는 `%LOCALAPPDATA%`에 분리되어 있어 업데이트 대상에 포함되지 않습니다.

## 요구 사항

- Windows 10/11 x64
- Microsoft Edge WebView2 Runtime

대부분의 Windows 10/11에는 Evergreen WebView2 Runtime이 설치되어 있습니다. 없는 경우 Microsoft에서 WebView2 Runtime을 설치해야 합니다.

## 로컬 빌드

.NET 8 SDK가 설치된 Windows에서 실행합니다.

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

저장 및 백업 복구 자체 검사는 다음과 같이 실행할 수 있습니다.

```powershell
.\publish\SWCouponManager.exe --self-test
```

## GitHub Release

프로젝트의 `<Version>`과 일치하는 `v1.0.0`, `v1.0.1`, `v1.1.0` 형식의 태그를 push하면 `.github/workflows/release.yml`이 Windows x64 self-contained 배포본을 빌드합니다.

워크플로는 자체 검사 후 다음 이름의 Release asset을 생성합니다.

`SWCouponManager-win-x64.zip`
