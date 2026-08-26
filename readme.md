<a href="doc/readme.md">
  <img src="brand/dist/readme-header.jpg" alt="Tabbit — Game Data Authoring & Build Tool" width="100%">
</a>

# Tabbit

**Game Data Authoring & Build Tool**

[![.NET](https://github.com/maxidea1024/tabbit/actions/workflows/dotnet.yml/badge.svg)](https://github.com/maxidea1024/tabbit/actions/workflows/dotnet.yml)
[![Names](https://github.com/maxidea1024/tabbit/actions/workflows/names.yml/badge.svg)](https://github.com/maxidea1024/tabbit/actions/workflows/names.yml)
[![Docs](https://github.com/maxidea1024/tabbit/actions/workflows/docs.yml/badge.svg)](https://maxidea1024.github.io/tabbit/)
[![release](https://img.shields.io/github/v/release/maxidea1024/tabbit?sort=semver)](https://github.com/maxidea1024/tabbit/releases)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

엑셀과 구글 스프레드시트에 적은 게임 데이터를 검증하고, 런타임 데이터와 그것을 읽는 코드로
빌드합니다.

| 단계 | 하는 일 |
| --- | --- |
| **Author** | 스프레드시트에 테이블, enum, 상수셋을 작성합니다 |
| **Schema** | 시트에서 타입, 인덱스, 참조, 제약을 읽습니다 |
| **Validate** | 타입으로 표현할 수 없는 규칙까지 검사하고, 문제가 발생한 셀을 알려줍니다 |
| **Analyze** | 생성된 데이터를 HTML로 확인하고, 커밋 사이의 변경을 셀 단위로 추적합니다 |
| **Optimize** | 컬럼의 데이터 특성에 맞는 인코딩을 선택합니다 |
| **Build** | 런타임 바이너리와 이를 읽는 코드를 언어별로 생성합니다 |

## 시작하기

현재는 [소스에서 빌드합니다](doc/install.md).

```bash
tabbit --new-recipe my-recipe.json --template unity
tabbit --recipe my-recipe.json
```

시트 한 장이 실제 코드로 어떻게 변하는지는
[시트에 무엇을 적을 수 있나](doc/concepts.md)에서 처음부터 끝까지 따라갈 수 있습니다.

## 문서

[문서 목록](doc/readme.md)이 시작점입니다. 사이트로는 <https://maxidea1024.github.io/tabbit/>.

| 문서 | 내용 |
| --- | --- |
| [시트 작성](doc/sheets.md) | 데이터 배치, 엔티티 마커, 이름 규칙, 지원 타입 |
| [CLI](doc/cli.md) · [Recipe 파일](doc/recipe.md) | 실행 방법과, 어디서 읽고 어디로 출력할지 |
| [언어별 가이드](doc/languages/readme.md) | 생성된 코드를 프로젝트에 적용하는 방법 |
| [검증](doc/validation.md) | 시트로 표현할 수 없는 규칙을 C#으로 |
| [내보내기](doc/exports.md) · [바이너리 형식](doc/binary-format.md) | 출력 대상과 런타임 데이터 형식 |
| [트러블슈팅](doc/troubleshooting.md) | 빌드 실패 시 실제 출력 메시지를 기준으로 |
| [설계 노트](doc/readme.md#설계-노트) | 기능과 형식이 현재 구조가 된 이유 |

## 기여하기

버그와 제안은 [이슈](https://github.com/maxidea1024/tabbit/issues)로 알려주세요.

- 개발과 테스트 방법은 [아키텍처와 개발](doc/architecture.md)에 있습니다.
- 생성기나 템플릿을 수정하였다면 골든 데이터를 다시 기록하고 diff를 확인해 주세요.
- 보안 문제는 공개 이슈가 아닌 [SECURITY.md](SECURITY.md)의 절차를 따라주세요.

변경 내역은 [CHANGELOG.md](CHANGELOG.md), 사용하는 외부 패키지는
[의존 패키지](doc/dependencies.md)에 있습니다.

## 라이선스

코드와 문서는 [MIT](LICENSE)입니다. 생성된 코드와 함께 제공되는 테이블 리더 및 업데이터도
동일합니다. 생성물에 이 저장소의 라이선스를 표시할 의무는 없으며, 시트에서 생성된 코드와
데이터의 소유권은 이를 만든 프로젝트에 있습니다.

**이름과 로고는 다릅니다** — [브랜드 자산의 이용 조건](brand/license.md). 가리키는 데는
마음껏 쓰시고, 후원이나 제휴로 읽힐 자리에는 두지 말아 주십시오.
