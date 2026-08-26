# 구글 시트의 정의된 이름

> [문서 목록으로](../../doc/readme.md)

정의된 이름을 테이블 경계로 쓰는 레이아웃이 **구글 스프레드시트에서도** 동작하게 하는
설계입니다.

엑셀 임포터만 이름을 읽고 있었습니다. 구글 임포터는 `RawSheet.NamedRanges`를 비운 채로
두었고, 그 결과가 **오류 없이 테이블 0개인 실행**이었습니다. 이 문서의 1절이 그 상태를,
2~3절이 구글 API가 제공하는 것과 엑셀과의 차이를, 4절이 설계를 기재합니다.

**구현되었습니다.** 좌표 변환·필터·클램프가
[`SheetNamedRanges`](../../src/Importers/SheetNamedRanges.cs)로 이동하여 두 임포터가 공유하고,
[`GoogleSheetsImporter.ResolveNamedRanges`](../../src/Importers/GoogleSheetsImporter.cs)가 응답에서
이름을 해석합니다. 게이트는
[`GoogleSheetsNamedRangeTests`](../../test/Tabbit.Tests/GoogleSheetsNamedRangeTests.cs)입니다.
엑셀 경로는 동작 보존이며 골든이 변경되지 않았습니다.

---

## 1. 현재 상태

[`XlsxImporter`](../../src/Importers/XlsxImporter.cs)는 이름 기반 레이아웃일 때 세 가지를
수행합니다.

