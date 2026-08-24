# 문서

아래로 갈수록 깊어집니다.

처음이라면 **시작하기**만 보면 되고, **설계 노트**는 「왜 이렇게 되었는가」가 궁금할 때
읽는 것입니다.

> [저장소로](../readme.md)

---

## 시작하기

| 문서 | 내용 |
| --- | --- |
| [설치](install.md) | 소스에서 빌드하기, 릴리즈 내려받기, 받은 파일 확인 |
| [용어](glossary.md) | 문서가 설명 없이 쓰는 말들. 골든, 픽스처, 스위트, 와이어, 쿠킹 |
| [시트에 무엇을 적을 수 있나](concepts.md) | 테이블, enum, 상수셋 세 가지와 각각이 무엇으로 생성되는지 |
| [시트 작성](sheets.md) | 선언 셀과 헤더 행, 경로 표기, 멀티 로우, 이름 규칙, 지원 타입, 서버/클라이언트 분리 |
| [CLI](cli.md) | 실행하는 방법과 명령줄 옵션 |
| [Recipe 파일](recipe.md) | 데이터를 어디서 읽고 어디로 출력할지 정의하는 파일 |

## 쓰는 법

| 문서 | 내용 |
| --- | --- |
| [기능](features.md) | Tabbit이 하는 일 전체 |
| [언어별 가이드](languages/readme.md) | 생성된 코드를 각 프로젝트에 적용하고 사용하는 방법 |
| [검증](validation.md) | C# 검증 코드. 실행 전, 테이블별, 전역, 런타임 네 단계와 공용 코드 |
| [내보내기](exports.md) | 바이너리, JSON, 데이터베이스 출력과 바이너리를 사용하는 이유 |
| [Summary와 히스토리](history.md) | 누가 언제 무엇을 바꿨는지 셀 단위로 추적하고 브라우저로 확인하기 |
| [트러블슈팅](troubleshooting.md) | 빌드 실패 시 실제 출력 메시지를 기준으로 문제를 찾는 방법 |

## 형식 — TCB

| 문서 | 내용 |
| --- | --- |
| [바이너리 형식](binary-format.md) | `.tcb` 파일의 레이아웃과 스키마가 달라졌을 때의 보장 |
| [왜 컬럼 지향인가](../spec/tcb-column-oriented-rationale.md) | 이 형식이 맞는 상황과 맞지 않는 상황. Parquet, Arrow, FlatBuffers와의 차이 |
| [벤치마크](benchmark.md) | 실제 게임 데이터로 측정한 크기, 로드 시간, CPU, 메모리 |

### 개정 기록

각 문서는 그 개정이 무엇을 바꿨고, 무엇을 바꾸지 않았는지 적습니다.

| 개정 | 내용 |
| --- | --- |
| [v102](../spec/tcb-v102-column-encoding.md) | 컬럼 인코딩과 암호화의 자리 |
| [v103](../spec/tcb-v103-presence-bitmap.md) | presence 비트맵. 값의 존재 여부를 와이어에 담습니다 |
| [v104](../spec/tcb-v104-composed-encodings.md) | 조합 인코딩 9종에서 13종으로, 그리고 파일 암호화 |
| [v105](../spec/tcb-v105-bit-width-packing.md) | 비트폭 패킹. 설계 결정 셋을 계측이 뒤집은 기록 |
| [v106](../spec/tcb-v106-element-presence.md) | 원소 presence 비트맵. 배열의 어느 자리에 값이 있는지 |
| [v107](../spec/tcb-v107-dynamic-arrays.md) | 동적 배열 단일화. 파일이 오히려 2.5% 작아졌습니다 |
| [MAC과 시그니처](../spec/tcb-mac-and-signature.md) | 변조 검출. 암호화가 변조 저항을 주지 않고 있던 자리 |

## 설계 노트

`spec/`의 문서들입니다.

사용법이 아니라 결정의 기록입니다. 무엇을 고쳤는지, 무엇을 거절했는지, 그리고 예측이 어디서
틀렸는지 적습니다.

### 값의 형태

