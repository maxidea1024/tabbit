# Tabbit 구현 분석 및 개선사항 (2026-08-04)

대상: `main` @ `c014a27`. 소스 22,539줄(129파일) · 테스트 7,016줄(241개) · 타깃 20종 · 커밋 42개.

최초 분석은 읽기 전용으로 수행하였습니다. 확인은 실제 빌드와 생성 산출물로 하였습니다.
§1의 「상태」 열은 이후 조치까지 반영한 것이고, 각 절의 서술은 문제를 발견한 시점의 기록입니다.

---

## 1. 요약

번호는 아래 본문 절과 동일합니다. 표는 마지막 갱신 시점의 상태입니다.

| # | 항목 | 원래 등급 | 상태 |
|---|---|---|---|
| 3-1 | 값 조회 실패가 **빈 셀로 기록됨** | 높음 | 완료 — 예외를 throw하도록 수정 + 테스트 5개 |
| 3-2 | 값 풀 GC의 락 범위와 주석의 불일치 | 중간 | 완료 — FK RESTRICT(마이그레이션 5) + 수거 내성 + 증명 테스트 |
| 3-3 | `--serve`의 오류 무고지 | 높음 | 완료 — `Report` 미들웨어. `TabbitException`→400+메시지, 그 외→500+사건 ID, 상세는 Serilog. 400 게이트 2개 |
| 3-4 | Html 타깃의 **폐쇄된 CDN** 참조 | 중간 | 완료 — 인라인화. 무효 enum 링크·빈 이름도 함께 |
| 3-5 | 엔티티 범위 초과가 **빈 사각형으로 무시됨** | 중간 | 완료 — 예외로 전환 |
| 4-1 | 커밋된 **OAuth 시크릿 미폐기** | 높음 | 보류 — 스크린샷은 가림 처리. **콘솔 폐기는 사용자 작업** |
| 4-2 | 테스트 프로젝트에 **취약 패키지 8건 재유입** | 중간 | 완료 — 테스트 프로젝트에서 직접 고정. 솔루션 전체 취약 0건·빌드 경고 0건 |
| 4-3 | serve 토큰의 쿼리스트링 전달 | 낮음 | 완료 — `Authorization: Bearer` 우선. `?token=`은 HttpOnly·SameSite=Strict 쿠키로 교환하고 페이지만 리다이렉트 |
| 5-2 | Unreal이 **엔진 타입 대신 `std::`를 사용** | 높음 | 완료 — UE 네이티브 테이블 리더 |
| 5-3 | Unreal 리더가 **예외를 throw하는데 모듈은 예외 비활성** | 높음 | 완료 — 누적 플래그로 전환 |
| 5-5 | Unreal 데이터가 **블루프린트에서 도달 불가** | 중간 | 완료 — `UBlueprintFunctionLibrary` + 게이트 2개 |
| 5-6 | Unreal의 적합성 코퍼스 미편입 | 낮음 | 완료 — 엔진 타입 스텁 기반 오프라인 하네스. 12개 언어 전부 코퍼스를 읽음 |
| 6 | 제너레이터의 **구조적 중복** | 중간 | 완료 — 본질(신규 타입 누락 지점)은 폐쇄 — 아래 참조 |
| 7-1 | `OUT/` 오커밋 | 낮음 | 완료 — 정식화(`OUT/showcase.json`) |
| 7-2 | 루트 오염 — 실수가 아니라 **가드 누락 결함** | 낮음 | 완료 — 가드 + 루트 전체 스냅샷 검사 |
| 10 | 타깃이 **테이블 전부를 한 파일에** 출력 — 삭제한 테이블의 코드가 잔존 | 보고서 이후 | 완료 — **12개 언어 전부 분할 + 스윕** — 아래 §10 |

**§6의 상세는 §9 아래에 기재하였습니다.** 요약하면 「신규 타입을 누락할 수 있는 자리」라는
본질은 두 게이트로 폐쇄하였고, 줄 수 자체의 감소는 수행하지 않았습니다 — 스크립트로 13개
파일을 일괄 수정하는 과정에서 동작하던 제너레이터를 두 차례 훼손하였고, 두 번째에는
`git checkout`으로 되돌리면서 같은 디렉터리의 §5-5 작업까지 함께 소실되었습니다(복구하였습니다).
이득은 유지보수성에 한정되고 위험은 실제로 발현되었습니다.

**Unreal 타깃은 상위 `layer`만 UE 규약을 따르는 구조였습니다.** USTRUCT·UENUM `layer`는 관례를
준수하나(§5-1), 그 아래 테이블 리더는 C++ 타깃 파일을 수정 없이 그대로 포함하고 어댑터로
UE 타입에 다시 대응시켰습니다.

> **§5-2 · §5-3은 본 보고서 작성 중에 수정하였습니다.** 아래 두 절은 무엇이 왜 문제였는지의
> 기록이고, 조치 결과는 §5-4에 있습니다. 나머지 항목은 당시 수정하지 않았습니다.

**전반적 평가는 양호합니다.** 게이트 설계 — 10개 언어를 실제로 컴파일·실행하여 익스포터
JSON과 대조하는 적합성 코퍼스, API와 CLI를 바이트 단위로 비교하는 검증, 골든 트리 — 는 이런
종류의 도구에서 보기 드문 수준이고, 아래 결함 중 어느 것도 그 설계가 놓친 것이 아니라
**게이트가 닿지 않는 자리**에 위치합니다.

---

## 2. 현재 구현 전경

### 2-1. 파이프라인

```
소스(xlsx / Google Sheets) → RawModel → ModelCooker → Model → 20개 타깃
```

소스·타깃 모두 `[TabbitSource]` / `[TabbitTarget]` 어트리뷰트 스캔으로 등록됩니다. 언어 하나를 추가할 때 `RecipeModel`이나 `Program`을 수정하지 않아도 되는 구조입니다.

| 종류 | 타깃 |
|---|---|
| 익스포트 (2) | `binary` `json` |
| 데이터베이스 (4) | `mysql` `postgresql` `mongodb` `redis` |
| 코드 생성 (12) | `csharp` `cpp` `typescript` `go` `rust` `python` `java` `kotlin` `ruby` `dart` `unreal` `html` |
| 히스토리 (2) | `summary` `history` |

### 2-2. 검증 게이트

