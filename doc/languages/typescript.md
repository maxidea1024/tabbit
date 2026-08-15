# TypeScript

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/
  index.ts                       전부 재수출
  tables.ts                      접근자 — 테이블 게터, readAll, 참조 연결
  tables/<Table>.ts              테이블당 하나
  enums/<Enum>.ts                enum당 하나
  constants/<Set>.ts             상수 세트당 하나
  tabbit/tcb_reader.ts 바이너리 리더 (함께 생성됩니다)
```

## 필요한 것

|항목|값|
|--|--|
|TypeScript|4.5 이상|
|컴파일 타겟|`ES2020` 이상 — 테이블 리더가 `BigInt`를 씁니다|
|런타임|Node 또는 브라우저. 테이블 리더 자체는 `Uint8Array` 위에서만 동작하고 외부 의존성이 없습니다|

파일에서 읽는 편의 함수만 Node가 필요합니다. 브라우저에서는 바이트를 직접 넘기세요.

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "typescript",
    "Path": "src/generated",
    "AccessorName": "Tables",
    "UseStringEnum": false,     // true면 enum이 문자열 값을 갖습니다
    "Sweep": true,
    "TargetSide": "c"
  }
]
```

## 쓰는 법

**두 읽기 경로가 모두 생성됩니다.** 배포 상황에 따라 고르고, 두 경로는 **같은 값**을 냅니다 — 회귀 스위트가 같은 테이블을 양쪽으로 읽어 필드 단위로 비교합니다.

```typescript
import { Tables } from './generated'

const tables = new Tables()

// JSON에서 — 사람이 들여다보거나 텍스트로 서빙할 때
tables.readAllSync('./data/json')
await tables.readAll('./data/json')

const sword = tables.item.findByIndex(1)
for (const row of tables.item.records) { /* ... */ }
```

```typescript
// 바이너리에서 — 크기와 파싱 시간이 중요할 때
tables.readAllBinarySync('./data/binary')

// 테이블 하나만 (참조는 연결되지 않습니다)
tables.item.readBinarySync('./data/binary/Item.tcb')

// 파일 시스템이 없는 환경에서는 바이트를 직접
const bytes = new Uint8Array(await (await fetch(url)).arrayBuffer())
tables.item.readBinaryFrom(bytes)
```

두 번째 인자로 확장자를 넘길 수 있습니다 (`readAll`은 `.json`, `readAllBinarySync`는 recipe의 `BinaryTableFileExtension`).

## 주의사항

**`bigint`입니다.** `bigint`·`datetime`·`timespan`은 `number`가 아니라 `BigInt`로 나옵니다. JavaScript의 `number`는 double이라 2^53을 넘는 정수를 **실패하지 않고 바꿔서** 담기 때문입니다. `JSON.stringify`가 `BigInt`를 거부하므로 직렬화할 때는 문자열로 바꾸세요.

**바이너리 리더는 출력에 자동 포함됩니다.** 생성된 테이블이 상대 경로로 import하는데 TypeScript에는 include 경로 개념이 없어 소비자가 다른 곳을 가리킬 방법이 없습니다. 소스는 `lib/ts`와 공유되는 하나뿐이라 어긋날 수 없습니다.

**테이블 하나만 읽으면 참조가 비어 있습니다.** 참조 연결은 접근자가 전부 읽은 뒤에 하므로, 참조가 필요하면 `readAll` / `readAllSync` / `readAllBinarySync`를 쓰세요. `readBinarySync`로 한 테이블만 읽으면 키(`_<필드>_<테이블>_index`)만 채워집니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`BigInt literals are not available when targeting lower than ES2020`|`tsconfig.json`의 `target`을 `ES2020` 이상으로|
|`Do not know how to serialize a BigInt`|`JSON.stringify` 전에 `String(value)`로 바꾸세요|
|`fs` 모듈을 찾을 수 없음 (브라우저)|`readAllSync`·`readBinarySync`는 Node 전용입니다. `readBinaryFrom(bytes)`를 쓰세요|
|참조가 `undefined`|`readAll` 대신 테이블 하나만 읽었습니다|
|JSON과 바이너리 값이 다름|버그입니다. 회귀 스위트가 두 경로를 대조하므로, 재현되면 코퍼스에 넣을 값입니다|
