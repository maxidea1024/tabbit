# 예제

> [「Recipe 파일」로 돌아가기](../recipe.md)

---

상황별로 하나씩. 그대로 두고 경로만 바꾸면 됩니다.

### 1. 가장 작은 것 — 엑셀 하나에서 C#으로

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    { "Type": "binary", "Path": "./generated/data" },
    { "Type": "csharp", "Path": "./generated/cs", "Namespace": "MyGame.Data", "AccessorName": "GameData" }
  ]
}
```

`sheets/`의 워크북을 읽어 `generated/data/<테이블>.tcb`와 `generated/cs/`의 C# 코드를 냅니다.

### 2. 유니티 클라이언트

확장자가 `.bytes`인 것에 주의하세요 — 유니티는 그것만 TextAsset으로 포함합니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    {
      "Type": "binary",
      // StreamingAssets는 모든 플랫폼에 배포됩니다.
      "Path": "./Assets/StreamingAssets/Data",
      "FileExtension": ".bytes",
      "TargetSide": "c"
    },
    {
      "Type": "csharp",
      "Path": "./Assets/Scripts/Generated",
      "Namespace": "MyGame.Data",
      "AccessorName": "GameData",
      "BinaryTableFileExtension": ".bytes",   // 익스포터와 짝
      "TargetSide": "c"                        // 서버 전용 데이터는 클라 빌드에 넣지 않습니다
    }
  ]
}
```

### 3. 서버와 클라이언트를 함께

같은 시트에서 두 벌을 뽑습니다. **`TargetSide`가 익스포터와 코드 생성 양쪽에서 맞아야** 합니다
— 어긋나면 컬럼 집합이 달라져 테이블 리더가 데이터와 맞지 않습니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    { "Type": "binary", "Path": "./build/client/data", "FileExtension": ".bytes", "TargetSide": "c" },
    { "Type": "binary", "Path": "./build/server/data", "TargetSide": "s" },
    {
      "Type": "csharp",
      "Path": "./client/Assets/Scripts/Generated",
      "Namespace": "MyGame.Data", "AccessorName": "GameData",
      "BinaryTableFileExtension": ".bytes", "TargetSide": "c"
    },
    {
      "Type": "go",
      "Path": "./server/internal/gamedata",
      "PackageName": "gamedata", "ModulePath": "myserver/internal/gamedata",
      "WriteGoMod": false,                      // 이미 서버 모듈 안입니다
      "TargetSide": "s"
    }
  ]
}
```

### 4. 웹 — 구글 스프레드시트에서 TypeScript로

TypeScript는 JSON과 바이너리 양쪽을 읽으므로 둘 다 내보냅니다.

```jsonc
{
  "Sources": {
    "GoogleSheets": [
      {
        // 커밋하지 마세요.
        "ClientSecretFilename": "./secrets/googlesheets-client-secret.json",
        "SheetsId": "10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU"
      }
    ]
  },

  "Targets": [
    { "Type": "json", "Path": "./public/data", "Indented": false },
    { "Type": "binary", "Path": "./public/data" },
    { "Type": "typescript", "Path": "./src/generated", "AccessorName": "Tables" },
    { "Type": "html", "Path": "./docs/data" }
  ]
}
```

### 5. 게임 서버 — 데이터베이스로 직접

비밀번호는 recipe에 적지 않습니다. `${NAME}`이 환경 변수를 채우고, 변수가 없으면 오류입니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    {
      "Type": "mysql",
      "ConnectionString": "Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}",
      "NamePrefix": "tb_",     // 한 데이터베이스에 여러 세트를 둘 때
      "TargetSide": "s"
    },
    {
      "Type": "redis",
      "ConnectionString": "${REDIS_HOST}:6379,password=${REDIS_PASSWORD}",
      "TargetSide": "s"
    },
    { "Type": "cpp", "Path": "./src/generated", "Namespace": "game::data",
      "AccessorName": "GameData", "TargetSide": "s" }
  ]
}
```

### 6. 언리얼

