# Unreal Engine

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

프로젝트가 그대로 추가할 수 있는 **모듈 하나**입니다.

```
<Path>/<ModuleName>/
  <ModuleName>.Build.cs               모듈 정의 (WriteBuildFile이 true일 때)
  Public/<AccessorName>.h             USTRUCT 행, UENUM, 정적 접근자, 블루프린트 라이브러리
  Public/TabbitTcbReader.h   바이너리 리더 (함께 생성됩니다)
  Private/<AccessorName>.cpp          구현
```

## 필요한 것

|항목|값|
|--|--|
|Unreal|**4.x ~ 5.x**. UE 4.27.2의 실제 UnrealHeaderTool로 검증|
|모듈 의존성|`Core`, `CoreUObject`, `Engine` — 생성되는 `Build.cs`가 선언합니다|
|플러그인|**없음**|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "unreal",
    "Path": "Source",
    "ModuleName": "GameData",          // 폴더 이름이자 모듈 이름
    "AccessorName": "FGameData",       // 정적 접근자 클래스
    "WriteBuildFile": true,
    "BinaryTableFileExtension": ".tcb",
    "Sweep": true,
    "TargetSide": "c"
  }
]
```

## 프로젝트에 넣기

1. `Path`를 프로젝트의 `Source`로 두면 모듈이 `Source/<ModuleName>/`에 생성됩니다.
2. `.uproject`의 `Modules`에 항목을 추가합니다.

```json
{ "Name": "GameData", "Type": "Runtime", "LoadingPhase": "Default" }
```

3. 프로젝트 파일을 다시 생성하고 빌드합니다.

## 쓰는 법

**접근자는 정적입니다.**

```cpp
#include "FGameData.h"

if (!FGameData::ReadAll(FPaths::ProjectContentDir() / TEXT("Data")))
{
    // 실패 이유는 이미 로그에 나가 있습니다.
    return;
}

const FItemRow* Sword = FGameData::Item().FindByIndex(1);
if (Sword != nullptr)
{
    UE_LOG(LogTemp, Log, TEXT("%s"), *Sword->Name);
}

for (const FItemRow& Row : FGameData::Item().Records()) { /* ... */ }
```

확장자는 기본 인자입니다.

```cpp
FGameData::ReadAll(BasePath, TEXT(".bytes"));
```

### 블루프린트에서

함수 라이브러리가 함께 생성됩니다. 이름은 `AccessorName` 앞의 `F`를 떼고 `U...Library`를 붙인 것입니다 — `FGameData`면 `UGameDataLibrary`. 언리얼에서 접두사는 타입이 무엇인지 나타내는 것이라, `U`와 `F`를 둘 다 달고 있는 이름이 나오지 않게 합니다.

|노드|하는 일|
|--|--|
|Load All Tabbit Tables|`ReadAll`|
|Get \<Table\> Row|인덱스로 행 하나 (`bFound` 출력 포함)|
|Get \<Table\> Row Count|행 수|
|Get \<Table\> Row At|위치로 행 하나|

행을 값으로 돌려주는 이유는 블루프린트가 구조체를 값으로 받기 때문입니다. 배열이나 참조를 돌려주는 시그니처는 UHT가 거부합니다.

## 데이터만 갱신하기 (`WriteUpdater`)

recipe의 언리얼 타깃에 `"WriteUpdater": true`를 적으면 모듈에 `TabbitUpdater.h/.cpp`가 함께 나오고, 생성되는 `Build.cs`에 **`HTTP` 모듈 의존성이 추가**됩니다. CDN에 올려둔 데이터를 받아 로컬 사본을 최신으로 유지하는 코드이고, **패키징을 새로 하지 않고 데이터만 패치**하기 위한 것입니다. 기본값이 `false`인 이유가 그 의존성입니다 — 데이터를 .pak에 넣어 배포한다면 필요가 없습니다.

```cpp
FTabbitUpdateOptions Options;
Options.MaxAttempts = 3;                 // 첫 시도 포함
Options.RetryDelaySeconds = 0.5f;        // 재시도마다 두 배

FTabbitUpdater::Update(
    TEXT("https://cdn.example.com/data"),
    FString(),                           // 비우면 ProjectPersistentDownloadDir 아래
    Options,
    FTabbitUpdateComplete::CreateLambda([](const FTabbitUpdateResult& Result)
    {
        if (!Result.bSucceeded)
        {
            // 이전 데이터는 그대로 있습니다. 그걸로 계속 가도 됩니다.
            UE_LOG(LogTemp, Warning, TEXT("데이터 갱신 실패: %s"), *Result.Error);
        }

        // 실패했더라도 이전 데이터가 남아 있으므로 읽을 것은 있습니다.
        FGameData::ReadAll(Result.LocalPath, TEXT(".tcb"));
    }));
