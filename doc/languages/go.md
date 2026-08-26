# Go

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)

---

## 생성되는 것

```
<Path>/
  go.mod                          모듈 선언 (WriteGoMod가 true일 때)
  <AccessorName>.go               접근자 — 테이블 필드, ReadAll, 참조 연결
  <table>_table.go                테이블당 하나
  enum_<enum>.go                  enum당 하나
  const_<set>.go                  상수 세트당 하나
  tabbit/tcb_reader.go  바이너리 리더 (함께 생성됩니다)
```

파일은 폴더 하나에 평평하게 놓입니다. Go에서는 디렉터리가 곧 패키지라 하위 폴더는 다른 패키지가
되고, 그러면 생성된 타입끼리 서로를 import해야 하기 때문입니다. 이름이 그 구분을 대신합니다.

## 필요한 것

|항목|값|
|--|--|
|Go|1.21 이상 (생성되는 `go.mod`가 선언합니다). CI는 1.23으로 검증|
|외부 모듈|**없음**|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "go",
    "Path": "internal/gamedata",
    "PackageName": "gamedata",
    "ModulePath": "gamedata",      // go.mod의 module 줄
    "WriteGoMod": true,            // 이미 모듈 안에 넣는다면 false
    "GoVersion": "1.21",
    "AccessorName": "Tables",         // 기본값. 파일은 tables.go, 타입은 Tables
    "BinaryTableFileExtension": ".tcb",
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

## 프로젝트에 넣기

**기존 모듈 안에 두는 경우** — `"WriteGoMod": false`로 두고 생성 폴더를 패키지로 쓰면 됩니다.

**독립 모듈로 두는 경우** — `go.mod`가 함께 생성되므로, 부모 프로젝트의 `go.mod`에 `replace`를
걸어 가리키세요.

```
require gamedata v0.0.0
replace gamedata => ./internal/gamedata
```

## 쓰는 법

```go
import "gamedata"

tables := &gamedata.Tables{}
if err := tables.ReadAll("./data"); err != nil {
    log.Fatal(err)
}

if sword := tables.Item.FindByIndex(1); sword != nil {
    // 참조는 로드 후 실제 레코드로 연결됩니다.
    fmt.Println(sword.Name, sword.ItemCategoryByCategoryId.Name)
}

for _, row := range tables.Item.Records() {
    _ = row
}
```

Go에는 기본 인자가 없으므로 확장자는 짝이 되는 메서드입니다.

```go
err := tables.ReadAllWithExtension("./data", ".bytes")
```

## 주의사항

**import는 파일별로 정확합니다.** Go에서 쓰지 않는 import는 경고가 아니라 **오류**라, 생성기가
파일마다 그 파일이 실제로 쓰는 것만 적습니다. 다른 언어들은 같은 목록을 모든 파일에 줘도 되지만
Go는 안 됩니다.

**생성된 타입끼리는 import가 없습니다.** 전부 한 패키지이므로 테이블 파일이 다른 테이블의
레코드 타입을 그냥 씁니다.

**내보내기 규칙.** Go는 첫 글자를 대문자로 해서 내보내므로 멤버는 PascalCase입니다. Go 키워드는
전부 소문자라 충돌하지 않습니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`imported and not used`|생성물에서 나면 버그입니다. 하네스나 직접 쓴 코드 쪽을 먼저 보세요|
|`package gamedata is not in GOROOT`|`replace`를 걸지 않았거나 모듈 경로가 `ModulePath`와 다릅니다|
|`go.mod`가 두 개|생성 폴더가 이미 모듈 안인데 `WriteGoMod`가 `true`입니다. `false`로 두세요|
|참조가 `nil`|`ReadAll` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
