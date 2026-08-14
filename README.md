# Summoners War Coupon Manager

Summoners War 쿠폰을 여러 공개 소스에서 찾아 계정별로 자동 등록하는 Tampermonkey userscript입니다.

## 현재 버전

v4.8.0

## 주요 기능

- SWGT와 SW-Teams의 활성 쿠폰을 독립적으로 스캔하고 결과를 병합·중복 제거
- 한 소스가 실패해도 다른 소스 결과로 계속 진행
- GUI 계정 추가·수정·삭제 및 계정 선택
- `새 쿠폰만` 또는 `모든 활성 쿠폰` 실행
- 계정별 성공/이미 사용/만료/무효 기록 보존
- 백그라운드 작업 탭에서 자동 등록 후 완료 시 자동 종료
- GitHub raw URL을 통한 Tampermonkey 자동 업데이트

## 설치

- [공유용 설치](https://raw.githubusercontent.com/Ke9318/summonerswar-coupon-manager/main/SW_Coupon_Manager.user.js)
- [개인용 설치](https://raw.githubusercontent.com/Ke9318/summonerswar-coupon-manager/main/SW_Coupon_Manager_Personal.user.js)

Raw 링크를 열어 Tampermonkey에 설치하세요.

## 공유용과 개인용의 차이

- 공유용 `SW_Coupon_Manager.user.js`: 기본 계정이 비어 있습니다.
- 개인용 `SW_Coupon_Manager_Personal.user.js`: 저장소에 설정된 개인 기본 계정 2개가 포함됩니다.
- 두 스크립트는 서로 다른 `@updateURL`과 `@downloadURL`을 사용하므로 개인용이 공유용으로 바뀌지 않습니다.

## 자동 업데이트

파일명은 버전과 무관하게 위 이름으로 고정합니다. 이후 v4.9, v5.0에서도 같은 raw URL에 더 높은 `@version`을 게시하면 Tampermonkey가 업데이트를 감지합니다.

저장 키 `sw_coupon_manager_v46`은 기존 계정 정보, 처리 기록, 쿠폰 사용 이력을 유지하기 위해 변경하지 않습니다.

## v4.8.0

- SWGT + SW-Teams 다중 소스 쿠폰 감지
- 부분 실패를 허용하는 독립 스캔
- 일반 영문+숫자 쿠폰 추출 및 UI 단어 blacklist 적용
- 개인용 자동 업데이트 URL 분리
- v4.7의 계정 관리, 미처리 쿠폰 큐, 공식 교환소 DOM 자동화, 백그라운드 실행 로직 유지
