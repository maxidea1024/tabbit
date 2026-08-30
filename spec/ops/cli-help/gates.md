# 게이트와 구현

> [「CLI 도움말」로 돌아가기](../cli-help.md)

---

## 8. 어긋남을 막는 게이트

손으로 쓴 help의 유일한 위험은 **옵션이 늘 때 조용히 낡는 것**입니다. 테스트 하나가
그것을 닫습니다.

|검사|무엇을 잡습니까|
|--|--|
|`[Option]` 이름 전부가 help 본문에 나옵니다|옵션을 추가하고 help에 안 적은 것|
|help 본문에 적힌 `--이름` 전부가 실재합니다|옵션을 지웠는데 help에 남은 것, 그리고 오타|
|짧은 이름이 선언된 옵션은 help에도 짧은 이름과 함께 나옵니다|`-e`를 붙였는데 화면에는 안 적은 것|
|`tabbit --help`의 종료 코드가 0|§6이 회귀하는 것|
|`tabbit --version`이 `.NET`을 담은 줄을 냅니다|런타임 줄이 빠지는 것|
|인자 없는 실행의 종료 코드가 1이고 출력이 5줄 이하|다시 90줄을 쏟는 것|

리플렉션으로 `Options`의 프로퍼티를 훑으므로, **옵션을 추가하는 사람이 이 테스트를
기억할 필요가 없습니다.** 잊으면 게이트가 보고합니다.

## 9. 하지 않는 것

|안 하는 것|이유|
|--|--|
|help의 다국어화|`--messages`는 5개 언어인데 help는 영어 고정으로 둡니다. §3의 분량을 5개로 유지하는 비용이 실제로 있고, 그 비용을 낼 근거가 아직 없습니다. **의도적 결정**이고, 필요해지면 [메시지 카탈로그](../../validation/message-ids.md)가 이미 그 자리입니다|
|서브커맨드 (`tabbit history …`)|여섯 모드가 서브커맨드에 잘 맞기는 합니다. 그런데 그것은 **호출 호환성을 깨는 변경**이고, 이 문서가 고치려는 것은 화면입니다. 화면이 모드를 말하기 시작하면 그때 서브커맨드가 정말 필요한지 다시 볼 수 있습니다|
|`--help <topic>`|위 화면이 한 화면에 안 들어오는 것은 사실입니다. 다만 지금 문제는 분량이 아니라 구조이고, 구조를 먼저 준 다음에 분량이 여전히 문제인지 봐야 합니다|
|`-p`·`-f`·`-s` 추가|§4|
|문서 URL|저장소가 private이라 지금 외부에서 열리지 않습니다. `doc/cli.md` 상대 경로로 둡니다|

## 10. 닿는 파일

|파일|무엇|
|--|--|
|[src/HelpScreen.cs](../../../src/HelpScreen.cs) (새로)|§3의 본문. 원시 문자열 리터럴 하나|
|[src/Program.cs](../../../src/Program.cs)|`--help`·`-h`·`--version` 가로채기, `AutoHelp` 끄기, §6의 짧은 출력, 종료 코드|
|[src/Options.cs](../../../src/Options.cs)|짧은 이름 5개 추가, 백틱 제거, `--keep` 중복 제거, `--verbose`·`--silent`·`--debug` 문구 교정|
|[src/ToolVersion.cs](../../../src/ToolVersion.cs)|`Built` 추가|
|[src/Tabbit.csproj](../../../src/Tabbit.csproj)|§7.1의 `AssemblyMetadata`|
|[.github/workflows/release.yml](../../../.github/workflows/release.yml)|`-p:BuildTimestamp=`|
|[test/Tabbit.Tests/CliHelpTests.cs](../../../test/Tabbit.Tests/CliHelpTests.cs) (새로)|§8. 14개|
|[doc/cli.md](../../../doc/cli.md)|`--debug` 설명을 help와 맞추고, 짧은 스위치와 `@FILE`을 옵션 표에 넣습니다|

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