| 문서 | 내용 |
| --- | --- |
| [중첩 필드](../spec/nested-fields.md) | 컬럼 여러 개를 레코드 하나로 접으면서 와이어 형식은 그대로 둔 방법 |
| [다중 중첩](../spec/nested-multi-level.md) | 멤버가 배열인 레코드와 배열의 배열. 깊이 제한을 없앤 근거 |
| [매트릭스 표](../spec/matrix-tables.md) | 컬럼 이름이 행 id인 격자. long-form과 map을 채택하지 않은 이유 |
| [가변 길이 레코드 배열](../spec/variable-length-record-arrays.md) | 배열 길이를 행마다 다르게. 뒤에서만 자르는 이유 |
| [비트셋](../spec/bitset.md) | `bitset` 타입과 진법 리터럴. 엄격함이 의도 선언을 요구하는 이유 |
| [합성 값 타입](../spec/composite-value-types.md) | 벡터, 회전, 색을 타입 추가가 아니라 레코드로 접는 이유 |
| [옵셔널 필드](../spec/optional-fields.md) | 타입 끝 `?`의 설계. 존재 여부를 와이어에 담는 방법 |
| [빈 칸과 없음](../spec/blank-and-null-cells.md) | 빈 칸이 값과 없음을 겸하던 것을 표기 하나로 나누는 설계 |
| [원소가 없을 수 있는 배열](../spec/nullable-array-elements.md) | `T?[]` · `T[]?` · `T?[]?`. 원소의 없음을 파일이 담는 설계 |
| [배열의 옵셔널](../spec/array-optionality.md) | 배열 컬럼들의 필수 표시가 엇갈릴 때 첫 원소가 전부를 정하는 이유 |
| [레코드 멤버별 옵셔널](../spec/record-member-optionality.md) | `:requiredInObject`가 검증 규칙이지 표현의 요구가 아닌 이유 |

### 참조

| 문서 | 내용 |
| --- | --- |
| [다중 대상 참조](../spec/multi-target-references.md) | 값이 여러 테이블 중 하나의 행이어야 할 때 |
| [참조가 가리킬 수 있는 키](../spec/reference-key-types.md) | 인덱스는 `int` 말고도 되는데 참조만 `int32`에 묶여 있던 것 |
| [레코드 안의 참조](../spec/references-in-records.md) | 레코드 그룹의 멤버가 다른 테이블을 가리킬 때 |
| [다중 대상 참조의 접근자](../spec/multi-target-accessors.md) | 공개 표면은 대상별로, 저장은 슬롯 하나와 식별자로 |
| [참조의 「없음」](../spec/reference-optionality.md) | 참조 컬럼의 빈 칸. 없음은 명시적으로, 허용된 자리에서만 |

### 검증

| 문서 | 내용 |
| --- | --- |
| [컬럼 제약](../spec/column-constraints.md) | 범위, 허용값, 필수 여부를 시트에서 읽어 어느 셀인지 함께 검사하기 |
| [검증 파이프라인](../spec/validation-pipeline.md) | 시트로 표현할 수 없는 규칙을 C# 규칙 파일로 |
| [규칙 우선순위](../spec/rule-priority.md) | 앞 티어가 오류를 낸 자리에서 뒤 티어를 실행하지 않는 것 |
| [검증 사용성과 어셈블리 산출](../spec/validation-usability-and-assembly-output.md) | 규칙이 참조하는 것을 별도 어셈블리로, C# 산출을 `.dll` 하나로 |

### 읽기와 내기

| 문서 | 내용 |
| --- | --- |
| [워크북 읽기](../spec/streaming-workbook-reader.md) | 엑셀을 객체 모델이 아니라 스트리밍으로. 후보 다섯의 실측 |
| [`.xlsb`의 정의된 이름](../spec/xlsb-defined-names.md) | 이진 워크북에서 이름을 직접 읽어 사전 변환 단계를 없애는 설계 |
| [`.xlsb`의 잘린 행 복구](../spec/xlsb-short-row-repair.md) | 셀 리더가 행을 짧게 보고해 값이 사라지던 결함과 0.31%만 되읽는 설계 |
| [구글 시트의 정의된 이름](../spec/google-sheets-named-ranges.md) | 임포터 둘 중 하나만 이름을 읽어 테이블이 0개가 되던 조합 |
| [행 키 레이아웃](../spec/keyed-layout.md) | 이름이 가리키는 사각형을 그대로 격자로 사용하는 레이아웃 |
| [수식 오류](../spec/formula-errors.md) | `#N/A`를 든 셀을 모델이 값으로 들 때만 보고하는 이유 |
| [시트의 datetime과 시간대](../spec/datetime-timezone.md) | 시트에 적힌 시각을 어느 시간대로 읽을지 recipe가 정하고, 저장은 UTC로 |
| [알려진 문제 목록](../spec/known-problems.md) | 지금 고칠 수 없는 자리를 적어 두고 빌드를 계속하는 장치 |
| [테이블의 행 벌](../spec/table-row-sets.md) | 한 테이블에 데이터 여러 벌. 타입은 하나입니다 |
| [생성 코드의 이름 체계](../spec/generated-naming.md) | `AccessorName` 하나가 모든 언어의 타입 이름과 파일 이름을 정하는 방법 |
| [이름 표기 규약](../spec/naming-conventions.md) | 시트 표기를 recipe가 선언하고 코어가 강제하는 설계 |
| [Swift 언어 지원](../spec/swift-language-support.md) | 행을 `final class`로, 원소를 `struct`로 가른 이유 |
| [Lua 언어 지원](../spec/lua-language-support.md) | 읽기 오타가 오류조차 아닌 언어에 엄격 메타테이블을 붙인 것 |
| [접근자 객체화](../spec/accessor-instances.md) | 전역 정적 대신 인스턴스로. 테스트 격리, 두 버전 동시 열기, 핫 리로드 |
| [출력 항목을 `Targets` 하나로](../spec/target-section-unification.md) | recipe의 출력 선언을 한 목록으로 모은 기록 |
| [전체 빌드의 소요 시간](../spec/conversion-time.md) | 139초가 27초가 된 기록. 문제는 일의 양이 아니라 같은 일의 반복이었습니다 |
| [빌드 캐시](../spec/build-cache.md) | 무엇이 전체 실행을 강제하는지, 히스토리 지문을 캐시 키로 쓰면 안 되는 이유 |
| [빌드 리포트](../spec/build-report.md) | 찾은 문제를 로그가 아니라 보고서로. 멈춘 실행이 본론입니다 |

