# Swift

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/
  <AccessorName>.swift       접근자 (class)
  tables/<Table>Table.swift  테이블당 하나
  enums/<Enum>.swift         enum당 하나
  constants/<Set>.swift      상수 세트당 하나
  Package.swift              WriteManifest를 켰을 때만
<Path>/tabbit/
  TcbReader.swift            바이너리 리더 (함께 생성됩니다)
  Updater.swift              데이터 갱신 (WriteUpdater를 켰을 때만)
```

**`Namespace`나 `PackageName`이 없습니다.** Swift에서 모듈을 정하는 것은 빌드 시스템이 가리키는
디렉터리이고 파일이 선언하는 것이 아니므로, 받아 두면 아무 데도 쓰이지 않는 옵션이 됩니다.

배치가 평평한 것은 **이미 빌드가 있는 프로젝트에 넣는 경우가 더 흔하기** 때문입니다.
`WriteManifest`를 켜면 파일을 `Sources/`로 옮기는 대신 그 배치를 그대로 가리키는 매니페스트가
하나 더 나옵니다.

## 필요한 것

|항목|값|
|--|--|
|Swift|6.1·6.3으로 검증. 5.9 이상|
|언어 모드|**Swift 5와 6 둘 다.** 게이트가 두 모드에서 경고를 오류로 두고 확인합니다|
|플랫폼 하한|iOS 13 · macOS 10.15 (CryptoKit이 있는 선). 리눅스·윈도우는 툴체인만 있으면 됩니다|
|외부 라이브러리|**조건부로 하나** — [swift-crypto](#mac-검증과-swift-crypto). 파일에 MAC을 쓰지 않거나 애플 플랫폼이면 **없습니다**|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "swift",
    "Path": "Sources/GameData",
    "AccessorName": "GameData",
    "BinaryTableFileExtension": ".tcb",
    "WriteUpdater": false,          // CDN에서 데이터를 갱신할 거라면 true
    "WriteManifest": false,         // SwiftPM 패키지로 낼 거라면 true
    "ModuleName": "GameData",       // WriteManifest일 때 타깃 이름
    "SwiftCryptoVersion": "3.0.0",  // 매니페스트가 선언할 하한
    "Sweep": true,
    "TargetSide": "c"
  }
]
```

## 쓰는 법

**접근자는 인스턴스입니다.** 만들어서 들고 있습니다.

```swift
let data = GameData()
try data.readAll("./data")

if let sword = data.item.findByIndex(1) {
    // 참조는 로드 후 실제 레코드로 연결됩니다.
    print("\(sword.name) / \(sword.itemCategoryByCategoryId?.name ?? "-")")
}

for row in data.item.records { /* ... */ }
```

전역 정적이 아닌 이유는 두 가지입니다.

Swift 6는 가변 전역 상태를 오류로 보므로 `static var` 테이블은 컴파일되지 않습니다.

그리고 인스턴스는 그 자체로 얻는 것이 있습니다. 테스트 격리, 두 버전 동시 로드, 한쪽을 읽는 동안
다른 쪽으로 바꾸는 핫 리로드입니다([설계](../../spec/accessor-instances.md)).

싱글턴처럼 쓰고 싶으면 `let` 하나를 어디든 두면 됩니다.

확장자는 기본 인자입니다.

```swift
try data.readAll("./data", fileExtension: ".bytes")
```

**읽기는 `throws`입니다.** 잘린 파일에서 값을 지어내지 않는 것이 이 형식의 계약이고, Swift에서
그 자리는 `throws`입니다.

```swift
do {
    try data.readAll("./data")
} catch let error as TcbError {
    // 파일이 잘렸거나, 스키마가 어긋났거나, MAC이 맞지 않습니다.
    print(error.message)
}
```

## MAC 검증과 swift-crypto

**켜지 않았으면 읽지 않아도 되는 절입니다.** recipe에서 `MacKeyFile`·`MacKeyVariable`을 쓰지
않는다면 이 타깃은 외부 패키지가 필요 없습니다.

### 왜 패키지를 쓰는가

