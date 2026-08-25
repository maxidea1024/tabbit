# Kotlin

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/<PackageName as folders>/
  <AccessorName>.kt          접근자 (object)
  tables/<Table>Table.kt     테이블당 하나
  enums/<Enum>.kt            enum당 하나
  constants/<Set>.kt         상수 세트당 하나
<Path>/tabbit/
  TcbReader.kt        바이너리 리더 (함께 생성됩니다)
  TabbitUpdater.kt         데이터 갱신 (WriteUpdater를 켰을 때만)
```

## 필요한 것

|항목|값|
|--|--|
|Kotlin|2.1로 검증|
|외부 라이브러리|**없음**|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "kotlin",
    "Path": "src/main/kotlin",
    "PackageName": "com.mygame.data",
    "AccessorName": "GameData",
    "BinaryTableFileExtension": ".tcb",
    "WriteUpdater": false,          // CDN에서 데이터를 갱신할 거라면 true
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

## 쓰는 법

**접근자는 `object`입니다.** 인스턴스를 만들지 않습니다.

```kotlin
import com.mygame.data.GameData

GameData.readAll("./data")

val sword = GameData.item.findByIndex(1)
if (sword != null) {
    // 참조는 로드 후 실제 레코드로 연결됩니다.
    println("${sword.name} / ${sword.itemCategoryByCategoryId?.name}")
}

for (row in GameData.item.records) { /* ... */ }
```

확장자는 기본 인자입니다.

```kotlin
GameData.readAll("./data", ".bytes")
```

## 데이터만 갱신하기 (`WriteUpdater`)

recipe에 `"WriteUpdater": true`를 적으면 `tabbit/TabbitUpdater.kt`가 함께 나옵니다.

CDN이나 버킷에 올려둔 데이터를 받아 로컬 사본을 최신으로 유지하는 코드입니다.
배포를 다시 하지 않고 데이터만 패치하기 위한 것입니다.

기본값이 `false`인 이유는 네트워크를 쓰는 유일한 생성물이기 때문입니다.

**의존성은 여전히 없습니다.** 전송은 `java.net.http`, 해시는 `MessageDigest`, 매니페스트 JSON은 그 파일 안의 작은 파서입니다. Java 업데이터를 부르는 게 아니라 **Kotlin 파일**이므로, Kotlin 프로젝트가 Java를 함께 컴파일할 필요가 없습니다.

```kotlin
import tabbit.TabbitUpdater

val options = TabbitUpdater.Options()
options.log = ::println

val result = TabbitUpdater.update("https://cdn.example.com/data", "./data", options)

if (result.succeeded) {
    GameData.readAll(result.localPath.toString())
} else {
    // 이전 데이터가 그대로 있습니다. 그것으로 계속해도 됩니다.
    System.err.println(result.error)
}
```

예외를 던지지 않습니다.

네트워크, 디스크, 손상된 파일은 모두 호출한 쪽이 다뤄야 할 상황이지 결함이 아니기 때문입니다.

실패하면 결과의 `error`에 이유가 들어 있고, 디스크의 이전 데이터는 손대지 않은 상태입니다.

받은 파일은 전부 매니페스트의 MD5와 대조하고, `.staging`을 거쳐 마지막에 한 번에 옮깁니다.

## 주의사항

**참조는 nullable입니다.** 시트가 `0`을 넣으면 "참조 없음"이고, 그때 값은 `null`입니다.

**미사용 import는 경고입니다.** Kotlin은 오류가 아니라 경고라, 생성물이 파일마다 같은 목록을 가져도 빌드가 깨지지 않습니다. Go가 같은 자리에서 오류를 내는 것과 대비됩니다.

**키워드는 백틱으로 이스케이프합니다.** 이름을 바꾸지 않고 `` `class` ``로 감싸므로, 생성된 멤버 이름이 시트의 이름과 그대로 같습니다.

**`datetime`과 `timespan`은 `Long`입니다.** .NET 틱이 그대로 들어옵니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`Unresolved reference: GameData`|`PackageName`과 import가 맞는지, `Path`가 소스 루트인지 확인하세요|
|`Expression 'item' of type 'ItemTable' cannot be invoked`|`object`라서 `GameData.item`이지 `GameData().item`이 아닙니다|
|참조가 `null`|`readAll` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
