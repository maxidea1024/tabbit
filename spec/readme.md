# 설계 문서

기능이 왜 그렇게 되었는지, 무엇을 재고 무엇을 안 쓰기로 했는지 적는 곳입니다.
**쓰는 법이 아니라 정한 이유**가 여기 있습니다 — 쓰는 법은
[문서 목록](../doc/readme.md)에 있습니다.

> 폴더가 주제입니다. 문서 하나를 지웠을 때 다른 폴더가 흔들리면 분류가 잘못된 것입니다.

> **「이전 코퍼스」라고 적힌 수치는 재현되지 않습니다.** 이 도구는 한동안 상용 프로젝트 둘의
> 워크북으로 검증했고, 그 데이터는 회사 자산이라 저장소에 없습니다. 설계의 근거로 그때의 실측을
> 인용하는 문서가 여럿 있으며, 그 자리에 「이전 대규모 코퍼스」·「이전 소규모 코퍼스」라고
> 적혀 있습니다 — **결론은 형식에 남아 있고 근거는 남아 있지 않으므로 기록으로 읽으십시오.**
> 지금 재현되는 수치는 [`samples/`](../samples/readme.md)의 합성 코퍼스에서 나옵니다.

---

## 시트 레이아웃

시트에 무엇이 어떻게 놓이는지 — 선언 셀 · 경계 · 헤더 행 · 제약

- [컬럼 제약 — 타입으로 나타낼 수 없는 것](layout/column-constraints.md)
- [구글 시트의 정의된 이름](layout/google-sheets-named-ranges.md)
- [매트릭스 표 — 컬럼 이름이 행 id인 것](layout/matrix-tables.md)
- [주 시트 레이아웃 — 설계](layout/primary-layout.md) *(5개로 나뉨)*
- [테이블의 행 벌 — 한 테이블에 데이터 여러 벌](layout/table-row-sets.md)

## 값과 타입

컬럼에 적을 수 있는 것과 그 형태 — 중첩 · 옵셔널 · 배열 · 합성 값 · 다형

- [배열 옵셔널리티 — 첫 원소가 배열 전체를 대표](types/array-optionality.md)
- [비트셋 — `bitset` 타입](types/bitset.md)
- [빈 칸과 없음 — `-`와 `\-`](types/blank-and-null-cells.md)
- [합성 값 타입 — 벡터 · 회전 · 색](types/composite-value-types.md)
- [시트의 datetime과 시간대](types/datetime-timezone.md)
- [수식 오류 — 읽는 셀만 보고합니다](types/formula-errors.md)
- [중첩 필드](types/nested-fields.md)
- [다중 중첩 — 두 형태와 그 값의 차이](types/nested-multi-level.md)
- [원소가 없을 수 있는 배열 — `T?[]` · `T[]?` · `T?[]?`](types/nullable-array-elements.md)
- [옵셔널 필드 — 타입 끝의 `?`](types/optional-fields.md)
- [다형과 참조 배열](types/polymorphism.md) *(3개로 나뉨)*
- [레코드 멤버별 옵셔널 — `:requiredInObject`](types/record-member-optionality.md)
- [`set`과 `map` — STRUCT DSL의 컨테이너](types/set-and-map.md)
- [가변 길이 레코드 배열](types/variable-length-record-arrays.md)

## 참조

한 테이블이 다른 테이블을 가리키는 것과, 그때 생성 코드가 내는 이름

- [다중 대상 참조의 접근자](references/multi-target-accessors.md)
- [다중 대상 참조와 빌드 변종](references/multi-target-references.md)
- [참조가 가리킬 수 있는 키 — `int32` 가정 걷어내기](references/reference-key-types.md)
- [참조의 「없음」 — 빈 칸과 0](references/reference-optionality.md)
- [참조가 내는 이름](references/reference-surface-naming.md)
- [레코드 안의 참조](references/references-in-records.md)

## `.tcb` 와이어 형식

파일에 실리는 배치와 인코딩. 버전마다 무엇이 달라졌고 왜 그랬는지

