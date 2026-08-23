# Rust

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/
  Cargo.toml              WriteCargoToml이 true일 때
  src/lib.rs              모듈 트리와 재수출
  src/tables.rs                     접근자
  src/<table>_table.rs              테이블당 하나
  src/enum_<enum>.rs                enum당 하나
  src/<set>.rs                      상수 세트당 하나 (모듈 이름이 곧 경로)
  src/tabbit/tcb_reader.rs  바이너리 리더 (함께 생성됩니다)
  src/tabbit/updater.rs           데이터 갱신 (WriteUpdater를 켰을 때만)
  src/tabbit/mod.rs               위 둘을 묶는 모듈 파일

타입 파일이 `tables/`·`enums/`·`constants/`로 나뉘지 않는 이유는 언어입니다 — Rust의 디렉터리는 모듈 경로이고, 접근자 모듈이 이미 `tables`라서 `src/tables/`와 부딪힙니다. Go·Python·Java도 같은 이유로 평평합니다.
```

`lib.rs`가 `mod`를 선언하고 `pub use`로 전부 재수출하므로, 소비자가 쓰는 경로는 타입이 어느 파일에 있는지와 무관합니다 — `gamedata::ItemRecord`입니다.

## 필요한 것

|항목|값|
|--|--|
|Rust|edition 2021 (생성되는 `Cargo.toml`이 선언합니다)|
|크레이트 의존성|**기본값에서는 없음.** 테이블 리더는 core와 std만 씁니다 — 레지스트리 접근 없이 빌드됩니다. `WriteUpdater`를 켜면 `ureq` 하나가 붙습니다 (아래)|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "rust",
    "Path": "crates/gamedata",
    "CrateName": "gamedata",
    "WriteCargoToml": true,   // 이미 크레이트 안에 넣는다면 false
    "Edition": "2021",
    "WriteUpdater": false,    // CDN에서 데이터를 갱신할 거라면 true
    "UreqVersion": "2",       // 그럴 때 Cargo.toml에 적힐 요구 버전
    "BinaryTableFileExtension": ".tcb",
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

## 프로젝트에 넣기

워크스페이스 멤버로 추가하거나, 경로 의존성으로 가리키세요.

```toml
[dependencies]
gamedata = { path = "crates/gamedata" }
```

이미 있는 크레이트 안에 포함시키려면 `"WriteCargoToml": false`로 두고 `Path`를 그 크레이트의 루트로 지정하세요 — 생성물이 `src/` 아래로 들어갑니다.

## 쓰는 법

```rust
use gamedata::Tables;
use std::path::Path;

let mut tables = Tables::default();
tables.read_all(Path::new("./data"))?;

if let Some(sword) = tables.item.find_by_index(1) {
    println!("{}", sword.name);

    // 참조는 인덱스입니다. 직접 찾으세요.
    if let Some(category) = tables.item_category.find_by_index(sword.category_id_index) {
        println!("{}", category.name);
    }
}

