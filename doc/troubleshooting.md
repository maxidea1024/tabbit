# 트러블슈팅

빌드가 실패했을 때 어디를 볼 것인가입니다.

여기 있는 메시지는 전부 도구가 실제로 출력하는 문장입니다.

> [문서 목록으로](readme.md)

생성된 코드를 프로젝트에 적용하다 생기는 문제는 [언어별 가이드](languages/readme.md)의 각 문서
끝에 따로 있습니다.

---

## 먼저 읽는 법

오류는 한 번에 모아서 보고됩니다. 오류 하나당 한 번씩 재실행할 필요가 없습니다.

```
Fatal: Field `Item.CategoryId` references `ItemCategory` row `99`, which does not exist.
   at test/fixtures/xlsx/core/core.xlsx : Refs : J8

Details:
  [  1] Index field `Item.Index` repeats the value `3`, ...
        at test/fixtures/xlsx/core/core.xlsx : Refs : I10
  [  2] ...
```

- **첫 줄**이 무엇이 잘못됐는지.
- **`at`** 이 어느 파일의 어느 시트, 어느 셀인지. 구글 스프레드시트라면 URL이라 바로 열립니다.
- **`Details:`** 는 같이 발견된 나머지입니다.

`--debug`를 붙이면 콜스택도 나옵니다. 도구 자체의 버그를 의심할 때만 사용하면 됩니다.

**실패한 빌드는 아무것도 남기지 않습니다.**

파일은 스테이징 영역을 거쳐 마지막에 일괄 반영되고, 데이터베이스는 섀도 테이블에 적재한 뒤
원자적으로 교체합니다.

그러므로 실패했다면 이전 출력이 그대로 유지됩니다.

---

## 이 문서의 나머지

|무엇|어디|
|--|--|
|[시트를 읽는 중에 나는 것](troubleshooting/reading.md)|워크북과 시트를 읽고 값을 해석하고 참조를 잇는 동안 나오는 메시지|
|[스키마가 바뀐 뒤](troubleshooting/schema.md)|컬럼을 더하거나 지우거나 타입을 바꾼 뒤에 나오는 것|
|[recipe · 검증 · 외부 연결](troubleshooting/setup.md)|recipe 설정 · 검증 규칙 컴파일 · 구글 인증 · DB 적재 · 웹서버|
|[결과가 이상할 때](troubleshooting/output.md)|변환은 됐는데 나온 것이 예상과 다를 때, 그리고 마지막 단계의 실패|