### 아직 하지 않은 것

결론과 근거는 정리되었으나 구현하지 않은 설계입니다.

하지 않기로 한 것에는 그 이유를 적어 둡니다.
이유 없이 비어 있으면 다음 사람이 같은 판단을 다시 해야 하기 때문입니다.

| 문서 | 내용 |
| --- | --- |
| [상수 세트 제거](../spec/constant-set-removal.md) | 초안. 엔티티를 테이블과 enum 둘로 줄이고 상수셋은 한 행 테이블이 맡는 안 |
| [도구가 내는 메시지의 ID](../spec/message-ids.md) | 번역이 아니라 메시지에 정체성을 주는 작업 |
| [`--help`가 내놓는 것](../spec/cli-help.md) | 옵션 44개의 평면 목록을 도구 설명부터 시작하게 하는 설계 |
| [내보내기 형식 늘리기](../spec/export-formats.md) | 파일 형식을 2개에서 6개로. 형식마다 답이 갈리는 값 표현 |
| [HTML 문서의 규격](../spec/html-documentation.md) | 데이터를 사람이 확인하는 산출물의 페이지 구성, 항해, 통계 |
| [설치 채널](../spec/install-channels.md) | 세 OS에서 한 줄로 설치하고 한 줄로 갱신하기 |
| [Godot 지원](../spec/godot-support.md) | 하나의 작업이 아니라 셋이고, 비용이 20배 이상 차이납니다 |
| [Cocos Creator 지원](../spec/cocos-creator-support.md) | 새 타깃을 만들지 않습니다. 소비 경로는 이미 있는 `typescript`입니다 |
| [워크북 병합](../spec/workbook-merge.md) | `.xlsx`를 파일이 아니라 테이블, 행 키, 컬럼 3단계로 정합해 병합하기 |
| [여러 사람이 함께 쓸 때](../spec/multi-user-operations.md) | 운영 규약과 도구의 결손. 혼자 쓰는 구성에는 해당하지 않습니다 |

## 사례

`samples/`의 프로젝트들입니다.

검증이 끝나면 폐기될 수 있습니다. 그 이름이 코어에 들어가지 않는 이유가 이것입니다.

| 문서 | 내용 |
| --- | --- |
| [다른 규칙으로 쓰인 시트 읽기](../samples/rescue/doc/적용-기록.md) | `rescue` 레이아웃의 규칙과 실제 프로젝트에 적용한 기록 |
| [named-range 레이아웃 분석](../samples/named-range/doc/레이아웃-분석-20260808.md) | 라이브 서비스 중인 프로젝트의 레이아웃 조사 |
| [검증 이식 견적](../samples/named-range/doc/검증-이식-견적-20260811.md) | 기존 검증 141개를 옮길 때의 견적과 결과 |
| [시트 확인 요청](../samples/named-range/doc/시트-확인-요청-20260818.md) | 기획 전달용. 6,487건을 소비 코드까지 확인해 걸러낸 결과 |

## 저장소

| 문서 | 내용 |
| --- | --- |
| [아키텍처와 개발](architecture.md) | 내부 구조, 패키징 주의점, 개발과 테스트 방법 |
| [의존 패키지](dependencies.md) | 외부 패키지와 그 역할. 생성된 코드에 의존이 없는 이유 |
| [브랜드 자산](../brand/readme.md) | 로고와 아이콘 원본, 그리고 파생본을 만드는 방법 |
| [문서 사이트](../website/readme.md) | 이 문서들을 사이트로 만드는 방법. 빌드가 문서의 게이트입니다 |
| [앞으로 할 것](roadmap.md) | 하려는 것과, 하지 않기로 한 것과 그 이유 |
| [변경 내역](../CHANGELOG.md) | 릴리즈별 변경 |
