# -*- coding: utf-8 -*-
"""sheets.md 의 예시를 엑셀 격자 모습의 SVG로 생성한다.

실행: `python doc/sheets-figures.py`

같은 폴더에 sheets-*.svg 를 다시 씁니다. 예시를 고치면 이 파일을 고치고 다시 실행한 뒤,
PNG로 렌더해 눈으로 확인하고 커밋합니다.

격자를 그리는 코드는 spec/primary-layout-figures.py 의 것을 그대로 씁니다 — 같은 격자를
두 벌 그리면 두 문서의 그림이 서로 달라 보이기 시작하는 자리가 되기 때문입니다."""
import importlib.util
import os

HERE = os.path.dirname(os.path.abspath(__file__))
BUILDER = os.path.join(os.path.dirname(HERE), "spec", "primary-layout-figures.py")

_spec = importlib.util.spec_from_file_location("layout_figures", BUILDER)
_figures = importlib.util.module_from_spec(_spec)

# The builder writes its own figures on import, so it is pointed here first and its own
# output directory is restored right after - nothing of the spec's is rewritten by this run.
_original = None


def _load():
    global _original
    _spec.loader.exec_module(_figures)
    _original = _figures.OUT_DIR
    _figures.OUT_DIR = HERE


_load()
build = _figures.build


build("sheets-table", [
    [":table Item", "상점에서 파는 아이템입니다."],
    [":field", "id", "name", "*code", "grade", "tags"],
    [":type", "int", "string", "string", "Grade", "string[]"],
    [":desc", "아이템 ID", "표시 이름", "고유 코드", "등급", "검색 태그"],
    [":target", "c,s", "c", "c,s", "c,s", "s"],
    ["", "1001", "금화", "gold", "common", "돈;화폐"],
    ["#", "1002", "(작업중)", "wip", "common", ""],
    ["", "1003", "물약", "potion", "rare", "회복"],
], notes={
    0: "선언 셀과 그 오른쪽의 설명",
    1: "생략할 수 없는 유일한 헤더 행",
    5: "마커 열이 비면 데이터 행",
    6: "이 행은 변환에서 빠집니다",
}, title="테이블의 시트 배치")

build("sheets-boundary", [
    [":table Item", "왼쪽 테이블입니다.", "", ":table Hero", "오른쪽 테이블입니다."],
    [":field", "id", "name", ":field", "id", "name"],
    [":type", "int", "string", ":type", "int", "string"],
    ["", "1001", "금화", "", "1", "전사"],
    ["", "", "", "", "", ""],
    ["", "여기서부터는 아무것도 읽지 않습니다."],
], notes={
    0: "각자 자기 마커 열에서 시작",
    4: "완전히 빈 행 — 두 엔티티가 끝남",
}, title="엔티티의 경계")

build("sheets-paths", [
    [":table Star", "경로 표기의 예입니다."],
    [":field", "id", "pos.x", "pos.y", "slot[0].id", "slot[1].id", "tag[0]", "grid[0][0]", "grid[0][1]"],
    [":type", "int", "float", "float", "int", "", "string", "int", ""],
    ["", "1", "1.5", "-2.5", "9001", "9002", "밝음", "1", "2"],
], notes={
    1: "점은 멤버, 대괄호는 원소",
    2: "그룹의 타입은 첫 자리에만",
}, title="컬럼의 이름과 경로")

build("sheets-arrays", [
    [":table Quest", "배열이 오는 세 자리입니다."],
    [":field", "id", "tags", "cost[0]", "cost[1]", "reward[]"],
    [":type", "int", "string[]", "int", "", "int"],
    ["", "1", "사냥;야간", "10", "20", "1001"],
    ["", "", "", "", "", "1002"],
    ["", "2", "호송", "30", "-", "2001"],
], notes={
    1: "셀 안 · 칸 · 행",
    4: "연장 행 — reward만 값을 담습니다",
}, title="배열의 세 자리")

build("sheets-multirow", [
    [":table Quest", "레코드 하나가 여러 행에 걸칩니다."],
    [":field", "id", "title", "reward[].itemId", "reward[].count"],
    [":type", "int", "string", "foreign Item", "int"],
    ["", "1", "길잃은 화물", "1001", "2"],
    ["", "", "", "1002", "1"],
    ["", "2", "해적 소탕", "2001", "5"],
    ["#", "", "", "2002", "1"],
], notes={
    3: "레코드 1의 첫 행이자 첫 원소",
    4: "연장 행 — 인덱스 칸이 비어 있음",
    5: "인덱스에 값 → 새 레코드",
    6: "제외 — 이 원소만 빠짐",
}, title="멀티 로우")

build("sheets-enum", [
    [":enum Grade", "아이템 등급입니다."],
    [":field", "label", "value", "alias", "desc"],
    ["", "common", "1", "일반", "기본 등급"],
    ["", "rare", "2", "희귀", ""],
    ["", "epic", "3", "영웅", "시즌 한정"],
], notes={
    1: "헤더 행은 이것 하나뿐",
}, title="enum")

build("sheets-const", [
    [":const GameConfig(side=s)", "서버 전역 설정입니다."],
    [":field", "name", "type", "value", "desc"],
    ["", "maxPartySize", "int", "5", "파티 최대 인원"],
    ["", "baseSpeed", "float", "1.25", ""],
    ["", "startGrade", "Grade", "rare", "enum 이름을 바로"],
], title="상수셋")

build("sheets-memo", [
    [":table Item", "모델에 넣지 않는 자리들입니다."],
    [":field", "id", "#", "name", "#old@4"],
    [":type", "int", "", "string", "string"],
    ["", "1001", "=VLOOKUP(...)", "금화", ""],
    ["#", "1002", "환율 확인", "(작업중)", ""],
    ["", "1003", "", "물약", ""],
], notes={
    1: "메모 컬럼과 묘비",
    4: "행 제외",
}, title="`#`의 세 자리")

build("sheets-index", [
    [":table Item(key=code)", "인덱스를 옮긴 테이블입니다."],
    [":field", "seq", "code", "*sku", "name"],
    [":type", "int", "string", "string", "string"],
    ["", "1", "gold", "A-1", "금화"],
    ["", "2", "potion", "A-2", "물약"],
], notes={
    0: "기본 인덱스를 code로",
    1: "`*`는 보조 인덱스",
}, title="인덱스의 지정")

build("sheets-variant", [
    [":table Item", "한 필드의 값 컬럼을 여러 벌."],
    [":field", "id", "name", "price", "price", "price"],
    [":type", "int", "string", "int", "", ""],
    [":variant", "", "", "", "kr", "jp"],
    ["", "1001", "금화", "10", "12", "14"],
    ["", "1003", "물약", "30", "33", "36"],
], notes={
    3: "빈 칸이 기본 변형",
}, title="필드 변형")

build("sheets-blank", [
    [":table Item", "빈 칸과 값 없음은 다릅니다."],
    [":field", "id", "name", "bonus", "note"],
    [":type", "int", "string", "int?", "string"],
    ["", "1001", "금화", "5", "판매용"],
    ["", "1002", "", "-", ""],
    ["", "1003", "물약", "", "회복"],
], notes={
    4: "빈 문자열 · 값 없음",
    5: "✗ 숫자 칸의 빈 칸은 오류",
}, errors={(5, 3)}, title="빈 칸과 값 없음")
