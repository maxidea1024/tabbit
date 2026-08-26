# C# / Unity

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/
  <AccessorName>.cs             접근자 — 테이블 프로퍼티, ReadAllAsync, 참조 연결
  tabbit/TabbitBinaryReader.cs  바이너리 리더 (함께 생성됩니다)
  tabbit/TabbitHelpers.cs       예외 타입과 보조 함수
  tabbit/TabbitUnityAdapter.cs  유니티에서만 컴파일되는 읽기 경로
  tabbit/TabbitUpdater.cs       데이터 갱신 (WriteUpdater를 켰을 때만)
  tables/<Table>Table.cs        테이블당 하나
  enums/<Enum>.cs               enum당 하나
  constants/<Set>.cs            상수 세트당 하나
```

## 필요한 것

|항목|값|
|--|--|
|C# 언어 버전|9.0 이상|
|.NET|`netstandard2.1` 이상 (또는 .NET Core 3.0+, .NET 5+)|
|Unity|**6.0 이상 (Unity 6).** 그 미만은 지원하지 않습니다|
|외부 패키지|**없음.** UniTask도, Newtonsoft도 필요 없습니다|

**유니티에서 설정할 것이 없습니다.** 생성된 코드가 유니티 내장 정의(`UNITY_5_3_OR_NEWER`,
`UNITY_WEBGL`)로 스스로 판별합니다. 별도의 심볼을 프로젝트에 추가할 필요가 없습니다.

**유니티를 아는 파일은 `tabbit/TabbitUnityAdapter.cs` 하나뿐입니다.** 유니티 밖에서는 몸통이
통째로 심볼 뒤에 있어 아무것도 컴파일되지 않고, 유니티 안에서는 첫 씬이 뜨기 전에 스스로
설치됩니다 — Android의 APK 안이나 WebGL의 HTTP처럼 File API로 읽을 수 없는 경로를
`UnityWebRequest`로 읽는 것이 그 내용입니다. 접근자를 비롯한 나머지 파일은 엔진을 모릅니다.

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "csharp",
    "Path": "Assets/Scripts/Generated",
    "Namespace": "MyGame.Data",       // 비우면 전역 네임스페이스
    "AccessorName": "GameData",       // 기본값 Tables. 타입과 파일 이름이 모두 이것입니다
    "Output": "source",               // "assembly" 로 두면 .dll 하나로 나옵니다
    "BinaryTableFileExtension": ".bytes",
    "WriteUpdater": false,            // CDN에서 데이터를 갱신할 거라면 true
    "Sweep": true,
    "TargetSide": "c"
  }
]
```

## 프로젝트에 넣기

생성 폴더를 그대로 프로젝트에 두면 끝입니다. 유니티라면 `Assets/` 아래 아무 곳이나 됩니다.

### 소스 대신 어셈블리로 받기

`"Output": "assembly"`로 두면 `.cs` 파일들 대신 **`.dll` 하나**가 나옵니다. 체크아웃해서 쓰는
사람에게 생성 소스 100여 개는 모든 diff와 모든 검색에 끼는 노이즈인데, 그것이 없어집니다.

```
<Path>/
  <AssemblyName>.dll                생성 코드 전부 — 접근자·테이블·enum·리더
  <AssemblyName>.xml                요약 문서
  tabbit/TabbitUnityAdapter.cs  유니티가 직접 컴파일해야 하는 한 장
```

|성질|내용|
|--|--|
|**배타입니다**|소스와 dll 중 하나입니다. 코드를 읽는 것은 IDE의 디컴파일러가 하고, **심볼이 어셈블리 안에 들어 있어** 단계 실행도 그대로 되므로 둘 다 둘 이유가 없습니다|
|`AssemblyName`|비우면 `Namespace`, 그것도 비우면 접근자 이름을 씁니다|
|**유니티 어댑터는 소스로 남습니다**|`UnityEngine`을 참조하는데 그것은 엔진의 컴파일러만 해석합니다. `WriteUpdater`를 켰다면 업데이터도 같은 이유로 소스입니다|
|컴파일 대상|`netstandard2.1`입니다 — 유니티 6과 일반 .NET이 모두 받는 표면입니다|
|**결정적입니다**|같은 데이터에서 같은 바이트가 나옵니다. 저장소에 커밋해 두어도 아무것도 바뀌지 않은 실행이 변경으로 보이지 않습니다|