```

**비동기입니다.** 언리얼의 HTTP가 `callback` 방식이므로 델리게이트로 끝을 알립니다. 업데이터는 자기 자신을 살려두므로 반환된 핸들을 붙들고 있지 않아도 되고, 델리게이트는 게임 스레드에서 한 번만 불립니다.

**무엇을 보장하나** — C#과 같습니다: 바뀐 파일만 받고, 매니페스트의 MD5로 검증하고, `.staging`을 거쳐 마지막에 옮기고, 로컬 매니페스트를 그보다 더 나중에 씁니다. 중간에 실패하거나 앱이 죽어도 **이전 데이터가 그대로** 남습니다. 일시적 장애(연결 실패·408·429·5xx)는 두 배씩 늘어나는 간격으로 재시도하고, 404는 재시도하지 않습니다. 표는 [C# 가이드](csharp.md#데이터만-갱신하기-writeupdater)에 있습니다.

**재시도 대기는 `FTicker`(UE5는 `FTSTicker`)로 합니다.** 게임 스레드를 재우지 않습니다.

> 이 코드는 **실제 엔진의 UnrealBuildTool로 빌드·실행하는 게이트**가 있습니다(`TABBIT_UE_ROOT` 지정 시). 스텁으로 컴파일해보는 것과 다릅니다 — 첫 실행에서 `ENGINE_MAJOR_VERSION`이 Program 타깃에 정의되어 있지 않다는 것을 검출했고, 그건 스텁으로는 영원히 검출되지 않았을 종류입니다.

## 주의사항

### 패키징 — 빌드 포함 여부

`.tcb`는 애셋이 아니므로 언리얼이 그냥 무시합니다. **Project Settings → Packaging → "Additional Non-Asset Directories to Package"** 에 데이터 폴더를 반드시 등록하세요.

등록하면 `.pak`에 들어가고, 생성 코드가 쓰는 `FFileHelper`는 `IPlatformFile`을 거치므로 **pak 안을 로컬 파일처럼 읽습니다** (안드로이드 `.obb`도 동일). 등록하지 않으면 에디터에서는 되고 패키징한 빌드에서만 파일이 없습니다 — 생성된 로더가 그 설정 이름을 로그에 그대로 적으니 메시지를 보면 바로 알 수 있습니다.

### 예외 미사용

언리얼은 `Build.cs`가 따로 요청하지 않으면 모듈을 **예외 비활성**으로 빌드합니다. 그래서 테이블 리더는 손상된 파일을 예외가 아니라 `false` 반환으로 알립니다. 실패는 누적되는 플래그라, 레코드의 필드 20개를 연달아 읽고 마지막에 한 번만 확인합니다.

`bEnableExceptions = true`를 넣지 않은 것도 의도입니다 — 넣으면 이 모듈에 의존하는 모든 모듈이 그 비용을 냅니다.

같은 이유로 **조회 함수가 둘입니다** — 인덱싱된 필드마다 `FindBy<Field>`와 `Contains<Field>`만 나오고, 다른 언어들이 내는 `GetBy<Field>OrThrow`는 없습니다. 없으면 안 되는 키는 `nullptr` 검사로 확인하세요. 자세한 것은 [조회 함수](readme.md#레코드-조회)에 있습니다.

### 엔진 타입 전용

`FString`, `TArray`, `FGuid`, `FDateTime`, `FTimespan`입니다. `std::string`도 자체 uuid 구조체도 없습니다. `FDateTime`과 `FTimespan`은 .NET과 같은 100나노초 틱을 세므로 변환이 없습니다.

### uint8을 넘는 enum

`UENUM(BlueprintType)`은 uint8이어야 하므로 레이블 값이 0~255를 벗어나면 그 enum은 **int32로 넓어지고 블루프린트 노출을 포기합니다.** 여전히 `UENUM`이라 리플렉션과 직렬화는 되고, C++에서는 그대로 읽힙니다. 그 enum으로 선언된 필드도 `UPROPERTY`를 잃습니다 — 블루프린트가 볼 수 없는 타입의 프로퍼티를 UHT가 노출하지 않기 때문입니다.

변환이 실패하지는 않고, 어느 레이블 때문인지 경고합니다.

### double 프로퍼티

UE4의 UHT는 `double` `UPROPERTY`를 거부하고 UE5는 받습니다. 두 버전에서 모두 빌드되도록 `double` 멤버에는 `UPROPERTY`를 붙이지 않습니다 — C++에서는 읽히고 블루프린트에서만 안 보입니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|패키징한 빌드에서만 "could not read"|"Additional Non-Asset Directories to Package"에 데이터 폴더를 등록하세요. 로그 메시지가 그 이름을 적고 있습니다|
|`Couldn't find parent type ... UBlueprintFunctionLibrary`|`Build.cs`의 `PublicDependencyModuleNames`에 `Engine`이 있는지 확인하세요. 생성물은 넣습니다|
|블루프린트에서 enum이 안 보임|레이블 값이 255를 넘었습니다 (위 「uint8을 넘는 enum」). 변환 로그에 어느 레이블인지 나옵니다|
|블루프린트에서 double 필드가 안 보임|UE4 호환을 위해 `UPROPERTY`가 없습니다. C++에서는 읽힙니다|
|`.generated.h` 관련 오류|생성된 헤더에서 `.generated.h` include는 **반드시 마지막**이어야 하고, 생성물은 그렇게 씁니다. 손으로 옮기지 마세요|
|참조가 `nullptr`|`ReadAll` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
