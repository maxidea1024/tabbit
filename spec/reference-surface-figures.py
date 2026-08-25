# -*- coding: utf-8 -*-
"""reference-surface-naming.md 2절의 사례를 엑셀 격자 모습의 SVG로 생성한다.

실행: `python spec/reference-surface-figures.py`

같은 폴더에 reference-surface-*.svg 를 다시 씁니다. 예시를 고치면 이 파일을 고치고 다시
실행한 뒤, PNG로 렌더해 눈으로 확인하고 커밋합니다.

격자를 그리는 코드는 primary-layout-figures.py 의 것을 그대로 씁니다 — 같은 격자를 두 벌
그리면 두 문서의 그림이 서로 달라 보이기 시작하는 자리가 되기 때문입니다."""
import importlib.util
import os

HERE = os.path.dirname(os.path.abspath(__file__))
BUILDER = os.path.join(HERE, "primary-layout-figures.py")

_spec = importlib.util.spec_from_file_location("layout_figures", BUILDER)
_figures = importlib.util.module_from_spec(_spec)
# 불러오는 것만으로 그쪽 그림이 다시 쓰입니다. 같은 폴더라 출력 위치는 그대로이고, 내용도
# 같은 입력에서 나오므로 바뀌지 않습니다.
_spec.loader.exec_module(_figures)
build = _figures.build


# 규칙 2의 ① — 컬럼 이름에서 id 낱말을 뗀 것이 행 이름입니다. 같은 테이블을 가리키는
# 컬럼이 둘이어도 갈라지는 것이 이 규칙이 사는 자리입니다.
build("reference-surface-single", [
    [":table Post", "받은 쪽지입니다."],
    [":field", "id", "mailId", "senderId", "receiverId"],
    [":type", "int", "foreign Mail", "foreign Character", "foreign Character"],
    ["", "1", "5001", "9001", "9002"],
    ["", "2", "5002", "9002", "9001"],
], notes={
    2: "참조 컬럼 셋 — 대상이 각각 하나",
    3: "셀에 들어 있는 것은 언제나 키",
}, title="단일 대상 참조의 시트 배치")


# 파생을 두지 않는 근거 — 짧은 이름은 이웃 컬럼이 무엇인지에 달리게 됩니다.
# mail 컬럼이 Mail을 이미 쓰고, only는 뗄 id 낱말이 없어 키와 부딪힙니다.
build("reference-surface-shortname", [
    [":table Holder", "짧은 이름을 파생하면 부딪히는 두 경우입니다."],
    [":field", "id", "mail", "mailId", "only"],
    [":type", "int", "string", "foreign Mail", "foreign Weapon"],
    ["", "1", "안 읽음", "5001", "7001"],
], notes={
    1: "mail 컬럼이 Mail을 이미 씁니다",
    2: "짧은 이름은 Mail과 Only — 둘 다 부딪힙니다",
}, title="짧은 이름을 파생할 수 없는 두 경우")


# 배열 — 원소마다 행 하나이고, 이름 규칙은 스칼라와 같습니다.
build("reference-surface-array", [
    [":table Chest", "한 셀에 키를 여러 개 적습니다."],
    [":field", "id", "mailIds"],
    [":type", "int", "foreign Mail[]"],
    ["", "1", "5001;5002;5003"],
    ["", "2", "5001"],
], notes={
    2: "키 배열 하나",
    3: "원소마다 행 하나",
}, title="참조 배열의 시트 배치")


# 유일한 거절 — 생성될 이름을 컬럼이 이미 쓰고 있는 경우입니다. 손으로 적을 이름이 아니라
# 실수로 생기지 않지만, 생기면 되돌리지 않고 거부합니다 — 되돌리면 이름이 이웃 컬럼에
# 달리게 되고, 그것이 1.5가 없애려는 것입니다.
build("reference-surface-clash", [
    [":table Post", "생성될 이름을 컬럼이 이미 쓰고 있습니다."],
    [":field", "id", "mailId", "mailByMailId"],
    [":type", "int", "foreign Mail", "string"],
    ["", "1", "5001", "메모"],
], notes={
    1: "✗ mailId가 낼 이름과 같습니다 — 거부",
}, errors={(1, 3)}, title="이름이 부딪히는 유일한 거절")


# 검사만 하는 표기 — 타입은 그대로이고 값의 출처만 적습니다. `allowed`가 값의 목록이라면
# 이쪽은 값의 출처 목록이고, 구분자도 그것과 같은 `;`입니다.
build("reference-surface-refs", [
    [":table Shop", "값이 어느 카탈로그의 id인지만 검사합니다."],
    [":field", "id", "rewardId", "price"],
    [":type", "int", "int (refs=Item;Mount)", "int"],
    ["", "1", "3001", "100"],
    ["", "2", "4002", "250"],
], notes={
    2: "타입은 int 그대로 — 참조가 아닙니다",
    3: "Item에 있음",
    4: "Mount에 있음",
}, title="검사만 하는 대상 목록")
