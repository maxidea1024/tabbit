# Java

> [「언어별 가이드」로 돌아가기](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/<PackageName as folders>/
  <AccessorName>.java     접근자
  <Table>Record.java      테이블당 둘 — 레코드와
  <Table>Table.java                    테이블
  <Enum>.java             enum당 하나
  <Set>.java              상수 세트당 하나
<Path>/tabbit/
  TcbReader.java   바이너리 리더 (함께 생성됩니다)
  TabbitUpdater.java    데이터 갱신 (WriteUpdater를 켰을 때만)
```

Java는 public 타입이 자기 이름과 같은 파일에 혼자 있어야 하므로 **테이블당 파일이 둘**입니다.
레코드를 테이블 안에 중첩해 `ItemTable.Record`로 부르는 대안도 있었지만, 이름이 나빠지는 값으로
파일 하나를 아끼는 것은 남는 장사가 아닙니다.

## 필요한 것

|항목|값|
|--|--|
|Java|21로 검증. 테이블 리더에 특별한 문법은 없지만 그 아래는 확인하지 않았습니다|
|외부 라이브러리|**없음**|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "java",
    "Path": "src/main/java",
    "PackageName": "com.mygame.data",
    "AccessorName": "GameData",
    "BinaryTableFileExtension": ".tcb",
    "WriteUpdater": false,          // CDN에서 데이터를 갱신할 거라면 true
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

`Path`는 소스 루트입니다. 패키지 이름이 그 아래 폴더로 펼쳐집니다.

## 쓰는 법

```java
import com.mygame.data.GameData;
import com.mygame.data.ItemRecord;

GameData data = new GameData();
data.readAll("./data");

ItemRecord sword = data.item.findByIndex(1);
if (sword != null) {
    // 참조는 로드 후 실제 레코드로 연결됩니다.
    System.out.println(sword.name + " / " + sword.itemCategoryByCategoryId.name);
}

for (ItemRecord row : data.item.records()) { /* ... */ }
```

Java에는 기본 인자가 없으므로 확장자는 오버로드입니다.

```java
data.readAll("./data", ".bytes");
```

## 데이터만 갱신하기 (`WriteUpdater`)

recipe에 `"WriteUpdater": true`를 적으면 `tabbit/TabbitUpdater.java`가 함께 나옵니다.

CDN이나 버킷에 올려둔 데이터를 받아 로컬 사본을 최신으로 유지하는 코드입니다.
배포를 다시 하지 않고 데이터만 패치하기 위한 것입니다.

기본값이 `false`인 이유는 네트워크를 쓰는 유일한 생성물이기 때문입니다.

**의존성은 여전히 없습니다.** 전송은 `java.net.http`, 해시는 `MessageDigest`, 매니페스트 JSON은
그 파일 안의 작은 파서입니다 — JDK에 JSON이 없기 때문입니다.

```java
import tabbit.TabbitUpdater;

var options = new TabbitUpdater.Options();
options.log = System.out::println;

var result = TabbitUpdater.update("https://cdn.example.com/data",
                                    Path.of("./data"), options);

if (result.succeeded) {
    data.readAll(result.localPath.toString());
} else {
    // 이전 데이터가 그대로 있습니다. 그것으로 계속해도 됩니다.
    System.err.println(result.error);
}
```

예외를 던지지 않습니다.

네트워크, 디스크, 손상된 파일은 모두 호출한 쪽이 다뤄야 할 상황이지 결함이 아니기 때문입니다.

실패하면 결과의 `error`에 이유가 들어 있고, 디스크의 이전 데이터는 손대지 않은 상태입니다.

받은 파일은 전부 매니페스트의 MD5와 대조하고, `.staging`을 거쳐 마지막에 한 번에 옮깁니다.

## 주의사항

**전부 한 패키지에 평평하게 놓입니다.** 그래서 생성된 타입끼리 import가 하나도 없습니다.
`tables`·`enums` 하위 패키지로 나누면 서로를 import해야 합니다.

**`datetime`과 `timespan`은 `long`입니다.** .NET 틱이 그대로 들어옵니다. `Instant`나
`Duration`으로 바꾸고 싶으면 직접 변환하세요.

**`uuid`는 `TcbReader.Uuid`입니다.** `java.util.UUID`가 아닙니다 — 바이트 순서가 .NET의 것이라
그대로 담습니다.

**멤버 이름은 camelCase입니다.** Java 키워드는 전부 소문자라 대부분 부딪히지 않지만, 부딪히는
경우는 이스케이프됩니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`class X is public, should be declared in a file named X.java`|생성물에서 나면 버그입니다. 손으로 파일을 옮겼는지 확인하세요|
|`package com.mygame.data does not exist`|`Path`가 소스 루트인지 확인하세요. 패키지 폴더는 그 아래에 생성됩니다|
|참조가 `null`|`readAll` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
|`Uuid`를 `java.util.UUID`에 대입할 수 없음|다른 타입입니다. 바이트로 꺼내 변환하세요|
