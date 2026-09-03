# TypeScript

> [「언어별 가이드」로 돌아가기](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/
  index.ts                       전부 재수출
  tables.ts                      접근자 — 테이블 게터, readAll, 참조 연결
  tables/<Table>.ts              테이블당 하나
  enums/<Enum>.ts                enum당 하나
  constants/<Set>.ts             상수 세트당 하나
  tabbit/tcb-reader.ts           바이너리 리더 (함께 생성됩니다)
```

## 필요한 것

|항목|값|
|--|--|
|TypeScript|4.5 이상|
|컴파일 타겟|`ES2020` 이상 — 테이블 리더가 `BigInt`를 씁니다|
|런타임|Node 또는 브라우저. 테이블 리더 자체는 `Uint8Array` 위에서만 동작하고 외부 의존성이 없습니다|

생성 코드는 Node 모듈을 임포트하지 않습니다. 파일을 여는 것은 호출자입니다 — [쓰는 법](#쓰는-법).

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

**생성 코드는 파일을 열지 않습니다.** 접근자의 읽기 메서드는 「파일 이름을 받아 내용을 돌려주는
함수」를 받고, 그 함수가 어디를 보는지는 호출자가 정합니다 — Node라면 디스크, 브라우저라면
`fetch`, 엔진이라면 그 엔진의 자산 로더입니다. 그래서 생성 코드에 `fs`·`path` 임포트가 없고,
같은 산출물이 세 곳에서 그대로 로드됩니다.

**두 읽기 경로가 모두 생성됩니다.** 배포 상황에 따라 고르고, 두 경로는 **같은 값**을 냅니다 —
회귀 스위트가 같은 테이블을 양쪽으로 읽어 필드 단위로 비교합니다.

```typescript
import * as fs from 'fs'
import * as path from 'path'
import { Tables } from './generated'

const tables = new Tables()

// Node — 바이너리에서. 크기와 파싱 시간이 중요할 때
tables.readAllBinarySync(name => new Uint8Array(fs.readFileSync(path.join('./data/binary', name))))

// Node — JSON에서. 사람이 들여다보거나 텍스트로 서빙할 때
tables.readAllSync(name => fs.readFileSync(path.join('./data/json', name), 'utf8'))

const sword = tables.item.findByIndex(1)
for (const row of tables.item.records) { /* ... */ }
```

```typescript
// 브라우저 — 비동기 짝. fetch가 비동기이기 때문입니다
await tables.readAllBinary(async name => {
  const response = await fetch(`/data/${name}`)
  return new Uint8Array(await response.arrayBuffer())
})
await tables.readAll(async name => (await fetch(`/data/${name}`)).text())

// 테이블 하나만 (참조는 연결되지 않습니다)
tables.item.readBinaryFrom(bytes)
tables.item.readJsonFrom(text)
```

로더가 받는 이름에는 확장자가 붙어 있습니다 (`Item.tcb`). 두 번째 인자로 확장자를 바꿀 수
있습니다 (`readAll`·`readAllSync`는 `.json`, `readAllBinary`·`readAllBinarySync`는 recipe의
`BinaryTableFileExtension`).

**봉인한 파일도 같은 호출입니다.** 로더는 파일의 바이트를 그대로 돌려주고, 봉인을 여는 것은
`readBinaryFrom`입니다 — 시작 시점에 `Tables.encryptionKey`·`Tables.macKey`를 두면 그 키로 엽니다.

## 주의사항

**`bigint`입니다.** `bigint`·`datetime`·`timespan`은 `number`가 아니라 `BigInt`로 나옵니다.
JavaScript의 `number`는 double이라 2^53을 넘는 정수를 **실패하지 않고 바꿔서** 담기 때문입니다.
`JSON.stringify`가 `BigInt`를 거부하므로 직렬화할 때는 문자열로 바꾸세요.

**바이너리 리더는 출력에 자동 포함됩니다.** 생성된 테이블이 상대 경로로 import하는데
TypeScript에는 include 경로 개념이 없어 소비자가 다른 곳을 가리킬 방법이 없습니다. 소스는
`lib/ts`와 공유되는 하나뿐이라 어긋날 수 없습니다.

**테이블 하나만 읽으면 참조가 비어 있습니다.** 참조 연결은 접근자가 전부 읽은 뒤에 하므로,
참조가 필요하면 `readAll` / `readAllSync` / `readAllBinary` / `readAllBinarySync`를 쓰세요.
`readBinaryFrom`으로 한 테이블만 읽으면 키(`_<필드>_<테이블>_index`)만 채워집니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`BigInt literals are not available when targeting lower than ES2020`|`tsconfig.json`의 `target`을 `ES2020` 이상으로|
|`Do not know how to serialize a BigInt`|`JSON.stringify` 전에 `String(value)`로 바꾸세요|
|로더가 받은 이름으로 파일을 찾지 못함|이름에 확장자가 붙어 옵니다(`Item.tcb`). 디렉터리나 URL과 이어 붙이는 것은 로더의 일입니다|
|`the file is encrypted and no key ... was given`|봉인한 데이터입니다. 첫 읽기 전에 `Tables.encryptionKey`를 두세요|
|참조가 `undefined`|`readAll` 대신 테이블 하나만 읽었습니다|
|JSON과 바이너리 값이 다름|버그입니다. 회귀 스위트가 두 경로를 대조하므로, 재현되면 코퍼스에 넣을 값입니다|
