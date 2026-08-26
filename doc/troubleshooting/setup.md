# recipe · 검증 · 외부 연결

> [「트러블슈팅」으로 돌아가기](../troubleshooting.md)

---

## Recipe

### `Recipe 'Targets[2]' has no 'Type', so there is nothing to say which target it configures`

`Targets`의 각 항목은 무엇을 만들지 나타내는 `Type`이 있어야 합니다.

### `Recipe 'Targets[2]' names target 'csharpp', which does not exist`

오타이거나 없는 타깃입니다. 쓸 수 있는 이름을 메시지가 함께 적어줍니다.

### `Recipe '...' sets up target 'csharp', but could not be read`

그 타깃에 없는 설정을 적었습니다. `FileExtention` 같은 오타가 **말없이 기본값으로 넘어가지
않도록** 오류입니다 — 그냥 넘어가면 증상이 "설정이 안 먹는다"로만 보입니다.

### `Recipe section '...' has TargetSide 'client-only', which is not recognized`

`client`, `server`, `both` 중 하나입니다.

### `Recipe setting 'ArrayDelimiter' is '...', but it must be exactly one character`

배열 구분자는 한 글자입니다.

### `Recipe '...' reads workbooks from '...', which does not exist`

`Sources`의 경로가 없습니다. 경로는 **CLI를 실행한 위치 기준**입니다.

---

## 검증 규칙

전체 사용법은 「[검증](../validation.md)」에 있고, 여기 있는 것은 규칙 파일을 쓰다 만나는 메시지입니다.

### `The recipe's 'Validation.Path' is '...', and there is no folder there`

지정했는데 없는 폴더입니다. **오류인 이유**는 오타 하나로 검증 전체가 걸리는 것 없이 통과하기
때문입니다. 검증 없이 돌리려면 `Path`를 비웁니다.

### `The validation folder '...' has a subfolder 'X', which is not one this layout runs`

`pre` · `tables` · `global` · `runtime` · `shared` 다섯뿐입니다. `table/`처럼 이름이 어긋난
폴더는 규칙이 하나도 돌지 않고, 산출물의 어디에도 그 사실이 남지 않습니다. 작업 중인 폴더는
`#`로 시작하면 건너뜁니다.

### `'rules/tables/X.cs' is a rule for table 'X', which this model does not have`

테이블 이름이 바뀌었거나 파일 이름에 오타가 있습니다. **오류인 이유**는 규칙이 말없이 안 도는
것이 더 나쁘기 때문입니다 — 비슷한 이름이 있으면 메시지가 함께 적어줍니다. 한 테이블에 대한
규칙이 아니라면 `rules/global/`로 옮깁니다.

### `'...' has nothing to run`

`public static void Validate(<그 단계의 컨텍스트> context)`가 없는 파일입니다. 규칙 파일은
클래스 하나에 `Validate` 하나이고, 진입점이 없는 헬퍼는 `rules/shared/`에 둡니다 — 거기 있는
것은 모든 규칙과 함께 컴파일되고 그 자체로는 실행되지 않습니다.

### `This rule reads the validation option 'X', which the recipe does not set`

recipe의 `Validation.Options`에 넣거나, 없어도 되는 값이면 `Option("X", 기본값)`을 씁니다. **빈
문자열로 말없이 대신하지 않는 이유**는 로케일 비교가 아무것과도 맞지 않아 **아무것도 검사하지
않는 규칙**이 되기 때문입니다.

### `This rule opens an external store, which only the 'rules/runtime/' rules may do`

`Db()`·`Redis()`를 `rules/runtime/` 밖에서 불렀습니다. 그 폴더가 `--skip-runtime-validation`이
건너뛰는 단위이므로, 밖에 있는 연결은 접근 권한이 없는 기계에서 무엇을 건너뛰든 실패합니다.

### `'...' made '50' more report(s) than the '100' shown`