모듈이 `Source/GameData/`에 생성됩니다. 데이터를 어디에 두고 패키징에 어떻게 포함시키는지는
[Unreal 가이드](../languages/unreal.md#패키징--빌드-포함-여부)를 보세요.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./Sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    { "Type": "binary", "Path": "./Content/Data", "TargetSide": "c" },
    {
      "Type": "unreal",
      "Path": "./Source",
      "ModuleName": "GameData",
      "AccessorName": "FGameData",
      "TargetSide": "c"
    }
  ]
}
```

### 7. CI — 누가 무엇을 바꿨는지 기록하며

`history`는 변환마다 셀 단위 스냅샷을 남깁니다. `OnFailure`가 `warn`이라, 기록용 데이터베이스가
잠깐 안 되어도 빌드는 계속됩니다.

`SchemaBaseline`은 CI에서 특히 값을 합니다 — 이미 배포된 클라이언트가 못 읽을 스키마 변경이면
**데이터를 쓰기 전에** 빌드가 멈춥니다. 베이스라인 파일은 커밋하세요.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    {
      "Type": "binary",
      "Path": "./build/data",
      "SchemaBaseline": "./schema-baseline.json",
      "AcceptSchemaChanges": []
    },
    { "Type": "summary", "Path": "./build/summary" },
    {
      "Type": "history",
      "ConnectionString": "Server=${HISTORY_HOST};Database=tabbit;Uid=ci;Pwd=${HISTORY_PASSWORD}",
      "ProjectKey": "mygame",
      "OnFailure": "warn"
    }
  ]
}
```

```
tabbit --recipe ci-recipe.json --commit $GITHUB_SHA
```

### 8. 전부 — 지원하는 언어를 한 번에

`test/reserved-words/reserved-words.json`이 저장소에 있고, 실제로 매번 실행되어
[test/reserved-words/](../../test/reserved-words)에 결과가 커밋됩니다. 언어별 출력이 어떻게
생겼는지 나란히 볼 수 있습니다.

```
dotnet run --project src/Tabbit.csproj -- --recipe test/reserved-words/reserved-words.json
```

### 실제로 돌아가는 recipe들

[test/fixtures/recipes/](../../test/fixtures/recipes)에 회귀 스위트가 매번 실행하는 recipe가 서른
개 가까이 있습니다. 문서의 예제와 달리 **반드시 최신**입니다 — 낡으면 테스트가 깨지기
때문입니다.

|파일|내용|
|--|--|
|`core.json`|엑셀 하나에서 바이너리·JSON·C#·C++·HTML까지|
|`core-client.json` / `core-server.json`|`TargetSide`로 나눠 뽑기|
|`conformance.json`|타깃 전부를 한 recipe에|
|`table-extension.json`|`.tcb`가 아닌 확장자로 맞추기|
|`databases.json`|MySQL / PostgreSQL / MongoDB / Redis|
|`history.json`|히스토리 기록|
|`core-dynamic.json`|`Targets` 목록만으로 전부 지정하기|

### 전체 예제 (모든 설정)

<details>
<summary>펼쳐보기</summary>