## 쓰는 법

**정적으로 쓰는 것이 기본입니다.** 대부분의 프로젝트는 데이터를 한 벌만 두므로 이걸로 끝입니다.

```csharp
using MyGame.Data;

await GameData.ReadAllAsync(Application.streamingAssetsPath);

var sword = GameData.Item.FindByIndex(1);
if (sword != null)
{
    // 참조는 로드 후 실제 레코드로 연결되어 있습니다.
    Debug.Log($"{sword.Name} / {sword.ItemCategoryByCategoryId.Name}");
}

foreach (var row in GameData.Item.Records)
    Debug.Log(row.Name);
```

패키징 과정에서 확장자가 바뀌었다면 두 번째 인자로 넘깁니다.

```csharp
await GameData.ReadAllAsync(Application.streamingAssetsPath, ".bytes");
```

### 데이터를 여러 벌 두기

`ReadAllAsync`는 두 가지를 이어서 합니다 — **읽어서 연결하고**, 그것을 **정적 멤버가 보는
자리에 올립니다.** 둘은 따로 부를 수도 있습니다.

```csharp
// 읽고 연결하기만 합니다. 올리지 않으므로 다른 코드가 보던 데이터는 그대로입니다.
GameData.Snapshot next = await GameData.LoadAsync(newPath);

// 준비가 되었을 때 올립니다.
GameData.Publish(next);
```

|쓰는 곳|어떻게|
|--|--|
|테스트가 자기 데이터를 씀|`LoadAsync`로 받아 그 인스턴스만 봅니다. 전역 상태를 건드리지 않으므로 병렬로 돌아도 서로 간섭하지 않습니다|
|서버가 두 버전을 동시에 엶|인스턴스를 둘 들고 각각 조회합니다|
|핫 리로드|다음 세대를 `LoadAsync`로 읽는 동안 현재 세대는 계속 읽힙니다. 다 읽은 뒤 `Publish`|

`GameData.Current`가 지금 올라가 있는 인스턴스이고, `GameData.Item` 같은 정적 프로퍼티는 그것을
가리킵니다. 한 번도 읽지 않았으면 `Current`는 `null`이라, 로드 전에 테이블을 만지면 그 자리에서
드러납니다.

> 인스턴스 안에서도 **참조 연결은 그 인스턴스 안에서만** 일어납니다. 두 벌을 동시에 들고 있어도
> 한쪽의 행이 다른 쪽의 행을 가리키는 일은 없습니다.

### 파일을 어디서 읽을지 바꾸기

`ReadAllBytesAsync`가 교체 가능한 델리게이트입니다. 팩 파일, CDN, Addressables 등에서 읽으려면
`ReadAllAsync`를 부르기 **전에** 자기 것을 넣으세요.

```csharp
GameData.ReadAllBytesAsync = async filename =>
{
    var handle = Addressables.LoadAssetAsync<TextAsset>(filename);
    var asset = await handle.Task;
    return asset.bytes;
};

await GameData.ReadAllAsync("");
```

## 데이터만 갱신하기 (`WriteUpdater`)

recipe에 `"WriteUpdater": true`를 적으면 `TabbitUpdater.cs`가 함께 나옵니다.

CDN이나 버킷에 올려둔 데이터를 받아 로컬 사본을 최신으로 유지하는 코드입니다.
빌드를 새로 내보내지 않고 데이터만 패치하기 위한 것입니다.

기본값이 `false`인 이유는 네트워크를 쓰기 때문이고, 데이터를 빌드 안에 넣어 배포한다면 필요가
없기 때문입니다.

익스포터가 데이터 옆에 이미 쓰고 있는 **매니페스트**(`manifest-binary.json` — 파일별 크기와
MD5)가 전부입니다. 서버에는 익스포트 결과를 그대로 올리면 되고, 따로 준비할 것이 없습니다.

```csharp
var result = await TabbitUpdater.UpdateAsync("https://cdn.example.com/data");

if (!result.Succeeded)
{
    // 이전 데이터는 그대로 있습니다. 그걸로 계속 가도 됩니다.
    Debug.LogWarning($"데이터 갱신 실패: {result.Error}");
}

await GameData.ReadAllAsync(result.LocalPath);
```

