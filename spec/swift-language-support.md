# Swift 언어 지원

> [문서 목록으로](../doc/readme.md)
>
> 상태: **구현 완료** — 게이트 17개가 로컬에서 통과합니다. 단계별 상태는 12절

새 코드 생성 타깃입니다. **형식은 바뀌지 않습니다** — v106을 읽는 리더가 하나 더 생기는
일이고, 변환기·익스포터·기존 언어는 손대지 않습니다.

그래서 이 문서가 정하는 것은 기능이 아니라 **Swift에서만 답이 갈리는 6개**입니다. 행을 무엇으로
낼지, 정수에 폭을 적을지, 옵셔널을 `T?`로 낼지, 암호를 어디서 얻을지, 산출물을 패키지로 낼지,
게이트를 무엇으로 돌릴지. 여섯 개 다 나중에 바꾸면 산출물의 모양이 바뀌므로, 리더보다 먼저
정합니다.

---

## 1. 범위 — 바뀌지 않는 것을 먼저

|무엇|상태|
|--|--|
|와이어 형식(v106)·변환기·익스포터|**무변경.** 리더를 하나 더 쓰는 일입니다|
|코어의 등록 코드|**없습니다.** `[TabbitTarget("swift", ...)]` 어트리뷰트 스캔이므로 CLI·recipe 스키마·레지스트리에 이름이 들어가지 않습니다|
|골든 트리|**무변경.** 시나리오별 골든은 `binary`·`csharp`·`typescript`·`html`·`json` 다섯 개뿐입니다|
|기존 언어|**무변경**|
|샘플 산출물|**무변경.** 샘플 recipe에 이 타깃을 넣지 않는 한|

새로 생기는 것은 런타임 둘(리더·업데이터), 생성기 둘(생성기·뷰), 템플릿 다섯, 프로파일 하나,
게이트 일곱, 문서 하나입니다.

## 2. 결정 1 — 행은 `final class`, 레코드 원소는 `struct`

**결정.** 테이블의 로우 타입은 `final class`, 레코드 그룹의 원소 타입은 `struct`입니다.

두 자리를 갈라 놓는 이유는 각각 다릅니다.

|자리|선택|근거|
|--|--|--|
|로우|`final class`|**해석된 참조가 값이면 데이터가 복제됩니다.** [레코드 안의 참조](references-in-records.md)에서 대부분의 언어가 원소 안에 *해석된 행*을 둡니다. 10만 행 테이블을 가리키는 참조가 구조체 복사라면 가리키는 쪽마다 행 전체가 복사됩니다|
|레코드 원소|`struct`|**원소는 신원을 갖지 않습니다.** 배열 안에 인라인으로 놓이므로 원소당 할당이 없고, `row.slots[j].position.x`의 제자리 변경이 그대로 성립합니다|

로우의 ARC 비용은 감수합니다 — 로우는 **한 번 읽고 계속 공유되는 것**이고, 읽기 경로에서
retain이 도는 것은 조회가 반환할 때뿐입니다.

`Uuid`는 16바이트 값이므로 `struct`이고 `Hashable`입니다.

### 고정 길이 레코드 배열을 만드는 자리

언어마다 갈렸던 그 질문의 Swift 답은 **초기화 패스**입니다(Go·Rust와 같은 쪽). 길이는
[파일에서 옵니다](../doc/roadmap.md) — 생성된 코드의 상수가 아니라 컬럼이 말하는 원소 수이므로,
선언에서 만들 수 없습니다.

## 3. 결정 2 — 정수는 폭을 적습니다

**결정.** `int`는 `Int32`, `bigint`는 `Int64`입니다. `Int`가 아닙니다.

|근거|내용|
|--|--|
|다수가 그렇습니다|대부분의 언어가 폭을 적습니다. `Int`로 눕히는 것은 폭이 없는 언어(TypeScript·Python·Ruby·Dart)의 사정입니다|
|폭이 사라지면 되돌아옵니다|[참조 키의 타입](reference-key-types.md)이 정확히 그 결함이었습니다 — 언어마다 6곳에 `int32`가 하드코딩되어 있었고, 두 언어만 대상의 키를 그대로 읽고 있었습니다|

