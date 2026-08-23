# CLI 도움말 — 첫 화면이 이 도구가 무엇인지 말하게 하기

> [문서 목록으로](../doc/readme.md)
>
> 상태: **됨** — 구현에서 드러난 것은 §12

`tabbit --help`가 지금 내놓는 것은 **옵션 44개의 평면 목록**입니다. 이 도구가 무엇을 하는지,
무엇이 필수인지, 어떤 옵션이 어떤 모드에서만 뜻이 있는지가 한 줄도 없습니다.

이 문서는 그 화면을 다시 설계하고, 함께 드러난 결함 다섯 개를 같이 고칩니다.

---

## 1. 지금 나오는 것

`0.0.0+1d074e8340db`를 그대로 실행한 것입니다. Copyright 다음 줄이 바로 첫 옵션입니다.

```
tabbit 0.0.0+1d074e8340db3182a0e336a9abd83018e496f5dd
Copyright (C) 2026 Tabbit

  -r, --recipe                 Recipe file.

  --new-recipe                 Write a starting recipe file and exit.

  --template                   Which starting recipe --new-recipe writes. Omit
                               for one holding every setting.
       ⋮  (같은 형태로 41개 더)
```

[CommandLineParser](../src/Program.cs)의 `HelpText.AutoBuild`가 만드는 형태입니다. 결함을
셋으로 나눌 수 있습니다.

### 1.1 구조가 없는 것

|없는 것|무엇을 못 하게 됩니까|
|--|--|
|Usage 줄|`-r`이 사실상 필수라는 것을 옵션 목록에서 역산해야 합니다|
|한 줄 설명|이 도구가 무엇을 하는 프로그램인지 화면에 없습니다. `cp`는 `Copy SOURCE to DEST`가 넷째 줄입니다|
|모드 구분|이 도구는 **여섯 모드**입니다 — 변환 · 조회 · 정리 · 서비스 · 검증 · 파일 쓰고 종료. 목록은 그것을 한 덩어리로 냅니다|
|모드 종속 표시|`--from`·`--to`·`--at`은 `--history`/`--stats` 없이는 아무 뜻이 없는데, 그 사실이 어디에도 없습니다|
|값 자리표시자|`--recipe`가 파일을 받는지, `--keep`이 숫자를 받는지 안 보입니다. `cp`는 `-t, --target-directory=DIRECTORY`로 씁니다|
|에필로그|종료 코드·환경 변수·`@파일`이 전부 빠졌습니다 — §5|

### 1.2 배치

**항목마다 빈 줄이 들어갑니다.** 옵션 44개가 90여 줄이 되어 앞부분이 스크롤로 사라집니다.
`cp`는 옵션 50개를 빈 줄 하나 없이 씁니다. `AutoBuild`에는 이것을 끄는 설정이 없습니다 —
빌더를 대체해야 없어집니다.

### 1.3 옵션 정의 자체의 결함

측정한 것들입니다.

|결함|현재|
|--|--|
|`-h`가 없습니다|`tabbit -h` → `ERROR(S): Option 'h' is unknown.`|
|짧은 스위치가 `-r` 하나입니다|44개 중 1개|
|`--help`가 종료 코드 1|`--version`도, 인자 없는 실행도 전부 1. `tabbit --help && echo ok`가 실패합니다|
|백틱이 그대로 보입니다|`` `client` `` — 터미널은 마크다운을 렌더하지 않습니다|
|`--keep`만 기본값을 두 번 말합니다|`(Default: 100) Most recent snapshots to leave alone. 100 by default.`|
|`--verbose`·`--silent`·`--debug`만 어체가 다릅니다|`Sets whether to output debugging log messages.` — 설명이 아니라 프로퍼티 서술입니다. `all logging message`는 오타입니다|
|`--debug`가 문서와 다른 말을 합니다|help는 `internal debugging`, [doc/cli.md](../doc/cli.md)는 「콜스택까지 출력」|