업데이터는 **읽지 않습니다.** 디렉터리를 만들어 그 경로를 돌려주고, 로드는 접근자가 합니다.
둘이 서로를 모르는 편이 낫고, 받은 데이터의 스키마가 이 빌드와 달라도
[바이너리 형식](../binary-format.md)의 태그 덕에 안전하게 읽힙니다.

**무엇을 보장하나.**

|상황|결과|
|--|--|
|바뀐 것이 없음|요청 한 번(매니페스트)으로 끝. `UpToDate == true`|
|일부 파일만 바뀜|바뀐 파일만 받습니다|
|서버에서 사라진 테이블|로컬 캐시에서도 지웁니다|
|받은 파일이 손상됨|매니페스트의 MD5와 대조해 **거부**하고, 캐시는 손대지 않습니다|
|중간에 실패·강제 종료|**이전 데이터가 그대로** 남습니다. 파일은 `.staging`을 거쳐 마지막에 옮겨지고, 로컬 매니페스트는 그보다 더 나중에 쓰입니다|
|일시적 네트워크 장애|재시도합니다 — 연결 실패·408·429·5xx. 대기 시간은 두 배씩 늘어납니다|
|404|재시도하지 않습니다. 서버가 답을 한 것이고, 세 번 더 물어도 같은 답입니다|

**설정할 수 있는 것.**

```csharp
var options = new TabbitUpdateOptions
{
    ManifestFileName = "manifest-binary.json",  // JSON 익스포트라면 manifest-json.json
    MaxAttempts = 3,                            // 첫 시도 포함
    RetryDelay = TimeSpan.FromMilliseconds(500),// 재시도마다 두 배
    RequestTimeout = TimeSpan.FromSeconds(30),
    VerifyHash = true,
    Log = Debug.Log,
};

var result = await TabbitUpdater.UpdateAsync(baseUrl, cacheDirectory: null, options, cancellationToken);
```

캐시 위치를 지정하지 않으면 유니티에서는 `Application.persistentDataPath/tabbit-data`, 그
외에서는 실행 파일 옆입니다.

**예외를 throw하지 않습니다.** 네트워크·디스크·손상된 파일은 전부 호출자가 다뤄야 하는 상황이지
결함이 아니고, 게임 루프 안으로 예외를 throw하는 패처는 이유를 삼키는 try/catch로 감싸이게
됩니다. 실패는 `result.Error`에 문장으로 옵니다.