|무엇|위치|
|--|--|
|워크북의 정의된 이름을 해석|[`XlsxImporter.cs:177`](../../src/Importers/XlsxImporter.cs#L177)|
|어떤 이름도 덮지 않는 시트를 건너뜀|[`XlsxImporter.cs:219`](../../src/Importers/XlsxImporter.cs#L219)|
|이름을 격자 좌표로 변환하여 `RawSheet`에 부착|[`AttachNamedRanges`](../../src/Importers/XlsxImporter.cs#L323)|

[`GoogleSheetsImporter`](../../src/Importers/GoogleSheetsImporter.cs)에는 셋 다 없습니다.
`LayoutRegistry.UsesNamedRanges`를 조회하지 않고, `RawSheet.NamedRanges`에 아무것도
넣지 않습니다.

### 실패 양상 — 감지되지 않는 빈 결과

이름 기반 레이아웃은 이름이 하나도 없는 시트를 정상적인 작업 시트로 판정합니다.

```csharp
if (sheet.NamedRanges.Count == 0)
{
    // Ordinary: a workbook in this layout holds working sheets beside its data ...
    Log.Information($"Skipping sheet `{sheet.Location?.Sheet}`: no defined name covers it. ...");
    continue;
}
```
— [`UwoLayoutParser.cs:69`](../../src/Cooking/Layouts/UwoLayoutParser.cs#L69)

그래서 구글 시트를 이 레이아웃으로 읽으면 **모든 시트가 이 분기로 들어갑니다.** 예외도
경고도 발생하지 않고, 정보 수준 로그만 시트 수만큼 출력된 뒤 실행이 성공으로 종료합니다.
[`.xlsb`의 정의된 이름](../import/xlsb-defined-names.md) 1절이 기재한 것과 같은 종류의 결함이며,
원인만 다릅니다 — 그쪽은 파트를 찾지 못하는 것이고 이쪽은 조회 자체가 없는 것입니다.

**현재 이 조합을 사용하는 recipe는 없습니다.** 정의된 이름을 쓰는 레이아웃이 하나 있고
워크북 파일을 입력으로 받으며, `GoogleSheets` 항목을 채운 recipe는
[`src/recipes/web.jsonc`](../../src/recipes/web.jsonc) 하나입니다. 즉 이것은 회귀가 아니라
**미구현이며, 구현되지 않았다는 사실이 표시되지 않는 상태**입니다.

## 2. 구글 API가 제공하는 것

추가 요청이 필요하지 않습니다. 임포터가 이미 호출하는
`Spreadsheets.Get`의 응답 객체에 이름이 포함되어 있습니다.

```
Spreadsheet
  ├ Sheets[]        ← 지금 읽고 있는 것
  └ NamedRanges[]   ← 읽지 않고 있는 것
        ├ Name           문자열
        ├ NamedRangeId   문자열
        └ Range : GridRange
              ├ SheetId            정수
              ├ StartRowIndex      0 기준, 포함
              ├ EndRowIndex        0 기준, 배타
              ├ StartColumnIndex   0 기준, 포함
              └ EndColumnIndex     0 기준, 배타
```

`GridRange`의 네 인덱스는 전부 nullable이고, null은 **그 방향으로 무한**을 의미합니다.
열 전체를 지시하는 이름이 `StartRowIndex`·`EndRowIndex`를 null로 반환합니다.

## 3. 엑셀과의 차이

|항목|엑셀 (`.xlsx`)|구글 시트|
|--|--|--|
|이름의 출처|`xl/workbook.xml`의 `<definedName>`을 직접 해석|응답 객체의 프로퍼티|
|참조의 표현|`'Ocean Zone'!$A$1:$IP$100` — **문자열**|`GridRange` — 정수 5개|
|시트의 지시|시트 이름. 따옴표·이스케이프 해석 필요|`SheetId` 정수|
|끝 좌표|포함|**배타**|
|사각형이 아닌 것|union·열 전체·외부 통합문서 참조가 문자열로 도달하므로 [`TryParseArea`](../../src/Importers/Xlsx/WorkbookPackage.cs#L148)가 판별|union이 표현되지 않습니다. 남는 것은 무한 방향 하나뿐|
|스코프|`localSheetId`가 있으면 시트 스코프이므로 제외|스코프 구분이 없습니다. 전부 문서 스코프|
|예약 이름|`_xlnm`·`_xlfn`·`!_`를 제외|해당하는 체계가 없습니다|

**해석 부담은 구글 쪽이 낮습니다.** `WorkbookPackage`가 수행하는 문자열 분해가 전부
불필요하고, 판별해야 하는 것은 인덱스의 null 여부 하나입니다.

`SkippedName`의 두 사유는 다음과 같이 대응합니다.

|사유|엑셀에서의 판정|구글에서의 판정|
|--|--|--|
|`NotARange`|참조가 비었거나 `#REF!`를 포함|`Range` 또는 `SheetId`가 null|
|`NotOneRectangle`|union·열 전체·외부 참조를 문자열 형태로 판별|네 인덱스 중 하나 이상이 null|

## 4. 설계

### 4.1 임포터별 부분과 공용 부분의 경계

두 임포터가 공통으로 수행하는 것은 **좌표 변환·필터 적용·클램프**이고, 이것은
[`AttachNamedRanges`](../../src/Importers/XlsxImporter.cs#L323)에 이미 구현되어 있습니다.
구글 임포터에 같은 것을 복제하지 않고 **공용 헬퍼로 추출합니다.**

|단계|엑셀|구글|
|--|--|--|
|① (이름, 시트, 절대 사각형) 산출|`WorkbookPackage`가 XML에서|응답의 `NamedRanges`에서|
|② 시트별로 분류|시트 **이름**으로 대조|`SheetId`로 대조|
|③ 격자 좌표로 변환·필터·클램프|**공용** — 지금의 `AttachNamedRanges`|**공용** — 같은 코드|

①과 ②만 임포터별이고 ③은 하나입니다. 이렇게 두는 이유는 중복 제거가 아니라
**두 소스가 서로 다르게 변화하는 것을 방지**하기 위해서입니다. 예를 들어 아래 세 가지는
소스와 무관한 판단이며, 한 곳에 있어야 두 소스에서 같게 동작합니다.

- 이름에도 `IncludeSheets` 필터를 적용하는 것 — 이름 기반 레이아웃에서는 **이름이 곧
  테이블의 이름**이므로, recipe가 테이블을 선별하는 수단이 이것입니다
- `Optimize`가 여백을 절삭한 뒤의 격자를 기준으로 좌표를 재계산하는 것
- 격자가 더 이상 가지지 않는 행·열을 사각형이 덮을 때 **거부하지 않고 클램프**하는 것

### 4.2 구글 쪽에서 추가로 필요한 것

|항목|내용|
|--|--|
|`SheetId` → 시트 대조|`sheet.Properties.SheetId`가 이미 사용 가능합니다. `MakeGoogleSheetsUrl`이 같은 값을 씁니다|
|블록 원점 보정|구글 임포터는 `sheet.Data`의 블록마다 `RawSheet`를 생성하고 원점이 `StartRow`·`StartColumn`입니다. 사각형은 시트 절대 좌표이므로, 블록 밖의 이름은 그 `RawSheet`의 것이 아닙니다|
|배타 끝의 변환|`Height = EndRowIndex - StartRowIndex`. 엑셀 경로의 `Last - First + 1`과 다릅니다|
|이름이 덮지 않는 시트|`RawSheet`를 모델에 등재하지 않습니다. 엑셀과 달리 **비용 절감 효과는 없습니다** — 셀을 이미 수신한 뒤이기 때문입니다. 동작과 로그를 일치시키기 위한 것입니다|

행 좌표의 변환은 엑셀 경로와 같은 방식으로 성립합니다. 구글 시트는 중간의 빈 행을
전송하지 않지만 [`RawSheet.Optimize`](../../src/Models/Raw/RawSheet.cs#L170)가 간격을 빈 행으로
복원하므로, 변환 이후 `Rows[i]`가 연속한 시트 행에 대응합니다.

### 4.3 코어에 대한 영향

`RawNamedRange`도, `UsesNamedRanges` 어트리뷰트도, recipe 스키마도 그대로입니다. 레이아웃은
자신이 어느 소스에서 왔는지 알지 못하며 이 작업이 그것을 변경하지 않습니다. 바뀌는 것은
**임포터 2개 중 하나만 채우던 필드를 둘 다 채우는 것**입니다.

## 5. 게이트

구글 API 응답을 대상으로 하는 테스트는 네트워크와 인증을 요구하므로 게이트에 부적합합니다.
대신 **응답 객체를 직접 구성하여** 4.1의 ①②만 검사하고, ③은 이미 엑셀 경로의 테스트가
대상으로 하고 있습니다.

|픽스처가 담아야 하는 것|무엇을 지키는가|
|--|--|
|시트 2개에 걸친 이름 여러 개|`SheetId` 대조|
|한 시트 안의 이름 2개|한 시트에 테이블 여러 개가 성립하는 것|
|끝 인덱스가 배타임을 드러내는 크기|`Height`·`Width` 계산의 off-by-one|
|인덱스가 null인 이름 하나|`NotOneRectangle`로 제외되고 경고가 출력되는 것|
|중간에 빈 행이 있는 시트|`Optimize`의 간격 복원 이후에도 좌표가 일치하는 것|
|`IncludeSheets`가 이름 하나를 제외|필터가 이름에도 적용되는 것|

## 6. 이번에 하지 않는 것

|항목|이유|
|--|--|
|이름이 덮는 시트만 수신|현재 `IncludeGridData = true`로 문서 전체를 한 번에 수신합니다. 이름을 먼저 조회하고 필요한 범위만 요청하는 것이 가능하나, 요청 횟수와 응답 크기의 균형을 실측한 뒤에 판단합니다|
|이름 기반 레이아웃의 `Optimize` 생략|[워크북 읽기](../import/streaming-workbook-reader.md) 9절이 기재한 별도 결정입니다. 그것이 적용되면 4.2의 좌표 변환이 **블록 원점 보정만 남고 단순해집니다.** 이 작업이 그 결정을 선행하지 않으며, 순서가 반대여도 결과가 같습니다|
|셀 메모|~~구글 임포터는 이미 `v.Note`를 읽고 있습니다~~ — **이제 읽지 않고, 요청하지도 않습니다.** 응답의 필드 마스크가 그것을 정합니다. [`RawCell`](../../src/Models/Raw/RawCell.cs)|

**미구현 상태의 표시는 이 작업으로 종료됩니다.** 지금은 구글 시트를 이름 기반 레이아웃으로
읽으면 조용히 0개가 되며, 그 상태를 유지하지 않습니다.
