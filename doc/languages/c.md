# C

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/
  <AccessorName>.h                       우산 헤더 — 이것만 include하면 전부 들어옵니다
  <AccessorName>.c                       접근자 구현 (LoadAll, Free, 참조 연결)
  <AccessorName>_Forward.h               레코드 전방선언 (테이블 간 참조용)
  <AccessorName>_Reader.c                테이블 리더 구현을 담는 번역 단위 하나
  tables/<AccessorName>_<Table>.h / .c   테이블당 하나씩
  enums/<AccessorName>_Enum<Enum>.h      enum당 하나
  constants/<AccessorName>_Const<Set>.h / .c  상수 세트당. `.c`는 헤더가 담을 수 없는 값이 있을 때만
  tabbit/tabbit_tcb_reader.h 바이너리 리더 (함께 생성됩니다)
  tabbit/tabbit_updater.h            데이터 갱신 (WriteUpdater를 켰을 때만)
  <AccessorName>_Updater.c               그 구현을 담는 번역 단위 (같은 조건)
```

## 필요한 것

|항목|값|
|--|--|
|C|C99 이상|
|외부 라이브러리|**없음.** 표준 라이브러리만 — `WriteUpdater`를 켤 때만 libcurl (아래)|
|빌드|생성된 `.c`를 **전부** 빌드에 넣으세요|

헤더는 `extern "C"`로 감싸여 있어 C++에서도 include할 수 있습니다. 회귀 스위트가 C++
컴파일러로도 컴파일해서 확인합니다.

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "c",
    "Path": "src/generated",
    "AccessorName": "GameData",     // 모든 타입·함수 이름의 접두사가 됩니다
    "BinaryTableFileExtension": ".tcb",
    "WriteUpdater": false,                 // CDN에서 데이터를 갱신할 거라면 true
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

C에는 네임스페이스가 없으므로 `AccessorName`이 충돌 회피의 전부입니다. 타입은
`GameData_ItemRecord_t`, 함수는 `GameData_ItemLoad`처럼 나옵니다.

## 쓰는 법

```c
#include "GameData.h"

/* = {0}가 중요합니다. LoadAll이 이전 로드를 해제하고 새 로드를 갈아끼우므로,
   첫 호출에서는 들고 있는 것이 없어야 합니다. */
GameData_t data = {0};
char error[512];

if (!GameData_LoadAll(&data, "./data", error, sizeof error)) {
    fprintf(stderr, "load failed: %s\n", error);
    return 1;
}

const GameData_ItemRecord_t* sword = GameData_ItemFindByIndex(&data.item, 1);
if (sword != NULL) {
    /* 참조는 로드 후 포인터로 연결됩니다. */
    printf("%s / %s\n", sword->name, sword->category_id->name);
}

int32_t row;
for (row = 0; row < data.item.count; ++row) {
    const GameData_ItemRecord_t* r = &data.item.records[row];
    /* ... */
}

GameData_Free(&data);
```

확장자가 다르면 짝이 되는 함수를 씁니다. C에는 기본 인자가 없습니다.

```c
GameData_LoadAllWithExtension(&data, "./data", ".bytes", error, sizeof error);
```

## 데이터만 갱신하기 (`WriteUpdater`)

recipe에 `"WriteUpdater": true`를 적으면 `tabbit/tabbit_updater.h`와 그 구현을 담는
`<AccessorName>_Updater.c`가 함께 나옵니다.

CDN이나 버킷에 올려둔 데이터를 받아 로컬 사본을 최신으로 유지하는 코드입니다.
배포를 다시 하지 않고 데이터만 패치하기 위한 것입니다.

**여기서만 외부 라이브러리가 붙습니다 — libcurl.** C에는 HTTP 클라이언트가 없고 대신 쓸 만한
표준 수단도 없습니다. 그것 하나뿐입니다: 매니페스트 JSON 파서와 MD5는 그 파일 안에 직접 써
두었으므로, 켜는 값은 **링크 플래그 한 줄**입니다.

```
cc ... -lcurl
```

끄면 생성물은 지금처럼 표준 라이브러리만으로 빌드됩니다. 기본값이 `false`인 이유가 그것입니다.

```c
#include "GameData.h"
#include "tabbit/tabbit_updater.h"

tb_update_options options;
tb_update_result result;

tb_update_options_init(&options);

