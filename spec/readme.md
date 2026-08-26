# 설계 문서

기능이 왜 그렇게 되었는지, 무엇을 재고 무엇을 거절했는지 적는 곳입니다.
**쓰는 법이 아니라 정한 이유**가 여기 있습니다 — 쓰는 법은
[문서 목록](../doc/readme.md)에 있습니다.

> 폴더가 주제입니다. 문서 하나를 지웠을 때 다른 폴더가 흔들리면 분류가 잘못된 것입니다.

---

## 시트 레이아웃

시트에 무엇이 어떻게 놓이는지 — 선언 셀 · 경계 · 헤더 행 · 제약

|문서|무엇|
|--|--|
|[column-constraints.md](layout/column-constraints.md)|컬럼 제약 — 타입으로 나타낼 수 없는 것|
|[google-sheets-named-ranges.md](layout/google-sheets-named-ranges.md)|구글 시트의 정의된 이름|
|[keyed-layout.md](layout/keyed-layout.md)|행 키 레이아웃 — 설계|
|[matrix-tables.md](layout/matrix-tables.md)|매트릭스 표 — 컬럼 이름이 행 id인 것|
|[primary-layout.md](layout/primary-layout.md)|주 시트 레이아웃 — 설계 *(+5)*|
|[table-row-sets.md](layout/table-row-sets.md)|테이블의 행 벌 — 한 테이블에 데이터 여러 벌|

## 값과 타입

컬럼에 적을 수 있는 것과 그 형태 — 중첩 · 옵셔널 · 배열 · 합성 값 · 다형

|문서|무엇|
|--|--|
|[array-optionality.md](types/array-optionality.md)|배열 옵셔널리티 — 첫 원소가 배열 전체를 대표|
|[bitset.md](types/bitset.md)|비트셋 — `bitset` 타입|
|[blank-and-null-cells.md](types/blank-and-null-cells.md)|빈 칸과 없음 — `-`와 `\-`|
|[composite-value-types.md](types/composite-value-types.md)|합성 값 타입 — 벡터 · 회전 · 색|
|[datetime-timezone.md](types/datetime-timezone.md)|시트의 datetime과 시간대|
|[formula-errors.md](types/formula-errors.md)|수식 오류 — 읽는 셀만 보고합니다|
|[nested-fields.md](types/nested-fields.md)|중첩 필드|
|[nested-multi-level.md](types/nested-multi-level.md)|다중 중첩 — 두 형태와 그 값의 차이|
|[nullable-array-elements.md](types/nullable-array-elements.md)|원소가 없을 수 있는 배열 — `T?[]` · `T[]?` · `T?[]?`|
|[optional-fields.md](types/optional-fields.md)|옵셔널 필드 — 타입 끝의 `?`|
|[polymorphism.md](types/polymorphism.md)|다형과 참조 배열|
|[record-member-optionality.md](types/record-member-optionality.md)|레코드 멤버별 옵셔널 — `:requiredInObject`|
|[variable-length-record-arrays.md](types/variable-length-record-arrays.md)|가변 길이 레코드 배열|

## 참조

한 테이블이 다른 테이블을 가리키는 것과, 그때 생성 코드가 내는 이름

|문서|무엇|
|--|--|
|[multi-target-accessors.md](references/multi-target-accessors.md)|다중 대상 참조의 접근자|
|[multi-target-references.md](references/multi-target-references.md)|다중 대상 참조와 빌드 변종|
|[reference-key-types.md](references/reference-key-types.md)|참조가 가리킬 수 있는 키 — `int32` 가정 걷어내기|
|[reference-optionality.md](references/reference-optionality.md)|참조의 「없음」 — 빈 칸과 0|
|[reference-surface-naming.md](references/reference-surface-naming.md)|참조가 내는 이름|
|[references-in-records.md](references/references-in-records.md)|레코드 안의 참조|

## `.tcb` 와이어 형식

파일에 실리는 배치와 인코딩. 버전마다 무엇이 달라졌고 왜 그랬는지

