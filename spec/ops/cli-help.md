# CLI 도움말 — 첫 화면이 이 도구가 무엇인지 말하게 하기

> [문서 목록으로](../../doc/readme.md)
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

[CommandLineParser](../../src/Program.cs)의 `HelpText.AutoBuild`가 만드는 형태입니다. 결함을
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
|`--keep`만 기본값을 두 번 적습니다|`(Default: 100) Most recent snapshots to leave alone. 100 by default.`|
|`--verbose`·`--silent`·`--debug`만 어체가 다릅니다|`Sets whether to output debugging log messages.` — 설명이 아니라 프로퍼티 서술입니다. `all logging message`는 오타입니다|
|`--debug`가 문서와 다른 말을 합니다|help는 `internal debugging`, [doc/cli.md](../../doc/cli.md)는 「콜스택까지 출력」|

`.NET` 버전도 어디에도 없습니다. [ToolVersion.Runtime](../../src/ToolVersion.cs)이 이미
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

## 이 문서의 나머지

|무엇|어디|
|--|--|
|[화면](cli-help/screens.md)|도움말 화면의 구성 · 짧은 스위치 · 에필로그 · 인자가 틀렸을 때|
|[7. 버전 줄](cli-help/version.md)|`--version` 이 실어야 하는 것과 그 근거|
|[게이트와 구현](cli-help/gates.md)|도움말과 실제가 어긋나지 않게 하는 게이트, 그리고 구현 기록|