비용은 호출자가 `Int(row.hp)`를 쓰게 되는 것입니다. **그 비용은 문서로 갚습니다** — 왜 `Int`가
아닌지를 언어 가이드에 적습니다.

## 4. 결정 3 — 옵셔널은 `T?`로 내지 않습니다

**결정.** [옵셔널 필드](optional-fields.md)의 규칙을 그대로 따릅니다 — 값 프로퍼티는 언제나
초기화되어 있고, 존재 여부는 `hasHp` 같은 이웃 프로퍼티가 답합니다.

**이것이 Swift 사용자가 가장 놀랄 자리이므로 근거를 여기 한 번 더 적습니다.** `T?`로 내면
값을 읽는 모든 자리가 언랩을 지불합니다 — 로우의 99%가 값을 갖는 컬럼에서도 그렇습니다. 그리고
[원소가 없을 수 있는 배열](nullable-array-elements.md)에서 `[T?]`가 되면 그 지불이 원소마다로
번집니다. 다른 언어가 합의한 「값은 항상 읽힌다, 없음은 따로 묻는다」를 Swift에서만 깨면, 같은
스키마를 두 언어에서 읽는 코드의 모양이 갈립니다.

## 5. 결정 4 — 암호는 3상태이고, 패키지가 없어도 컴파일됩니다

**결정.** HMAC-SHA-256은 플랫폼에서 얻고, ChaCha20은 손으로 씁니다.