> 언리얼에도 같은 것이 있습니다 — [언리얼 가이드](unreal.md#데이터만-갱신하기-writeupdater).

## 주의사항

**유니티 배포 경로.** 어댑터가 컴파일 대상 플랫폼에 맞게 고릅니다. StreamingAssets은 어느
플랫폼에서나 배포되지만, 두 곳에서는 경로가 아니라 URL입니다.

|플랫폼|`Application.streamingAssetsPath`|어댑터가 하는 일|
|--|--|--|
|Android|`jar:file:///.../base.apk!/assets` (APK 안)|`UnityWebRequest`|
|WebGL|웹서버 URL|`UnityWebRequest`|
|그 외|실제 경로|`File.ReadAllBytesAsync`|

둘 다 `"://"`를 포함하므로 한 번의 검사로 갈립니다. `persistentDataPath`는 어디서나 실제 경로라
파일 API로 갑니다.

**WebGL에는 스레드가 없습니다.** WebGL 빌드에서는 `File.ReadAllBytes`를 동기로 부릅니다 —
`Task.Run`이 동작하지 않기 때문입니다. 에디터에서는 그렇지 않으므로
`UNITY_WEBGL && !UNITY_EDITOR`로 갈립니다.

> 어댑터를 지우면 유니티에서도 접근자의 기본 구현(`File.ReadAllBytesAsync`)이 그대로 쓰이므로,
> Android와 WebGL에서 파일을 읽지 못합니다. 직접 만든 읽기 구현으로 갈아끼웠다면 지워도 되지만,
> 그 구현이 위 두 경우를 처리해야 합니다.

**확장자.** 유니티는 `.bytes`인 파일만 TextAsset으로 포함합니다. `Resources/`나 Addressables로
배포한다면 recipe에서 `"BinaryTableFileExtension": ".bytes"`로 두세요. StreamingAssets은
확장자를 가리지 않으므로 `.tcb` 그대로도 됩니다.

## 데이터의 배포 위치

배포 방식이 셋이고, 갈리는 지점은 하나입니다 — **`ReadAllBytesAsync`를 그대로 두는가, 갈아끼우는가.**

|배포|`ReadAllAsync`에 넘기는 것|`ReadAllBytesAsync`|
|--|--|--|
|StreamingAssets|`Application.streamingAssetsPath`|**그대로.** 어댑터가 처리합니다|
|CDN|`https://cdn.example.com/data`|**그대로.** 어댑터가 처리합니다|
|Addressables|`""` (주소로 읽으므로 경로가 없습니다)|**갈아끼웁니다**|

### CDN — 교체 불필요

어댑터는 경로에 `://`가 있으면 `UnityWebRequest`로 가므로, **베이스 경로를 URL로 주면 그것으로 끝입니다.**

```csharp
await Tables.ReadAllAsync("https://cdn.example.com/data", ".bytes");
```

다만 이것은 매번 받습니다. 캐시도, 재시도도, 버전 확인도 없습니다.

그것들이 필요하면 recipe에서 `"WriteUpdater": true`로 두세요.
`tabbit/TabbitUpdater.cs`가 함께 생성되고, `persistentDataPath` 아래에 캐시하며 일시적 실패만
재시도합니다.

그 경우 읽는 곳은 CDN이 아니라 캐시 폴더가 됩니다.

### Addressables — 델리게이트 교체

Addressables는 경로가 아니라 **주소로** 읽으므로 파일 경로를 만들 수 없습니다. 읽기 자체를 바꿉니다.

```csharp
// 첫 ReadAllAsync보다 먼저 한 번만. 어댑터는 BeforeSceneLoad에 설치되므로
// 그 뒤에 대입하면 이쪽이 남습니다.
Tables.ReadAllBytesAsync = async filename =>
{
    // filename은 ReadAllAsync가 만든 "<basePath>/<테이블>.bytes"입니다.
    // basePath를 ""로 주면 "Item.bytes"처럼 파일 이름만 남으므로, 그것을 주소로 씁니다.
    var handle = Addressables.LoadAssetAsync<TextAsset>(filename);
    var asset = await handle.Task;

    if (handle.Status != AsyncOperationStatus.Succeeded)
        throw new TabbitException($"Cannot load '{filename}' from Addressables.");

    byte[] bytes = asset.bytes;
    Addressables.Release(handle);

    return bytes;
};

await Tables.ReadAllAsync("", ".bytes");
```

**두 가지를 함께 맞춰야 합니다.** recipe의 `BinaryTableFileExtension`을 `.bytes`로 두어야
유니티가 TextAsset으로 포함하고, 어드레서블 주소를 그 이름과 같게 잡아야 위 코드가 그대로
성립합니다.

> **갈아끼우면 어댑터가 하던 일도 함께 사라집니다.** 위 코드는 Addressables가 플랫폼을 알아서
> 처리하므로 문제가 없지만, 직접 만든 구현으로 바꾸는 경우에는 Android의 APK 안과 WebGL의 URL을
> 스스로 처리해야 합니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|안드로이드에서만 "파일 없음"|StreamingAssets이 APK 안이라 파일 API로 못 읽습니다. 기본 구현은 처리하지만, `ReadAllBytesAsync`를 직접 교체했다면 URL 경로를 함께 처리해야 합니다|
|WebGL에서 멈춤|`ReadAllBytesAsync`를 교체하면서 스레드를 쓰는 코드를 넣었는지 확인하세요|
|참조가 `null`|테이블 하나만 읽었기 때문입니다. 참조 연결은 `ReadAllAsync`가 전부 읽은 뒤에 일어납니다|
|삭제한 테이블의 클래스가 남아 있음|`"Sweep": false`로 꺼두었을 때만 그렇습니다. 켜져 있으면 생성 파일 중 이번에 쓰지 않은 것은 지워집니다|
|`TextAsset`으로 잡히지 않음|확장자를 `.bytes`로 바꾸세요 (위 「확장자」)|