| 게이트 | 검사 내용 |
|---|---|
| 적합성 코퍼스 | 경계값 테이블 하나를 **12개 언어로 컴파일·실행하여 판독하고** 익스포터 JSON과 대조 (`ConformanceTests`, 12개) |
| 예약어 컴파일 | 키워드 이름 필드를 **12개 언어로 컴파일** (`ReservedWordTests`) |
| 헤더 자립 | C·C++ 헤더 각각이 번역 단위의 유일한 include로 컴파일되는지 여부 (`HeaderIncludeTests`) |
| 골든 트리 | 워크북 변환 후 전 산출물 바이트 비교, 타임스탬프만 정규화 |
| 데이터베이스 | docker로 4개 엔진을 기동하고 적재 후 **서버에 직접 질의** |
| 웹서버 | 실제 포트에 기동하고 **API 응답과 CLI 출력을 바이트 비교** |
| 셀프컨테인드 | CI가 매 실행 linux-x64 퍼블리시 후 그 산출물로 변환 |

빌드 상태는 양호합니다 — Release에서 오류 0, **경고 0**. 보고서 작성 시점의 경고 16건은 전부 §4-2 한 건에서 발생하였고, 해당 항목은 해결되었습니다.

### 2-3. 히스토리

세 단계 지문(모델→테이블→로우)으로 하강하며 비교하고, 값은 내용 주소 풀에 등재합니다. 스키마는 4차 마이그레이션까지 적용되어 있고 `--prune`으로 오래된 변경 상세와 참조 없는 값을 회수합니다. 브랜치별 이름 락으로 동시 변환의 seq 경합을 방지합니다 — 이 부분의 설계는 견고합니다.

---

## 3. 동작 정확성

### 3-1. 값 조회 실패 시의 빈 셀 기록 — 높음

`src/History/HistoryStore.cs:547`

```csharp
private static object ValueId(IReadOnlyDictionary<string, long> values, string text)
    => text != null && values.TryGetValue(text, out long id) ? id : (object)DBNull.Value;
```

`text != null`이면서 조회가 실패하면 `DBNull`을 기록합니다. 그런데 이 스키마에서 **`NULL`은 「모름」이 아니라 「셀이 비어 있었음」을** 의미합니다. 앞의 조건이 이미 `text != null`로 필터하므로, 뒤의 `DBNull`에 도달하는 유일한 경로는 **값이 존재하는데 id를 찾지 못한 경우**뿐입니다.

결과 — 리포트가 「이 셀이 값 X로 변경되었다」 대신 「**이 셀이 비워졌다**」로 출력합니다. 조회는 전부 `LEFT JOIN value`(`HistoryQuery.cs:305,306,552,553`)이므로 NULL이 그대로 통과합니다. 실패하는 대신 값이 달라지는, 이 프로젝트가 정확히 방어하려는 형태입니다.

**수정 범위는 한 줄입니다** — 조회가 실패하면 예외를 throw합니다. `DBNull`은 `text == null`인 경우로 한정합니다.

이 폴백은 `ResolveValues`가 실패하지 않는다는 전제에 의존하는데, 그 전제를 보장하는 장치가 없습니다. 아래 §3-2가 그 전제가 무너지는 경로입니다.

### 3-2. 값 풀 GC의 락 범위와 주석의 불일치 — 중간

`src/History/HistorySchema.cs:49`의 주석:

> 그것[브랜치 쓰기 락]이 값 풀을 수거할 수 있게 해주는 것이기도 합니다. 이것이 없으면 prune이 변환이 값을 찾은 시점과 참조하는 시점 사이에서 값을 삭제할 수 있습니다.

그런데 락 이름은 `tabbit_history_write:{projectId}:{branch}`(`HistorySchema.cs:52`)로 **브랜치별**인데, `HistoryMaintenance.Collect`(`HistoryMaintenance.cs:212`)가 삭제하는 `value` 테이블은 **프로젝트·브랜치 전역 공유**입니다.

`main` 브랜치를 prune하는 동안 `dev` 브랜치(또는 다른 프로젝트)가 변환 중이면 두 프로세스는 **서로 다른 락**을 획득합니다. 워터마크는 GC 시작 이후 삽입된 값만 보호하고, 외래 키는 스키마에 **하나도 없습니다.**

이 구간이 실제로 발생하는지는 InnoDB의 중복키 잠금 동작(`INSERT IGNORE`가 기존 행에 S락을 거는지)에 의존하는데, **코드는 그것을 주장하지도 검증하지도 않습니다.** 안전하다면 우연히 안전한 것입니다.

§3-1과 결합하면 — GC가 삭제한 값을 변환이 찾지 못하고 → `DBNull` → 리포트에 빈 셀. 고지는 없습니다.

**대응안** (택1)

- `cell_change.old_value_id` / `new_value_id` / `cell_current.value_id`에 **FK RESTRICT**. 삭제가 실패하고 해당 값은 제외됩니다. 가장 확실하나 큰 테이블에 제약을 부과합니다.
- `Collect`를 **전역 GC 락**으로 감싸고, `ResolveValues`도 같은 락을 짧게 획득합니다. 변환 간 동시성은 유지됩니다.
- 값 풀을 **프로젝트별로 분리**하고 락 범위를 정합합니다. 중복 제거 효율이 다소 저하됩니다.

FK를 권합니다 — 불변식을 데이터베이스가 강제하는 편이 주석이 담당하는 것보다 낫습니다. 위 주석이 그 사례입니다.

### 3-3. `--serve`의 오류 무고지 — 높음

`src/History/HistoryServer.cs`에는 예외 처리가 **없습니다.** `UseExceptionHandler`도, 핸들러 내부 `try`도 없습니다.

`--from`에 없는 커밋을 지정하면 `HistoryQuery`가 `TabbitException("... no snapshot ...")`을 throw하고 → ASP.NET 기본 미들웨어가 처리하여 **본문 없는 500**을 반환합니다. 그리고 `builder.Logging.ClearProviders()`(`HistoryServer.cs:59`)로 로깅 공급자를 전부 제거하였기 때문에 **미처리 예외 로그도 출력되지 않습니다.**

즉 잘못된 요청 하나가 **클라이언트에도 서버에도 흔적을 남기지 않습니다.** CLI는 같은 상황에서 정확한 문장을 출력합니다. API와 CLI가 같은 결과를 반환하도록 바이트 비교까지 수행하는 설계인데, **오류 경로만 그 대칭에서 제외되어 있습니다.**

`HistoryServerTests`(286줄)도 성공·304·401만 확인하고 4xx/5xx 경로는 없습니다.

**대응** — `TabbitException` → 400 + 메시지, 그 외 → 500 + 상관 ID로 매핑하는 미들웨어를 추가합니다. Serilog를 `ILoggerProvider`로 재부착하거나, 최소한 미처리 예외만 Serilog로 전달합니다. 테스트도 한 건 — 없는 커밋에 대해 400과 그 안의 메시지.

