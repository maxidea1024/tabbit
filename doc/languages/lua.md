# Lua

> [언어별 가이드로](readme.md) · [문서 목록으로](../readme.md)
>
> 설계 근거는 [Lua 언어 지원](../../spec/lua-language-support.md)에 있습니다.

**LuaJIT 2.1과 Lua 5.3 이상**에서 동작합니다. 순수 Lua 5.1·5.2는 지원하지 않습니다 —
그 숫자는 double이라 `bigint`와 `datetime` 틱이 2⁵³ 너머에서 조용히 바뀝니다. LuaJIT에서는
FFI `int64_t` cdata가, 5.3+에서는 네이티브 64비트 정수가 그 자리를 맡고, 리더가 로드
시점에 알아서 갈라탑니다.

---

## 생성되는 것

```
<Path>/
  tables.lua                      접근자 (AccessorName)
  tables/<table>_table.lua        테이블당 하나
  enums/enum_<enum>.lua           enum당 하나
  constants/const_<set>.lua       상수 세트당 하나
  tabbit/tcb_reader.lua           바이너리 리더 (함께 생성됩니다)
  tabbit/tcb_ops_jit.lua          LuaJIT용 숫자 백엔드
  tabbit/tcb_ops_53.lua           Lua 5.3+용 숫자 백엔드
  tabbit/native/tabbit_native.c   네이티브 모듈 소스 — 암호화·MAC·매니페스트 해시
  tabbit/updater.lua              데이터 갱신 (WriteUpdater를 켰을 때만)
```

전역은 하나도 만들지 않습니다. 모든 파일이 테이블 하나를 `return`하고, 이웃은 자기 모듈
이름에서 떼어낸 상대 접두어로 찾습니다 — 이 디렉터리를 `package.path`의 어디에 얹어도
그대로 동작합니다.

백엔드가 두 파일인 것은 문법 때문입니다. 5.3의 비트 연산자와 정수 나눗셈은 LuaJIT에서
**파싱조차 되지 않으므로**, 리더는 실행 중인 런타임을 보고 한쪽만 `require`합니다.

## 필요한 것

|항목|값|
|--|--|
|런타임|LuaJIT 2.1 또는 Lua 5.3+ (5.4로 검증)|
|외부 패키지|**없음**|
|C 컴파일러|**암호화·MAC·업데이터를 쓸 때만.** 아래 「네이티브 모듈」|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "lua",
    "Path": "src/lua",
    "AccessorName": "tables",    // 접근자 모듈 이름이자 파일 이름
    "BinaryTableFileExtension": ".tcb",
    "WriteUpdater": false,
    "Sweep": true,
    "TargetSide": "c"
  }
]
```

`Namespace`·`PackageName`은 없습니다 — Lua의 모듈 이름은 파일이 선언하는 것이 아니라
`package.path`에 어떻게 놓이는지가 정합니다.

## 쓰는 법

```lua
local tables = require("tables")

local data = tables.new()
data:readAll("./data")

local sword = data.item:findByIndex(1)
if sword ~= nil then
  -- 참조는 로드 후 실제 레코드로 연결됩니다.
  print(sword.name, sword.categoryId.name)
end
```

파일이 디렉터리에 있지 않은 환경 — 자체 아카이브, `io`가 막힌 샌드박스 — 에서는 바이트를
가져오는 함수를 대신 넘깁니다.

```lua
data:readAll(function(fileName)
  return MyEngine.readBundledFile("data/" .. fileName)
end)
```

### 없는 필드는 오류입니다

로우·enum·상수·접근자 전부에서, 선언되지 않은 키를 읽거나 쓰면 그 자리에서 타입 이름과
함께 오류가 납니다 — `row.maxHP`(오타)가 조용히 `nil`을 돌려주고 그 `nil`이 멀리서 다른
오류로 나타나는 Lua의 익숙한 사고를 막는 장치입니다. 올바른 접근에는 비용이 없습니다:
메타테이블은 키가 테이블에 없을 때만 불립니다.

의도적인 동적 접근에는 `rawget(row, key)`가, 로우에 자기 데이터를 붙이고 싶다면
`rawset`이 그대로 열려 있습니다.

생성 파일에는 lua-language-server 주석(`---@class` · `---@field`)도 함께 들어 있어서,
그 서버를 쓰는 편집기는 오타를 타이핑하는 순간 표시합니다.

## 네이티브 모듈 — 암호화·MAC·매니페스트 해시

ChaCha20 복호, HMAC-SHA-256 검증, 업데이터의 MD5는 순수 Lua로 감당할 수 없는 바이트
루프라서 C에 있습니다 — 산출물에 함께 나오는 `tabbit/native/tabbit_native.c` 하나이고,
`lua.h`와 표준 라이브러리 외에는 아무것도 include하지 않으며 Lua 5.1(LuaJIT)~5.4에서
컴파일됩니다.

**평문 파일만 읽는 프로젝트는 빌드할 필요가 없습니다.** 리더는 암호 경로에 들어갈 때만
모듈을 `require`하고, 없으면 무엇을 빌드해야 하는지 말하면서 멈춥니다.

### 경로 ① 게임 엔진에 임베드

Lua를 소스로 임베드한 엔진이라면 `tabbit_native.c`를 빌드에 넣고 첫 로드 전에 한 번
등록합니다.

```c
int luaopen_tabbit_native(lua_State* L);

