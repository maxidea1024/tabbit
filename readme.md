# Tabbit

**Game Data Authoring & Build Tool**

게임 시스템의 근간은 정적 데이터입니다. 아이템·스테이지·밸런스·설정 — 코드보다 자주 바뀌고, 손이 더 많이 타고, 틀렸을 때 가장 늦게 드러납니다.

**Tabbit**(테빗)은 그 데이터를 **짜고, 검증하고, 런타임 데이터로 빌드하는** 도구입니다. 기획자가 쓰는 자리에서 데이터를 작성하고, 스키마를 관리하고, 값이 규칙에 맞는지 검증하고, 컬럼마다 인코딩을 고르고, 런타임이 그대로 읽는 바이너리를 냅니다.

|단계|하는 일|
|--|--|
|**Author**|엑셀·구글 스프레드시트에 테이블·enum·상수셋을 적습니다 — [시트 작성](doc/sheets.md)|
|**Schema**|타입·인덱스·참조·제약을 시트에서 읽어 스키마로 세웁니다 — [컬럼 제약](spec/column-constraints.md)|
|**Validate**|타입으로 표현할 수 없는 규칙까지 C#으로 검사합니다. 문제는 **어느 셀인지** 함께 보고합니다 — [검증](doc/validation.md)|
|**Analyze**|로드된 데이터를 HTML로 펼쳐 보고, 커밋마다 무엇이 바뀌었는지 셀 단위로 남깁니다 — [Summary와 히스토리](doc/history.md)|
|**Optimize**|컬럼마다 그 값들에 가장 짧은 인코딩을 고릅니다 — 사전·RLE·델타·비트폭 패킹 — [비트폭 패킹](spec/tcb-v105-bit-width-packing.md)|
|**Build**|런타임 바이너리와, 그것을 읽는 코드를 13개 언어로 냅니다 — [내보내기](doc/exports.md)|

**TCB — Tabbit Binary.** 빌드의 결과물입니다. 파싱이 없고, 인코딩이 값의 크기에 맞고, **스키마가 바뀌어도 감지되지 않는 읽기 오류가 되지 않습니다** — 읽을 수 있으면 정확히 읽고, 읽을 수 없으면 필드 이름과 양쪽 타입을 대고 멈춥니다. [바이너리 형식](doc/binary-format.md)

엑셀과 구글 스프레드시트는 **입력 수단**입니다. 지금 데이터가 거기 있어서 거기서 읽을 뿐이고, 이 도구의 정체는 무엇을 읽느냐가 아니라 무엇을 내놓느냐에 있습니다.

![임포트 → 검증 → 내보내기/코드생성 파이프라인](doc/pipeline.svg)

## 시트에 무엇을 적을 수 있나

세 가지입니다. 시트의 셀에 마커를 적으면 그 자리가 엔티티가 됩니다.

|엔티티|마커|무엇인가|나오는 것|
|--|--|--|--|
|**테이블**|`~~table:Item~~`|행과 열로 된 데이터. 기본 인덱스와 보조 인덱스, 다른 테이블 참조, 배열 컬럼|레코드 타입, 인덱스별 조회, 데이터 파일|
|**enum**|`~~enum:Grade~~`|이름 붙은 정수 값의 집합. 테이블 컬럼의 타입으로 씁니다|언어별 열거형 타입|
|**상수셋**|`~~const:Limits~~`|이름·타입·값의 목록. 행이 아니라 개별 상수입니다|언어별 상수 선언|

한 시트에 여러 개를 놓아도 되고, 어디에 놓아도 됩니다. 자세한 것은 [시트 작성](doc/sheets.md)에 있습니다.