### 3-4. Html 타깃의 폐쇄된 CDN 참조 — 중간

`src/templates/html-head.sbn:9-10`

```html
<link rel="stylesheet" href="https://stackpath.bootstrapcdn.com/bootstrap/4.3.1/css/bootstrap.min.css" ...>
<script src="https://stackpath.bootstrapcdn.com/bootstrap/4.3.1/js/bootstrap.min.js" ...></script>
```

세 가지가 중첩됩니다.

1. **StackPath BootstrapCDN은 서비스를 종료하였습니다.** 생성된 페이지는 현재 스타일 없이 렌더됩니다.
2. **프로젝트 자신의 원칙과 충돌합니다.** `src/History/web/history.js:13`은 CDN을 사용하지 않는 이유를 「도구는 폐쇄망에서 실행될 것으로 예상된다」로 기재하고 있습니다. `HistoryView.cs:16`도 동일합니다. 히스토리 페이지에만 적용된 원칙이 Html 타깃에는 적용되지 않았습니다.
3. **로컬 사본이 이미 존재하나 사용하지 않습니다.** `lib/bootstrap-4.3.1-dist/`가 커밋되어 있고, 참조하는 코드는 없습니다.

게이트가 검출하지 못한 이유는 명확합니다 — 골든 비교는 `<link>` 태그가 **커밋된 것과 동일한지**만 확인하고, 그 URL의 생존 여부는 확인하지 않습니다.

**대응** — `history.css`와 동일하게 임베디드 리소스로 인라인합니다. 히스토리 페이지가 이미 라이브러리 없이 SVG를 직접 생성하고 있으므로, Html 타깃도 부트스트랩 없이 구성할 수 있습니다. 그 경우 `lib/bootstrap-4.3.1-dist/`도 삭제합니다.

### 3-5. 엔티티 범위 초과의 무고지 처리 — 중간

`src/Cooking/ModelCooker.cs:627`

```csharp
if (y < 0 || y >= rawSheet.Rows.Count || x < 0 || x >= rawSheet.ColumnCount)
{
    //TODO 예외를 던져야하는거 아닐까?
    return new DefinitionRect { x = 0, y = 0, width = 0, height = 0 };
}
```

TODO의 지적이 타당합니다. 바로 아래 최소 크기 검사는 예외를 throw합니다(`:641`). 범위 초과만 빈 사각형을 반환하고, 해당 엔티티는 **출력에서 제외됩니다.** 시트 끝에 마커가 있는 워크북에서 테이블 하나가 누락된 채 변환이 성공합니다.

`ModelCooker.cs`에는 이런 TODO가 5건 더 있습니다(`:267 :296 :509 :632 :962`). 나머지는 스타일·설계 메모이므로 긴급하지 않습니다.

---

## 4. 보안

### 4-1. 커밋된 OAuth 시크릿의 미폐기 — 높음

확인한 사실은 다음과 같습니다.

- `af96691` "Stop tracking the committed OAuth secret and the published binaries" 로 추적만 해제
- 원본은 `d428c26`(최초 커밋)에 **잔존합니다**
- `doc/figures/google-oauth-5-secret-downloaded.png`가 **현재도 추적 중**이고, 스크린샷에 시크릿이 **평문으로 노출됩니다**

`.gitignore`(97행)도 「해당 자격증명은 삭제만이 아니라 Google Cloud 콘솔에서 폐기해야 합니다」로 기재하고 있습니다.

**추적 해제로는 해결되지 않습니다.** 공개 히스토리에 잔존하고 스크린샷으로도 노출됩니다. GCP 콘솔(프로젝트 `crested-photon-338102`)에서 **클라이언트 시크릿을 폐기하고 재발급**하는 것 외에 대안이 없습니다. 히스토리 재작성은 이미 클론된 사본에 대해 효력이 없습니다.

폐기 후 스크린샷도 시크릿이 가려진 것으로 교체합니다.

### 4-2. 테스트 프로젝트의 취약 패키지 재유입 — 중간

```
> System.Security.Cryptography.Xml  8.0.2  High × 8
```

이전 작업에서 발생시킨 회귀입니다. ASP.NET FrameworkReference가 이 어셈블리를 공유 프레임워크에서 공급하게 되면서 `src/Tabbit.csproj`의 pull-up이 중복이 되었고 — NuGet이 매 `dotnet run` 앞에서 그렇게 경고하였고 그로 인해 테스트 두 건이 실패하여 — 제거하였습니다. 그런데 **`test/Tabbit.Tests`에는 FrameworkReference가 없어서** NPOI의 전이 의존 8.0.2를 그대로 복원합니다.

| 프로젝트 | 결과 |
|---|---|
| `src/Tabbit.csproj` | **정상** (`dotnet list package --vulnerable` 확인) |
| `test/Tabbit.Tests` | **취약 8건** |

배포물에는 포함되지 않습니다. 다만 빌드 경고 16건이 전부 여기에서 발생하고, 무엇보다 **경고에 익숙해지면 유효한 경고를 놓칩니다.** 실제로 이 소음 때문에 `HistoryCommandTests`가 `IndexOf('{')`로 JSON을 잘라내는 우회를 갖게 되었습니다(`HistoryCommandTests.cs:65`).

**대응** — 테스트 프로젝트에 pull-up을 추가합니다. `src`의 주석(「이 프레임워크 참조가 제거되면 이것을 되돌린다」)은 정확하였으나, 테스트 프로젝트라는 두 번째 지점을 누락하였습니다.

### 4-3. serve 토큰의 쿼리스트링 전달 — 낮음

`HistoryServer.cs:201`이 `?token=`을 수용합니다. 브라우저에서 페이지를 여는 데 필요하므로 설계 의도는 이해되나, 쿼리스트링은 리버스 프록시 액세스 로그·브라우저 히스토리·`Referer`에 잔존합니다. HTTPS도 없으므로 토큰이 평문으로 전송됩니다.

비교는 `CryptographicOperations.FixedTimeEquals`로 적절히 구현되어 있고(`:221`), 비루프백 바인딩을 토큰 없이 거부하는 것(`:115`)도 타당합니다.

**개선** — 첫 요청에서 쿼리 토큰을 수신하면 `HttpOnly` 쿠키로 교환하고 리다이렉트하여 URL에서 제거합니다. 그리고 문서에 「리버스 프록시 뒤 TLS」를 명시합니다.

---

## 5. Unreal 타깃

### 5-1. 명명 규칙의 준수 상태