for row in tables.item.records() { /* ... */ }
```

기본 인자가 없으므로 확장자는 짝이 되는 메서드입니다.

```rust
tables.read_all_with_extension(Path::new("./data"), ".bytes")?;
```

## 데이터만 갱신하기 (`WriteUpdater`)

recipe에 `"WriteUpdater": true`를 적으면 `src/updater.rs`가 함께 나오고, `lib.rs`가 `pub mod updater;`를 선언하며, **생성되는 `Cargo.toml`에 의존성 한 줄이 추가됩니다.**

```toml
[dependencies]
ureq = "2"
```

**그 한 줄이 전부입니다.** Rust 표준 라이브러리에는 HTTP 클라이언트가 없어서 전송만 크레이트에 맡기고, 매니페스트 JSON 파서와 MD5는 `updater.rs` 안에 직접 썼습니다 — `serde`와 `md-5`까지 소비자 빌드에 끌어들이는 것보다 문법 하나를 적는 편이 싸기 때문입니다. 끄면 생성물은 다시 의존성 0개가 되고 레지스트리 없이 빌드됩니다.

### 그 한 줄을 어떻게 넣는가

생성되는 `Cargo.toml`을 그대로 쓴다면 **할 일이 없습니다** — 위의 줄이 이미 그 안에 있습니다. 이미 있는 크레이트에 소스만 넣는 경우에만 손으로 적습니다.

|상황|하는 일|
|--|--|
|생성물을 그대로 크레이트로 씀|없습니다. `cargo build`가 알아서 받습니다|
|기존 크레이트에 소스만 넣음|그 크레이트의 `Cargo.toml` `[dependencies]`에 `ureq = "2"`를 추가합니다|
|워크스페이스|버전을 한곳에서 관리한다면 워크스페이스 루트의 `[workspace.dependencies]`에 두고, 크레이트에서는 `ureq = { workspace = true }`|
|버전을 바꾸고 싶다|recipe의 `"UreqVersion"`을 적으세요. 소비자의 락파일은 소비자의 것이므로 이 값은 recipe 설정입니다|

확인은 `cargo build`이고, 처음 한 번은 레지스트리에 접근합니다.

|증상|원인과 조치|
|--|--|
|`error[E0432]: unresolved import 'ureq'`|`[dependencies]`에 그 줄이 없습니다|
|`error: no matching package named 'ureq' found`|오프라인이거나 레지스트리에 닿지 못합니다. 사내 미러를 쓰면 `.cargo/config.toml`의 `[source]`로 바꿉니다|
|받는 것 자체를 원하지 않는다|`"WriteUpdater": false`. 데이터 갱신을 쓰지 않는 프로젝트에는 이 의존이 없습니다|

```rust
use gamedata::updater;

let options = updater::UpdateOptions::default();
let mut log = |message: &str| println!("{}", message);

let result = updater::update(
    "https://cdn.example.com/data",
    Path::new("./data"),
    &options,
    &mut log,
);

if result.succeeded {
    tables.read_all(&result.local_path)?;
} else {
    // 이전 데이터가 그대로 있습니다. 그것으로 계속해도 됩니다.
    eprintln!("{}", result.error.unwrap());
}
```

`Result`를 돌려주지 않습니다.

네트워크, 디스크, 손상된 파일은 모두 호출한 쪽이 다뤄야 할 상황이지 결함이 아니기 때문입니다.

원하는 답은 「안 됐고, 이유는 이것이고, 이전 데이터는 그대로 있다」이지 match해야 할 에러가
아닙니다.

받은 파일은 전부 매니페스트의 MD5와 대조하고, `.staging`을 거쳐 마지막에 한 번에 옮깁니다.

## 주의사항

**참조는 인덱스로 남습니다. Rust만 그렇습니다.** 레코드가 서로를 참조하면 그래프가 되는데 Rust는 한 레코드가 이웃을 소유하는 구조를 허용하지 않습니다. 대안은 생성 타입 전부에 수명을 꿰거나 행마다 참조 카운트 셀을 두는 것인데, 인덱스를 남기고 `find_by_index`를 부르게 하는 편이 읽기 쉽고 호출 한 번이면 됩니다.

필드 이름은 `<name>_index`입니다 (`category_id_index`).

**미사용 import가 없습니다.** 파일마다 그 파일이 쓰는 `use`만 적습니다. 크레이트 전체에 `#![allow(dead_code)]`와 `#![allow(clippy::all)]`이 걸려 있지만 미사용 import는 그 대상이 아닙니다 — 생성물은 경고 없이 빌드됩니다.

**멤버 이름은 snake_case입니다.** Rust 키워드와 부딪히면 뒤에 밑줄이 붙습니다 (`type` → `type_`). raw identifier(`r#type`)를 쓰지 않은 이유는 `crate`·`self`·`super`·`Self`가 raw가 될 수 없어서, 항상 통하는 규칙 하나가 거의 통하는 규칙 둘보다 낫기 때문입니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`unresolved import gamedata::...`|`lib.rs`가 재수출하는 이름인지 확인하세요. 파일 이름이 아니라 타입 이름입니다|
|`no method named category_id`|참조는 인덱스로 남습니다. `category_id_index`와 `find_by_index`를 쓰세요|
|`Cargo.toml`이 덮어써짐|`"WriteCargoToml": false`로 두세요|
|`unused import` 경고|생성물에서 나면 버그입니다|
