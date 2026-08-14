# Summoners War Coupon Manager

Windows용 Summoners War 쿠폰 스캔·자동 등록 GUI 프로그램입니다.

## 주요 기능

- SWGT, SW-Teams, SWQ의 실제 쿠폰 요소만 출처별 전용 파서로 읽고 코드를 중복 제거
- 후보 쿠폰을 공식 Hive 쿠폰 페이지에서 계정별로 한 번씩 시도
- GUI에서 계정 추가, 수정, 삭제 및 사용 계정 선택
- 성공, 이미 사용, 만료, 무효 결과를 계정별로 기록하고 완료된 쿠폰은 재시도하지 않음
- 숨겨진 WebView2에서 한국 서버 선택과 쿠폰 등록 자동화
- GitHub Releases 기반 자동 업데이트 및 자동 재시작
- `새 쿠폰 찾기 → 새 쿠폰 받기` 중심의 간단한 사용자 화면

쿠폰 출처는 기본 목록에서 숨기고 마우스를 올렸을 때만 표시합니다. 사이트 전체 텍스트에 범용 영숫자 정규식을 적용하지 않으므로 HTML 해시, 메뉴 문자열, 만료된 SWQ 항목이 쿠폰으로 수집되지 않습니다.

## 사용자 데이터

배포 파일에는 기본 계정, Hive ID, 닉네임 등 개인정보가 포함되지 않습니다. 사용자가 최초 실행 후 직접 계정을 등록합니다.

사용자 데이터는 프로그램 설치 폴더가 아닌 다음 위치에 보존됩니다.

`%LOCALAPPDATA%\SWCouponManager\`

- `state.json`: 계정, 선택 상태, 쿠폰 처리 기록, 마지막 스캔 결과, 창 상태
- `state.backup.json`: 이전 정상 상태의 자동 백업

자동업데이트는 이 폴더를 변경하지 않으므로 프로그램 버전이 바뀌어도 계정과 쿠폰 기록이 유지됩니다.

## 필수 런타임

- Windows 10/11 x64
- [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/#download-section)

배포본은 framework-dependent 방식입니다. .NET 또는 WebView2 Runtime은 Release ZIP에 포함하지 않습니다.

.NET 8 Desktop Runtime이 없으면 Windows용 .NET apphost가 프로그램 시작 전에 Microsoft 설치 안내를 표시합니다. WebView2 Runtime이 없으면 프로그램이 시작 시 이를 확인하고 설치 페이지를 열 수 있도록 안내합니다. 이미 설치되어 있으면 별도 안내 없이 바로 실행합니다.

## 자동 업데이트

실행 시 `Ke9318/summonerswar-coupon-manager`의 최신 GitHub Release를 확인합니다. 사용자가 업데이트를 선택하면 가벼운 `SWCouponManager-win-x64.zip`만 임시 폴더에 다운로드하고 프로그램 파일을 교체한 후 자동 재시작합니다.

사용자 데이터가 있는 `%LOCALAPPDATA%\SWCouponManager\`는 업데이트 ZIP과 교체 대상에 포함되지 않습니다.

## 로컬 빌드

.NET 8 SDK가 설치된 Windows에서 실행합니다.

```powershell
dotnet restore -r win-x64 -p:SelfContained=false
dotnet build -c Release -r win-x64 --no-restore
dotnet publish -c Release -r win-x64 --self-contained false --no-restore -o publish
```

저장 및 백업 복구 자체 테스트:

```powershell
.\publish\SWCouponManager.exe --self-test
```

## GitHub Release

프로젝트의 `<Version>`과 일치하는 `v1.1.0`, `v1.2.0` 형식의 태그를 push하면 `.github/workflows/release.yml`이 Windows x64 framework-dependent 배포본을 만들고 다음 이름으로 업로드합니다.

`SWCouponManager-win-x64.zip`
