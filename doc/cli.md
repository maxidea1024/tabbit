# CLI

빌드하고 실행하는 법, 그리고 명령줄 옵션.

> [문서 목록으로](readme.md)

오류 메시지별 대처는 [트러블슈팅](troubleshooting.md)에 있습니다.

---

## Build

`build/` 폴더에 빌드용 스크립트들이 있습니다. 각 플랫폼별로 빌드하려면 아래 표를 참고하세요.

|플랫폼|빌드 스크립트|산출 위치|
|--|--|--|
|Windows|`build-win64.ps1`|`bin/win-x64/tabbit.exe` · `bin/win-arm64/tabbit.exe`|
|Linux|`build-linux64.sh`|`bin/linux-x64/tabbit` · `bin/linux-arm64/tabbit`|
|Mac|`build-osx64.sh`|`bin/osx-arm64/tabbit` · `bin/osx-x64/tabbit`|

빌드 전에 [.NET 10 SDK](https://dotnet.microsoft.com/download)를 설치해 주셔야합니다.

**아키텍처는 실행한 머신에서 판정합니다** — 셸 쪽은 `uname -m`, PowerShell 쪽은
`RuntimeInformation.OSArchitecture`입니다. 애플 실리콘에서 `build-osx64.sh`를 실행하면
`osx-arm64`가 나옵니다 — self-contained 산출물은 네이티브 코드라서 아키텍처가 어긋나면
실행 자체가 되지 않기 때문입니다. 다른 아키텍처가 필요하면 런타임 식별자를 인자로 넘깁니다.

```bash
./build/build-osx64.sh osx-x64      # 인텔 맥
./build/build-linux64.sh linux-arm64
```

```powershell
.\build\build-win64.ps1 win-arm64
.\build\build-osx64.ps1 osx-x64     # 윈도우에서 맥용으로 크로스 퍼블리시
```

> `build/`의 PowerShell 스크립트는 각 플랫폼용으로 **크로스 퍼블리시**하는 용도이기도 합니다.
> 리눅스·맥에서는 같은 이름의 `.sh`를 쓰면 되고, 그쪽이 아키텍처까지 판정합니다.

**런타임 식별자마다 디렉터리가 분리됩니다.** 예전에는 전부 `bin/` 하나를 공유했는데,
self-contained 퍼블리시는 네이티브 의존 파일을 실행 파일 옆에 두므로 두 플랫폼을 한
머신에서 빌드하면 나중 것이 앞의 것의 의존 파일을 덮어씁니다.

생성되는 실행 파일은 self-contained 단일 파일입니다. `PublishTrimmed`는 의도적으로 사용하지 않습니다 — NPOI, Newtonsoft.Json, Google.Apis가 모두 리플렉션으로 타입을 찾기 때문에 트리밍이 런타임에 필요한 멤버를 제거합니다.




### 배포 (Publish)

```
dotnet publish src/Tabbit.csproj -c Release -r win-x64 --self-contained true -o out
```

`-r`는 `win-x64` / `linux-x64` / `osx-arm64` 등으로 바꿉니다. 결과는 실행파일 하나(약 60MB)와 네이티브 의존 두 개뿐이며, **.NET이 설치되지 않은 머신에서 그대로 동작합니다.**

프레임워크 의존(`--self-contained false`)으로 배포한다면 대상 머신에 **ASP.NET Core 런타임**이 필요합니다. 기본 .NET 런타임만으로는 `--serve`뿐 아니라 변환도 시작되지 않습니다 — 웹서버가 프레임워크 참조로 들어가 있기 때문입니다. 빌드 머신에 무엇이 깔려 있을지 확신할 수 없다면 self-contained가 안전합니다.

> CI가 매 실행마다 linux-x64로 self-contained 퍼블리시를 만들고 그 결과물로 변환 하나를 돌립니다. 위 문장은 주장이 아니라 검증된 사실입니다.



## Run

```
tabbit --recipe recipe.json
```

|옵션|설명|
|--|--|
|`-r`, `--recipe`|사용할 recipe 파일|
|`--new-recipe <파일>`|시작용 recipe를 만들고 종료. 모든 목록에 기본값이 채워진 항목 하나가 들어 있어 **어떤 설정이 있는지 파일만 보고 알 수 있습니다**. 필요 없는 항목은 지우면 되고, 경로가 빈 항목은 꺼진 것으로 취급되니 그냥 둬도 됩니다.|
|`--target-side <side>`|실행 전체를 한쪽으로 좁힘. `client` / `server` / `both`(기본).|
|`--time-zone <시간대>`|시트의 `datetime` 셀을 어느 시간대로 읽을지 **강제로** 지정. `Asia/Seoul` 또는 `+09:00`. recipe 전체 설정과 소스 항목별 설정 **둘 다** 덮어씁니다 — recipe가 그 시간대를 잘못 적고 있는 실행을 위한 것이므로, 덮어쓸 대상이 바로 항목별 설정입니다. 적용한 값은 실행 로그에 한 줄로 남습니다. 「[시트의 값](recipe.md#timezone--시트의-날짜를-어느-시간대로-읽을지)」 참고|
|`--env <이름>`|**이 실행이 어느 환경의 것인가.** summary에 기록되고, recipe의 `${TABBIT_ENV}`가 이 값으로 채워집니다 — 「[환경 지정](#--env--이-실행이-어느-환경의-것인가)」|
|`--template <이름>`|`--new-recipe`가 어떤 시작점을 쓸지. 생략하면 모든 설정이 기본값으로 채워진 파일이 나옵니다. 「[시작점 고르기](recipe.md#시작점-고르기)」 참고.|
|`--commit <id>`|이 변환이 어느 커밋의 것인지. 생략하면 시트가 있는 워킹카피에서 git으로 읽습니다. 「[Summary와 히스토리](history.md)」 참고.|
|`--branch <name>`|스냅샷이 속할 브랜치. 생략하면 git에서 읽습니다.|
|`--commit-author "Name <email>"`|작성자를 직접 지정. git 값을 덮어씁니다.|
|`--commit-date <ISO8601>`|변경 시각을 직접 지정. git 값을 덮어씁니다.|
|`--repository <경로>`|커밋 정보를 읽을 워킹카피. 생략하면 시트의 소스 디렉터리, 그다음 현재 디렉터리를 봅니다.|
|`--history`|변환 대신 **변경 내역을 조회**하고 종료|
|`--stats`|변환 대신 **한 커밋의 통계를 조회**하고 종료|
|`--serve`|변환 대신 **HTTP로 히스토리를 서비스**하고 계속 실행|
|`--prune`|변환 대신 **오래된 스냅샷의 변경 상세를 정리**하고 종료|
|`--validate-only`|**검증까지만** 돌고 종료. 산출물을 하나도 만들지 않습니다 — PR 검사가 쓰는 형태입니다. 「[검증](validation.md)」 참고.|
|`--skip-runtime-validation`|외부 저장소를 읽는 `rules/runtime/` 규칙만 건너뜁니다. 건너뛴 규칙 수가 기록에 남습니다|
|`--new-validator <테이블>`|해당 테이블의 **시작 검증 규칙**을 쓰고 종료. 이미 있으면 덮어쓰지 않고 거부합니다|
|`--list-validators`|검증 규칙을 **실행 순서대로** 출력하고 종료. 시트를 하나도 읽지 않습니다. 우선순위는 규칙마다 붙이므로 전체 순서가 한곳에 모이지 않는데, 이 출력이 그 자리입니다 — 목록 파일과 달리 **출력한 것이 곧 실행되는 것**입니다|
|`--new-encryption-key`|**바이너리 암호화 키**를 새로 만들고 종료. 운영체제의 난수원에서 뽑습니다. 표준 출력에는 키 한 줄만 나가므로(안내문은 표준 오류로) 시크릿 저장소에 그대로 파이프할 수 있고, `--out`을 주면 파일로 씁니다 — **이미 있는 파일은 덮어쓰지 않습니다.** 「[내보내기](exports.md#바이너리-익스포트의-recipe-옵션)」|
|`--verbose`|디버그 로그까지 출력|
|`--silent`|ERROR/FATAL 외에는 출력하지 않음|
|`--debug`|오류 발생 시 콜스택까지 출력|

### `--env` — 이 실행이 어느 환경의 것인가

|어떤 상황|무엇을 하나|
|--|--|
|**혼자, 한 기계에서**|**아무것도 하지 않습니다.** recipe에 경로를 그대로 적고 `tabbit --recipe recipe.jsonc`로 끝입니다. `--env`도 변수도 필요 없습니다|
|**여러 사람, 여러 환경**|recipe가 경로를 `${TABBIT_ENV}`로 적고, 실행이 `--env`로 어느 환경인지 말합니다|

아래는 두 번째 경우입니다.

**한 낱말이 두 가지를 합니다.** summary에 기록되어 산출물만 보고 어느 환경의 빌드인지 판정할
수 있게 하고, 동시에 recipe의 `${TABBIT_ENV}`가 그 값으로 채워집니다.

```jsonc
// recipe.jsonc
{
  "Sources": { "Xlsx": [ { "Path": "./sheets/${TABBIT_ENV}" } ] },
  "Targets": [ { "Type": "binary", "Path": "./build/${TABBIT_ENV}/data" } ]
}
```

```
tabbit --recipe recipe.jsonc --env live
```

**둘을 갈라 두지 않은 이유가 이 옵션의 요점입니다.** 라벨용 플래그와 경로용 환경 변수를 따로
두면 둘이 어긋날 수 있고, 어긋난 결과는 **개발 시트로 만들어 놓고 `live`라고 적힌 산출물**입니다.
라벨이 없는 것보다 나쁩니다 — 질문을 열어두는 것이 아니라 틀리게 답하기 때문입니다.

- `TABBIT_ENV`가 이미 **다른 값으로** 설정되어 있으면 **거절합니다.** 어느 쪽을 택하든 한쪽이
  라벨을 정하고 다른 쪽이 경로를 정하는 상태가 됩니다.
- 같은 값이면 통과합니다. 변수를 내보내면서 플래그도 넘기는 CI 잡은 잘못한 것이 없습니다.
- 변수만 설정하고 `--env`를 생략해도 됩니다. 결과는 같습니다.
- **적지 않으면 summary의 `environment`가 `null`입니다.** 기본값을 적어 넣지 않는 것은, 아무도
  하지 않은 주장을 기록하지 않기 위해서입니다.
- 이름은 **영문자·숫자·`.`·`_`·`-`**만 받습니다. 이 낱말이 경로에 들어가므로, 구분자나 `..`가
  들어가면 recipe가 적지 않은 곳에 산출물이 쓰입니다. 변수로 넣은 값도 같게 검사합니다.

인자가 많아지면 파일로 빼서 `@`로 넘길 수 있습니다. 한 줄에 인자 하나씩 적습니다.

```
tabbit @args.txt
```

성공하면 `0`, 실패하면 `0`이 아닌 값을 반환하므로 빌드 파이프라인에서 그대로 사용할 수 있습니다.
