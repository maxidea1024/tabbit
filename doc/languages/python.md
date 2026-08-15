# Python

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 생성되는 것

```
<Path>/<PackageName>/
  __init__.py                     전부 재수출 (__all__ 포함)
  <ModuleName>.py                 접근자 (기본 tables.py)
  <table>_table.py                테이블당 하나
  enum_<enum>.py                  enum당 하나
  const_<set>.py                  상수 세트당 하나
  tabbit/tcb_reader.py  바이너리 리더 (함께 생성됩니다)
  tabbit/updater.py             데이터 갱신 (WriteUpdater를 켰을 때만)
  tabbit/__init__.py            위 둘을 재수출

타입 파일이 `tables/`·`enums/`·`constants/`로 나뉘지 않는 이유는 언어입니다 — 파이썬의 하위 디렉터리는 하위 **패키지**라 임포트가 한 겹 깊어지고, 무엇보다 `ModuleName`의 기본값이 `tables`라서 `tables/` 패키지가 접근자 `tables.py`를 **가려버립니다.** Go·Rust·Java도 같은 이유로 평평합니다.
```

패키지 안은 평평합니다. 하위 폴더는 서브패키지가 되어 각각 `__init__`이 필요하고, 무엇보다 `tables/`가 `tables.py` 옆에 있으면 import가 패키지 쪽으로 가서 접근자가 조용히 사라집니다.

## 필요한 것

|항목|값|
|--|--|
|Python|3.12로 검증. 그 아래는 확인하지 않았습니다|
|외부 패키지|**없음.** 표준 라이브러리만|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "python",
    "Path": "src",
    "PackageName": "gamedata",   // 폴더 이름이자 import 이름
    "ModuleName": "tables",      // 접근자가 든 모듈
    "BinaryTableFileExtension": ".tcb",
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

## 쓰는 법

```python
from gamedata import Tables

tables = Tables()
tables.read_all("./data")

sword = tables.item.find_by_index(1)
if sword is not None:
    # 참조는 로드 후 실제 레코드로 연결됩니다.
    print(sword.name, sword.category_id.name)

for row in tables.item.records:
    ...
```

확장자는 두 번째 인자입니다.

```python
tables.read_all("./data", ".bytes")
```

## 주의사항

**레코드는 `__slots__`를 씁니다.** 로컬라이제이션 테이블은 수만 행이고, 행마다 딕셔너리를 하나씩 두면 수십 메가바이트와 수 메가바이트의 차이가 납니다. 레코드에 없는 속성을 나중에 붙일 수는 없습니다.

**`__init__`은 이름을 하나씩 재수출합니다.** `import *`가 아니라서 `__all__`이 정확합니다 — `enum`이나 `os` 같은 모듈이 섞여 나오지 않습니다.

**`datetime`과 `timespan`은 `int`입니다.** .NET 틱(100나노초, 0001-01-01 기준)이 그대로 들어옵니다. `datetime.datetime`으로 바꾸고 싶으면 직접 변환하세요.

**멤버 이름은 snake_case입니다.** Python 키워드와 부딪히면 뒤에 밑줄이 붙습니다 (`class` → `class_`). PEP 8이 정확히 이 경우를 위해 정해둔 규칙입니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`ModuleNotFoundError: gamedata`|`Path`(패키지의 부모)가 `sys.path`에 있어야 합니다|
|`AttributeError: 'ItemRecord' object has no attribute ...`|`__slots__` 때문입니다. 오타이거나, 참조 필드의 이름을 잘못 알고 있습니다 (`category_id` vs `category_id_index`)|
|참조가 `None`|`read_all` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
|`from .tables import` 가 실패|`PackageName`과 `ModuleName`이 같으면 폴더와 모듈이 부딪힙니다. 다르게 두세요|
