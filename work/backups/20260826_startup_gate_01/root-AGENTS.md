# Workspace router

이 폴더에서 SWCouponManager 작업을 시작하면 먼저 다음 문서를 순서대로 모두 읽는다.

1. `source/AGENTS.md`
2. `source/PROJECT_CHARTER.md`
3. `source/AUTONOMY_POLICY.md`
4. `source/AI_WORKING_MODEL.md`
5. `source/ACCEPTANCE_GATES.md`
6. `source/EXPERIENCE_RULES.md`
7. `source/HANDOFF.md`
8. `source/work/current-task.md`

`source/`가 canonical 구현 저장소다. 제품 코드·문서·테스트 변경은 그 안에서 수행한다. `app/`은 생성된 실행 배포본이므로 source와 검증 없이 직접 수정하거나 source로 간주하지 않는다. `tools/`와 `review-work/`는 지원·임시 자료이며 제품 진실의 근거가 아니다.

계정/Hive ID, 실제 사용자 상태, credential 및 민감 로그를 읽거나 복사하거나 저장소에 포함하지 않는다. 나머지 작업 규칙은 `source/AGENTS.md`를 따른다.