`test/fixtures/output/unreal/Source/TabbitCore/Public/FTabbitCore.h` 실측:

| 관례 | 상태 |
|---|---|
| `E` 접두 (enum) | `EValueType` `EGrade` `ESkillType` ✔ |
| `F` 접두 (USTRUCT) | `FItemRow` `FTestFieldTypesRow` ✔ |
| `F` 접두 (일반 클래스) | `FItemTable` `FTabbitCore` ✔ |
| PascalCase 멤버 | `Index` `StringField` `DatetimeField` ✔ |
| PascalCase 함수 | `Read` `Find` `Records` `ReadAll` ✔ |
| `b` 접두 (bool) | `bBoolField` ✔ |
| UE 타입 | `FString` `FGuid` `FDateTime` `FTimespan` `TArray` `TMap` `int32` `int64` ✔ |
| API 매크로 | `TABBITCORE_API` ✔ |

`U`(UObject) · `A`(Actor)가 없는 것은 위반이 아니라 **그 둘을 생성하지 않기 때문**입니다. 접두어 없는 snake_case는 일반 C++ 타깃(`OUT/cpp/A.h` — `struct TemplateRecord { std::int32_t index; }`)의 것이고, 그 언어에서는 타당합니다.

### 5-2. 테이블 리더의 UE 규약 미적용 — 높음

USTRUCT `layer`만 UE 규칙을 따르고, **그 아래 테이블 리더는 일반 C++ 타깃의 파일을 수정 없이 그대로 포함합니다.** `Public/TabbitTcbReader.h`:

```cpp
namespace tabbit {
class TcbError : public std::runtime_error { ... };
struct DateTime  { std::int64_t ticks = 0; };          // 엔진에 FDateTime 존재
struct TimeSpan  { std::int64_t ticks = 0; };          // 엔진에 FTimespan 존재
struct Uuid      { std::array<std::uint8_t,16> bytes;  // 엔진에 FGuid 존재
                   std::string to_string() const; };
void read(std::string& value);                          // 엔진에 FString 존재
```

그리고 그것을 되돌리기 위해 `unreal.sbn:36,48`이 어댑터를 부착합니다. 즉 **UE 타입 → 표준 C++ 타입 → 다시 UE 타입**의 경로입니다. 엔진에 이미 존재하는 것을 생성하여 사용하고 다시 폐기합니다.

생성된 `Private/FTabbitCore.cpp` 실측:

```cpp
{ std::string Temp{};       Reader.read(Temp); Name = TabbitConvert::ToString(Temp); }
{ tabbit::Uuid Temp{};    Reader.read(Temp); UuidField = TabbitConvert::ToGuid(Temp); }
```

셀당 비용은 다음과 같습니다.

| 타입 | 현재 | 대체안 |
|---|---|---|
| string | `std::string` 힙 할당 → `UTF8_TO_TCHAR` → `FString` 재할당·복사 = **할당 2회** | 버퍼에서 `FString`으로 직접 = 1회 |
| uuid | `Uuid` → `to_string()`(할당) → `FString`(할당) → **`FGuid::Parse` 텍스트 파싱** = 할당 3회 + 파싱 | 16바이트 → `FGuid` 직접 = 0회 |
| datetime | `tabbit::DateTime` → ticks → `FDateTime` | `FDateTime(Ticks)` 직접 |
| array | `std::vector` 경유 | `TArray` 직접 |

로컬라이제이션 테이블 하나가 메가바이트 단위라는 것은 템플릿 자신이 기재한 사실입니다(`unreal-cpp.sbn:86`). 그 규모에서 문자열마다 할당 2회, uuid마다 텍스트 파싱 1회는 불필요한 비용입니다.

### 5-3. 예외 비활성 모듈에서의 예외 사용 — 높음

테이블 리더는 8곳에서 예외를 throw합니다:

```cpp
throw TcbError("table data ended after " + std::to_string(position_) + " of " + ...);
throw TcbError("table format version ... is not supported");
throw TcbError("string length is negative");
```

그런데 생성된 `TabbitCore.Build.cs`는 **`bEnableExceptions`를 설정하지 않습니다.** Unreal은 C++ 모듈을 기본적으로 예외 비활성으로 빌드합니다.

더 중대한 것은 시그니처입니다:

```cpp
bool FItemTable::Read(const FString& Filename)   // ← bool 반환을 선언
{
    ...
    const std::int32_t RowCount = tabbit::read_table_header(Reader);   // ← try/catch 없음
```

`unreal-cpp.sbn:76-105` 어디에도 `try`가 없습니다. 파일 열기 실패만 `false`를 반환하고(`:79-83`), **손상된 `.tcb`는 예외로 이탈합니다.** 예외가 비활성인 모듈에서 그것은 복구 가능한 실패가 아니라 프로세스 종료입니다.

`bool`을 반환하는 함수가 이행할 수 없는 계약을 선언하고 있습니다.

### 5-4. 대응 — Unreal 전용 테이블 리더

**엔진에 이미 존재하는 것을 사용하는 편이 타당합니다.** 어댑터를 부가하는 것이 아니라 테이블 리더 자체를 UE 것으로 출력해야 합니다. UE 모듈 안에 표준 라이브러리 타입이 등장할 이유가 없습니다 — 전부 대응물이 존재합니다.

| 현재 (표준 C++) | 대체 (엔진 제공) |
|---|---|
| `std::string` | **`FString`** |
| `std::vector<T>` | **`TArray<T>`** |
| `std::array<uint8,16>` + `tabbit::Uuid` | **`FGuid`** |
| `tabbit::DateTime` (ticks 래퍼) | **`FDateTime`** (.NET과 같은 100ns ticks) |
| `tabbit::TimeSpan` (ticks 래퍼) | **`FTimespan`** (동일) |
| `std::int32_t` `std::int64_t` `std::uint8_t` | **`int32` `int64` `uint8`** |
| `std::size_t` | **`int32`** |
| `std::runtime_error` 상속 예외 | **`bool` 반환 + 오류 상태** (§5-3) |
| `std::ifstream` | **`FFileHelper::LoadFileToArray`** (이미 상위에서 사용 중) |

`FDateTime`·`FTimespan`의 손실이 특히 큽니다 — **둘 다 .NET과 동일한 100나노초 ticks**를 사용하므로 `FDateTime(Ticks)` 한 줄로 처리되는데, 현재는 같은 값을 담기만 하는 구조체를 별도로 정의하여 경유합니다.

`lib/unreal/tabbit/TabbitTcbReader.h`를 11번째 테이블 리더로 추가합니다 — C++ 리더의 파생이 아니라 형제입니다.

