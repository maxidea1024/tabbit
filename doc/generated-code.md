# 시트가 코드가 되는 모습

시트에 적은 것과 거기서 생성된 코드를 나란히 놓았습니다. 언어는 탭에서 고릅니다.

<!-- 이 파일은 `doc/figures/showcase.py` 가 생성합니다. 손으로 고치지 마십시오 - 다음 실행이 덮어씁니다. -->

> [문서 목록으로](readme.md)

---

**여기 실린 것은 전부 생성물입니다.** 왼쪽의 시트 그림은
[`doc-showcase` 워크북](../test/fixtures/xlsx/doc-showcase)에서 셀을 그대로 읽어 그린 것이고,
오른쪽의 코드는 회귀 테스트가 매번 비교하는
[골든 트리](../test/fixtures/golden/doc-showcase)에서 오려 온 것입니다. 지어낸 예제가 아니고,
사람이 옮겨 적은 것도 아닙니다.

같은 워크북 하나가 모든 언어로 생성됩니다. 탭을 바꾸면 **같은 시트의 같은 자리**가 그 언어에서
어떻게 되는지 보입니다.

## 문서

| 무엇 | 어디 |
| --- | --- |
| [테이블 하나](generated-code/table.md) | 선언 셀 · 헤더 행 · 데이터 행, 그리고 컬럼 하나가 되는 멤버 하나 |
| [enum](generated-code/enum.md) | `:field` 줄 하나로 끝나는 선언과, 0을 두었을 때와 두지 않았을 때 |
| [상수셋](generated-code/const.md) | 행이 없는 값들. 데이터 파일에 흔적이 남지 않습니다 |
| [적을 수 있는 타입](generated-code/types.md) | 스칼라 9종이 언어마다 무엇이 되는지 |
| [한 칸에 여러 값 — 합성 값과 비트셋](generated-code/composite.md) | vec3f·color32가 레코드가 되는 것, 그리고 bitset이 정수가 되는 것 |
| [문자열의 역할 — text와 asset](generated-code/roles.md) | 번역을 위해 수집되는 문자열과, 파일이 있어야 하는 문자열 |
| [행을 찾는 방법](generated-code/keys.md) | 기본 인덱스 · 보조 인덱스 · 문자열 키 · 컬럼 둘이 키인 것 |
| [컬럼에 번호 달기](generated-code/wire.md) | `@N` — 컬럼을 위치가 아니라 번호로 가리키게 하는 것 |
| [다른 테이블 가리키기](generated-code/reference.md) | `foreign` 이 컬럼 하나를 멤버 둘로 만드는 것, 그리고 참조의 네 가지 꼴 |
| [값이 여러 개일 때](generated-code/array.md) | 셀 안에서 나누는 것과 컬럼으로 나누는 것, 그리고 코드에서 같아지는 것 |
| [컬럼 묶음과 빈 칸](generated-code/record.md) | 점 앞이 같은 컬럼이 레코드가 되는 것, 두 단계 중첩, 그리고 비워도 되는 칸 |
| [행으로 쌓는 배열](generated-code/multirow.md) | 레코드 하나가 여러 행에 걸치는 것 — 원소 수가 행마다 다를 때 |
| [서버와 클라이언트에 다른 것 주기](generated-code/sides.md) | 컬럼 하나만 빼는 것과 테이블 전체를 빼는 것 |
| [같은 필드를 여러 벌 적기](generated-code/variant.md) | 지역별 가격처럼 컬럼 하나만 갈리는 데이터. 빌드가 하나를 고릅니다 |
| [행마다 모양이 다른 묶음](generated-code/polymorphism.md) | `$type` 이 그 행의 모양을 고르는 것. 선언은 `.tbs` 에 |

## 여기 없는 것

시트에 적을 수 있는 것 전부는 [시트 작성](sheets.md)에 있습니다. 이 문서가 담는 것은 **생성된
코드가 함께 보여야 뜻이 통하는 것들**입니다.

`set`과 `map`은 아직 없습니다 — 모델과 파일에는 도달하지만 코드 타깃이 아직 이름을 대고
거부하므로, 나란히 놓을 코드가 없습니다 ([`set`과 `map`](sheets/containers.md)).

## 이 문서를 다시 만드는 방법

```bash
python doc/figures/grid_dump.py     # 워크북 -> 격자
python doc/figures/showcase.py      # 격자와 골든 -> 이 문서
```

생성기나 템플릿을 고쳤으면 **골든을 먼저 다시 기록하고** 이것을 돌립니다. 순서가 반대이면 옛
코드가 문서에 남습니다.

## 다음

| 무엇이 궁금한가 | 어디 |
| --- | --- |
| 시트에 적을 수 있는 것 전부 | [시트 작성](sheets.md) |
| 생성된 코드를 프로젝트에 적용하는 방법 | [언어별 가이드](languages/readme.md) |
| 세 가지 엔티티가 각각 무엇이 되는가 | [시트에 무엇을 적을 수 있나](concepts.md) |