`.NET` 버전도 어디에도 없습니다. [ToolVersion.Runtime](../src/ToolVersion.cs)이 이미
`.NET 10.0.11 (win-x64)`을 만들어 **실행 첫 줄에는 찍는데**, `--version`과 `--help`에만
없습니다 — 이슈 리포트에서 제일 먼저 되묻게 되는 값입니다.

## 2. 결정 요약

|항목|결정|근거|
|--|--|--|
|help 본문|**직접 씁니다.** `AutoBuild`를 쓰지 않습니다|그룹·Usage 여러 줄·예제·에필로그·빈 줄 없는 배치는 전부 그 빌더가 안 하는 것들이고, 설정으로 켜지지도 않습니다|
|파싱|CommandLineParser 그대로|바꿀 이유가 없습니다. 바뀌는 것은 화면뿐입니다|
|어긋남 방지|리플렉션 게이트 하나 — §8|손으로 쓴 help는 옵션이 늘 때 조용히 낡습니다. 이 게이트가 있어야 「직접 쓴다」가 안심할 수 있는 선택이 됩니다|
|짧은 스위치|`-h` `-r` `-e` `-o` `-v` `-q` **여섯 개만**|§4|
|`--help`·`--version` 종료 코드|**0**|「도움말을 요청했다」는 오용이 아닙니다|
|인자 없는 실행|짧은 usage 3줄, 종료 코드 **1**|90줄을 쏟는 것이 아니라 `cp`가 하는 것을 합니다 — §6|
|인식 못 한 옵션|오류 한 줄 + `Try 'tabbit --help'`, 종료 코드 1|같은 이유. 지금은 오류 뒤에 전체 help가 또 붙습니다|
|help의 언어|**영어 고정**|§9|
|빌드 시각|**CI 주입만.** 로컬 빌드는 비어 있습니다|§7|
|서브커맨드화|**하지 않습니다**|§9|

## 3. 화면

`built` 줄이 있는 것은 릴리스 바이너리입니다 — §7.