```cpp
class FTabbitBinaryReader
{
public:
    explicit FTabbitBinaryReader(TArrayView<const uint8> Data);

    bool ReadString(FString& Out);              // 버퍼 → FString 직접, 중간 std::string 없음
    bool ReadGuid(FGuid& Out);                  // 16바이트 → FGuid 직접, 텍스트 파싱 없음
    bool ReadDateTime(FDateTime& Out);          // ticks → FDateTime(Ticks)
    bool ReadTimespan(FTimespan& Out);          // ticks → FTimespan(Ticks)
    bool ReadInt32(int32& Out);  ...

    bool HasError() const { return bFailed; }
    const FString& GetError() const { return Error; }
};
```

**예외 대신 `bool` + 오류 상태**를 사용합니다. UE의 관례이고, `Read()`가 이미 `bool`을 선언하고 있으므로 그 계약을 실제로 이행하게 됩니다.

변경 대상:

| 파일 | 변경 |
|---|---|
| `lib/unreal/tabbit/TabbitTcbReader.h` | **신규.** UE 타입 네이티브 테이블 리더 |
| `src/Tabbit.csproj` | 임베디드 리소스 한 줄 추가 |
| `src/templates/unreal.sbn` | `namespace TabbitConvert` **삭제** — 불필요 |
| `src/templates/unreal-cpp.sbn` | `Temp` 왕복 제거, `Reader.ReadX(Field)` 직접 호출 |
| `src/CodeGeneration/UnrealCodeGenerator.cs` | `temp_type` / `from_temp` 뷰 필드 제거, `read_call`이 UE 메서드를 지시하도록 변경 |

`.tcb` 와이어 포맷은 유지되므로 **적합성 코퍼스가 그대로 적용됩니다** — 오히려 Unreal 리더가 코퍼스에 편입될 수 있게 되어 §5-6의 공백도 함께 해소됩니다. 현재는 C++ 리더를 재사용한다는 이유로 코퍼스 밖에 있는데, 전용 테이블 리더가 생기면 그 근거가 소멸하기 때문입니다. (엔진 없이 컴파일할 수 없다는 제약은 남습니다 — `TABBIT_UE_ROOT`가 존재할 때만 실행되는 게이트로 부착합니다.)

작업 범위는 위 5개 파일이고, **§5-3(예외) 때문에 우선순위가 높습니다** — 현재는 손상된 데이터 파일 하나가 프로세스를 종료시킵니다.

### 5-5. 블루프린트에서의 데이터 접근 불가 — 중간

`unreal.sbn:125`

```cpp
class TABBITCORE_API FTabbitCore   // UCLASS 아님
{
public:
    static const FItemTable& Item() { return ItemStorage; }   // UFUNCTION 아님
```

구조체는 전부 `USTRUCT(BlueprintType)`이고 프로퍼티도 `BlueprintReadOnly`인데, **접근자가 일반 static C++ 클래스**입니다. 블루프린트에서 `FItemRow` 변수를 보유할 수는 있으나, **그것을 취득할 방법이 없습니다.** C++ 코드를 경유해야 합니다.

`unreal.sbn:120-124`의 주석은 UObject를 사용하지 않는 이유를 「데이터가 어떤 월드보다 오래 유지되고, 시작 시 한 번 읽은 뒤 변경되지 않으며, 리플렉션이나 GC가 필요 없다」로 기재합니다. 수명 관리 논리로는 타당합니다. 다만 **리플렉션이 필요 없다는 부분은 사실과 다릅니다** — 모든 USTRUCT를 `BlueprintType`으로 출력하는 것 자체가 블루프린트 접근을 의도한 것인데, 그 의도가 마지막 단계에서 단절됩니다.

**대응** — 얇은 `UBlueprintFunctionLibrary`를 하나 더 출력합니다. 소유권은 그대로 static 저장소에 유지하고, 테이블별로 `UFUNCTION(BlueprintPure)` 두 개(`GetItemRow(int32 Index, bool& bFound)` / `GetAllItemRows()`)만 감싸면 됩니다. GC도 월드 의존성도 발생하지 않습니다.

### 5-6. Unreal의 적합성 코퍼스 미편입 — 낮음

`UnrealTargetTests`(93줄)가 확인하는 것은 파일 존재, include 순서, uint8 범위, 그리고 `TABBIT_UE_ROOT`가 존재할 때의 UHT 통과입니다. **판독한 값의 정확성은 확인하지 않습니다.**

주석(`UnrealTargetTests.cs:13`)의 근거 — 「와이어 포맷은 여기에서 재구현하지 않는다, 모듈은 C++ 타깃과 같은 테이블 리더를 포함하고 그것은 이미 코퍼스로 검증된다」 — 는 테이블 리더에 대해서는 타당합니다. 다만 **`FTabbitCore.cpp`가 생성하는 `Read()` 본문**은 Unreal 전용이고, `ToString`/`ToGuid` 변환(`unreal.sbn:36,48`)도 Unreal 전용입니다. FGuid 바이트 순서가 어긋나도 UHT는 통과합니다.

UE 엔진 없이 검증할 방법이 마땅치 않다는 현실적 제약이 있으므로, **긴급하지 않으나 알려진 공백으로 기록**합니다.

---

## 6. 코드 생성기의 구조적 중복 — 중간

| | 파일 | 줄 |
|---|---|---|
| `*CodeGenerator.cs` | 12 | 4,321 |
| `*View.cs` | 12 | 1,283 |
| 합계 | 24 | **5,604** |

`KotlinCodeGenerator`와 나머지를 비교하면 동일한 골격이 반복됩니다.

```
Run → Generate → WriteBinaryReaderRuntime
BuildView / BuildEnum / BuildConstantSet / BuildTable / BuildField
ReadExpression / RenderConstantValue / Quote / CommentLines / DefaultValue
```

특히 반복 비용이 큰 항목은 다음과 같습니다.

- **`WriteBinaryReaderRuntime`** — 리소스 이름과 파일명만 다른 동일 코드가 10벌
- **`ReadExpression`** — 동일한 `ValueType` switch가 언어별 문자열만 변경되어 12벌. 새 타입을 추가하면 **12곳을 수정해야 하고, 누락해도 컴파일됩니다**(`default:`에서 예외를 throw하므로 런타임에 드러납니다)
- **`Quote`** — 이스케이프 규칙이 언어마다 다르므로 분리가 타당하나, 구조는 동일
- **`CommentLines`** — 완전히 동일한 코드가 12벌

