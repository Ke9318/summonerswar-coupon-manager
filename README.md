# Summoners War Coupon Manager

Windows용 Summoners War 쿠폰 검색·자동 등록 GUI 프로그램입니다.

## 주요 기능

- SWGT, SW-Teams, SWQ의 명시적 쿠폰 영역과 GitHub 원격 후보 목록 수집
- 핵심 소스 다중 요청, payload hash/advertised count/급감 비교, 최근 관측 코드 grace 보존
- 스캔 직후 fetch 성공 수·추출 수·보존 수·stale/inconsistent 경고를 보여주는 소스 상태 창
- 각 소스를 별도 기준 파서로 다시 읽고 production 결과와 차집합 비교
- HTTP/응답 크기/production·reference 개수/missing·extra를 `scan-health.log`에 기록
- 여러 출처의 후보를 대소문자 구분 없이 중복 제거하고 출처 함께 표시
- 후보는 선택한 모든 계정에서 공식 Hive 쿠폰 페이지로 한 번씩 검증
- 성공, 이미 사용, 만료, 무효는 계정별 완료 기록으로 보존하여 재시도하지 않음
- 네트워크·페이지 인식 등 오류 상태만 다음 실행에서 재시도
- GUI 계정 추가·수정·삭제·선택, 계정별 서버 드롭다운, 새 쿠폰 찾기·받기, 진행 상황 및 처리 기록
- GitHub Releases 기반 자동 업데이트 및 자동 재시작

탐지는 쿠폰 누락 방지를 우선합니다. Hive 링크, `<code>` 영역, 쿠폰 테이블 열, JSON의 명시적 코드 필드처럼 쿠폰으로 표시된 영역은 공백·HTML·URL·비정상 장문만 최소 정리하며 코드 모양으로 제외하지 않습니다. 쿠폰 문맥 없는 페이지 전체 영문 문자열은 수집하지 않습니다. `coupon_candidates.json`을 수정하면 프로그램 업데이트 없이 원격 후보를 추가할 수 있고, 실제 유효 여부는 Hive의 응답으로 최종 판정합니다.

`SeenCodes`는 화면의 “새 쿠폰 후보” 개수만 계산합니다. 실제 실행 여부는 계정별 `History[accountId][code]`로 판단하므로, 이전에 발견했더라도 해당 계정에서 완료 판정이 없으면 Hive 검증 큐에 포함됩니다. `success`, `already`, `expired`, `invalid`는 영구 재시도하지 않고 `error`만 다음 실행에서 재시도합니다.

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

실제 네 소스의 명시적 전체 목록과 production 추출 결과를 비교하는 릴리스 게이트:

```powershell
.\publish\SWCouponManager.exe --scan-test
Get-Content "$env:TEMP\SWCouponManager-scan-test.log"
```

`reference - production`에 하나라도 남거나 네트워크/응답 구조를 검증할 수 없으면 종료 코드 1입니다. `production - reference`는 오탐 검토용으로 기록하지만 누락 우선 정책에 따라 실패 조건은 아닙니다.

과거 누락 감사는 사용자의 실제 `%LOCALAPPDATA%\SWCouponManager\state.json`과 배포본의 `known_codes_archive.json`을 입력합니다. 원본 History는 수정하지 않고 같은 폴더에 `state.audit.txt`를 만듭니다.

```powershell
.\SWCouponManager.exe --audit-history "$env:LOCALAPPDATA\SWCouponManager\state.json" --audit-codes .\known_codes_archive.json
```

다른 과거 목록은 `{"codes":[{"code":"...","category":"swc-emblem"}]}` JSON 또는 줄 단위 텍스트로 전달할 수 있습니다. 실제 History 파일 없이 과거 수령 여부를 결론낼 수는 없습니다.

## GitHub Release

프로젝트 `<Version>`과 일치하는 `v1.0.0`, `v1.3.1` 형식의 태그를 push하면 `.github/workflows/release.yml`이 Windows x64 framework-dependent 배포본을 만들고 다음 이름으로 업로드합니다.

`SWCouponManager-win-x64.zip`