한 규칙이 상한을 넘겨 보고했습니다. **규칙 자체가 틀린 경우가 대부분입니다** — 실제로 상점
규칙을 이식할 때 대상 테이블 하나를 빠뜨려 4,400건이 나왔습니다. 조건을 먼저 의심하세요.

### 규칙 파일의 컴파일 오류

검증 오류와 같은 경로로, 파일·줄·열과 함께 보고합니다. 한 파일이 깨져도 나머지는 전부
컴파일하므로 한 번에 전부 나옵니다. **이것이 타입을 쓰는 이유입니다** — 없는 컬럼이나 없는 enum
값은 실행 중의 드러나지 않는 미스가 아니라 여기서 걸립니다.

---

## 구글 스프레드시트

### 브라우저가 열리고 인증을 요구함

첫 실행에는 OAuth 동의가 필요합니다. 토큰은 홈 디렉터리 아래
`.credentials/sheets.googleapis.com-tabbit`에 저장되므로 다음부터는 묻지 않습니다.

### `Recipe '...' names client secret file ...`

클라이언트 시크릿 파일 경로가 잘못됐거나 파일이 없습니다. 발급 절차는 [시트 작성](../sheets.md)의
「Google Spread Sheets」에 스크린샷과 함께 있습니다.

> 시크릿 파일은 **커밋하지 마세요.** 저장소 히스토리에 한 번 들어가면 지워도 이미 복제된 사본에는 남습니다.

### 갑자기 인증이 안 됨

홈 디렉터리의 `.credentials/sheets.googleapis.com-tabbit`을 지우고 다시 실행하면 재인증합니다.

---

## 데이터베이스

### `Recipe section '...' has no ConnectionString`

연결 문자열이 없습니다.

### 연결 문자열의 비밀번호 취급

**적지 마세요.** `${VAR}` 형식으로 쓰면 환경 변수에서 채웁니다.

```
Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}
```

변수가 설정되어 있지 않으면 **오류이고, 어느 변수인지 이름으로 출력합니다.** 빈 문자열로 말없이
치환하지 않습니다 — 그러면 인증 실패가 "비밀번호가 틀렸다"로 보이고, 진짜 원인인 "변수를 안
넣었다"는 어디에도 안 나옵니다.

### `MySQL exporter cannot map type '...' of column '...'`

그 엔진으로 옮길 수 없는 타입입니다.

### `Could not clean up MySQL shadow tables` / `Redis refused the swap transaction`

적재는 섀도 테이블에 한 뒤 원자적으로 교체합니다. 교체가 거부되면 **기존 데이터는
그대로**입니다. 정리 실패는 경고이고 남은 섀도 테이블은 다음 실행이 덮어씁니다.

---

## `--serve`

### 400과 함께 메시지가 옴

요청이 잘못됐습니다. 메시지에 무엇이 잘못됐는지 나옵니다.

### 500과 사건 ID만 옴

도구 쪽 문제입니다. 상세는 서버 로그에 그 ID로 남습니다.

### `--bind ... is not an address`

IP, `localhost`, 또는 모든 인터페이스를 뜻하는 `0.0.0.0`이어야 합니다.

### 외부 바인딩을 거부함

`127.0.0.1` 밖으로 열려면 토큰이 필요합니다. 히스토리에는 시트 내용과 그것을 건드린 사람들의
이름이 들어 있기 때문입니다.

환경 변수로 토큰을 주고 `Authorization: Bearer <token>`으로 보내세요. 브라우저로 열 때는
`?token=<token>`을 한 번 붙이면 HttpOnly 쿠키로 바뀌고 URL에서 사라집니다.

### `The history holds no project called '...'`

프로젝트 키가 다릅니다. 기록할 때 쓴 것과 같아야 합니다.

### `No working copy was found, so a range can only be asked for by commit hash`

`HEAD~3` 같은 상대 표기는 워킹카피가 있어야 풉니다. 없으면 커밋 해시로 지정하세요.

---