**제안(긴급하지 않음)** — `Target<TRecipe>` 아래에 `CodeGeneratorBase`를 배치하고 `WriteBinaryReaderRuntime`·`CommentLines`·전체 골격을 상향합니다. `ReadExpression`은 `LanguageProfile`로 이관하여 **타입×언어 표 하나**로 구성하면, 새 타입 추가 시 누락된 칸이 컴파일 오류가 됩니다.

실익 추정 — 언어 추가 비용이 ~430줄에서 ~200줄로, 그리고 **새 `ValueType` 추가 시 누락 가능 지점이 12곳에서 1곳으로** 감소합니다. 후자가 본질입니다.

---

## 7. 저장소 위생 — 낮음

### 7-1. `OUT/` 트리의 정식화 — 완료

`c43c18e`(2026-08-03) 문서 작업 중 함께 커밋된 30개 파일이고, 최초에는 삭제를 제안하였습니다. **그 제안을 철회합니다.**

이 트리는 `reserved-words` 픽스처를 **11개 언어 전부로 생성한 결과**이고, 회귀 스위트가 수행하지 못하는 역할을 합니다 — 11개 제너레이터의 실제 출력을 **나란히, diff로** 제시합니다. §5의 Unreal 문제를 발견한 것도 스위트가 아니라 이 트리입니다. 게이트는 전부 통과 상태였습니다.

그래서 재현 가능하도록 구성하였습니다:

```sh
dotnet run --project src/Tabbit.csproj -- --recipe OUT/showcase.json --silent
```

`OUT/showcase.json`을 추가하였고, 무엇을 왜 포함하는지 파일 안에 기재하였습니다. **바이너리 익스포트는 제외하였습니다** — 런타임 형식이고 골든 트리가 이미 덮으며, diff에서 판독할 수 없습니다. JSON과 Html은 유지하였습니다.

재생성 직후 이 방식의 값어치가 확인되었습니다 — 커밋되어 있던 `OUT/dart/a.dart`에 **이전에 수정한 `int int = 0;` 결함이 그대로 잔존하였습니다.** 산출물을 커밋해 두고 갱신하지 않으면 그 자체가 사실과 어긋나게 됩니다.

### 7-2. 저장소 루트의 산출물 — 실수가 아니라 가드 누락 결함 — 완료

| 경로 | 커밋 |
|---|---|
| `TabbitAccessor.cs` `TabbitBinaryReader.cs` `index.ts` `tabbit/tcb_reader.ts` | `b24bc72` `2818a66` (2026-07-30) |

삭제로 종결할 사안으로 기재하였으나, 삭제 후 스위트를 실행하자 **재생성되었습니다.**

원인 — `--new-recipe` 스켈레톤의 모든 엔트리는 `Path`가 빈 문자열이고, 제너레이터 12개 중 **`csharp`과 `typescript`에만 빈 Path 가드가 없었습니다.** `Path.Combine("", "GameData.cs")`는 무효가 아니라 **상대 경로**이므로, 두 타깃이 작업 디렉터리에 파일을 기록합니다. `SourceRegistryTests`가 그 스켈레톤을 실제로 실행하므로, 테스트를 실행할 때마다 저장소 루트가 오염되었습니다.

그리고 **그 테스트는 바로 이 결함을 검출하려고 작성된 것이었습니다:**

```csharp
// The HTML target had no blank-path guard, so `Path.Combine("", "index.html")` put
// three pages in the working directory ...
Assert.Empty(Directory.GetFiles(RepoLayout.Root, "*.html"));
```

Html 타깃이 원인이던 시점에 작성되었고, **확장자 하나만 확인합니다.** 그래서 csharp·typescript는 계속 산출물을 남겼고 테스트는 계속 통과하였습니다.

조치:

- `CsCodeGenerator` · `TsCodeGenerator`에 나머지 열 개와 동일한 가드 추가
- 테스트를 **루트 전체 스냅샷 비교**로 교체 — 확장자가 아니라 「없던 것이 생성되었는가」를 확인합니다. 임의로 작성된 확장자 목록에 의존하지 않습니다

교훈은 §3-1과 동일합니다. 고지 없이 그럴듯한 값을 생성하는 폴백(`Path.Combine`의 빈 문자열, `ValueId`의 `DBNull`)이 실패보다 나쁩니다.

### 7-3. 미사용 파일

- `src/Tests/NameCaseTest.cs.txt` — 2022-01-27 최초 커밋 이후 수정되지 않았고, `.txt`이므로 빌드에도 포함되지 않습니다. 안의 TODO(「이건 동작을 안하네?? 왜지??」)도 4년 된 것입니다
- `lib/bootstrap-4.3.1-dist/` — 참조하는 코드 없음 (§3-4를 수정하면 함께 정리됩니다)

---

## 8. 검토 후 기각한 항목

기각한 항목도 기록합니다 — 이유 없이 비어 있으면 다음 담당자가 같은 검토를 반복합니다.

| 검토한 것 | 결론 |
|---|---|
| 값 풀 GC 미구현 | **구현되어 있습니다**(`HistoryMaintenance.Collect`, 배치 + 워터마크). 범위 문제만 §3-2 |
| 백필 | 사용자 결정으로 수행하지 않음. readme TODO에 이유와 함께 기록됨 |
| 예약어 컴파일 검증 부족 | **10개 언어 전부** 커버 (`ReservedWordTests`) |
| CI 누락 | `.github/workflows/dotnet.yml` 완비 — 10개 툴체인 설치, docker, 셀프컨테인드 퍼블리시 후 실행까지 |
| 마이그레이션 순서 | 딕셔너리 선언 순서는 `[1] [4] [3] [2]`이나 적용은 `for version = current+1 .. Version` 루프이므로 **번호순으로 정확히 적용됩니다** |
| 토큰 비교 타이밍 공격 | `FixedTimeEquals`로 방어됨 |
| 스테이징 커밋 원자성 | 파일 간 원자성은 없으나 `Rollback` + 안내 메시지로 **문서화된 한계**. 4개 DB와 파일을 한 트랜잭션으로 묶으려면 분산 코디네이터가 필요하고, 그것은 이 도구의 범위를 넘습니다 |
| 배포물 취약 패키지 | `src` **정상** |

---

## 9. 권장 순서와 조치 결과

> 아래는 보고서를 작성한 시점의 순서입니다. **11번을 제외한 전부와, 목록에 없던 §10이
> 완료되었습니다.** 남은 것은 아래 「잔여 항목」뿐입니다.

**지금**

