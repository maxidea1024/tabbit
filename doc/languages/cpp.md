# C++

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

헤더 온리입니다. 소스 파일이 없습니다.

```
<Path>/
  <AccessorName>.h                  우산 헤더 — 이것만 include하면 전부 들어옵니다
  <AccessorName>_forward.h          레코드 전방선언 (테이블 간 참조용)
  tables/<AccessorName>_<table>.h   테이블당 하나
  enums/<AccessorName>_enum_<enum>.h      enum당 하나
  constants/<AccessorName>_const_<set>.h  상수 세트당 하나
  tabbit/tcb_reader.h     바이너리 리더 (함께 생성됩니다)
  tabbit/updater.h                데이터 갱신 (WriteUpdater를 켰을 때만)
```

## 필요한 것

|항목|값|
|--|--|
|C++|17 이상|
|외부 라이브러리|**없음** — `WriteUpdater`를 켤 때만 libcurl (아래)|
|include 경로|생성 폴더 하나. `lib/cpp`를 추가할 필요가 **없습니다** — 테이블 리더가 함께 생성됩니다|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "cpp",
    "Path": "src/generated",
    "Namespace": "mygame::data",   // 비우면 전역 네임스페이스
    "AccessorName": "GameData",
    "BinaryTableFileExtension": ".tcb",
    "WriteUpdater": false,          // CDN에서 데이터를 갱신할 거라면 true
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

## 쓰는 법

```cpp
#include "GameData.h"

mygame::data::Tables tables;
tables.read_all("./data");

const auto* sword = tables.item().find_by_index(1);
if (sword != nullptr) {
    // 참조는 로드 후 포인터로 연결됩니다.
    std::cout << sword->name << " / " << sword->category_id->name << "\n";
}

for (const auto& row : tables.item().records()) { /* ... */ }
```

확장자는 두 번째 인자입니다.

```cpp
tables.read_all("./data", ".bytes");
```

## 시간 타입 — `std::chrono`

시트의 `datetime`과 `timespan`은 표준 시간 타입으로 나옵니다. 틱 정수를 들고 다니며 직접 환산할 일이 없습니다.

|시트 타입|C++ 타입|
|--|--|
|`timespan`|`tabbit::TimeSpan` = `std::chrono::duration<int64_t, std::ratio<1, 10'000'000>>`|
|`datetime`|`tabbit::DateTime` = `std::chrono::time_point<std::chrono::system_clock, tabbit::TimeSpan>`|

```cpp
const auto* item = data.item().find_by_index(1);

// 원하는 단위로 변환은 chrono가 합니다. 손실이 생기는 변환은 컴파일러가 차단합니다.
auto seconds = std::chrono::duration_cast<std::chrono::seconds>(item->cooldown);

// 표준 라이브러리와 바로 이어집니다.
std::time_t when = std::chrono::system_clock::to_time_t(
    std::chrono::time_point_cast<std::chrono::system_clock::duration>(item->released_at));
```

**기간 단위가 100나노초(.NET 틱)인 이유**는 그것이 파일에 실린 단위라 아무것도 잃지 않기 때문입니다. `std::chrono::nanoseconds`로 두면 `TimeSpan`의 최대값(9.2e18틱)이 64비트를 넘칩니다.

**에폭은 유닉스 에폭입니다.** 파일은 .NET 기준(0001-01-01)으로 실려 오고, 테이블 리더가 읽는 순간 한 번 옮깁니다 — C++의 모든 시계와 C 라이브러리가 합의한 기준이 그쪽이기 때문입니다. .NET 쪽과 틱으로 이야기해야 한다면 `tabbit::to_net_ticks(value)`와 `tabbit::from_net_ticks(ticks)`가 있습니다.

## 데이터만 갱신하기 (`WriteUpdater`)

recipe에 `"WriteUpdater": true`를 적으면 `tabbit/updater.h`가 함께 나옵니다. CDN이나 버킷에 올려둔 데이터를 받아 로컬 사본을 최신으로 유지하는 코드이고, **배포를 다시 하지 않고 데이터만 패치**하기 위한 것입니다.

**여기서만 외부 라이브러리가 붙습니다 — libcurl.** C++에는 HTTP 클라이언트가 없고 대신 쓸 만한 표준 수단도 없습니다. 그것 하나뿐입니다: 매니페스트 JSON 파서와 MD5는 그 파일 안에 직접 써 두었으므로, 켜는 값은 **링크 플래그 한 줄**입니다.

```
c++ ... -lcurl
```

끄면 생성물은 지금처럼 표준 라이브러리만으로 빌드됩니다. 기본값이 `false`인 이유가 그것입니다.

```cpp
#include "tabbit/updater.h"

tabbit::UpdateOptions options;
options.log = [](const std::string& message) { std::cout << message << "\n"; };

const auto result = tabbit::update("https://cdn.example.com/data", "./data", options);

if (result.succeeded) {
    tables.read_all(result.local_path.string());
} else {
    // 이전 데이터가 그대로 있습니다. 그것으로 계속해도 됩니다.
    std::cerr << result.error << "\n";
}
```

예외를 throw하지 않습니다. 네트워크·디스크·손상된 파일은 모두 호출한 쪽이 다뤄야 할 상황이지 결함이 아니기 때문입니다. 실패하면 이유가 결과에 들어있고, **디스크의 이전 데이터는 손대지 않은 상태**입니다. 받은 파일은 전부 매니페스트의 MD5와 대조하고, `.staging`을 거쳐 마지막에 한 번에 옮깁니다.

## 주의사항

**테이블 헤더는 서로를 include하지 않습니다.** 두 테이블이 서로를 참조하는 것은 시트에서 흔하고, 그러면 include 순환이 됩니다. 포인터 멤버는 불완전 타입만 있으면 되므로 모든 레코드는 `<AccessorName>_forward.h`에 전방선언되어 있고 테이블 헤더는 그것을 include합니다.

**enum은 다릅니다.** enum으로 선언된 필드는 포인터가 아니라 값이므로 완전 타입이 필요하고, 그 헤더는 실제 include입니다.

**헤더 하나하나가 단독으로 컴파일됩니다.** 우산 헤더를 거치지 않고 `<AccessorName>_item.h`만 include해도 됩니다 — 회귀 스위트가 모든 헤더를 번역 단위의 유일한 include로 컴파일해서 확인합니다.

**멤버 이름은 snake_case입니다.** C++ 키워드와 부딪히면 `tb_` 접두사가 붙습니다 (`class` → `tb_class`).

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`tcb_reader.h`를 찾을 수 없음|생성 폴더가 include 경로에 있는지 확인하세요. 테이블 리더는 그 아래 `tabbit/`에 함께 생성됩니다|
|`incomplete type` 오류|우산 헤더 대신 테이블 헤더만 include하고 다른 테이블의 레코드를 **역참조**했습니다. 전방선언은 포인터까지만 허용합니다 — 그 테이블의 헤더도 include하세요|
|참조가 `nullptr`|테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다 (0은 "참조 없음")|
|`std::` 관련 링크 오류|헤더 온리라 링크할 것이 없습니다. 다른 문제입니다|