MAC은 HMAC-SHA-256이고, 그것을 직접 구현하면 **CPU의 SHA 확장 명령에 닿지 못합니다.**
[측정](../../spec/tcb-mac-and-signature.md#검증-비용--실측)은 이렇습니다.

|구현|처리량|
|--|--|
|플랫폼|약 2,250 MB/s|
|직접 구현|약 345 MB/s|

**6배 차이는 구현 품질이 아닙니다.** 검증은 파일 전체를 한 번 더 훑으므로 그 차이가 로드
시간에 그대로 실립니다. 이 저장소의 기본은 의존성을 억제하는 것이지만, 이 자리는 유불리가
분명한 쪽입니다([방침](../dependencies.md#생성된-코드의-의존)).

### 안 넣으면 무엇이 안 되는가

**MAC 검증 하나뿐입니다.** 리더는 그대로 컴파일되고, 평문 파일도 암호화된 파일도 그대로
읽힙니다. MAC이 붙은 파일을 검증하려는 순간에만 「무엇을 넣어야 하는지 말하는 오류」가 납니다.

|구성|HMAC|패키지|MAC 검증|
|--|--|--|--|
|iOS 13+ · macOS 10.15+|CryptoKit|**0개**|됩니다|
|리눅스·윈도우 + swift-crypto|`Crypto`|1개|됩니다|
|리눅스·윈도우, 패키지 없음|없음|**0개**|오류로 알려줍니다|

### 경로 ① iOS·macOS — 할 일이 없습니다

CryptoKit은 **OS에 들어 있는 프레임워크**이지 패키지가 아닙니다. 패키지 추가 화면을 열 필요가
없고, `import`도 리더가 알아서 합니다. 애플 플랫폼만 대상이라면 이 절의 나머지는 넘기셔도 됩니다.

### 경로 ② Xcode 프로젝트

리눅스나 윈도우도 대상일 때만 필요합니다.

1. Xcode에서 프로젝트를 엽니다.
2. 메뉴 **File ▸ Add Package Dependencies…**
3. 오른쪽 위 검색창에 이 주소를 붙입니다 — `https://github.com/apple/swift-crypto.git`
4. **Dependency Rule**을 `Up to Next Major Version`으로 두고 `3.0.0`을 확인합니다.
5. **Add Package**를 누릅니다.
6. 이어 나오는 목록에서 `Crypto` 옆의 **Add to Target**이 데이터를 읽는 타깃인지 확인하고
   **Add Package**를 누릅니다.

**6번이 가장 자주 빠지는 단계입니다.** 여기서 타깃을 고르지 않으면 패키지는 받아졌는데
`No such module 'Crypto'`가 납니다.

### 경로 ③ `Package.swift`를 쓰는 프로젝트

**두 곳에 적습니다.** `dependencies`에 패키지를, `targets`의 그 타깃에 프로덕트를.

```swift
// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "MyGame",
    dependencies: [
        // ① 패키지
        .package(url: "https://github.com/apple/swift-crypto.git", from: "3.0.0")
    ],
    targets: [
        .target(
            name: "GameData",
            // ② 그 타깃이 쓰는 프로덕트
            dependencies: [.product(name: "Crypto", package: "swift-crypto")])
    ]
)
```

`WriteManifest`를 켜면 **이 매니페스트가 생성됩니다** — 그때는 손으로 적을 것이 없습니다.

### 경로 ④ 소스만 복사해서 쓰는 경우

`tabbit/TcbReader.swift`를 프로젝트에 그냥 넣어도 됩니다. 그때 포기하는 것은 위 표의 세 번째
줄이고, 되찾는 방법은 그 타깃에 swift-crypto를 붙이는 것뿐입니다. MAC을 쓰지 않는 프로젝트는
포기하는 것이 없습니다.

### 툴체인 설치 — 리눅스·윈도우

애플 플랫폼은 Xcode가 툴체인입니다. 그 밖에서는 [swift.org/install](https://www.swift.org/install/)에서
받습니다.

|플랫폼|절차|확인|
|--|--|--|
|리눅스|배포판별 tar 또는 apt 저장소|`swift --version`|
|윈도우|`winget install Swift.Toolchain`|`swift --version`|

**윈도우는 먼저 두 가지가 있어야 합니다.**

1. **Visual Studio의 C++ 도구** — Swift가 MSVC의 링커와 헤더를 씁니다.
2. **Windows SDK.** 여기서 한 번 걸립니다 — Swift의 `ucrt.modulemap`이 `stdnoreturn.h`를
   참조하므로, 그 헤더가 없는 부분 설치 SDK에서는 **`import Foundation`이 빌드되지 않습니다.**
   증상은 Swift와 무관해 보이는 다음 오류입니다.

   ```
   ucrt.modulemap:130:22: error: header 'stdnoreturn.h' not found
   <unknown>:0: error: could not build C module 'SwiftOverlayShims'
   ```

   Visual Studio Installer의 **개별 구성 요소 ▸ SDK, 라이브러리 및 프레임워크**에서
   `Windows 11 SDK (10.0.22621.0)`을 설치하면 해결됩니다. 확인은 이렇게 합니다.

   ```
   dir "C:\Program Files (x86)\Windows Kits\10\Include\10.0.22621.0\ucrt\stdnoreturn.h"
   ```

설치 직후에는 **셸을 다시 여세요.** 인스톨러가 `PATH`와 `SDKROOT`를 사용자 환경에 넣는데, 먼저
열려 있던 셸은 그것을 물려받지 못합니다.

### 막혔을 때

|증상|원인과 조치|
|--|--|
|`No such module 'Crypto'`|`dependencies`에만 적고 `targets`에 안 적었습니다. 경로 ③의 ②를 확인하세요|
|MAC 파일에서 「swift-crypto를 추가하라」는 오류|그 빌드에 CryptoKit도 swift-crypto도 없습니다. 검증을 미루려면 `verifyMac = false`|
|오프라인·사내 미러|`.package(url:)`을 사내 미러 주소로 바꾸거나, 체크아웃을 두고 `.package(path:)`로 가리킵니다|
|버전을 고정하고 싶다|`Package.resolved`를 커밋하면 그 해석이 고정됩니다|

## 데이터만 갱신하기 (`WriteUpdater`)

recipe에 `"WriteUpdater": true`를 적으면 `tabbit/Updater.swift`가 함께 나옵니다.

CDN이나 버킷에 올려둔 데이터를 받아 로컬 사본을 최신으로 유지하는 코드입니다.
배포를 다시 하지 않고 데이터만 패치하기 위한 것입니다.

기본값이 `false`인 이유는 네트워크를 쓰는 유일한 생성물이기 때문입니다.

**여기에는 의존성이 없습니다.** 전송은 Foundation의 `URLSession`(리눅스·윈도우에서는
`FoundationNetworking`을 조건부로 import합니다), 매니페스트는 `JSONSerialization`, MD5는 그 파일
안에 직접 구현되어 있습니다 — 해시가 하는 일은 짧게 도착한 전송을 잡는 것이지 공격자를 막는
것이 아니므로, 암호 패키지를 요구할 이유가 없습니다.

```swift
var options = TabbitUpdater.Options()
options.log = { print($0) }

let result = TabbitUpdater.update(
    "https://cdn.example.com/data", cacheDirectory: "./data", options: options)

if result.succeeded {
    try data.readAll(result.localPath)
} else {
    // 이전 데이터가 그대로 있습니다. 그것으로 계속해도 됩니다.
    FileHandle.standardError.write(Data((result.error ?? "").utf8))
}
```

throw하지 않습니다. 네트워크·디스크·손상된 파일은 모두 호출한 쪽이 다뤄야 할 상황이지 결함이
아니기 때문입니다. 받은 파일은 전부 매니페스트의 MD5와 대조하고, `.staging`을 거쳐 마지막에 한
번에 옮깁니다.

## 주의사항

**정수는 폭을 적습니다.** `int`는 `Int32`, `bigint`는 `Int64`입니다. `Int`가 아닌 것은 데이터
충실성 때문이고, 참조 키가 폭을 잃으면 [한 번 났던 결함](../../spec/reference-key-types.md)이
다시 납니다. 호출하는 쪽에서 `Int(row.hp)`를 쓰게 되는 비용은 그 대가입니다.

**옵셔널 필드는 `T?`가 아닙니다.** 값 프로퍼티는 언제나 초기화되어 있고, 없음은 `hasHp` 같은
이웃 프로퍼티가 답합니다. Swift 관용과 어긋나는 유일한 자리이고, 근거는
[옵셔널 필드](../../spec/optional-fields.md)에 있습니다 — 값을 읽는 모든 자리가 언랩을 지불하지
않게 하려는 것입니다. 참조는 예외로 `T?`입니다(없는 참조가 곧 `nil`입니다).

**로우는 `class`, 레코드 원소는 `struct`입니다.** 해석된 참조가 값이면 가리키는 쪽마다 행이
복사되므로 로우는 참조 타입이고, 배열 안에 인라인으로 놓이는 원소는 값 타입입니다
([근거](../../spec/swift-language-support.md#2-결정-1--행은-final-class-레코드-원소는-struct)).

**키워드는 백틱으로 이스케이프합니다.** 이름을 바꾸지 않고 `` `class` ``로 감싸므로, 생성된
멤버 이름이 시트의 이름과 그대로 같습니다.

**`datetime`과 `timespan`은 `Int64`입니다.** .NET 틱이 그대로 들어옵니다.

**리더의 이름은 전부 `Tcb` 안에 있습니다.** 이 파일이 남의 모듈로 복사되는 것을 전제하므로,
`magic`·`headerSize` 같은 전역을 30개 넘게 풀어놓지 않습니다. 밖에 있는 것은 잡게 되는 오류
둘 — `TcbError`와 `RecordNotFoundError` — 뿐입니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`Cannot find 'GameData' in scope`|생성물이 그 타깃의 소스에 들어 있는지, `Path`가 타깃 디렉터리인지 확인하세요|
|`Call can throw but is not marked with 'try'`|읽기는 `throws`입니다. `try`를 붙이거나 `do`로 감싸세요|
|`No such module 'Crypto'`|위 [막혔을 때](#막혔을-때)를 보세요|
|`error: header 'stdnoreturn.h' not found`|윈도우 SDK가 부분 설치입니다. [툴체인 설치](#툴체인-설치--리눅스윈도우)를 보세요|
|참조가 `nil`|`readAll` 대신 테이블 하나만 읽었거나, 시트가 그 셀을 비웠습니다|
|`static property ... is not concurrency-safe`|Swift 6 모드에서 가변 전역을 만든 것입니다. 접근자는 인스턴스로 두세요|