1. ~~**§4-1 OAuth 시크릿 폐기**~~ — 코드 작업이 아니고, 시간 경과에 따라 악화되는 유일한 항목입니다
2. ~~**§3-1 `ValueId` 한 줄**~~ — 조회 실패 시 예외. 비용이 가장 낮고 효과가 큽니다
3. ~~**§4-2 테스트 프로젝트 pull-up**~~ — 한 줄, 경고 16건 해소

**다음**

4. ~~**§5-2 ~ §5-4 Unreal 전용 테이블 리더**~~ — `std::`를 UE 타입으로 전면 대체하고 예외를 `bool`+오류 상태로
5. ~~**§3-3 serve 오류 매핑**~~ + 테스트 한 건
6. ~~**§3-4 Html CDN 인라인화**~~ — 현재 산출물이 손상된 상태로 배포되고 있습니다
7. ~~**§3-2 값 풀 FK**~~ — 설계 결정이 필요하므로 위 여섯 개 뒤에

**여유 있을 때**

8. ~~§5-5 블루프린트 함수 라이브러리~~
9. ~~§3-5 `ParseDefinitionRect` 예외~~
10. ~~§7 저장소 정리~~
11. §6 제너레이터 공통화 — 부분만 수행. 중단 근거는 §1의 주석에 기재하였습니다

### 잔여 항목

| 항목 | 잔여 사유 |
|---|---|
| §4-1 | 저장소 밖의 작업이고, 사용자가 직접 처리합니다 |
| Unreal 엔진 API 자체 | UHT 게이트가 실제 UE 4.27.2로 통과합니다(아래). 남는 것은 스텁의 `FGuid` 레이아웃 가정뿐이고, 그것은 리플렉션이 아니라 런타임 동작이므로 UHT의 검사 범위 밖입니다 |

### §5-6 조치 — 적합성 코퍼스에 대한 Unreal 편입

엔진 없이 실행하는 하네스를 구축하였습니다. `test/fixtures/tools/unreal-stubs`에 `CoreMinimal.h`의 필요한 범위(FString, TArray, FGuid, FDateTime, FTimespan, UHT 매크로는 no-op)가 포함되어 있고, C# 타깃의 `UnityStubs.cs`와 같은 거래를 한 단계 더 진행한 것입니다 — 그쪽은 컴파일만 되면 되었고, 이쪽은 동작해야 합니다.

**증명하는 범위** — 테이블 리더의 디코딩. varint, 지그재그, UTF-8, GUID 바이트 순서, 틱. 그것이 생성된 코드의 역할이고 스텁은 결과를 담아 형식만 정합합니다. GUID 조립에서 바이트 두 개를 교환하는 사보타주로 확인하였고, 두 행의 값을 정확히 지시하여 실패합니다.

**증명하지 못하는 범위** — 엔진의 타입이 스텁과 동일하게 동작하는지 여부.

그 절반은 UHT 게이트가 확인합니다 — 리플렉션 매크로, include 순서, UHT가 수용하는 프로퍼티 타입. `TABBIT_UE_ROOT`를 실제 엔진에 연결하고 최초로 실행한 결과 **게이트 자체가 두 차례 실패하였습니다.**

1. `UBlueprintFunctionLibrary`의 부모 타입 미검출 — 게이트가 매니페스트에 CoreUObject만 포함하고 있었습니다. §5-5가 블루프린트 라이브러리를 추가하면서 발생하였는데, 그 이후 이 게이트를 실행한 사례가 없었습니다. 생성기는 정확하였고(Build.cs가 `Engine`을 제대로 선언) 게이트가 부정확하였습니다.
2. Engine 항목을 전부 포함하자 이번에는 Engine 자신의 의존성이 연쇄적으로 따라왔습니다. 게이트가 확인해야 하는 것은 「UHT가 **우리** 모듈을 수용하는가」이므로, Engine에서는 `BlueprintFunctionLibrary.h` 하나만 포함합니다. 그 부모는 CoreUObject의 `UObject`이므로 사슬이 거기에서 종결됩니다.

현재는 통과하고, 사보타주로도 확인하였습니다 — double 필드에 `UPROPERTY`를 부착하면 UE4의 UHT가 파일과 줄과 필드 이름을 지시하여 거부합니다. 그 특례가 실재하는 규칙이라는 의미이고, 이제 그렇게 검사됩니다.

진행 과정에서 한 건을 수정하였습니다. **BlueprintType enum은 uint8이므로 범위 밖 레이블이 변환 전체를 거부**하고 있었습니다. 코퍼스의 `Flag`가 세 바이트 varint를 위해 1048576을 사용하므로 Unreal만 코퍼스를 판독하지 못하였습니다. 값은 시트의 것이고 — 에러 코드나 비트 플래그 enum은 흔합니다 — 코드 생성기가 거부할 사안이 아닙니다. 현재는 해당 enum만 int32로 확장하고 Blueprint 노출을 포기하며, 어느 레이블이 원인인지 경고합니다.

### §6 조치 — 신규 타입 누락 지점의 축소

보고서가 지적한 실질은 줄 수가 아니라 **새 `ValueType`을 추가하면 12곳을 수정해야 하고 누락해도 컴파일된다**는 것이었습니다. `default:`에서 예외를 throw하므로 소비자의 프로젝트에서 런타임에 드러납니다.

두 단계로 폐쇄하였습니다.

- **1단계 — 프로파일 검사**
  - *타입 이름* — `LanguageProfileTests`가 프로파일을 리플렉션으로 전부 찾아 모든 스칼라 타입을 요구합니다. 타입을 추가하면 즉시 실패하며 아직 학습하지 못한 언어를 이름으로 제시합니다.
  - *읽기 호출 표* — 테이블 리더 호출 switch 10벌을 `LanguageProfile`의 표 하나로 이관하였습니다. `LanguageProfileTests`가 표를 가진 모든 언어에 모든 스칼라 타입의 호출을 요구하므로, 타입 이름과 **같은 파일 같은 실행에서** 함께 실패합니다. 11곳이 1곳이 되었습니다.
- **2단계 — 코퍼스 실행**
  - *읽기 호출 실행* — `CorpusCoverageTests`가 `ValueType` enum을 판독하여 **적합성 코퍼스에 그 타입의 필드가 있는지** 요구합니다. 존재하면 12개 하네스가 실제로 컴파일하고 실행하여 판독하므로, 표는 정확한데 값이 틀린 경우까지 검출됩니다.