이 결정은 새 방침이 아닙니다. [v104](tcb-v104-composed-encodings.md#구현-방침--언어마다-다릅니다)가
이미 적어 둔 것이 **「직접 구현이 기본이지만, 순수 구현이 느린 언어는 플랫폼이 이미 가진 것을
쓴다」**이고, 판단 기준까지 있습니다 — 「그 언어에서 바이트 단위 루프가 수 MB를 감당하는가」.
Swift는 감당하는 쪽이므로 **속도 때문에 패키지를 쓰는 것이 아니고**, HMAC만 가져오는 이유는
따로입니다(아래 측정).

```swift
#if canImport(CryptoKit)
import CryptoKit          // 애플 플랫폼 — OS에 들어 있으므로 패키지가 아닙니다
#elseif canImport(Crypto)
import Crypto             // swift-crypto — 리눅스·윈도우에서 넣었을 때
#endif
```

|구성|HMAC|외부 패키지|MAC 검증|
|--|--|--|--|
|iOS 13+ · macOS 10.15+|CryptoKit|**0개**|됩니다|
|리눅스·윈도우 + swift-crypto|`Crypto`|1개|됩니다|
|리눅스·윈도우, 패키지 없음|없음|**0개**|`MacKey`를 쓴 파일에서 **무엇을 넣어야 하는지 말하는 오류**|

**세 번째 줄이 이 설계의 요점입니다.** `#elseif canImport(Crypto)`이므로 패키지가 없는
빌드에서도 리더는 **컴파일됩니다.** 평문 파일도 암호화된 파일도 그대로 읽히고, 없는 것은 MAC
검증 하나입니다.

**이 모양에도 선례가 있습니다.** Python 리더는 `cryptography`를 모듈 최상단이 아니라 **쓰는 함수
안에서** import하고, 없으면 `pip install cryptography`를 말하면서 실패합니다 — 파일이 암호화되어
있지 않은 프로젝트는 그 의존을 만나지 않습니다. Swift에서 그 자리를 컴파일 조건이 대신합니다:
Python은 실행 시점에 갈리고 Swift는 빌드 시점에 갈립니다.

### 직접 구현하지 않는 근거

[MAC 설계](tcb-mac-and-signature.md#검증-비용--실측)의 측정이 그대로 근거입니다.

|구현|처리량|
|--|--|
|플랫폼(`HMACSHA256`)|약 2,250 MB/s|
|직접 구현(C, `/O2`)|약 345 MB/s|

**6배는 구현 품질이 아니라 CPU의 SHA 확장 명령**입니다. 직접 구현은 그 명령에 닿지 않습니다.
의존성 0은 이 저장소의 기본값이지만 제약이 아니고, 이 자리는 유불리가 분명한 쪽입니다.

### ChaCha20을 손으로 쓰는 것은 정책이 아니라 API 표면입니다

CryptoKit도 swift-crypto도 `ChaChaPoly`(**AEAD**)만 노출하고 **raw 키스트림을 주지 않습니다.**
스트림 하나만 필요한 이 형식에는 대응하는 함수가 없습니다.

**이것은 PHP에서 이미 한 번 겪은 실패와 같은 모양입니다.** ext-sodium이 번들되어 있다는 이유로
그쪽을 적어 두었다가, sodium이 내놓는 것이 nonce 24바이트의 `xchacha20` — **다른 구성**이라
변환기가 봉인한 파일을 열 수 없다는 것을 실제로 돌려 보고 알았습니다([기록](tcb-v104-composed-encodings.md#구현-방침--언어마다-다릅니다)).
「그 언어에 ChaCha20이 있다」와 「이 형식이 쓰는 구성이 있다」는 다른 질문이고, Swift는 후자가
없습니다.

기존 언어의 갈림은 이렇습니다 — 직접 구현하는 쪽이 C·C++·Unreal·C#·Rust·Go·Dart·TypeScript이고,
Java·Kotlin은 JDK의 `javax.crypto`, Python은 `cryptography`, Ruby는 `openssl`, PHP는
ext-openssl입니다. **Swift는 첫 번째 그룹으로 들어갑니다.** RFC 8439의 시험 벡터로 확인합니다.

카운터는 **0에서 시작**합니다 — 라이브러리를 쓰는 쪽이 16바이트 IV로 넘겨야 하는 그 값이고,
직접 구현에서는 초기 상태의 카운터 워드입니다.

## 6. 결정 5 — 산출물의 모양과 패키징

파일 배치는 다른 언어와 같은 모양입니다.

```
<Path>/
  tabbit/TcbReader.swift        임베디드 리소스에서 복사
  tabbit/Updater.swift          WriteUpdater일 때만
  Tables.swift                  액세서 (AccessorName)
  enums/<Enum>.swift
  tables/<Table>Table.swift
  Package.swift                 WriteManifest일 때만
```

recipe 옵션:

|옵션|기본값|비고|
|--|--|--|
|`Path`|—|없으면 이 타깃은 아무것도 하지 않습니다|
|`AccessorName`|`Tables`|액세서 타입 이름이자 파일 이름|
|`BinaryTableFileExtension`|`.tcb`|바이너리 익스포터에 적은 것과 같아야 합니다|
|`WriteUpdater`|`false`|`Updater.swift`를 함께 냅니다|
|`WriteManifest`|`false`|`Package.swift`를 냅니다|
|`ModuleName`|`GameData`|`WriteManifest`일 때 타깃 이름|
|`SwiftCryptoVersion`|`3.0.0`|매니페스트가 선언할 하한. Rust의 `UreqVersion`과 같은 이유로 recipe 설정입니다 — 빌드해야 하는 패키지는 소비자의 것입니다|
|`Sweep`|`true`|이 실행이 쓰지 않은 생성 파일을 지웁니다|
|`TargetSide`|`cs`|—|

**`Namespace`·`PackageName`은 없습니다.** Swift의 모듈은 디렉터리와 빌드 타깃이 정하는 것이고
파일이 선언하는 것이 아닙니다. 이름을 받아 두면 아무 데도 쓰이지 않는 옵션이 됩니다.

`WriteManifest`가 기본 꺼짐인 것은 Rust와 같은 판단입니다 — 소비자의 프로젝트 안으로 소스를
넣는 쪽이 더 흔하고, 그때 매니페스트는 방해입니다.

## 7. 결정 6 — 예약어는 백틱

Kotlin과 같습니다. 이름을 바꾸지 않고 `` `class` ``로 감쌉니다.

## 8. 결정 7 — 이름과 오류 전파

**결정.** 리더가 정의하는 모든 이름은 `Tcb` 안에 있고, 읽기는 `throws`입니다.

|무엇|어떻게|근거|
|--|--|--|
|네임스페이스|`public enum Tcb`에 상수·`Column`·`Header`·`Uuid`·`Reader`·`ColumnCursor`가 들어갑니다. 밖에 남는 것은 호출자가 잡는 `TcbError`와 `RecordNotFoundError` 둘뿐입니다|Swift에는 모듈보다 작은 이름 공간이 없습니다. **소스 복사 통합**(13절 경로 ④)에서 리더를 그냥 넣으면 `magic`·`headerSize`·`formatVersion` 같은 전역이 30개 넘게 소비자 모듈에 풀립니다|
|`openTable`|봉인을 여는 함수의 이름. `open`이 아닙니다|`import Foundation`이 POSIX `open`을 이미 들여옵니다. 전역 `open`은 그 옆에 서게 되고, 오버로드 해소가 인자에 따라 갈리는 이름을 리더가 만들 이유가 없습니다|
|`throws`|모든 읽기가 던지고, 생성 코드가 `try`를 붙입니다|이 형식의 계약은 **거절**입니다 — 잘린 파일에서 값을 지어내지 않습니다. Rust의 `Result`와 Go의 `error`가 같은 자리이고, Swift에서 그 자리는 `throws`입니다. 생성 코드가 `try`로 덮이는 것은 그 대가입니다|

### 확인한 것

리더를 다 쓴 자리에서 **변환기가 봉인한 파일을 실제로 열어** 확인했습니다 — 생성기를 쓰기 전에
확인할 수 있는 가장 강한 검증이고, 암호 두 갈래가 여기서 한 번에 걸립니다.

|확인|결과|
|--|--|
|C#이 쓴 HMAC-SHA-256 태그 검증|통과|
|ChaCha20 복호와 키 체크|통과 — 값이 같은 실행의 평문 JSON과 일치|
|컬럼 인코딩 raw·varint, 원소 string·f32·i32|통과|
|**다른 키**|키 체크에서 거절|
|**한 바이트 변조**|MAC에서 거절|
|변조된 파일을 MAC 키 없이|열립니다 — 그 쌍이 [MAC 설계](tcb-mac-and-signature.md)가 요구하는 확인입니다|
|의존성 0 빌드(swift-crypto 없음)|컴파일되고 MAC만 못 씁니다 — 5절의 세 번째 상태|

## 9. 타입 매핑

|테빗|Swift|읽기|
|--|--|--|
|`string` · `text` · `asset`|`String`|`reader.readString()`|
|`bool`|`Bool`|`reader.readBool()`|
|`int`|`Int32`|`reader.readI32As(column.element)`|
|`bigint` · `bitset`|`Int64`|`reader.readI64As(column.element)`|
|`float`|`Float`|`reader.readFloat()`|
|`double`|`Double`|`reader.readF64As(column.element)`|
|`datetime`|`Int64` (틱)|`reader.readDateTimeTicks()`|
|`timespan`|`Int64` (틱)|`reader.readDurationTicks()`|
|`uuid`|`Uuid`|`reader.readUuid()`|
|배열|`[T]`|—|

`datetime`을 `Date`로 내지 않는 것은 Kotlin·Java와 같은 이유입니다 — 왕복이 손실이고, 틱은
손실이 없습니다.

## 10. 리더가 손으로 갖는 것

|무엇|비고|
|--|--|
|헤더 42바이트·시그니처·버전 거절|—|
|[인코딩 14종](tcb-v104-composed-encodings.md)|raw·varint·delta·rle·delta-rle·dict 4종·array·whole·dict-segment 2종·[bitpack](tcb-v105-bit-width-packing.md)|
|[로우 presence](tcb-v103-presence-bitmap.md)·[원소 presence](tcb-v106-element-presence.md)|비트맵 자체도 인코딩됩니다|
|ChaCha20 (RFC 8439)|약 100줄|
|HMAC-SHA-256|**플랫폼에서**. 5절|
|`Uuid`|.NET Guid 바이트 순서 — 앞의 세 성분만 리틀엔디언|

### Swift만의 함정 — 오버플로 트랩

Swift의 `+`·`<<`는 오버플로에서 **트랩합니다.** varint 누적, zig-zag 복원, 델타 합, ChaCha20의
라운드 함수가 전부 랩어라운드를 전제하므로 `&+`·`&<<`·`&>>`를 써야 합니다. 놓치면
**컴파일은 통과하고 런타임에 죽습니다** — 다른 언어에 없던 실패 방식이므로 게이트가
실제 데이터를 읽어야 잡힙니다.

## 11. 게이트

|게이트|무엇|
|--|--|
|적합성 코퍼스|다른 언어와 같은 하네스 — 모든 타입이 든 코퍼스를 읽어 JSON으로 답하고 기대값과 대조합니다|
|MAC 거부|값을 바꾼 사본을 거부하는 것. swift-crypto가 있는 구성에서|
|**의존성 0 컴파일**|swift-crypto **없이** 리더가 컴파일되는 것. 5절이 이 게이트로만 지켜집니다|
|중첩·옵셔널|`nested`·`optional`·`record-trim`·`nested-deep`·`record-ref`|
|예약어|`reserved-words` 픽스처|
|키 타입|`key-types`·`reference-keys`|
|업데이터|`swift-updater` 픽스처|

하네스는 `swiftc`를 PATH와 표준 설치 위치에서 찾습니다 — Kotlin의 `KotlinHomes`와 같은 모양이고,
찾지 못하면 **무엇을 깔아야 하는지 말하면서** 실패합니다.

## 12. 단계

|단계|무엇|상태|
|--|--|--|
|0|툴체인(로컬·CI)과 `SwiftIsAvailable`|**됨.** 로컬은 툴체인 6.3.3 + Windows 11 SDK 10.0.22621로 돌고, CI는 ubuntu·windows에 `setup-swift`를, macOS는 Xcode의 것을 씁니다(14절에 윈도우에서 걸린 것 셋)|
|1|리더 — 헤더·인코딩 14종·presence 둘·`Uuid`|**됨.** 1,798줄. 적합성 코퍼스로 **인코딩 14종·원소 8종 전부** 확인|
|2|암호 — ChaCha20 직접, HMAC은 `#if` 분기|**됨.** 봉인된 파일 왕복, 다른 키·변조 거절, 의존성 0 빌드까지 8절에|
|3|생성기·뷰·템플릿·`LanguageProfile.Swift`|**됨.** 생성기 1,181줄·뷰 357줄·템플릿 5개(536줄). 생성물이 **Swift 5·6 두 모드에서 경고 0**으로 컴파일|
|4|적합성 하네스와 게이트 일곱|**됨.** 적합성·MAC·의존성 0 컴파일·중첩/옵셔널 8종·예약어·키 타입. Python 하네스와 36행 × 20필드 전부 일치|
|5|업데이터와 그 게이트|**됨.** 545줄, MD5는 직접 구현하고 RFC 1321 벡터 10개로 확인. 게이트는 **실제 HTTP 서버**에 붙습니다 — 바뀐 것만 받기, 손상된 다운로드 거부, 재시도, 404 비재시도|
|6|문서 — `doc/languages/swift.md`·side-by-side|**됨.** 가이드는 **설치 절차를 초보자 기준으로** 씁니다(통합 경로 4개·툴체인·막혔을 때). side-by-side에 산출물이 커밋되고, 문서에서 **언어 개수 표기를 전부 걷어냈습니다**|
|7|기준 정리 — `doc/languages/rust.md` 보강, `DependencyDocTests` 확장|**됨.** `ureq` 절에 상황별 절차와 증상 표를 넣었고, 게이트는 **side-by-side의 생성 매니페스트에서 의존성을 읽어** 문서와 대조합니다 — 이름을 지우고 실패하는 것까지 확인했습니다|

## 13. 문서 — 외부 패키지를 쓰는 첫 번째 언어 가이드

`doc/languages/swift.md`에 들어가는 것은 다른 12개와 같지만, 의존성 절이 하나 더 붙습니다.
**이유와 설치 절차를 초보자 기준으로** 적습니다.

|절|내용|
|--|--|
|왜 이 패키지인가|5절의 측정. 안 넣으면 무엇이 안 되는지(MAC 검증 하나)까지|
|경로 ① iOS·macOS|**할 일이 없습니다** — CryptoKit은 OS에 있습니다|
|경로 ② Xcode|File ▸ Add Package Dependencies… 부터 **타깃 체크박스**까지. 초보자가 가장 자주 빠뜨리는 마지막 단계입니다|
|경로 ③ `Package.swift`|`dependencies`와 `targets` **두 곳**. 한쪽만 적으면 `No such module 'Crypto'`가 나옵니다 — 증상과 원인을 짝지어 적습니다|
|경로 ④ 소스 복사|패키지 없이 통합할 때 무엇을 포기하는지|
|툴체인 설치|리눅스·윈도우. 확인은 `swift --version`, 윈도우는 Visual Studio의 C++ 구성요소가 먼저|
|막혔을 때|오프라인·사내 미러, `Package.resolved` 핀|

그리고 두 가지를 함께 정리합니다.

- [`doc/dependencies.md`](../doc/dependencies.md)의 「생성된 코드의 의존」 표에 한 줄. **그 절은
  이미 고쳤습니다** — 「모든 언어의 테이블 리더는 각 언어의 표준 라이브러리만 씁니다」라고 적혀
  있었는데 Python 리더의 암호화 경로가 `cryptography`를 쓰고 있었고, 표에도 빠져 있었습니다.
  규칙을 적는 문장으로 바꿨습니다 — **의존은 억제하지만 금지하지 않고, 직접 구현이 성능에서 크게
  불리한 자리는 이미 있는 것을 쓴다.**
- [`doc/languages/rust.md`](../doc/languages/rust.md)의 `ureq` 절은 **이유는 충분하고 절차가
  스니펫 한 조각**입니다. 기준을 세우면서 기존 항목을 두면 기준이 아니게 되므로 함께 보강합니다.
- `DependencyDocTests`는 `.csproj`의 `PackageReference`만 봅니다. **생성 산출물이 선언하는
  의존성은 게이트가 없어서**, 생성기의 기본 버전 문자열을 읽어 문서에 있는지 확인하는 것으로
  넓힙니다.

## 14. 하는 중에 나온 것

|무엇|내용|
|--|--|
|**Swift 6 모드가 결함을 잡았습니다**|`uuid` 상수는 `Tcb.Uuid`의 `static let`이고, Swift 6에서 그 타입은 `Sendable`이어야 합니다. 아니어서 **`uuid` 상수를 가진 프로젝트는 Swift 6 모드에서 컴파일되지 않았습니다.** 리더의 불변 타입 다섯에 `Sendable`을 달아 고쳤고, 게이트가 **두 모드 다** 확인합니다|
|**비정규수 리터럴**|C#의 `R` 포맷이 내는 `5E-324`는 Swift에서 값은 맞지만 경고를 냅니다 — `-warnings-as-errors`를 쓰는 소비자에게는 오류입니다. 비정규수·무한·NaN을 비트 패턴과 이름으로 냅니다(`Double(bitPattern:)`·`Double.infinity`). `Infinity`·`NaN`은 Swift에 리터럴이 아예 없으므로 이건 경고가 아니라 필수입니다|
|**`URLSession` 콜백**|캡처한 `var`를 콜백에서 쓰는 것이 Swift 6에서 오류입니다. 세마포어가 보장하는 것을 `@unchecked Sendable` 박스로 적어 뒀습니다|
|**윈도우에서만 걸린 것 셋**|① `swift.exe`를 전체 경로로 부르면 DLL을 못 찾습니다(`0xC0000135`) — 툴체인과 **런타임** 디렉터리가 둘 다 PATH에 있어야 합니다. ② SwiftPM의 `.build`에는 패키지 체크아웃의 **읽기 전용 파일**이 있어서 출력 디렉터리 정리가 실패합니다 — `--scratch-path`로 출력 트리 밖으로 뺐고, 덤으로 swift-crypto를 한 번만 받습니다. ③ 산출물 경로는 `debug/`가 아니라 **트리플 디렉터리** 아래입니다 — 조립하지 않고 `--show-bin-path`로 물어봅니다|
|**Kotlin 쪽에서 본 것**|Kotlin의 **최상위 참조 키 선언이 `Int`로 하드코딩**되어 있습니다(`Declarations`). `bigint`·`string`·`uuid`로 키를 잡은 테이블을 가리키면 어긋나는데, `reference-keys` 픽스처가 `binary`·`json`·`csharp`·`typescript`만 돌아서 **어느 게이트에도 걸리지 않습니다.** Swift는 키 타입을 그대로 씁니다. 이 저장소의 「게이트 없는 언어를 믿지 말 것」이 다시 맞았습니다|
