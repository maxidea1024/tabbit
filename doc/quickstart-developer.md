# 빠른 시작 — 개발자

엑셀 하나를 게임 코드가 읽는 데까지 **10분**.

> [문서 목록으로](readme.md) · [기획자용 빠른 시작](quickstart-designer.md)

---

## 1. 도구를 손에 넣습니다

[릴리즈](https://github.com/maxidea1024/tabbit/releases)에서 자기 플랫폼의 아카이브를 받아
풀면 끝입니다. .NET 런타임을 따로 설치하지 않아도 됩니다.

저장소를 이미 받아 두었다면 소스에서 빌드해도 됩니다 — `.NET 10 SDK`가 필요합니다.

```bash
dotnet build Tabbit.slnx -c Release
```

내려받는 명령과 받은 파일 확인은 [설치](install.md)에 있습니다.

## 2. recipe 를 만듭니다

백지에서 시작하지 마십시오. 상황에 맞는 것을 골라 받습니다.

```bash
tabbit --new-recipe my-recipe.jsonc --template unity
```

|`--template`|무엇을 위한 것|
|--|--|
|`unity`|유니티 클라이언트 — `.bytes` + C# + HTML 문서|
|`unreal`|언리얼 — 바이너리 + 모듈 하나|
|`web`|브라우저 — 구글 스프레드시트 → JSON + TypeScript|
|`server`|게임 서버 — 바이너리 + MySQL 적재 + C++|
|`client-server`|같은 시트에서 두 벌 — `TargetSide` 로 가릅니다|

**설정마다 무엇을 위한 것이고 언제 바꾸는지 주석이 붙어 나옵니다.** 경로가 빈 항목은 꺼진
것으로 취급되므로, 받자마자 실행해도 아무것도 만들지 않고 정상 종료합니다.

고칠 곳은 대개 둘입니다 — 시트가 어디 있는지, 결과를 어디에 놓을지.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },
  "Targets": [
    { "Type": "binary", "Path": "./Assets/StreamingAssets/data", "FileExtension": ".bytes" },
    { "Type": "csharp", "Path": "./Assets/Scripts/Data", "Namespace": "MyGame.Data",
      "AccessorName": "GameData" }
  ]
}
```

## 3. 돌립니다

```bash
tabbit --recipe my-recipe.jsonc
```

바이너리와 C# 코드가 나옵니다. **두 번째 실행부터는 바뀐 것이 없으면 아무것도 하지
않습니다** ([빌드 캐시](../spec/ops/build-cache.md)).

## 4. 읽습니다

```csharp
await GameData.ReadAllAsync(Application.streamingAssetsPath);

var sword = GameData.Item.FindByIndex(1);

// 참조는 로드가 끝난 뒤 실제 레코드로 연결되어 있습니다 — 다시 조회하지 않습니다.
Debug.Log($"{sword.Name} / {sword.ItemCategoryByCategoryId.Name}");

foreach (var row in GameData.Item.Records)
    Debug.Log(row.Name);
```

**설치할 패키지가 없습니다.** 리더도 함께 생성되므로 생성 폴더를 프로젝트에 넣으면 끝입니다.

## 그 다음에 알면 좋은 것

|무엇|어디|
|--|--|
|컬럼에 무슨 타입을 적을 수 있나|[적을 수 있는 타입](sheets/types.md)|
|서버와 클라이언트에 다른 것을 주고 싶다|[Target Side](sheets/rules-and-pitfalls.md#target-side-서버클라-분리)|
|시트에 적을 수 없는 규칙을 검사하고 싶다|[정적 검증](validation.md)|
|다른 언어로 내고 싶다|[언어별 가이드](languages/readme.md)|
|왜 바이너리인가|[바이너리를 쓰는 이유](exports/binary.md)|
|안 될 때|[트러블슈팅](troubleshooting.md)|

## 자주 걸리는 곳 셋

**셀의 앞뒤 공백은 지워집니다.** 공백으로 줄을 맞추는 방식은 동작하지 않습니다.

**컬럼을 지웠다가 그 자리에 다른 것을 넣으면 안 됩니다.** 와이어 태그가 재사용되면 구
클라이언트가 조용히 다른 값을 읽습니다. 지운 컬럼은 `#이름@N` 으로 자리를 예약해 두십시오
([Tombstone](sheets/naming.md#와이어-태그-n)).

**참조 컬럼을 비워 두는 것은 「없음」이 아닙니다.** 없음은 `-` 이고, 그 컬럼이 옵셔널일 때만
통과합니다 ([참조 컬럼의 빈 칸](sheets/shapes.md#참조-컬럼의-빈-칸)).
