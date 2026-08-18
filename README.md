# Summoners War Coupon Manager

Windows용 Summoners War 쿠폰 검색·자동 등록 GUI 프로그램입니다.

## 주요 기능

- SWGT, SW-Teams, SWQ의 명시적 쿠폰 영역과 GitHub 원격 후보 목록 수집
- 여러 출처의 후보를 대소문자 구분 없이 중복 제거하고 출처 함께 표시
- 후보는 선택한 모든 계정에서 공식 Hive 쿠폰 페이지로 한 번씩 검증
- 성공, 이미 사용, 만료, 무효는 계정별 완료 기록으로 보존하여 재시도하지 않음
- 네트워크·페이지 인식 등 오류 상태만 다음 실행에서 재시도
- GUI 계정 추가·수정·삭제·선택, 계정별 서버 드롭다운, 새 쿠폰 찾기·받기, 진행 상황 및 처리 기록
- GitHub Releases 기반 자동 업데이트 및 자동 재시작

탐지는 쿠폰 누락 방지를 우선합니다. Hive 링크, `<code>` 영역, 쿠폰 테이블 열, JSON의 명시적 코드 필드, 쿠폰 문맥에 붙은 문자열만 읽으며 페이지 전체 영문 문자열은 수집하지 않습니다. `coupon_candidates.json`을 수정하면 프로그램 업데이트 없이 원격 후보를 추가할 수 있고, 실제 유효 여부는 Hive의 응답으로 최종 판정합니다.

## 사용자 데이터

배포 파일에는 기본 계정, Hive ID, 닉네임 등 개인정보가 포함되지 않습니다. 사용자가 최초 실행 후 직접 계정을 등록합니다.

사용자 데이터는 프로그램 설치 폴더가 아닌 다음 위치에 보존됩니다.

`%LOCALAPPDATA%\SWCouponManager\`

- `state.json`: 계정, 선택 상태, 계정별 쿠폰 처리 기록, 전체 발견 이력, 마지막 스캔 결과, 창 상태
- `state.backup.json`: 이전 정상 상태의 자동 백업

자동업데이트는 이 폴더를 변경하지 않으므로 버전이 바뀌어도 계정과 쿠폰 기록이 유지됩니다.

## 필수 환경

- Windows 10/11 x64
- [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/#download-section)

배포본은 framework-dependent 방식이며 .NET 또는 WebView2 Runtime을 Release ZIP에 포함하지 않습니다. 필요한 런타임이 없으면 프로그램 시작 시 설치 안내를 표시합니다.

## 자동 업데이트

실행 시 `Ke9318/summonerswar-coupon-manager`의 최신 GitHub Release를 확인합니다. 사용자가 업데이트를 누르면 `SWCouponManager-win-x64.zip`을 임시 폴더에 다운로드하고 프로그램 파일만 교체한 뒤 자동 재시작합니다. `%LOCALAPPDATA%\SWCouponManager\`의 사용자 데이터는 업데이트 대상에 포함되지 않습니다.

## 로컬 빌드

```powershell
dotnet restore -r win-x64 -p:SelfContained=false
dotnet build -c Release -r win-x64 --no-restore
dotnet publish -c Release -r win-x64 --self-contained false --no-restore -o publish
```

저장·백업 복구, 후보 파서, Hive 결과 분류, 상태별 재시도 정책 자체 테스트:

```powershell
.\publish\SWCouponManager.exe --self-test
```

## GitHub Release

프로젝트 `<Version>`과 일치하는 `v1.0.0`, `v1.3.0` 형식의 태그를 push하면 `.github/workflows/release.yml`이 Windows x64 framework-dependent 배포본을 만들고 다음 이름으로 업로드합니다.

`SWCouponManager-win-x64.zip`