```json
{
  // 배열 셀의 구분자. 쉼표가 기본이 아닌 이유는 문장과 숫자 표기에 너무 흔하기 때문입니다.
  "ArrayDelimiter": ";",

  // datetime 셀에 적힌 시각을 어느 시간대로 읽을지. 지역 이름 또는 고정 오프셋("+09:00").
  // 비우면 셀이 이미 UTC로 적힌 것으로 봅니다. 저장되는 값은 어느 쪽이든 UTC입니다.
  "TimeZone": "",

  // 0번 라벨이 없는 enum에 `None = 0`을 넣어줍니다.
  // 켜두는 쪽이 기본인 이유: enum 타입의 필드는 값이 대입되기 전에도 뭔가를 들고 있어야 하는데,
  // 그게 이름 없는 0이면 디버거에서도 로그에서도 읽을 수 없기 때문입니다.
  // 시트에 적은 것만 정확히 나오길 원한다면 끄세요.
  "AutoInsertEnumNoneLabel": true,

  "Sources": {
    "Xlsx": [
      {
        "Path": "./sheets",
        "FileExtensionPatterns": ".xls;.xlsx",

        // 시트를 읽는 방식. 기본은 `tabbit` — `:table` 선언 셀로 엔티티를 선언하는 방식입니다.
        // 다른 규칙으로 작성된 시트를 그대로 읽으려면 `sheet-per-table`. 자세한 건 sheets.md 참고.
        "Layout": "tabbit",

        // 읽을 워크북 목록. 비우면 Path 아래 전부. 상대 경로·파일명·확장자를 뗀 이름
        // 중 무엇으로 적어도 되고, 여기 적었는데 없는 워크북은 오류로 알려줍니다.
        "IncludeWorkbooks": [],

        // 제외할 워크북. IncludeWorkbooks 다음에 적용되고, 제외된 워크북은 열지도
        // 않습니다. 보통 쓰게 되는 것은 이쪽입니다.
        "ExcludeWorkbooks": ["백업/*"],

        // 읽을 시트 목록. 비우면 전부. 배열로도, `;`로 이은 문자열로도 쓸 수 있습니다.
        // `*` `?` 와일드카드가 파일 글롭과 같게 동작하고, 여기 적었는데 없는 시트는
        // 말없이 빠지는 대신 오류로 알려줍니다.
        "IncludeSheets": [],

        // 제외할 시트. IncludeSheets 다음에 적용됩니다. `[워크북]시트`로 적으면 그
        // 워크북에만 적용됩니다 — 시트 이름은 워크북마다 겹칩니다.
        "ExcludeSheets": ["*참고용*", "[Items.xlsx]Define"],

        // 인덱스 값이 겹칠 때: `error`(기본) / `keep-first` / `keep-last`.
        // 뒤의 둘은 겹치는 것을 허용하는 레이아웃 전용이며, 버린 행을 로그에 남깁니다.
        "OnDuplicateIndex": "error",

        // `#REF!` 같은 수식 오류 셀: `error`(기본) / `empty`.
        // `empty`는 남이 관리하는 워크북을 위한 것입니다. 삼킨 셀은 하나하나 경고하고
        // 끝에 총계를 냅니다.
        "OnFormulaError": "error",

        // `Text1`/`Text2`를 배열 하나로 접을지. 기본은 끔 — 이름의 숫자가 배열을 뜻하는지는
        // 이름으로는 판정할 수 없는 문제입니다.

        // 레코드 배열에서 값이 없는 뒤쪽 원소를 버릴지. 기본은 끔 — 배열이 짧아지는 것은
        // 아무 말도 하지 않습니다. 가운데는 지우지 않습니다.
        "TrimTrailingArrayElements": false,

        // 이 레이아웃만 아는 설정. 코어는 키를 모르고, 레이아웃이 오타를 보고합니다.
        "LayoutOptions": {}
      }
    ],
    "GoogleSheets": [
      {
        // 이 파일은 커밋하지 마세요. .gitignore에 등록되어 있습니다.
        // 변환을 돌리는 *사람*으로 접속합니다.
        "ClientSecretFilename": "./googlesheets-client-secret.json",

        // CI라면 이쪽입니다 — 잡 자신으로 접속하므로 개인 계정에 종속되지 않습니다.
        // 위의 것과 함께 적으면 거부합니다. 셋 다 비우면 이 항목은 꺼집니다.
        "ServiceAccountKeyFile": "",
        "ServiceAccountKeyVariable": "",

        "SheetsId": "10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU"

        // 위의 소스 항목 공통 설정은 여기서도 같습니다.
      }
    ]
  },

  "Targets": [
    {
      "Type": "binary",
      "Path": "./generated/binary",
      "FileExtension": ".tcb"
    },
    {
      "Type": "json",
      "Path": "./generated/json",
      // true면 이름 없이 값만 배열로 담습니다. 파일이 작아집니다.
      "UseCompactRowFormat": false,
      "Indented": false
    },

    // 데이터베이스 적재. 비밀값은 ${환경변수}로 빼세요.
    {
      "Type": "mysql",
      "ConnectionString": "Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}",
      "NamePrefix": "tb_"
    },
    {
      "Type": "postgresql",
      "ConnectionString": "Host=db;Database=game;Username=tabbit;Password=${DB_PASSWORD}",
      "Schema": "public",
      "NamePrefix": "tb_"
    },
    {
      "Type": "mongodb",
      // 데이터베이스 이름을 반드시 포함해야 합니다.
      "ConnectionString": "mongodb://db:27017/game",
      "NamePrefix": "tb_"
    },
    {
      "Type": "redis",
      "ConnectionString": "db:6379,password=${REDIS_PASSWORD}",
      "Database": 0,
      "NamePrefix": "tb_"
    },
    {
      "Type": "csharp",
      // 출력 타겟 폴더입니다. 없으면 자동으로 만듭니다.
      "Path": "./generated/cs",
      "Namespace": "StaticData",
      "AccessorName": "SheetAccessor"
    },
    {
      "Type": "typescript",
      "Path": "./generated/ts",
      // true면 enum을 숫자 대신 문자열로 생성합니다.
      "UseStringEnum": false
    },
    {
      "Type": "cpp",
      "Path": "./generated/cpp",
      // `.`이나 `::`로 중첩 네임스페이스를 지정할 수 있습니다.
      "Namespace": "game::data",
      "AccessorName": "SheetAccessor"
    },
    {
      "Type": "html",
      "Path": "./generated/html"
    }
  ]
}
```

</details>