**나오는 것이 다르니 배포도 다릅니다.** 테이블은 데이터 파일로 나가므로 대개 데이터만 올려도 되지만, enum과 상수셋은 코드로 나가므로 **코드 배포가 함께 필요합니다.** 특히 상수셋은 데이터 파일에 흔적이 아예 없어서, 값을 고쳐도 코드를 다시 배포하기 전에는 아무것도 달라지지 않습니다 — [어느 변경이 어느 쪽인지](doc/languages/readme.md#데이터만-나가도-되는-변경과-코드가-함께-나가야-하는-변경).

### 다른 규칙으로 쓰인 시트 읽기

위 마커 방식이 기본(`tabbit` 레이아웃)이지만, **다른 규칙으로 작성된 시트도 그대로 읽을 수 있습니다.** 시트를 먼저 고치지 않아도 됩니다.

```jsonc
"Xlsx": [
  { "Path": "./sheets",       "Layout": "tabbit" },
  { "Path": "./other-sheets", "Layout": "rescue"   }
]
```

레이아웃은 **소스 항목마다** 지정하므로 한 recipe에서 섞어 읽을 수 있고, 한쪽에서 선언한 enum을 다른 쪽 테이블이 타입으로 써도 됩니다.

- **`rescue` 레이아웃 규칙과 실제 적용 기록**: [다른 규칙으로 쓰인 시트 읽기](samples/rescue/doc/적용-기록.md)
- recipe 설정: [Recipe 파일 — Layout](doc/recipe.md#layout--시트를-읽는-방식)

---

## 문서

|문서|내용|
|--|--|
|[시트 작성](doc/sheets.md)|엑셀·구글 스프레드시트에 데이터를 배치하는 법, 엔티티 마커, 이름 규칙, 지원 타입, 서버/클라 분리, 정적 검증|
|[중첩 필드](spec/nested-fields.md)|`Group.Member` 표기의 설계 — 컬럼 여러 개를 레코드 하나로 접으면서 **와이어 형식은 그대로 둔 방법**|
|[다중 중첩](spec/nested-multi-level.md)|`Pos.X1`(멤버가 배열인 레코드)과 `Grid1.2`(배열의 배열)가 **형식을 한 비트도 안 건드리고** 되는 이유 — 그리고 이 문서의 예측이 어디서 틀렸는지|
|[컬럼 제약](spec/column-constraints.md)|타입으로 나타낼 수 없는 것 — 범위·허용값·필수 여부를 시트에서 읽어 **어느 셀인지 함께** 검사하기|
|[검증 파이프라인](spec/validation-pipeline.md)|시트로 표현할 수 없는 규칙을 C# 규칙 파일로 — 설계와 그 근거. 사용법은 [검증](doc/validation.md)에 있습니다|
|[매트릭스 표](spec/matrix-tables.md)|컬럼 이름이 행 id인 격자 — 배열 하나와 컬럼 id 테이블로 내는 이유, 그리고 long-form과 map을 채택하지 않은 이유|
|[배열의 옵셔널](spec/array-optionality.md)|배열 컬럼들의 필수 표시가 엇갈릴 때 — 첫 원소가 전부를 정하는 이유|
|[레코드 멤버별 옵셔널](spec/record-member-optionality.md)|`:requiredInObject` — **검증 규칙이지 표현의 요구가 아닌 이유**와, 「레코드가 존재한다」를 무엇으로 판정하는가|
|[다중 대상 참조](spec/multi-target-references.md)|값이 여러 테이블 중 하나의 행이어야 할 때 — 검사부터 세운 순서, **대상별 가상 프로퍼티로 가는 설계**, 빌드에 없는 대상을 판정하지 않는 근거|
|[참조가 가리킬 수 있는 키](spec/reference-key-types.md)|인덱스는 `int` 말고도 되는데 **참조만 `int32`에 묶여 있던 것** — 그 가정이 고정되어 있던 자리들과, 대상 해석 뒤에 셀 타입을 정하는 순서|
|[레코드 안의 참조](spec/references-in-records.md)|레코드 그룹의 멤버가 다른 테이블을 가리킬 때 — **거절이 「아직」이었던 자리**, 레코드 그룹의 세 가지 모양, 그리고 어려운 픽스처가 드러낸 결함 5개|
|[참조의 「없음」](spec/reference-optionality.md)|참조 컬럼의 빈 칸 — **없음은 명시적으로, 그리고 허용된 자리에서만**. 거부를 값 파서에서 검증으로 옮겨 메시지가 고치는 법을 말하게 한 기록|
|[옵셔널 필드](spec/optional-fields.md)|타입 끝 `?`의 설계 — 빈 칸을 받는 것과, 존재 여부를 와이어에 담는 것|
|[비트셋](spec/bitset.md)|`bitset` 타입과 진법 리터럴 — **엄격함이 의도 선언을 요구하는 이유**, 그리고 형식을 건드리지 않고 64비트를 싣는 방법|
|[가변 길이 레코드 배열](spec/variable-length-record-arrays.md)|레코드 배열의 길이를 로우마다 다르게 — **뒤에서만 자르는 이유**와 와이어에서 길이를 멤버마다 반복하는 이유|
|[**다른 규칙으로 쓰인 시트 읽기**](samples/rescue/doc/적용-기록.md)|`rescue` 레이아웃의 규칙과, 실제 프로젝트에 적용한 기록|
|[named-range 레이아웃 분석](samples/named-range/doc/레이아웃-분석-20260808.md)|라이브 서비스 중인 프로젝트의 레이아웃 조사. **우리와 겹치는 것과, 우리에게 없는 것**|
|[**검증**](doc/validation.md)|시트에 적을 수 없는 규칙을 C#으로 — 테이블별·전역·런타임, 그리고 **셀 위치가 나오는 보고**|
|[CLI](doc/cli.md)|빌드하고 실행하는 법, 명령줄 옵션|
|[Recipe 파일](doc/recipe.md)|무엇을 어디서 읽어 어디로 내보낼지 적는 파일|
|[내보내기](doc/exports.md)|바이너리·JSON 파일과 MySQL / PostgreSQL / MongoDB / Redis 적재. **바이너리를 쓰는 이유**|
|[바이너리 형식](doc/binary-format.md)|`.tcb` 파일의 레이아웃과 **스키마가 바뀌었을 때의 보장** — 컬럼 태그, 컬럼 인코딩, 타입 승격, 배포 전 검사. **프로토버프 와이어 포맷에서 가져온 것과 바꾼 것**|
|[**왜 컬럼 지향인가**](spec/tcb-column-oriented-rationale.md)|형식이 지금의 모양인 이유 — 무엇을 얻고 무엇을 포기했는지, **어떤 상황에 맞고 어떤 상황에 틀린지**, Parquet·Arrow·FlatBuffers와의 차이|
|[비트폭 패킹 (v105)](spec/tcb-v105-bit-width-packing.md)|정수 컬럼을 그 범위가 요구하는 폭으로 — **`bool`의 RAW만 정보량의 8배인 이유**와, 설계 결정 셋을 계측이 뒤집은 기록|
|[벤치마크](doc/benchmark.md)|실제 게임 데이터 67개 테이블 109,218행을 JSON·compact JSON·바이너리로 실었을 때의 **크기·로드 시간·CPU·메모리 실측**|
|[워크북 읽기](spec/streaming-workbook-reader.md)|엑셀을 객체 모델이 아니라 스트리밍으로 읽는 설계 — 후보 다섯의 실측, **값이 같다는 것을 무엇으로 판정하는가**, 그리고 마이크로소프트 SDK로 직접 쓴 리더가 어디서 틀렸는지|
|[**언어별 가이드**](doc/languages/readme.md)|생성된 코드를 프로젝트에 넣고 쓰는 법. 언어마다 준비물·주의사항·트러블슈팅이 다릅니다|
|[**트러블슈팅**](doc/troubleshooting.md)|변환이 실패했을 때 어디를 볼 것인가. 도구가 실제로 출력하는 메시지별로|
|[Summary와 히스토리](doc/history.md)|누가 언제 무엇을 바꿨는지 셀 단위로 추적하고 브라우저로 확인하기|
|[아키텍처와 개발](doc/architecture.md)|내부 구조, 패키징 주의점, 이 저장소에서 개발·테스트하는 법|
|[앞으로 할 것](doc/roadmap.md)|하려는 것과, 하지 않기로 한 것과 그 이유|

---

## Features

- 엑셀과 구글 스프레드시트를 둘 다 씁니다. 팀이 편한 쪽으로 고르면 되고, 한 프로젝트에서 섞어 써도 결국 하나로 합쳐집니다.
- 변환하면서 걸러낼 수 있는 실수는 최대한 걸러냅니다. 게임에서 문제가 생긴 뒤에 찾는 대신 변환할 때 알게 됩니다.
- 테이블끼리 참조할 수 있습니다. 같은 값을 여러 시트에 베껴 적지 않아도 됩니다.
- **데이터만 따로 패치**할 수 있습니다. 익스포트 결과를 CDN에 올려두면 생성된 업데이터가 바뀐 파일만 받아 최신으로 유지합니다 — 해시로 검증하고, 일시적인 장애는 재시도하고, 실패하면 이전 데이터를 그대로 둡니다. (C#·유니티·언리얼. `WriteUpdater` 옵션)
- 여러 언어로 뽑을 수 있습니다. C#(**.NET과 유니티 모두**), TypeScript, C++, C, Go, Rust, Python, Java, Kotlin, Ruby, PHP, Dart 코드와 언리얼 모듈을 생성합니다.
- 실제로 로드된 데이터를 HTML로 펼쳐 볼 수 있습니다. 값이 제대로 들어갔는지 눈으로 확인하고 넘어갈 수 있습니다.
- 파일(바이너리·JSON)로 내보내는 것 말고, MySQL / PostgreSQL / MongoDB / Redis에 바로 적재할 수도 있습니다.
- 서버와 클라이언트 중 한쪽에만 필요한 테이블과 컬럼은 그쪽 빌드에만 넣을 수 있습니다. (`TargetSide`)
- **누가 언제 무엇을 바꿨는지 셀 단위로 남고**, 웹 브라우저에서 볼 수 있습니다. (`--serve`)
- 히스토리가 커밋마다 **데이터 패치와 코드 배포 중 무엇이 나가야 하는지 판정**합니다. 상수만 고쳐 데이터를 올리는 헛배포, enum 값이 밀린 채 코드만 나가는 사고를 배포 전에 보고합니다.
- 문제가 생기면 어느 셀인지 보고합니다. 구글 시트라면 링크를 눌러 그 자리로 바로 갑니다.
- 시트의 문제를 한 번에 모아서 보고합니다. 하나 고치고 다시 돌리기를 반복하지 않아도 됩니다.
- **변환이 중간에 실패해도 이전 결과는 그대로 남습니다.** 파일은 스테이징 영역에 모았다가 마지막에 한꺼번에 옮기고, 데이터베이스는 섀도 테이블에 채운 뒤 통째로 바꿉니다.
- **읽는 쪽도 마찬가지입니다.** 이미 로드된 테이블을 다시 읽어도(데이터 패치·핫 리로드) 전부 읽고 참조까지 연결한 다음에 한 번에 교체합니다. 중간에 실패하면 **이전 데이터가 그대로 남고** 이유를 알려줍니다 — 빈 테이블이나 반쯤 채워진 테이블로 남는 일이 없습니다.

> 다만 **저장소 하나 단위**입니다. 파일 여러 개와 데이터베이스 여러 개를 한 트랜잭션으로 묶는 건 분산 트랜잭션 없이는 안 되므로, 각각이 따로 안전하게 바뀌도록 만들어져 있습니다.

---

## 시작하기

### 설치

[릴리즈](https://github.com/maxidea1024/Tabbit/releases)에서 내려받아 압축을 풀면 끝입니다. **.NET을 설치하지 않아도 됩니다** — 런타임이 실행 파일 안에 들어 있습니다.

|플랫폼|파일|
|--|--|
|Linux|`tabbit-<버전>-linux-x64.tar.gz` · `linux-arm64`|
|Windows|`tabbit-<버전>-win-x64.zip` · `win-arm64`|
|macOS|`tabbit-<버전>-osx-x64.tar.gz` · `osx-arm64` (애플 실리콘)|

터미널에서 받는 쪽이 편하면 아래를 그대로 붙여넣으세요. `VERSION`만 원하는 버전으로 바꾸면 됩니다.

**Linux · macOS**

```bash
VERSION=0.1.0
RID=linux-x64            # linux-arm64 · osx-x64 · osx-arm64 중 하나

curl -fsSL "https://github.com/maxidea1024/Tabbit/releases/download/v$VERSION/tabbit-$VERSION-$RID.tar.gz" \
  | tar -xz -C /usr/local/bin tabbit

tabbit --help
```

> `/usr/local/bin`에 권한이 없으면 `sudo`를 붙이거나, `-C ~/.local/bin`처럼 쓰기 가능한 곳으로 바꾸세요.
>
> macOS는 서명되지 않은 바이너리를 격리합니다. 한 번만 풀어주면 됩니다 —
> `xattr -d com.apple.quarantine /usr/local/bin/tabbit`

**Windows (PowerShell)**

```powershell
$Version = '0.1.0'
$Rid     = 'win-x64'      # 또는 win-arm64
$Dest    = "$env:LOCALAPPDATA\Programs\tabbit"

New-Item -ItemType Directory -Force $Dest | Out-Null
Invoke-WebRequest "https://github.com/maxidea1024/Tabbit/releases/download/v$Version/tabbit-$Version-$Rid.zip" -OutFile "$env:TEMP\tabbit.zip"
Expand-Archive "$env:TEMP\tabbit.zip" -DestinationPath $Dest -Force

# 이번 세션에서만. 계속 쓰려면 시스템 환경변수 PATH에 $Dest를 추가하세요.
$env:PATH = "$Dest;$env:PATH"
tabbit --help
```

**최신 버전을 자동으로** 집으려면 (`jq` 필요)

```bash
VERSION=$(curl -fsSL https://api.github.com/repos/maxidea1024/Tabbit/releases/latest | jq -r .tag_name)
VERSION=${VERSION#v}
```

**받은 파일 확인.** 릴리즈마다 `SHA256SUMS`가 함께 올라갑니다.

```bash
curl -fsSLO "https://github.com/maxidea1024/Tabbit/releases/download/v$VERSION/SHA256SUMS"
sha256sum -c SHA256SUMS --ignore-missing
```

<details>
<summary>소스에서 빌드하기</summary>

`.NET 10 SDK`가 필요합니다. 버전은 저장소 루트의 `global.json`에 고정되어 있습니다.

```
dotnet build Tabbit.slnx -c Release
```

</details>

### 실행

무엇을 어디서 읽어 어디로 내보낼지는 recipe 파일에 적습니다.

```
tabbit --new-recipe my-recipe.json --template unity   # 상황에 맞는 시작점
tabbit --recipe my-recipe.json                        # 변환
```

`--template`은 **그 상황에 필요한 설정만, 각각 왜 있는지 주석을 달아** 내놓습니다. 처음부터 백지로 시작하지 않아도 됩니다.

|템플릿|무엇을 위한 것|
|--|--|
|`unity`|유니티 클라이언트 — StreamingAssets + C#|
|`client-server`|같은 시트에서 클라이언트와 서버 두 벌|
|`web`|구글 스프레드시트 → TypeScript + JSON|
|`server`|게임 서버 — 데이터베이스 적재 + C++|
|`unreal`|언리얼 모듈|
|`ci`|변경 이력을 남기는 CI 변환|

`--template`을 생략하면 **모든 설정이 기본값으로 채워진** 파일이 나옵니다 — 무엇을 쓸 수 있는지 훑어볼 때.

자세한 것은 [CLI](doc/cli.md)와 [Recipe 파일](doc/recipe.md)을 보세요.

### 생성된 코드 쓰기

접근자 이름은 recipe의 `AccessorName`으로 정해집니다. 언어마다 준비물과 주의사항이 다르므로 [언어별 가이드](doc/languages/readme.md)에 각각 정리해 두었습니다.

```csharp
// C# — 정적입니다
await GameData.ReadAllAsync("./data");
var sword = GameData.Item.FindByIndex(1);
```

```typescript
// TypeScript
const tables = new Tables()
tables.readAllSync('./data')
const sword = tables.item.findByIndex(1)
```

```python
# Python
tables = Tables()
tables.read_all("./data")
sword = tables.item.find_by_index(1)
```

**참조는 로드 후 자동으로 연결됩니다.** `foreign` 필드는 파일에 인덱스로 저장되고, `readAll`이 모든 테이블을 읽은 뒤 실제 레코드 참조로 바꿔줍니다. (Rust만 예외 — [이유](doc/languages/rust.md#주의사항))

---

## 생성되는 산출물

|종류|타깃|
|--|--|
|익스포트|`binary` `json`|
|데이터베이스|`mysql` `postgresql` `mongodb` `redis`|
|코드 생성|`csharp` `typescript` `cpp` `c` `unreal` `go` `rust` `python` `java` `kotlin` `ruby` `php` `dart`|
|문서|`html`|
|기록|`summary` `history`|

**따로 설치할 것이 없습니다.** 바이너리를 읽는 코드까지 출력 폴더에 같이 나오므로, 플러그인을 깔거나 include 경로를 잡을 일이 없습니다. Go는 `go.mod`, Rust는 `Cargo.toml`, 언리얼은 `Build.cs`까지 함께 나옵니다.

**타입 하나에 파일 하나입니다.** 시트에서 테이블을 지우면 그 파일도 없어집니다 — 헤더에 `Generated by Tabbit`이 적힌 파일 중 이번 실행이 쓰지 않은 것을 지우는 식입니다. 생성된 파일을 손으로 고쳐 쓰고 있다면 `"Sweep": false`로 꺼두세요.

---

## 검증

|게이트|하는 일|
|--|--|
|적합성 코퍼스|경계값 테이블 하나를 **13개 언어로 각각 컴파일·실행해서 읽고** 익스포터 JSON과 대조합니다|
|예약어 컴파일|키워드 이름 필드를 **12개 언어로 컴파일**합니다|
|헤더 단독 컴파일|C·C++ 헤더를 하나씩, 그 헤더만 include한 상태로 컴파일해 봅니다|
|C 헤더의 C++ 호환|`extern "C"`로 약속해놓고 못 쓰는 일이 없도록, C 헤더를 C++로도 컴파일합니다|
|Unreal|**실제 UnrealHeaderTool**에 통과시키고, 생성된 업데이터를 **실제 엔진의 UnrealBuildTool로 빌드·실행**합니다 (`TABBIT_UE_ROOT` 지정 시). 엔진 없이도 코퍼스를 읽어 익스포터와 대조합니다|
|생성 코드의 C# 수준|C# 리더를 `netstandard2.1`로 컴파일해, 유니티 2020.3이 받아들이는 C# 8을 넘지 않는지 확인합니다|
|골든 트리|워크북 변환 후 전 산출물 바이트 비교, 타임스탬프만 정규화|
|데이터베이스|`docker compose`로 네 엔진을 띄우고 적재한 뒤 서버에 직접 질의|
|웹서버|실제 포트에 띄우고 **API 응답과 CLI 출력을 바이트 단위로 비교**|
|self-contained 퍼블리시|CI가 매 실행마다 linux-x64로 퍼블리시하고, 그 산출물로 실제 변환을 돌립니다|

테이블 리더는 언어마다 별도 구현이라 어긋날 수 있습니다. 포맷을 정의하는 건 익스포터의 writer 하나이고, 13개의 테이블 리더는 그 하나를 각자 구현한 것입니다 — 그래서 회귀 스위트가 **익스포터로 쓰고 13개 언어로 각각 읽어 대조**합니다. 실제로 이 방식이 `long`을 32비트로 잘라내던 writer 버그를 찾아냈습니다.

---

## 기여하기

버그와 제안은 [이슈](https://github.com/maxidea1024/Tabbit/issues)로 올려 주세요. 무엇을 어떻게 했을 때 그렇게 되는지가 있으면 가장 빠릅니다.

- 개발·테스트하는 법은 [아키텍처와 개발](doc/architecture.md)에 있습니다.
- 생성기나 템플릿을 건드렸다면 골든을 다시 기록하고 diff를 리뷰해 주세요. 방법은 같은 문서에 있습니다.
- 보안 문제는 공개 이슈 대신 [SECURITY.md](SECURITY.md)의 절차를 따라 주세요.

변경 내역은 [CHANGELOG.md](CHANGELOG.md)에 있습니다.

---

## References

- [Google.Apis.Sheets](https://github.com/googleapis/google-api-dotnet-client)
- [NPOI](https://github.com/nissl-lab/npoi)
- [Serilog](https://serilog.net/)
- [CommandLineParser](https://github.com/commandlineparser/commandline)
- [Netonsoft.Json](https://www.newtonsoft.com/json)

---

## 라이선스

[MIT](LICENSE).

생성된 코드와 함께 나오는 테이블 리더·업데이터도 같은 라이선스입니다. **생성물에 이 저장소의 라이선스를 표시할 의무는 없습니다** — 시트에서 나온 코드와 데이터는 그것을 만든 프로젝트의 것입니다.