```
Tabbit 1.5.0+8b2a884d84bf
.tcb v107 · .NET 10.0.11 (win-x64)

Reads spreadsheets and writes them out as code and data files. A recipe says
which sheets to read and which outputs to build.

Usage: tabbit -r RECIPE [OPTION]...                    convert
   or: tabbit -r RECIPE --history [--from A] [--to B]   report what changed
   or: tabbit -r RECIPE --stats [--at COMMIT]           report one commit
   or: tabbit -r RECIPE --serve [--port N]              serve, and stay up
   or: tabbit -r RECIPE --prune --before AGE            drop old change detail
   or: tabbit --new-recipe FILE [--template NAME]       write a starting recipe
   or: tabbit @ARGFILE                                  read options from a file

Examples:
  tabbit -r recipe.json
  tabbit -r recipe.json --env live --target-side server
  tabbit -r recipe.json --validate-only
  tabbit -r recipe.json --full -v
  tabbit -r recipe.json --history --from HEAD~10 --format json -o out.json
  tabbit --new-recipe recipe.json --template binary

Recipe and run:
  -r, --recipe=FILE       The recipe to run.
  -e, --env=NAME          Environment this run is for. Recorded in the
                            summary, and available as ${TABBIT_ENV}.
      --target-side=SIDE  Narrow the run to one side: 'client', 'server', or
                            'both' (the default).
      --time-zone=ZONE    Time zone the sheets' dates were written in, forced
                            over the recipe: 'Asia/Seoul' or '+09:00'.
Cache:
      --full              Convert everything, ignoring what the cache says.
      --force-output      Run every output entry, whatever the cache says.
      --cache-dir=DIR     Where to keep the build cache. '.tabbit/' when
                            left out.
      --detailed-exit-code
                          Exit with 2 when the run had nothing to do, instead
                            of 0. For a pipeline that publishes next.
Validation:
      --validate-only     Validate and exit. No output target is run.
      --skip-runtime-validation
                          Skip the rules that read an external store.
      --list-validators   Print the rules in the order they run, and exit.
Write a file and exit:
      --new-recipe=FILE   Write a starting recipe file.
      --template=NAME     Which starting recipe --new-recipe writes. Omit for
                            one holding every setting.
      --new-validator=TABLE
                          Write a starting validation rule for this table.
      --new-encryption-key
                          Write a new encryption key, to --out or stdout.
      --dump-schema=FILE  Write where each table sits in its sheet, as JSON,
                            for tools that read these workbooks without
                            cooking them.
      --show-report       Open the last build report for this recipe.
What a conversion records in the history:
      --commit=ID         Commit this conversion is of. Read from git when
                            left out.
      --branch=NAME       Branch this snapshot belongs to. Read from git when
                            left out.
      --commit-author=WHO
                          Author of the change, as 'Name <email>'. Overrides
                            git.
      --commit-date=WHEN  When the change was made, ISO 8601. Overrides git.
      --repository=DIR    Working copy to read commit information from.
Reading the history, with --history, --stats, or --prune:
      --history           Report what changed between two commits, and exit.
      --stats             Report the statistics of a commit, and exit.
      --prune             Remove the change detail of old snapshots, and exit.
      --from=COMMIT       Commit the range starts after. Exclusive.
      --to=COMMIT         Commit the range ends at. Inclusive.
      --at=COMMIT         Commit to report statistics for. The head when
                            left out.
      --before=WHEN       Prune snapshots older than this: a date, or an age
                            like '90d'.
      --keep=N            Most recent snapshots to leave alone. 100 by default.
      --table=NAME        Only report changes to this table.
      --field=NAME        Only report changes to this column.
      --author=WHO        Only report changes by this person.
      --project=NAME      Project whose history to read. From the recipe when
                            left out.
      --limit=N           Most changes to report. What is cut is reported
                            as cut.
Serving the history, with --serve:
      --serve             Serve the history over HTTP and stay running.
      --port=N            Port to serve on. 8080 when left out.
      --bind=ADDRESS      Address to serve on. 127.0.0.1 when left out.
                            Anything else needs TABBIT_SERVE_TOKEN.
Reporting:
  -o, --out=FILE          Where to write a report. Standard output when
                            left out.
      --format=FORMAT     Report format: 'json' or 'text'.
      --messages=LANG     Language for this tool's own reports: en, ko, ja,
                            zh-Hans, zh-Hant. English by default, and also
                            read from TABBIT_MESSAGES.
  -v, --verbose           Print the debug log as well.
  -q, --silent            Print nothing below ERROR.
      --debug             Print the call stack when something fails.
  -h, --help              Display this help and exit.
      --version           Output version information and exit.

Exit codes:
  0   the run did what it was asked to
  1   the run failed, and said why
  2   nothing had changed, so nothing was produced
        (only with --detailed-exit-code)

Environment:
  TABBIT_ENV           what --env sets. A value that disagrees is refused,
                         not overwritten
  TABBIT_MESSAGES      default for --messages
  TABBIT_SERVE_TOKEN   required when --bind is not loopback

An argument of @FILE is replaced by the lines of that file, one option per
line.

Full documentation: doc/cli.md
```

실제로 나오는 화면입니다. **정본은 [HelpScreen.cs](../src/HelpScreen.cs)이고**, 이 사본이
어긋나면 이쪽이 틀린 것입니다.

### 3.1 배치 규칙

|규칙|값|
|--|--|
|옵션 열|2칸 들여쓰기, 짧은 이름 자리는 없어도 비워 둡니다 (`      --full`)|
|설명 열|26칸에서 시작합니다|
|줄바꿈|80칸을 넘지 않게 접고, 이어지는 줄은 28칸|
|긴 이름|`--detailed-exit-code`·`--skip-runtime-validation`·`--new-validator=TABLE`·`--new-encryption-key`·`--commit-author=WHO`는 설명 열을 넘거나 닿으므로 설명이 다음 줄로 갑니다|
|빈 줄|**옵션 사이에 없습니다.** 그룹 제목이 구분자입니다|
|따옴표|홑따옴표. 백틱은 터미널에서 그냥 백틱입니다|

### 3.2 그룹을 나눈 기준

