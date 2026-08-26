# 의존 패키지

이 도구가 쓰는 외부 패키지와, 그것이 무엇을 하는지.

> [문서 목록으로](readme.md)

---

## 변환기 — `src/Tabbit.csproj`

|패키지|버전|무엇에|
|--|--|--|
|[Sylvan.Data.Excel](https://github.com/MarkPflug/Sylvan)|0.5.8|**엑셀 워크북을 스트리밍으로** 읽습니다. 시트를 행 단위로 흘려 읽으므로 워크북을 객체 모델로 펼치지 않습니다 ([설계와 실측](../spec/import/streaming-workbook-reader.md))|
|[Google.Apis.Sheets.v4](https://github.com/googleapis/google-api-dotnet-client)|1.75.0.4178|구글 스프레드시트를 읽습니다|
|[Scriban](https://github.com/scriban/scriban)|7.2.6|코드 생성 템플릿 엔진. `src/templates/*.sbn`이 모든 언어의 산출물을 만듭니다|
|[Microsoft.CodeAnalysis.CSharp](https://github.com/dotnet/roslyn)|5.6.0|검증 규칙 `.cs` 파일을 변환 중에 컴파일합니다. `"Output": "assembly"`의 C# 어셈블리 산출도 여기서 나옵니다 ([검증](validation.md))|
|[Newtonsoft.Json](https://www.newtonsoft.com/json)|13.0.4|recipe 파싱과 JSON 익스포트|
|[CommandLineParser](https://github.com/commandlineparser/commandline)|2.9.1|명령줄 옵션|
|[Serilog](https://serilog.net/)|4.4.0|로그. 싱크는 `Serilog.Sinks.Console` 6.1.1 · `Serilog.Sinks.File` 7.0.0|

### 데이터베이스 적재

파일 대신 데이터베이스로 내보낼 때만 쓰입니다 ([내보내기](exports.md)).

|패키지|버전|대상|
|--|--|--|
|[MySqlConnector](https://github.com/mysql-net/MySqlConnector)|2.6.1|MySQL — 히스토리 저장소도 같은 드라이버입니다|
|[Npgsql](https://github.com/npgsql/npgsql)|10.0.3|PostgreSQL|
|[MongoDB.Driver](https://github.com/mongodb/mongo-csharp-driver)|3.10.0|MongoDB|
|[StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis)|3.0.17|Redis|

## 계약 어셈블리 — `src/Contract`

검증 규칙이 대고 컴파일되는 `Tabbit.Validation.dll`입니다. **여기 있는 것이 곧 규칙이 쓸 수 있는
것**이므로 목록이 짧은 것 자체가 설계입니다
([근거](../spec/validation/validation-usability-and-assembly-output.md)).

|패키지|버전|
|--|--|
|Newtonsoft.Json|13.0.4|

## 생성된 코드의 의존

**의존은 억제하지만 금지하지 않습니다.** 기본은 각 언어의 표준 라이브러리이고, **직접 구현이
성능에서 크게 불리한 자리는 플랫폼이나 외부 패키지가 이미 가진 것을 씁니다.** 판단 기준은
[v104에 적어 둔 것](../spec/wire/tcb-v104-composed-encodings.md#구현-방침--언어마다-다릅니다)과 같습니다 —
「그 언어에서 바이트 단위 루프가 수 MB를 감당하는가」.

외부 패키지가 필요한 자리는 아래뿐이고, **전부 기본값에서는 만나지 않습니다.**

|언어|무엇|언제|어디에 적히나|
|--|--|--|--|
|Rust|`ureq`|`WriteUpdater`를 켤 때|생성되는 `Cargo.toml`|
|C · C++|libcurl|`WriteUpdater`를 켤 때|링크 플래그|
|Swift|[swift-crypto](https://github.com/apple/swift-crypto)|**MAC을 검증할 때, 애플 플랫폼이 아닐 때**|생성되는 `Package.swift`(`WriteManifest`) 또는 소비자의 매니페스트. 애플 플랫폼에서는 OS의 CryptoKit이 대신하므로 받지 않고, 없는 빌드에서도 **리더는 컴파일되고 파일도 읽힙니다** — 안 되는 것은 검증 하나이고 그때 무엇을 넣어야 하는지 말합니다 ([설치 절차](languages/swift.md#mac-검증과-swift-crypto))|
|Python|`cryptography`|**암호화된 파일을 읽을 때**|`pip install cryptography`. import가 그 함수 안에 있어서 암호화를 쓰지 않는 프로젝트는 도달하지 않고, 없으면 **무엇을 설치해야 하는지 말하면서** 실패합니다|
|Lua|동봉된 C 소스 (`tabbit/native/tabbit_native.c`)|**암호화·MAC·업데이터를 쓸 때**|패키지가 아니라 **산출물에 함께 나오는 소스 한 파일**입니다 — 호스트의 빌드에 넣거나 공유 라이브러리로 컴파일합니다. require가 암호 경로 안에 있어서 평문만 읽는 프로젝트는 도달하지 않고, 없으면 **무엇을 빌드해야 하는지 말하면서** 실패합니다 ([빌드 절차](languages/lua.md#네이티브-모듈--암호화mac매니페스트-해시))|

표준 라이브러리로 해결되는 자리는 의존이 아니므로 위에 없습니다 — Java·Kotlin의
`javax.crypto`, Ruby의 `openssl`, PHP의 ext-openssl이 그렇습니다
([암호 구현 방침](../spec/wire/tcb-v104-composed-encodings.md#구현-방침--언어마다-다릅니다)).

## 저장소 안에서만 쓰는 것

배포본에 들어가지 않습니다.

|패키지|버전|어디|
|--|--|--|
|[NPOI](https://github.com/nissl-lab/npoi)|2.8.0|`test/fixtures/tools/FixtureGen` — 테스트 픽스처 `.xlsx`를 **씁니다**. 변환기는 더 이상 NPOI로 읽지 않습니다|
|xunit|2.9.2|테스트. 러너는 `xunit.runner.visualstudio` 2.8.2, 호스트는 `Microsoft.NET.Test.Sdk` 17.11.1|
|System.Security.Cryptography.Xml|10.0.10|테스트와 픽스처 도구|

---

> 이 문서는 `.csproj`의 `PackageReference`와 일치해야 합니다. 손으로 맞추는 목록은 썩으므로 —
> 실제로 한 번 썩어서 없어진 의존이 남아 있었습니다 — **어긋나면 스위트가 실패합니다**
> (`DependencyDocTests`). 참조하는데 여기 없는 것, 여기 있는데 아무도 참조하지 않는 것,
> 그리고 버전이 다른 것을 각각 봅니다.
