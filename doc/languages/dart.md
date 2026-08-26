# Dart

> [「언어별 가이드」로 돌아가기](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/
  <AccessorName>.dart              라이브러리 — import와 part 선언, 접근자
  tables/<table>_table.dart        테이블당 하나 (part)
  enums/<enum>.dart                enum당 하나 (part)
  constants/<set>.dart             상수 세트당 하나 (part)
  tabbit/tcb_reader.dart 바이너리 리더 (함께 생성됩니다)
  tabbit/updater.dart            데이터 갱신 (WriteUpdater를 켰을 때만)
```

조각들은 `library`가 아니라 **`part`입니다**. part는 라이브러리의 import를 공유하므로 파일마다
import를 따로 계산할 필요가 없고, 소비자는 파일 하나만 import하면 모델 전체를 얻습니다.

## 필요한 것

|항목|값|
|--|--|
|Dart|3.6 이상 — null safety 필요|
|패키지|**없음**|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "dart",
    "Path": "lib/gamedata",
    "AccessorName": "Tables",      // 접근자 클래스. 파일은 tables.dart
    "BinaryTableFileExtension": ".tcb",
    "WriteUpdater": false,           // CDN에서 데이터를 갱신할 거라면 true
    "Sweep": true,
    "TargetSide": "c"
  }
]
```

## 쓰는 법

```dart
import 'lib/gamedata/game_data.dart';

final tables = Tables();
tables.readAll('./data');

final sword = tables.item.findByIndex(1);
if (sword != null) {
  // 참조는 로드 후 실제 레코드로 연결됩니다.
  print('${sword.name} / ${sword.itemCategoryByCategoryId?.name}');
}

for (final row in tables.item.records) { /* ... */ }
```

확장자는 선택적 위치 인자입니다.

```dart
tables.readAll('./data', '.bytes');
```

## 데이터만 갱신하기 (`WriteUpdater`)

recipe에 `"WriteUpdater": true`를 적으면 `tabbit/updater.dart`가 함께 나옵니다.

CDN이나 버킷에 올려둔 데이터를 받아 로컬 사본을 최신으로 유지하는 코드입니다.
배포를 다시 하지 않고 데이터만 패치하기 위한 것입니다.

기본값이 `false`인 이유는 네트워크를 쓰는 유일한 생성물이기 때문입니다.

**패키지는 여전히 필요 없습니다.** 전송은 `dart:io`, 해시 검증은 그 파일 안에 직접 쓴 MD5입니다
— Dart SDK에 MD5가 없고, 생성물이 패키지를 요구하지 않는다는 약속이 `package:crypto` 의존보다
지킬 가치가 있기 때문입니다.

```dart
import 'gamedata/tabbit/updater.dart' as tabbit;

final result = await tabbit.update(
    'https://cdn.example.com/data', './data',
    tabbit.UpdateOptions(log: print));

if (result.succeeded) {
  tables.readAll(result.localPath);
} else {
  // 이전 데이터가 그대로 있습니다. 그것으로 계속해도 됩니다.
  print(result.error);
}
```

예외를 던지지 않습니다.

네트워크, 디스크, 손상된 파일은 모두 호출한 쪽이 다뤄야 할 상황이지 결함이 아니기 때문입니다.

실패하면 결과의 `error`에 이유가 들어 있고, 디스크의 이전 데이터는 손대지 않은 상태입니다.

받은 파일은 전부 매니페스트의 MD5와 대조하고, `.staging`을 거쳐 마지막에 한 번에 옮깁니다.

## 주의사항

**`int64`·`datetime`·`timespan`은 `BigInt`입니다.** Dart의 `int`는 VM에서 64비트지만 **웹에서는
double이라 53비트만 담습니다** — 그 범위를 넘는 값은 실패하는 게 아니라 **바뀐 채로**
돌아옵니다. TypeScript가 `bigint`를 쓰는 것과 같은 이유입니다.

**`dart:io`를 씁니다.** 파일에서 읽는 경로가 `dart:io`에 의존하므로 웹에서는 그대로 쓸 수
없습니다. 웹으로 배포한다면 바이트를 직접 넘기는 경로가 필요하고, 지금은 없습니다.

**타입 이름과 부딪히는 필드 이름은 바뀝니다.** `Int`라는 필드는 `int`가 되어 클래스 안에서 타입
이름을 가립니다 — `int int = 0;`은 컴파일되지 않고 그 뒤 선언도 전부 깨집니다. Dart 키워드
목록으로는 잡히지 않는데, `int`는 키워드가 아니라 타입을 가리키는 평범한 식별자이기 때문입니다.
회귀 스위트가 실제로 컴파일해서 확인합니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`Undefined name 'Tables'`|라이브러리 파일(`<AccessorName>.dart`)을 import했는지 확인하세요. part 파일은 직접 import할 수 없습니다|
|`The part-of directive must be the only directive in a part file`|생성물에서 나면 버그입니다|
|웹 빌드에서 `dart:io` 오류|파일 읽기 경로가 `dart:io`를 씁니다. 웹은 아직 지원하지 않습니다|
|큰 정수가 이상함|`BigInt`를 `int`로 변환하면서 잘렸을 수 있습니다|
|참조가 `null`|`readAll` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