`--commit`·`--branch` 계열과 `--history`·`--from` 계열을 **떼어 놓은 것**이 이 화면에서
현재보다 가장 크게 달라지는 부분입니다. 앞의 것은 변환이 히스토리에 **기록하는** 값이고, 뒤의
것은 리포트가 히스토리를 **읽는** 값입니다. [Options.cs](../src/Options.cs)에는 이 구분이
`CacheRelevance.Commit`과 `CacheRelevance.NotAConversion`으로 이미 있는데, 화면에는 없었습니다.

## 4. 짧은 스위치

**관례가 이미 정해 준 것만** 붙입니다. 44개에 한 글자씩 붙이면 기억이 아니라 사전이
필요해집니다.

|스위치|옵션|
|--|--|
|`-h`|`--help`|
|`-r`|`--recipe` (이미 있음)|
|`-e`|`--env`|
|`-o`|`--out`|
|`-v`|`--verbose`|
|`-q`|`--silent`|

거절한 것을 남겨 둡니다. 나중에 「왜 이건 없지」가 다시 올라오기 때문입니다.

|글자|안 주는 이유|
|--|--|
|`-f`|**`--full`과 `--force-output`에 절대 주지 않습니다.** 캐시를 의심하는 것과 캐시를 믿고 출력만 다시 하는 것은 다른 질문이라 [Options.cs](../src/Options.cs)가 명시적으로 나눠 둔 것인데, `-f` 하나가 그 구분을 지웁니다|
|`-s`|`--serve`·`--stats`·`--silent`·`--skip-runtime-validation`이 모두 s입니다. 어느 하나에 주면 나머지 셋이 오답이 됩니다|
|`-t`|`--template`과 `--target-side`와 `--table`과 `--to`|
|`-p`|`--port`에 줄 수는 있지만, 배포마다 한 번 적는 값입니다. `--project`·`--prune`도 p를 원합니다|
|`-V`|`--version`. 대문자 하나를 기억해야 하는 스위치는 `--version`보다 나을 것이 없습니다|

## 5. 에필로그가 실어야 하는 것

전부 **코드에는 있고 help에는 없던** 것들입니다.

|항목|출처|
|--|--|
|종료 코드 0·1·2|[ExitCode.cs](../src/ExitCode.cs)|
|`TABBIT_ENV`|`--env`가 설정하고, 어긋나는 값은 덮어쓰지 않고 거부합니다|
|`TABBIT_MESSAGES`|`--messages`의 기본값|
|`TABBIT_SERVE_TOKEN`|`--bind`가 루프백이 아닐 때 필수|
|`@FILE`|[Program.cs](../src/Program.cs) — 인자를 파일에서 읽습니다. **help에 한 줄도 없어서 소스를 읽은 사람만 압니다**|

## 6. 인자가 없거나 틀렸을 때

지금은 둘 다 90줄을 쏟습니다. `cp`가 하는 것으로 바꿉니다.

인자 없음 — 종료 코드 1:

```
tabbit: no options given
Usage: tabbit -r RECIPE [OPTION]...
Try 'tabbit --help' for more information.
```

인식 못 한 옵션 — 종료 코드 1:

```
tabbit: unrecognised option '-h'
Try 'tabbit --help' for more information.
```

전체 help는 **요청했을 때만** 나옵니다. 오류 뒤에 붙는 90줄은 오류 메시지를 화면 밖으로
밀어냅니다.

> `--help`·`--version`이 0으로 끝나게 바뀝니다. 이 종료 코드에 기대는 스크립트가
> 저장소에 없는 것은 확인했습니다.

## 7. 버전 줄

`--version`이 [ToolVersion](../src/ToolVersion.cs)의 값을 그대로 씁니다 — 실행 첫 줄이
쓰는 것과 같은 자리에서 오므로 둘이 어긋날 수 없습니다.

```
$ tabbit --version
Tabbit 1.5.0+8b2a884d84bf
.tcb v107 · .NET 10.0.11 (win-x64)
built 2026-08-22T14:41:52Z
```

로컬 빌드는 마지막 줄이 없습니다.