if (tb_update("https://cdn.example.com/data", "./data", &options, &result)) {
    char error[TABBIT_ERROR_MAX];

    GameData_LoadAll(&data, "./data", error, sizeof error);
} else {
    /* 이전 데이터가 그대로 있습니다. 그것으로 계속해도 됩니다. */
    fprintf(stderr, "%s\n", result.error);
}
```

예외를 throw하지 않습니다. 네트워크·디스크·손상된 파일은 모두 호출한 쪽이 다뤄야 할 상황이지
결함이 아니기 때문입니다. 실패하면 이유가 결과에 들어있고, **디스크의 이전 데이터는 손대지 않은
상태**입니다. 받은 파일은 전부 매니페스트의 MD5와 대조하고, `.staging`을 거쳐 마지막에 한 번에
옮깁니다.

## 주의사항

**다시 로드하는 것이 안전합니다.** 같은 구조체에 `LoadAll`을 다시 불러도 됩니다 — 데이터 패치를
받아 갈아끼우는 흐름이 그것입니다. 모든 파일을 **옆에** 읽고 마지막에 교체하므로, 실패하면
`data`는 손대지 않은 상태이고 들고 있던 레코드 포인터도 그대로 유효합니다. 성공하면 그 지점에서
이전 아레나가 해제되므로, **교체 이후에는 옛 포인터를 쓰지 마세요.**

**메모리는 테이블이 소유합니다.** 테이블마다 아레나가 하나이고, 레코드의 문자열과 배열은 전부
그 안을 가리킵니다. `GameData_Free` 한 번으로 전부 해제되고, 어떤 레코드의 포인터도 그보다 오래
살지 않습니다. 개별 `free`를 부르지 마세요.

**배열은 포인터와 개수입니다.** 길이가 로우마다 다른 배열이든 모든 로우가 같은 배열이든 멤버가
둘로 나옵니다 — `const char** tag_array`와 `int32_t tag_array_count`. 고정 길이 배열을 구조체
안에 박아 두면 그 크기는 코드를 생성한 시점의 시트가 가졌던 것이 되고, C는 구조체 크기를 데이터에서
정할 수 없으므로 선택은 그 숫자와 포인터 중 하나입니다. 한 로우가 원소를 몇 개 갖는지는 파일이
적은 것이므로 포인터입니다
([설계](../../spec/types/nullable-array-elements.md#12-딸린-정리--생성-코드의-고정-길이)).

```c
int32_t element;

for (element = 0; element < r->tag_array_count; ++element)
    printf("%s\n", r->tag_array[element]);
```

파일이 그 컬럼을 담고 있지 않으면 포인터는 `NULL`이고 개수는 `0`입니다 — 위 루프가 그대로 도는
빈 배열이고, 개수를 보지 않고 인덱싱하는 코드만 문제가 됩니다. 레코드 그룹(`Slot1.Id`)의 배열은
컬럼 여럿이 하나를 채우므로 예외이고, 그 길이는 생성된 코드의 일부이므로 고정 배열로 나옵니다.

**조회 함수가 둘입니다.** 인덱싱된 필드마다 `<Accessor>_<Table>FindBy<Field>`와
`<Accessor>_<Table>Contains<Field>`가 나옵니다. 다른 언어들이 내는 `GetBy<Field>OrThrow`는 C에
throw할 것이 없어서 없고, 없으면 안 되는 키는 `NULL` 검사로 확인합니다. 키 타입은 그 컴럼의
타입이고(`int32_t`, `const char*`, `tb_uuid` 등), 맵이 없는 언어라 정렬된 배열과
이분탐색입니다.

**던지지 않습니다.** 실패는 `false` 반환과 `error` 버퍼입니다. 실패한 로드는 자기가 할당했던
것을 해제하고 테이블을 비워두므로, 반환값을 무시해도 절반만 든 데이터가 아니라 빈 테이블을 보게
됩니다.

**`_Reader.c`를 빼지 마세요.** 테이블 리더는 헤더 하나에 선언과 구현이 함께 있고, 구현은 정확히
한 번역 단위에서만 켜져야 합니다. 그 일만 하는 파일이 `<AccessorName>_Reader.c`입니다.

**테이블 헤더는 서로를 include하지 않습니다.** 두 테이블이 서로를 참조하면 순환이 되기
때문입니다. 포인터 멤버에는 불완전 타입이면 충분하므로 모든 레코드가 `_Forward.h`에 한 번
전방선언되어 있습니다. C99에서 같은 `typedef`를 두 번 적는 것은 제약 위반이라, 각 헤더가 따로
적지 않고 한 곳에 모았습니다.

**이름 규칙.** 타입은 `Prefix_NameRecord_t`, 함수는 `Prefix_NameVerb`, 멤버는 snake_case,
상수는 SCREAMING_SNAKE입니다. Doom·Quake 계열의 관례에 네임스페이스 대용의 접두사를 붙인
형태입니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`tb_read_int32` 등이 정의되지 않음 (링크 오류)|`<AccessorName>_Reader.c`를 빌드에 넣지 않았습니다|
|`tb_read_*`가 두 번 정의됨|`TABBIT_TCB_IMPLEMENTATION`을 다른 곳에서 또 정의했습니다. 그 일은 `_Reader.c`만 합니다|
|`incomplete type` 오류|테이블 헤더만 include하고 다른 테이블의 레코드를 역참조했습니다. 그 테이블의 헤더도 include하세요|
|C++에서 컴파일 오류|헤더는 C++로도 컴파일되도록 검사됩니다. 재현되면 버그입니다|
|해제 후 문자열이 깨짐|레코드의 포인터는 테이블 아레나를 가리킵니다. `Free` 뒤에는 유효하지 않습니다|