2단계가 없으면 1단계의 논증은 「코퍼스에 모든 타입이 존재한다」는 전제에 의존하는데, 그것을 보장하는 장치가 없었습니다. 실제로 이 게이트를 적용하자마자 **어떤 테이블 리더도 판독한 적이 없는 배열 형태 8개**가 도출되었습니다. 그중 원소 읽기가 구조적으로 다른 둘(`enum[]`은 캐스트를, C에서는 스크래치 변수를 경유하고, `uuid[]`는 값이 아니라 16바이트)은 코퍼스에 등재하였고 12개 하네스가 판독합니다. 나머지 여섯은 `int[]`와 동일한 루프에서 이미 스칼라로 판독하고 있는 호출이므로 판단으로 제외하였고, 그 판단과 근거를 테스트에 기재하였습니다.

C++·C#·Unreal은 표가 없습니다 — 그 세 테이블 리더는 이름별 메서드가 아니라 **타입별 오버로드**로 판독하므로 표로 기재할 per-type 호출 자체가 없습니다. 누락이 아니라 해당 테이블 리더들의 성질이므로, 빈 표가 아니라 `null`이고 테스트도 이름이 아니라 「표의 존재 여부」로 건너뜁니다.

이관 후 생성물은 HTML 타임스탬프를 제외하고 바이트 동일하였습니다. 사보타주로도 확인하였습니다 — Rust 표에서 `Uuid` 한 줄을 삭제하면 `LanguageProfileTests`가 언어와 타입을 지시하여 실패하고, 적합성 하네스도 함께 실패합니다.

기계적 중복(`CommentLines`·`WriteBinaryReaderRuntime`)도 공통 기반 클래스로 상향되어 있습니다. 세 제너레이터가 자기 `CommentLines`를 유지하는데, 3개 다 실제로 상이하고 그 근거가 각각 기재되어 있습니다.

---

## 10. 테이블당 파일 하나로의 분할 (보고서 이후 추가 작업)

보고서에는 없던 항목입니다. TypeScript만 테이블당 파일을 사용하고 있었고, 나머지 11개는 한 파일에 전부 포함하고 있었습니다. 시트에서 테이블을 삭제하면 그 코드가 파일 안에 잔존합니다 — 파일 자체는 계속 생성되므로 스윕이 관여할 수 없습니다.

12개 언어 모두 테이블당·enum당·상수 세트당 파일 하나로 변경하였고, 마커 기반 스윕(`Sweep: false`로 비활성화 가능)이 이번 실행에서 사용하지 않은 생성 파일을 삭제합니다.

**「무엇이 무엇을 참조하는가」는 언어마다 결과가 다르나 질문은 하나입니다.** 그래서 `TypeDependencies`로 한 번만 계산합니다 — enum은 잎, 테이블은 자기 필드가 사용하는 enum과 자기가 참조하는 테이블, 접근자는 모든 테이블. 순환할 수 있는 간선은 테이블→테이블 하나뿐이고 그것은 별도로 보고하므로, 엄격한 순서가 필요한 언어(C, C++)는 전방선언으로 처리합니다. 순환을 해소하려고 간선을 폐기하는 처리는 없습니다 — 그 경우 자기가 참조하는 타입을 볼 수 없는 파일이 고지 없이 생성됩니다.

| 언어 | 분할 단위 | 파일 간 의존 해결 |
|---|---|---|
| TypeScript | 도입 이전부터 | `export`/`import` |
| C# | 타입당 | 네임스페이스, 임포트 불필요 |
| Kotlin | 타입당 | 미사용 임포트는 경고뿐 |
| PHP | 타입당 | 오토로더 없음 → 명시적 `require_once` |
| Dart | 타입당 `part` | `part`가 라이브러리 임포트를 공유 |
| Ruby | 타입당 | 오토로더 없음 → `require_relative` |
| Go | 타입당, 평면 | 한 패키지 → 임포트 불필요. **미사용 임포트는 오류**이므로 표준 임포트를 파일별로 정확히 산출 |
| Python | 타입당, 평면 | 상대 임포트. `__init__`이 `__all__`과 함께 전부 재수출 |
| Rust | 타입당, 평면 | `lib.rs`의 `mod` 트리 + `pub use`로 기존 경로 유지 |
| Java | 타입당 (레코드/테이블 별도 파일) | 한 패키지. public 타입은 같은 이름 파일에 단독으로 존재해야 함 |
| C | 타입당 헤더+소스 | **전방선언 헤더** + enum은 완전 타입으로 include |
| C++ | 타입당 헤더 (헤더 온리) | 동일 |

**가장 어려운 것은 C와 C++입니다.** 다른 언어는 이름을 모듈이나 패키지로 해석하므로, 임포트를 누락한 파일은 로드 시점에 실패합니다. C와 C++은 앞에 위치한 텍스트로 해석하므로, **include를 누락한 헤더도 올바른 것을 먼저 include한 번역 단위 안에서는 정상 컴파일됩니다.** 그래서 게이트를 두 개 배치하였습니다 — 모든 헤더가 번역 단위의 유일한 include로서 컴파일되는지, 그리고 테이블 헤더가 다른 테이블 헤더를 include하지 않는지. 후자는 오늘의 코퍼스에 순환이 없다는 근거만으로는 부족합니다 — include 가드가 있는 헤더 간의 순환은 명시적으로 실패하지 않고, 어느 번역 단위가 먼저 도달하였는지에 따라 다르게 해소됩니다.

**게이트 공백도 한 건 폐쇄하였습니다.** 생성된 상수 파일을 검사하는 게이트가 12개 언어 어디에도 없었습니다 — 적합성 코퍼스에도 `reserved-words`에도 상수 세트가 없었기 때문입니다. Rust에서 그 대가가 드러났습니다 — enum 타입 상수는 그 enum을 참조하는데 의존성 그래프가 그것을 반영하지 않아 크레이트가 컴파일되지 않았고, 무관한 코퍼스를 수동으로 빌드해야 확인할 수 있었습니다. 코퍼스에 상수 세트 `Limits`를 등재하였고, 이제 수정한 것을 되돌리면 `Generated_rust_reader`가 rustc의 줄 번호와 함께 실패합니다.

**같은 경로에 서로 다른 파일 두 개를 기록하는 것은 이제 오류입니다.** 분할이 발생시킨 문제입니다 — 파일 이름이 테이블·enum·상수 세트 이름에서 도출되고, 그중 둘이 같은 이름으로 축약될 수 있습니다. 이전 동작은 나중에 기록한 쪽이 남고 다른 타입은 출력에 부재하는 것이었습니다. 소비자는 그 사실을 자기 컴파일러로부터, 이 도구가 생성하였다고 보고한 타입의 이름과 함께, 근거는 어디에도 없이 확인하게 됩니다.
