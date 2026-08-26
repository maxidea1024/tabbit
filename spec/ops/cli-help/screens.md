# 화면

> [「CLI 도움말」로 돌아가기](../cli-help.md)

---

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

실제로 나오는 화면입니다. **정본은 [HelpScreen.cs](../../../src/HelpScreen.cs)이고**, 이 사본이
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
것은 리포트가 히스토리를 **읽는** 값입니다. [Options.cs](../../../src/Options.cs)에는 이 구분이
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

거부한 것을 남겨 둡니다. 나중에 「왜 이건 없지」가 다시 올라오기 때문입니다.

|글자|안 주는 이유|
|--|--|
|`-f`|**`--full`과 `--force-output`에 절대 주지 않습니다.** 캐시를 의심하는 것과 캐시를 믿고 출력만 다시 하는 것은 다른 질문이라 [Options.cs](../../../src/Options.cs)가 명시적으로 나눠 둔 것인데, `-f` 하나가 그 구분을 지웁니다|
|`-s`|`--serve`·`--stats`·`--silent`·`--skip-runtime-validation`이 모두 s입니다. 어느 하나에 주면 나머지 셋이 오답이 됩니다|
|`-t`|`--template`과 `--target-side`와 `--table`과 `--to`|
|`-p`|`--port`에 줄 수는 있지만, 배포마다 한 번 적는 값입니다. `--project`·`--prune`도 p를 원합니다|
|`-V`|`--version`. 대문자 하나를 기억해야 하는 스위치는 `--version`보다 나을 것이 없습니다|

## 5. 에필로그가 실어야 하는 것

전부 **코드에는 있고 help에는 없던** 것들입니다.

|항목|출처|
|--|--|
|종료 코드 0·1·2|[ExitCode.cs](../../../src/ExitCode.cs)|
|`TABBIT_ENV`|`--env`가 설정하고, 어긋나는 값은 덮어쓰지 않고 거부합니다|
|`TABBIT_MESSAGES`|`--messages`의 기본값|
|`TABBIT_SERVE_TOKEN`|`--bind`가 루프백이 아닐 때 필수|
|`@FILE`|[Program.cs](../../../src/Program.cs) — 인자를 파일에서 읽습니다. **help에 한 줄도 없어서 소스를 읽은 사람만 압니다**|

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
