# -*- coding: utf-8 -*-
"""polymorphism.md 의 시트 예시를 엑셀 격자 모습의 SVG로 생성한다.

실행: `python spec/polymorphism-figures.py`

같은 폴더에 polymorphism-*.svg 를 다시 씁니다. 예시를 고치면 이 파일을 고치고 다시
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


# 5.2 — 컬럼의 합집합. 판별자 컬럼 하나와 모든 변종 멤버가 나란히 서고, 그 행의 변종에
# 없는 컬럼은 비어 있습니다. 빈 칸이 「값이 없다」가 아니라 「그 변종에 그 멤버가 없다」인
# 것이 이 그림이 보여야 하는 것입니다.
build("polymorphism-union", [
    [":table Skill", "스킬과 그 효과입니다."],
    [":field", "id", "effect.$type", "effect.chance", "effect.damage", "effect.amount"],
    [":type", "int", "Effect"],
    ["", "101", "DamageEffect", "30", "50"],
    ["", "102", "HealEffect", "100", "", "20"],
], notes={
    3: "damage 만 있습니다 — amount 는 이 변종에 없는 멤버",
    4: "amount 만 있습니다 — damage 칸이 비는 이유가 같습니다",
}, title="다형 레코드 — 컬럼의 합집합")


# 5.3 — 멀티 로우와 결합. 연장 행마다 $type 셀이 그 원소의 변종을 정합니다. 원소 개수가
# 행마다 다른 것과 원소마다 형태가 다른 것이 한 표에 함께 있는 것이 요점입니다.
build("polymorphism-array", [
    [":table Skill", "효과가 여러 개인 스킬입니다."],
    [":field", "id", "effects[].$type", "effects[].chance", "effects[].damage", "effects[].amount"],
    [":type", "int", "Effect"],
    ["", "101", "DamageEffect", "30", "50"],
    ["", "", "HealEffect", "100", "", "20"],
    ["", "102", "HealEffect", "50", "", "10"],
], notes={
    3: "101 의 1번째 원소",
    4: "연장 행 — 101 의 2번째 원소이고 변종이 다릅니다",
    5: "102 는 원소가 하나뿐입니다",
}, title="다형 배열 — 원소마다 변종이 갈립니다")


# 8절 — 그 행의 변종에 없는 멤버 칸에 값이 있는 것. 합집합 표기에서 가장 나오기 쉬운
# 실수이고, 빈 칸이 뜻을 가지는 표에서는 조용히 지나가면 안 되는 자리입니다.
build("polymorphism-refusal", [
    [":table Skill", "거절되는 표입니다."],
    [":field", "id", "effect.$type", "effect.chance", "effect.damage", "effect.amount"],
    [":type", "int", "Effect"],
    ["", "101", "DamageEffect", "30", "50"],
    ["", "102", "HealEffect", "100", "7", "20"],
], notes={
    4: "HealEffect 에 damage 가 없는데 값이 있습니다 — 그 셀을 가리켜 거절합니다",
}, errors={(4, 4)}, title="거절 — 그 변종에 없는 멤버에 값이 있음")