|문서|무엇|
|--|--|
|[tcb-column-oriented-rationale.md](wire/tcb-column-oriented-rationale.md)|컬럼 지향을 고른 이유 — TCB의 배치 결정|
|[tcb-mac-and-signature.md](wire/tcb-mac-and-signature.md)|TCB — 파일 시그니처와 MAC|
|[tcb-v102-column-encoding.md](wire/tcb-v102-column-encoding.md)|TCB v102 — 컬럼 인코딩과 암호화|
|[tcb-v103-presence-bitmap.md](wire/tcb-v103-presence-bitmap.md)|TCB v103 — presence 비트맵|
|[tcb-v104-composed-encodings.md](wire/tcb-v104-composed-encodings.md)|TCB v104 — 조합 인코딩과 파일 암호화|
|[tcb-v105-bit-width-packing.md](wire/tcb-v105-bit-width-packing.md)|TCB v105 — 비트폭 패킹|
|[tcb-v106-element-presence.md](wire/tcb-v106-element-presence.md)|TCB v106 — 원소 presence 비트맵|
|[tcb-v107-dynamic-arrays.md](wire/tcb-v107-dynamic-arrays.md)|v107 — 동적 배열 단일화|

## 타깃과 생성 코드

언어 지원 · 이름 표기 · 내보내기 형식 · 산출 문서

|문서|무엇|
|--|--|
|[accessor-instances.md](targets/accessor-instances.md)|접근자 객체화 — 인스턴스와 전역 헬퍼|
|[cocos-creator-support.md](targets/cocos-creator-support.md)|Cocos Creator 지원|
|[constant-set-removal.md](targets/constant-set-removal.md)|상수 세트 제거|
|[export-formats.md](targets/export-formats.md)|파일 내보내기 형식 — `bson` · `jsonl` · `csv` · `sqlite`|
|[generated-naming.md](targets/generated-naming.md)|생성 코드의 이름 체계|
|[godot-support.md](targets/godot-support.md)|Godot 지원|
|[html-documentation.md](targets/html-documentation.md)|HTML 문서 — 데이터 확인 산출물의 구성|
|[lua-language-support.md](targets/lua-language-support.md)|Lua 언어 지원|
|[naming-conventions.md](targets/naming-conventions.md)|이름 표기 규약|
|[swift-language-support.md](targets/swift-language-support.md)|Swift 언어 지원|

## 검증

시트로 나타낼 수 없는 규칙, 그리고 그것을 쓰고 돌리는 환경

|문서|무엇|
|--|--|
|[message-ids.md](validation/message-ids.md)|메시지 ID — 보고에 신분증을 주고, 그 뒤에 언어를 붙이기|
|[rule-priority.md](validation/rule-priority.md)|규칙 우선순위 — 티어와 차단점|
|[validation-pipeline.md](validation/validation-pipeline.md)|검증 파이프라인 — 시트로 나타낼 수 없는 규칙 *(+3)*|
|[validation-usability-and-assembly-output.md](validation/validation-usability-and-assembly-output.md)|검증 사용성과 C# 어셈블리 산출 *(+4)*|

## 워크북 읽기

엑셀과 `.xlsb` 를 읽는 경로, 그리고 워크북 병합

|문서|무엇|
|--|--|
|[streaming-workbook-reader.md](import/streaming-workbook-reader.md)|워크북 읽기 — 객체 모델에서 스트리밍으로|
|[workbook-merge.md](import/workbook-merge.md)|mabbit — 워크북 3-way 의미 병합|
|[xlsb-defined-names.md](import/xlsb-defined-names.md)|`.xlsb`의 정의된 이름 — 변환 단계 제거|
|[xlsb-short-row-repair.md](import/xlsb-short-row-repair.md)|`.xlsb`의 잘린 행 복구|

## 빌드와 운영

캐시 · 리포트 · CLI · 설치 · 여러 사람이 함께 쓸 때

|문서|무엇|
|--|--|
|[build-cache.md](ops/build-cache.md)|빌드 캐시 — 바뀐 것이 없으면 아무것도 하지 않기|
|[build-report.md](ops/build-report.md)|빌드 리포트 — 찾은 문제를 고칠 사람에게 보이게 하기|
|[cli-help.md](ops/cli-help.md)|CLI 도움말 — 첫 화면이 이 도구가 무엇인지 말하게 하기|
|[conversion-time.md](ops/conversion-time.md)|전체 변환의 소요 시간 — 어디로 가는지, 무엇을 고치는지|
|[install-channels.md](ops/install-channels.md)|설치 경로 — 패키지 관리자 배포|
|[known-problems.md](ops/known-problems.md)|알려진 문제 목록 — 고칠 수 없는 자리를 적어 두고 넘어가기|
|[multi-user-operations.md](ops/multi-user-operations.md)|여러 사람이 함께 쓸 때 — 운영 규약과 도구의 결손|
|[target-section-unification.md](ops/target-section-unification.md)|출력 항목을 `Targets` 하나로|

---

문서 59개입니다. *(+n)* 이 붙은 것은 그 문서가 n개로 나뉘어 있다는 뜻이고,
들어가면 목차가 있습니다.