```
$ tabbit --version
Tabbit 0.0.0+c401cb366657
.tcb v107 · .NET 10.0.11 (win-x64)
```

### 7.1 빌드 시각은 CI만 주입합니다

|후보|판정|
|--|--|
|PE 헤더의 링커 타임스탬프|**안 됩니다.** .NET SDK는 `Deterministic`이 기본값이라 그 필드에 실제 시각이 아니라 콘텐츠 해시가 들어갑니다. 날짜로 읽으면 미래의 값이 나옵니다|
|실행 파일의 mtime|**안 됩니다.** self-contained single-file은 `Assembly.Location`이 빈 문자열이고, `Environment.ProcessPath`로 우회해도 그것은 「이 파일이 여기 놓인 시각」입니다 — 복사·다운로드·체크아웃하면 바뀝니다|
|빌드가 값을 심는 것|**이것만 됩니다.** 단 조건이 붙습니다|

조건은 **로컬 빌드에서는 심지 않는 것**입니다. `$([System.DateTime]::UtcNow)`를 프로퍼티로
평가하면 아무것도 고치지 않아도 컴파일 입력이 매번 달라집니다 — 결정론적 빌드가 깨지고,
증분 빌드가 매번 재컴파일합니다. 이 저장소에서는
[13분 스위트](../doc/architecture.md#개발--테스트) 앞에 그것이 매번 붙습니다.

```xml
<!-- CI가 넘기지 않으면 아이템 자체가 붙지 않습니다. -->
<ItemGroup Condition="'$(BuildTimestamp)' != ''">
  <AssemblyMetadata Include="BuildTimestamp" Value="$(BuildTimestamp)" />
</ItemGroup>
```

`AssemblyMetadata` 아이템은 SDK가 `AssemblyMetadataAttribute`로 자동 생성합니다 — 텍스트
파일을 만들어 넣는 단계가 필요 없습니다.

주입은 [release.yml](../.github/workflows/release.yml) 한 곳입니다. `build/`의 스크립트는
개발자가 손으로 돌리는 것이므로 넘기지 않습니다 — 그쪽 산출물은 릴리스가 아닙니다.

### 7.2 `ToolVersion.Current`에 붙이지 않습니다

이것이 이 절의 유일한 함정입니다.

|자리|`Current`를 무엇으로 씁니까|빌드 시각이 섞이면|
|--|--|--|
|[BuildCache.cs](../src/Caching/BuildCache.cs)|**빌드 캐시의 키**|재빌드마다 전체 변환|
|[BuildReport.cs](../src/Reporting/BuildReport.cs)|리포트에 기록하는 `Tool`|리포트 골든이 빌드마다 바뀝니다|

그래서 `Built`는 별개 프로퍼티이고, `Banner`·`Current`는 손대지 않습니다.

### 7.3 실행 첫 줄에는 넣지 않습니다

`--version`에만 넣습니다. 실행 첫 줄이 답하는 질문은 「어느 빌드가 돌고 있나」이고
버전과 커밋이 이미 그것을 답합니다. 빌드 시각을 원하는 사람은 **바이너리를 손에 들고
이게 뭔지 묻는 사람**이고, 그 사람이 치는 것이 `--version`입니다.

[ToolVersion.cs](../src/ToolVersion.cs)가 「버전과 포맷 번호와 런타임과 플랫폼을 한 줄에
담으면 아무도 아무것도 못 찾는다」고 적어 둔 것과 같은 판단입니다. 그리고 실행 출력을
건드리지 않으므로 골든에 닿는 경로가 없습니다.

### 7.4 커밋 날짜 — 이번에 하지 않는 것

로컬 빌드의 `--version`에는 날짜가 한 줄도 없게 됩니다. 커밋 해시(`+1d074e8340db`)는
있지만 **해시로는 앞뒤를 읽을 수 없습니다.**

커밋 날짜를 심으면 그것이 해결되고, 같은 커밋이면 같은 값이므로 **결정론도 깨지지
않습니다.** 하지 않는 이유는 하나입니다 — 빌드 중에 `git`을 실행하는 단계가 새로
생기고, 그것은 이번 변경에 승인된 범위가 아닙니다. 필요해지면 이 조각을 그대로 넣으면
됩니다.

```xml
<Target Name="StampSourceDate" BeforeTargets="GetAssemblyAttributes">
  <!-- git이 없는 곳(소스 아카이브, 도커 빌드)에서도 빌드는 되어야 하므로 실패를 넘깁니다. -->
  <Exec Command="git show -s --format=%%cI HEAD"
        ConsoleToMSBuild="true" ContinueOnError="true" StandardOutputImportance="low">
    <Output TaskParameter="ConsoleOutput" PropertyName="SourceDate" />
  </Exec>
  <ItemGroup Condition="'$(SourceDate)' != ''">
    <AssemblyMetadata Include="SourceDate" Value="$(SourceDate)" />
  </ItemGroup>
</Target>
```

> 아이템 추가가 **타겟 안에** 있는 것이 중요합니다. 어셈블리 속성은 그것을 추가할 수 있는
> 타겟이 돌기 전에 수집되므로, 바깥의 `ItemGroup`은 `$(SourceDate)`가 아직 빈 상태로
> 평가됩니다. [Tabbit.csproj](../src/Tabbit.csproj)의 `EmbedRuleCompilationReferences`
> 위에 같은 함정이 이미 적혀 있습니다 — 리소스에서 겪은 것이고, 어셈블리 속성에도 같이
> 적용됩니다.

## 8. 어긋남을 막는 게이트

손으로 쓴 help의 유일한 위험은 **옵션이 늘 때 조용히 낡는 것**입니다. 테스트 하나가
그것을 닫습니다.

|검사|무엇을 잡습니까|
|--|--|
|`[Option]` 이름 전부가 help 본문에 나옵니다|옵션을 추가하고 help에 안 적은 것|
|help 본문이 말하는 `--이름` 전부가 실재합니다|옵션을 지웠는데 help에 남은 것, 그리고 오타|
|짧은 이름이 선언된 옵션은 help에도 짧은 이름과 함께 나옵니다|`-e`를 붙였는데 화면에는 안 적은 것|
|`tabbit --help`의 종료 코드가 0|§6이 회귀하는 것|
|`tabbit --version`이 `.NET`을 담은 줄을 냅니다|런타임 줄이 빠지는 것|
|인자 없는 실행의 종료 코드가 1이고 출력이 5줄 이하|다시 90줄을 쏟는 것|

리플렉션으로 `Options`의 프로퍼티를 훑으므로, **옵션을 추가하는 사람이 이 테스트를
기억할 필요가 없습니다.** 잊으면 게이트가 말합니다.

## 9. 하지 않는 것

|안 하는 것|이유|
|--|--|
|help의 다국어화|`--messages`는 5개 언어인데 help는 영어 고정으로 둡니다. §3의 분량을 5개로 유지하는 비용이 실제로 있고, 그 비용을 낼 근거가 아직 없습니다. **의도적 결정**이고, 필요해지면 [메시지 카탈로그](message-ids.md)가 이미 그 자리입니다|
|서브커맨드 (`tabbit history …`)|여섯 모드가 서브커맨드에 잘 맞기는 합니다. 그런데 그것은 **호출 호환성을 깨는 변경**이고, 이 문서가 고치려는 것은 화면입니다. 화면이 모드를 말하기 시작하면 그때 서브커맨드가 정말 필요한지 다시 볼 수 있습니다|
|`--help <topic>`|위 화면이 한 화면에 안 들어오는 것은 사실입니다. 다만 지금 문제는 분량이 아니라 구조이고, 구조를 먼저 준 다음에 분량이 여전히 문제인지 봐야 합니다|
|`-p`·`-f`·`-s` 추가|§4|
|문서 URL|저장소가 private이라 지금 외부에서 열리지 않습니다. `doc/cli.md` 상대 경로로 둡니다|

## 10. 닿는 파일

|파일|무엇|
|--|--|
|[src/HelpScreen.cs](../src/HelpScreen.cs) (새로)|§3의 본문. 원시 문자열 리터럴 하나|
|[src/Program.cs](../src/Program.cs)|`--help`·`-h`·`--version` 가로채기, `AutoHelp` 끄기, §6의 짧은 출력, 종료 코드|
|[src/Options.cs](../src/Options.cs)|짧은 이름 5개 추가, 백틱 제거, `--keep` 중복 제거, `--verbose`·`--silent`·`--debug` 문구 교정|
|[src/ToolVersion.cs](../src/ToolVersion.cs)|`Built` 추가|
|[src/Tabbit.csproj](../src/Tabbit.csproj)|§7.1의 `AssemblyMetadata`|
|[.github/workflows/release.yml](../.github/workflows/release.yml)|`-p:BuildTimestamp=`|
|[test/Tabbit.Tests/CliHelpTests.cs](../test/Tabbit.Tests/CliHelpTests.cs) (새로)|§8. 14개|
|[doc/cli.md](../doc/cli.md)|`--debug` 설명을 help와 맞추고, 짧은 스위치와 `@FILE`을 옵션 표에 넣습니다|

`HelpText` 문자열은 Options에 그대로 남습니다. 화면이 쓰지 않게 되더라도 지우지 않습니다 —
파싱 오류 메시지가 그것을 쓰고, 옵션 옆에 그 옵션의 한 줄 설명이 있는 것 자체가 값입니다.

## 11. 골든

**한 바이트도 움직이지 않아야 합니다.** 이 변경이 닿는 것은 명령줄 화면과 `--version`이고,
생성기·템플릿·와이어에는 경로가 없습니다. `--verbose` 등의 `HelpText` 문구가 바뀌지만 그것은
화면 문자열이고 변환 산출물이 아닙니다.

`dotnet test --filter "FullyQualifiedName~ConversionGolden"`으로 33초에 확인하고, 새 게이트는
`--filter "FullyQualifiedName~CliHelp"`로 따로 돌립니다.

## 12. 구현에서 드러난 것

### 12.1 「레시피 없음」 경로가 파서로 도움말을 뿌리고 있었습니다

`Run`의 마지막 `else`가 `parser.ParseArguments<Options>(new[] { "--help" })`를 불러
전체 화면을 냈습니다. `HelpWriter`를 끄자 **그 경로가 아무것도 출력하지 않게** 되었습니다 —
`tabbit --verbose`가 배너만 찍고 조용히 1로 끝났습니다.

옵션 목록에서는 보이지 않는 자리였습니다. 파서를 화면에서 떼어내면 파서로 화면을 내던 곳이
전부 드러나는데, 그 곳이 `Main`이 아니라 400줄 아래에 하나 더 있었습니다.

같은 수정으로 `Run(Parser, Options)`의 첫 인자가 필요 없어져 없앴습니다 — 실행이 파서를
참조하는 마지막 자리였습니다.

### 12.2 게이트가 실패하는 것을 확인했습니다

|심은 것|잡은 테스트|
|--|--|
|`Options`에 `--drift-probe`를 추가하고 화면에는 안 적음|`EveryOptionIsOnTheHelpScreen`|
|화면에 `--shwo-report` 오타를 심음|`EveryOptionOnTheHelpScreenExists`|

둘 다 되돌렸습니다. 실패할 수 없는 게이트는 게이트가 아니므로, 이 확인이 §8의 일부입니다.

### 12.3 정렬 판정은 빈 줄을 기준으로 해야 합니다

`Exit codes:`와 `Environment:`는 그룹 제목과 생김새가 같고 그 아래가 들여쓰여 있습니다.
콜론으로 판정하면 그 줄들까지 옵션 열로 재게 되므로, **빈 줄이 옵션 구역을 끝낸다**는 규칙을
씁니다.

### 12.4 XML 주석에는 `-` 두 개를 쓸 수 없습니다

csproj 주석에 옵션 이름을 적으면 `MSB4025`로 프로젝트가 로드되지 않습니다. 이 파일이 이미
「the version option」이라고 적고 있던 이유가 그것이었습니다.