/* Lua 5.2+ */
luaL_requiref(L, "tabbit.native", luaopen_tabbit_native, 0);
lua_pop(L, 1);
```

LuaJIT(5.1 API)에는 `luaL_requiref`가 없으므로 `package.preload`에 넣습니다.

```c
lua_getglobal(L, "package");
lua_getfield(L, -1, "preload");
lua_pushcfunction(L, luaopen_tabbit_native);
lua_setfield(L, -2, "tabbit.native");
lua_pop(L, 2);
```

### 경로 ② 독립 인터프리터

공유 라이브러리로 빌드해 `package.cpath`가 찾는 자리에 둡니다. 모듈 이름이
`tabbit.native`이므로 파일은 `tabbit/native.dll`(또는 `.so`)입니다.

```
# Windows (x64 Native Tools 프롬프트, Lua 5.4 기준)
cl /O2 /MD /LD /I <lua include> tabbit_native.c /link <lua>.lib /OUT:tabbit\native.dll

# Linux / macOS
gcc -O2 -shared -fPIC -I <lua include> tabbit_native.c -o tabbit/native.so
```

### 경로 ③ 빌드 없이

평문 `.tcb`는 순수 Lua만으로 읽힙니다. 포기하는 것은 암호화된 파일, MAC 검증, 그리고
업데이터입니다.

## 데이터만 갱신하기 (`WriteUpdater`)

업데이터는 매니페스트를 대조해 바뀐 파일만 받고, 전부 도착해 해시가 맞기 전에는 아무것도
바꾸지 않습니다. **HTTP는 구현하지 않습니다** — 게임 클라이언트는 자기 HTTP 스택을 이미
갖고 있으므로, URL을 받아 본문을 돌려주는 함수를 넘깁니다.

```lua
local updater = require("tabbit.updater")

local result = updater.update("https://cdn.example.com/data", "./cache", {
  fetch = function(url)
    local body, status = MyEngine.httpGet(url)

    if body ~= nil then
      return body
    end

    -- 재시도할 가치가 있는 실패인지가 세 번째 반환값입니다.
    local transient = status == 0 or status == 408 or status == 429 or status >= 500
    return nil, url .. " answered " .. status, transient
  end,
})

if result.succeeded then
  data:readAll(result.localPath)
end
```

`update`는 던지지 않습니다 — 실패해도 이전 데이터는 그대로 읽을 수 있고, 그 사실이
`result.error`와 함께 돌아옵니다. 해시 검사와 디렉터리 생성이 네이티브 모듈에서 오므로
**업데이터는 모듈 없이 돌지 않습니다.**

## 주의사항

- **배열은 1-기반입니다.** 시트의 `name[0]`이 `row.name[1]`입니다. `#`과 `ipairs`가
  성립하는 대가이고, 다른 언어와 코드를 나란히 둘 때 기억할 한 칸입니다.
- **옵셔널은 `nil`이 아닙니다.** 값 필드는 언제나 채워져 있고, 존재 여부는 `hasHp` 같은
  이웃 필드가 답합니다. `if row.hp == nil`은 이 계약에서 이미 잘못이고, 엄격 메타테이블이
  그것을 즉시 오류로 만듭니다.
- **`bigint`·`datetime`·`timespan`은 LuaJIT에서 cdata입니다.** 산술과 비교는 일반
  연산자로 되지만, `tostring`은 `LL` 접미사를 붙입니다 — 십진 문자열이 필요하면
  `tcb.int64String(v)`를 씁니다. 테이블 키로는 쓰지 마십시오(cdata 키는 값이 아니라
  참조 비교입니다); 키 인덱스가 필요한 자리는 생성된 `findBy…`가 이미 처리합니다.
- **예약어 필드는 이름을 그대로 유지합니다.** `function`이라는 컬럼은 `row["function"]`
  으로 읽습니다.
- **`uuid`는 정규형 소문자 문자열**, `datetime`·`timespan`은 틱 정수입니다.
- **참조 배열의 길이는 키 목록에서 읽습니다.** Lua 테이블은 `nil`을 담을 수 없어서,
  해석되지 않은 원소는 값 배열의 구멍입니다 — `#row.slotArrayIndex`가 원소 수이고,
  `row.slotArray[k]`는 그 자리가 해석되지 않았으면 `nil`입니다.

## 트러블슈팅

|증상|원인|
|--|--|
|`this reader needs LuaJIT 2.1 or Lua 5.3+`|순수 5.1·5.2 인터프리터입니다. 위 「필요한 것」|
|`... needs the tabbit.native module`|암호화·MAC·업데이터를 쓰는데 모듈이 등록되지 않았습니다. 「네이티브 모듈」의 세 경로 중 하나로|
|`... has no field 'x'`|필드 이름 오타이거나, 스키마가 바뀌었는데 코드를 재생성하지 않았습니다|
|`module 'tabbit.tcb_reader' not found`|출력 디렉터리가 `package.path`에 없습니다. `require("tables")`가 되는 자리라면 나머지는 상대 접두어로 따라옵니다|