- [컬럼 지향을 고른 이유 — TCB의 배치 결정](wire/tcb-column-oriented-rationale.md)
- [TCB — 파일 시그니처와 MAC](wire/tcb-mac-and-signature.md)
- [TCB v102 — 컬럼 인코딩과 암호화](wire/tcb-v102-column-encoding.md)
- [TCB v103 — presence 비트맵](wire/tcb-v103-presence-bitmap.md)
- [TCB v104 — 조합 인코딩과 파일 암호화](wire/tcb-v104-composed-encodings.md)
- [TCB v105 — 비트폭 패킹](wire/tcb-v105-bit-width-packing.md)
- [TCB v106 — 원소 presence 비트맵](wire/tcb-v106-element-presence.md)
- [v107 — 동적 배열 단일화](wire/tcb-v107-dynamic-arrays.md)

## 타깃과 생성 코드

언어 지원 · 이름 표기 · 내보내기 형식 · 산출 문서

- [접근자 객체화 — 인스턴스와 전역 헬퍼](targets/accessor-instances.md)
- [Cocos Creator 지원](targets/cocos-creator-support.md)
- [상수 세트 제거](targets/constant-set-removal.md)
- [C# 레코드 이름 — 중첩 `Record`에서 `{테이블}Record`로](targets/csharp-record-name.md)
- [파일 내보내기 형식 — `bson` · `jsonl` · `csv` · `sqlite`](targets/export-formats.md)
- [생성 코드의 이름 체계](targets/generated-naming.md)
- [Godot 지원](targets/godot-support.md)
- [HTML 문서 — 데이터 확인 산출물의 구성](targets/html-documentation.md)
- [Lua 언어 지원](targets/lua-language-support.md) *(3개로 나뉨)*
- [이름 표기 규약](targets/naming-conventions.md) *(3개로 나뉨)*
- [Swift 언어 지원](targets/swift-language-support.md)
- [테이블의 컬렉션 표면 — 개수 · 순회 · 첨자](targets/table-collection-surface.md)

## 검증

시트로 나타낼 수 없는 규칙, 그리고 그것을 쓰고 돌리는 환경

- [메시지 ID — 메시지를 식별할 수 있게 하고, 그 뒤에 언어를 붙이기](validation/message-ids.md)
- [규칙 우선순위 — 티어와 차단점](validation/rule-priority.md)
- [검증 파이프라인 — 시트로 나타낼 수 없는 규칙](validation/validation-pipeline.md) *(3개로 나뉨)*
- [검증 사용성과 C# 어셈블리 산출](validation/validation-usability-and-assembly-output.md) *(4개로 나뉨)*

## 워크북 읽기

엑셀과 `.xlsb` 를 읽는 경로, 그리고 워크북 병합

- [워크북 읽기 — 객체 모델에서 스트리밍으로](import/streaming-workbook-reader.md)
- [mabbit — 워크북 3-way 의미 병합](import/workbook-merge.md) *(3개로 나뉨)*
- [`.xlsb`의 정의된 이름 — 변환 단계 제거](import/xlsb-defined-names.md)
- [`.xlsb`의 잘린 행 복구](import/xlsb-short-row-repair.md)

## 빌드와 운영

캐시 · 리포트 · CLI · 설치 · 편집기 · 여러 사람이 함께 쓸 때

- [빌드 캐시 — 바뀐 것이 없으면 아무것도 하지 않기](ops/build-cache.md) *(3개로 나뉨)*
- [빌드 리포트 — 찾은 문제를 고칠 사람에게 보이게 하기](ops/build-report.md)
- [CLI 도움말 — 첫 화면이 이 도구가 무엇인지 말하게 하기](ops/cli-help.md) *(3개로 나뉨)*
- [전체 변환의 소요 시간 — 어디로 가는지, 무엇을 고치는지](ops/conversion-time.md)
- [설치 경로 — 패키지 관리자 배포](ops/install-channels.md)
- [알려진 문제 목록 — 고칠 수 없는 자리를 적어 두고 넘어가기](ops/known-problems.md)
- [편집기 지원 — `.tbs` 를 쓰는 동안 답하기](ops/lsp.md)
- [여러 사람이 함께 쓸 때 — 운영 규약과 도구의 결손](ops/multi-user-operations.md) *(3개로 나뉨)*
- [출력 항목을 `Targets` 하나로](ops/target-section-unification.md)
