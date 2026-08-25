# -*- coding: utf-8 -*-
"""`samples/wildling/data/*.tsv` 를 처음 한 번 만든다.

**정본은 `.tsv` 이다** — `data/readme.md` 를 보라. 이 스크립트를 다시 돌리면 손으로 고친 값이
사라지므로, 값을 다시 계산해야 하는 경우에만 쓴다.

`.tsv` 하나가 시트 하나의 격자 그대로이다. 첫 열이 마커 열이고, 저작기가 하는 것은 이 격자에
서식을 얹어 `.xlsx` 로 쓰는 것뿐이다.
"""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "data"))

# ---------------------------------------------------------------- 격자 쓰기

def emit(filename, entities):
    """엔티티 하나 이상을 파일 하나로 쓴다. 엔티티 사이는 완전히 빈 줄이 경계이다."""
    lines = []
    for i, e in enumerate(entities):
        if i:
            lines.append([])
        lines.extend(e)
    width = max((len(r) for r in lines), default=1)
    text = "\n".join("\t".join(r + [""] * (width - len(r))).rstrip("\t") for r in lines)
    path = os.path.join(OUT, filename)
    io.open(path, "w", encoding="utf-8", newline="\n").write(text + "\n")
    body = sum(1 for r in lines if r and r[0] == "" and any(r[1:]))
    print("%-28s %5d rows" % (filename, body))
    return body


def table(name, desc, cols, rows, meta=""):
    """cols = [(field, type, desc, target)] · rows = [[값...]]"""
    decl = ":table " + name + (("(" + meta + ")") if meta else "")
    out = [[decl, desc]]
    out.append([":field"] + [c[0] for c in cols])
    out.append([":type"] + [c[1] for c in cols])
    out.append([":desc"] + [c[2] for c in cols])
    if any(len(c) > 3 and c[3] for c in cols):
        out.append([":target"] + [(c[3] if len(c) > 3 else "") for c in cols])
    # 옵셔널 컬럼의 빈 칸은 `-` 로 적는다 — 빈 칸의 뜻은 정책이 정하고 기본은 오류이므로,
    # 「이 행에는 값이 없다」는 표기가 따로 있다. 다만 **멀티 로우의 연장 행은 건드리지
    # 않는다** — 거기서 값이 허용되는 곳은 `[]` 컬럼뿐이다.
    optional = [i for i, c in enumerate(cols) if "?" in c[1]]

    # 다형 그룹의 합집합 컬럼. 그 행의 변종이 갖지 않는 멤버는 **`-`(값 없음)** 로 적는다 —
    # 빈 칸으로 두면 참조 컬럼에서 「값이 있다」로 읽혀 「그 변종의 것이 아닌 값」으로 걸린다.
    groups = [c[0][:-len(".$type")] for c in cols if c[0].endswith(".$type")]
    union = [i for i, c in enumerate(cols)
             if any(c[0].startswith(g + ".") for g in groups)
             and not c[0].endswith(".$type")]
    for r in rows:
        cells = [str(v) for v in r]
        if cells and cells[0]:
            for i in optional:
                if i < len(cells) and cells[i] == "":
                    cells[i] = "-"
        out.append([""] + cells)
    return out


def enum(name, desc, labels):
    """labels = [(label, value, desc)] 또는 [(label, value, desc, alias)]"""
    wide = any(len(l) > 3 for l in labels)
    head = ["label", "value", "desc"] + (["alias"] if wide else [])
    out = [[":enum " + name, desc], [":field"] + head]
    for l in labels:
        row = [l[0], str(l[1]), l[2]] + ([l[3] if len(l) > 3 else ""] if wide else [])
        out.append([""] + row)
    return out


def const(name, desc, items):
    """items = [(name, type, value, desc)]"""
    out = [[":const " + name, desc], [":field", "name", "type", "value", "desc"]]
    for it in items:
        out.append([""] + [str(v) for v in it])
    return out


# ---------------------------------------------------------------- 종 정의

# (id, 속성, 등급, 역할, 단계 수, 단계별 이름)
SPECIES = [
    ("sprout_deer",  "Leaf",  "Common",    "Vanguard", 3, ["새싹사슴", "잎뿔사슴", "숲지기사슴"]),
    ("moss_toad",    "Leaf",  "Common",    "Warden",   1, ["이끼두꺼비"]),
    ("thorn_beetle", "Leaf",  "Rare",      "Breaker",  2, ["가시딱정벌레", "창날딱정벌레"]),
    ("vine_ape",     "Leaf",  "Rare",      "Vanguard", 2, ["덩굴원숭이", "등나무원숭이"]),
    ("bloom_moth",   "Leaf",  "Epic",      "Tuner",    1, ["개화나방"]),
    ("elder_bark",   "Leaf",  "Legendary", "Warden",   2, ["늙은껍질", "고목수호자"]),
    ("ember_fox",    "Flame", "Rare",      "Breaker",  3, ["잉걸여우", "불꼬리여우", "화염여우"]),
    ("cinder_pup",   "Flame", "Common",    "Breaker",  1, ["불씨강아지"]),
    ("ash_boar",     "Flame", "Common",    "Vanguard", 2, ["재멧돼지", "화산멧돼지"]),
    ("flare_hawk",   "Flame", "Rare",      "Breaker",  1, ["섬광매"]),
    ("magma_crab",   "Flame", "Epic",      "Vanguard", 2, ["용암게", "분화구게"]),
    ("pyre_lion",    "Flame", "Legendary", "Breaker",  1, ["불길사자"]),
    ("tide_serpent", "Tide",  "Epic",      "Tuner",    3, ["물비늘뱀", "조류뱀", "심해뱀"]),
    ("dew_slug",     "Tide",  "Common",    "Warden",   1, ["이슬달팽이"]),
    ("reef_turtle",  "Tide",  "Common",    "Vanguard", 2, ["산호거북", "암초거북"]),
    ("mist_ray",     "Tide",  "Rare",      "Tuner",    1, ["안개가오리"]),
    ("brine_otter",  "Tide",  "Rare",      "Breaker",  2, ["소금수달", "물살수달"]),
    ("abyss_whale",  "Tide",  "Legendary", "Vanguard", 1, ["심연고래"]),
    ("spark_mouse",  "Arc",   "Common",    "Breaker",  1, ["스파크쥐"]),
    ("bolt_lynx",    "Arc",   "Rare",      "Breaker",  3, ["스파크살쾡이", "번개살쾡이", "뇌전살쾡이"]),
    ("coil_snake",   "Arc",   "Rare",      "Tuner",    2, ["감김뱀", "나선뱀"]),
    ("storm_crane",  "Arc",   "Epic",      "Tuner",    2, ["폭풍두루미", "뇌운두루미"]),
    ("volt_golem",   "Arc",   "Epic",      "Vanguard", 1, ["전압골렘"]),
    ("thunder_stag", "Arc",   "Legendary", "Breaker",  1, ["우레사슴"]),
    ("shade_bat",    "Umbra", "Common",    "Tuner",    1, ["그림자박쥐"]),
    ("dusk_weasel",  "Umbra", "Rare",      "Breaker",  2, ["해거름족제비", "밤그늘족제비"]),
    ("gloom_spider", "Umbra", "Rare",      "Tuner",    3, ["그늘거미", "어스름거미", "심연거미"]),
    ("night_hound",  "Umbra", "Epic",      "Vanguard", 2, ["밤사냥개", "흑야사냥개"]),
    ("umbra_owl",    "Umbra", "Legendary", "Warden",   2, ["그늘부엉이", "달그늘부엉이"]),
    ("void_seraph",  "Umbra", "Mythic",    "Breaker",  3, ["공허날개", "무광날개", "종막날개"]),
]

ELEMENTS = ["Flame", "Tide", "Leaf", "Arc", "Umbra"]
ELEMENT_KO = {"Flame": "불꽃", "Tide": "물결", "Leaf": "잎새", "Arc": "번개", "Umbra": "그늘"}
ELEMENT_CODE = {"Leaf": "lf", "Flame": "fl", "Tide": "td", "Arc": "ar", "Umbra": "um"}

REGIONS = [
    ("weir_forest",   "여울숲",     "Leaf"),
    ("salt_coast",    "소금해안",   "Tide"),
    ("ashen_crater",  "재의 화구",  "Flame"),
    ("frost_ridge",   "서릿능선",   "Arc"),
    ("sunken_temple", "잠긴 신전",  "Umbra"),
]
REGION_OF_ELEMENT = {"Leaf": 0, "Tide": 1, "Flame": 2, "Arc": 3, "Umbra": 4}

GRADE_FACTOR = {"Common": 100, "Rare": 115, "Epic": 132, "Legendary": 152, "Mythic": 175}
GRADE_WEIGHT = {"Common": 1000, "Rare": 300, "Epic": 80, "Legendary": 15, "Mythic": 2}
GRADE_SHARD = {"Common": 1, "Rare": 3, "Epic": 8, "Legendary": 20, "Mythic": 50}
GRADE_OBSERVE = {"Common": 8, "Rare": 12, "Epic": 16, "Legendary": 24, "Mythic": 32}
ELEMENT_FACTOR = {"Flame": 100, "Tide": 100, "Leaf": 100, "Arc": 94, "Umbra": 90}
STAGE_FACTOR = {1: 100, 2: 180, 3: 317}

ROLE_BASE = {
    #          hp  atk  def  spd  crit_rate  crit_power
    "Vanguard": (420, 38, 32, 44, 500, 15000),
    "Breaker":  (280, 62, 18, 58, 800, 17000),
    "Warden":   (340, 34, 26, 50, 500, 15000),
    "Tuner":    (300, 44, 22, 54, 600, 15000),
}


def stat_block(role, grade, element, stage):
    hp, atk, dfn, spd, cr, cp = ROLE_BASE[role]
    f = GRADE_FACTOR[grade] * ELEMENT_FACTOR[element] * STAGE_FACTOR[stage]
    scale = lambda v: int(round(v * f / 1000000.0))
    return [scale(hp), scale(atk), scale(dfn), spd + (stage - 1) * 6, cr, cp]


def monster_id(sid, stage):
    return "%s_%d" % (sid, stage)


def habitat_bits(element, grade):
    """서식 지역 플래그. 주 지역과, 등급이 낮으면 인접 지역까지."""
    primary = REGION_OF_ELEMENT[element]
    bits = 1 << primary
    if grade in ("Common", "Rare"):
        bits |= 1 << ((primary + 1) % 5)
    return "0b" + format(bits, "05b")


# ---------------------------------------------------------------- Monster

def build_monster():
    cols = [
        ("monster_id", "string", "종과 단계를 함께 가리키는 식별자", "c,s"),
        ("*display_code", 'string (regex="^[a-z]{2}[0-9]{3}$")', "운영 도구가 쓰는 짧은 코드", "c,s"),
        ("species_id", "string", "같은 종을 묶는 식별자", "c,s"),
        ("stage", "int (min=1, max=3)", "각성 단계", "c,s"),
        ("name", "string (text=Monster)", "표시 이름", "c"),
        ("description", "string (text=Monster, namespace=Codex)", "기록부 설명", "c"),
        ("element", "Element", "속성", "c,s"),
        ("grade", "Grade", "희소성. 종의 성질이고 변하지 않는다", "c,s"),
        ("role", "Role", "전투에서의 역할", "c,s"),
        ("base.hp", "StatBlock", "1레벨 기준 능력치", "c,s"),
        ("base.attack", "", "공격", "c,s"),
        ("base.defense", "", "방어", "c,s"),
        ("base.speed", "", "행동 순서", "c,s"),
        ("base.crit_rate", "", "치명타 확률. 만분율", "c,s"),
        ("base.crit_power", "", "치명타 배수. 만분율", "c,s"),
        ("habitat", "bitset", "서식 지역 플래그", "c,s"),
        ("max_stage", "int (min=1, max=3)", "이 종이 도달할 수 있는 최대 단계", "c,s"),
        ("icon", "string (asset=icon)", "기록부와 편성 화면의 아이콘", "c"),
        ("model_offset", "vec3f?", "연출 보정. 대부분의 종은 비운다", "c"),
        ("tags", "string[]", "검색 태그", "c"),
        ("#old_grade", "", "등급을 종 단위로 옮기기 전의 컬럼", ""),
        ("#", "", "", ""),
    ]
    rows = []
    seq = {}
    for sid, el, grade, role, stages, names in SPECIES:
        for stage in range(1, stages + 1):
            seq[el] = seq.get(el, 0) + 1
            code = "%s%03d" % (ELEMENT_CODE[el], seq[el])
            sb = stat_block(role, grade, el, stage)
            offset = "(0,0.2,0)" if stage == stages and stages > 1 else ""
            tags = [ELEMENT_KO[el], names[stage - 1][-2:]]
            if stage == 1:
                tags.append("초기형")
            rows.append([
                monster_id(sid, stage), code, sid, stage,
                names[stage - 1],
                "%s에 서식하는 %s 와일드링이다." % (REGIONS[REGION_OF_ELEMENT[el]][1], ELEMENT_KO[el]),
                el, grade, role,
                sb[0], sb[1], sb[2], sb[3], sb[4], sb[5],
                habitat_bits(el, grade), stages,
                "wl_%s_%d" % (sid, stage),
                offset, ";".join(tags), "", "",
            ])
    return table("Monster", "종의 한 단계이다. 같은 종의 1단과 2단은 다른 행이다.", cols, rows)


# ---------------------------------------------------------------- MonsterAwakening

def build_awakening():
    cols = [
        ("from_monster_id", "foreign Monster", "각성 전", "c,s"),
        ("to_monster_id", "foreign Monster", "각성 후", "c,s"),
        ("gain.hp", "int (min=0)", "각성으로 더해지는 값", "c,s"),
        ("gain.attack", "int (min=0)", "공격 증가분", "c,s"),
        ("gain.defense", "int (min=0)", "방어 증가분", "c,s"),
        ("gain.speed", "int (min=0)", "행동 순서 증가분", "c,s"),
        ("gain.crit_rate", "int (min=0)", "치명타 확률 증가분", "c,s"),
        ("gain.crit_power", "int (min=0)", "치명타 배수 증가분", "c,s"),
        ("requirement_group_id", "foreign RequirementGroup", "각성 조건", "c,s"),
        ("costs[]", "Cost", "소모 재화. 셀 하나에 `재화,수량`", "c,s"),
    ]
    rows = []
    for sid, el, grade, role, stages, names in SPECIES:
        for stage in range(1, stages):
            a = stat_block(role, grade, el, stage)
            b = stat_block(role, grade, el, stage + 1)
            gain = [b[i] - a[i] for i in range(6)]
            group = "req_awaken_%s_%d" % (sid, stage + 1)
            shard = GRADE_SHARD[grade] * 10 * stage
            gold = 4000 * stage * (1 + GRADE_FACTOR[grade] // 100)
            rows.append([
                monster_id(sid, stage), monster_id(sid, stage + 1),
                gain[0], gain[1], gain[2], gain[3], gain[4], gain[5],
                group, "shard,%d" % shard,
            ])
            rows.append(["", "", "", "", "", "", "", "", "", "gold,%d" % gold])
            if stage == 2:
                rows.append(["", "", "", "", "", "", "", "", "", "food,%d" % (60 * stage)])
    return table("MonsterAwakening",
                 "각성 관계이다. 연장 행은 소모 재화의 원소이다.", cols, rows)


# ---------------------------------------------------------------- Skill

SKILL_ACTIONS = [
    # (접미, 역할, 대상, 재사용, 효과 종류)
    ("물기",   "Breaker",  "Single",   0, "damage"),
    ("일격",   "Breaker",  "Single",   2, "damage_big"),
    ("파열",   "Breaker",  "AllEnemy", 3, "damage_wide"),
    ("장막",   "Warden",   "AllAlly",  3, "buff_def"),
    ("잇기",   "Warden",   "OneAlly",  2, "heal"),
    ("휘감기", "Tuner",    "Single",   2, "status"),
    ("포효",   "Vanguard", "AllAlly",  4, "buff_atk"),
    ("가시",   "Vanguard", "Single",   1, "damage"),
    ("부름",   "Tuner",    "AllEnemy", 4, "status_wide"),
    ("결계",   "Warden",   "AllAlly",  4, "buff_def"),
]

EFFECT_SPEC = {
    "damage":      [("DamageEffect", 10000, {"power": 18000})],
    "damage_big":  [("DamageEffect", 10000, {"power": 26000})],
    "damage_wide": [("DamageEffect", 10000, {"power": 11000}),
                    ("StatusEffect", 2500, {"status": "Stun", "duration": 1})],
    "heal":        [("HealEffect", 10000, {"power": 14000})],
    "buff_def":    [("BuffEffect", 10000, {"stat": "Defense", "ratio": 3000, "duration": 2})],
    "buff_atk":    [("BuffEffect", 10000, {"stat": "Attack", "ratio": 2500, "duration": 2})],
    "status":      [("StatusEffect", 6000, {"status": "Slow", "duration": 2})],
    "status_wide": [("StatusEffect", 4000, {"status": "Blind", "duration": 2}),
                    ("BuffEffect", 10000, {"stat": "Speed", "ratio": -1500, "duration": 2})],
}


def skill_list():
    """속성 5종 × 동작 10종에서 46개를 고른다. 속성마다 동작 구성이 다르다."""
    out = []
    for i, el in enumerate(ELEMENTS):
        actions = SKILL_ACTIONS[:] if i < 4 else SKILL_ACTIONS[:6]
        # 속성마다 하나씩 빼서 46개가 되게 한다 — 무속성 2개를 뒤에 더한다.
        if i in (1, 3):
            actions = actions[:9]
        for suffix, role, scope, cd, kind in actions:
            out.append(("%s_%s" % (el.lower(), kind), el,
                        "%s %s" % (ELEMENT_KO[el], suffix), role, scope, cd, kind))
    # 동작 이름이 겹치면 id도 겹치므로 속성 안에서 번호를 붙인다.
    seen = {}
    fixed = []
    for sid, el, name, role, scope, cd, kind in out:
        seen[sid] = seen.get(sid, 0) + 1
        if seen[sid] > 1:
            sid = "%s_%d" % (sid, seen[sid])
        fixed.append((sid, el, name, role, scope, cd, kind))
    fixed.append(("plain_focus", "", "집중", "Tuner", "Self", 3, "buff_atk"))
    fixed.append(("plain_guard", "", "방비", "Vanguard", "AllAlly", 3, "buff_def"))
    return fixed


SKILLS = skill_list()


def build_skill():
    cols = [
        ("skill_id", "string", "식별자", "c,s"),
        ("name", "string (text=Skill)", "표시 이름", "c"),
        ("description", "string (text=Skill)", "설명", "c"),
        ("element", "Element?", "무속성 스킬은 비운다", "c,s"),
        ("target_scope", "TargetScope", "대상 범위", "c,s"),
        ("cooldown", "int (min=0, max=9)", "재사용 대기 턴", "c,s"),
        ("icon", "string (asset=icon)", "아이콘", "c"),
        ("#", "", "", ""),
    ]
    rows = []
    for sid, el, name, role, scope, cd, kind in SKILLS:
        rows.append([sid, name, "%s 계열 스킬이다." % (ELEMENT_KO.get(el, "무속성")),
                     el, scope, cd, "sk_%s" % sid, ""])
    return table("Skill", "스킬이다. 효과는 SkillEffect 에 있다.", cols, rows)


def build_skill_effect():
    cols = [
        ("skill_id", "foreign Skill", "어느 스킬인가", "c,s"),
        ("order", "int (min=0, max=7)", "적용 순서", "c,s"),
        ("effect.$type", "Effect", "이 행의 효과가 어떤 형태인가", "c,s"),
        ("effect.chance", "", "발동 확률. 만분율", "c,s"),
        ("effect.power", "", "피해 또는 회복 배수. 만분율", "c,s"),
        ("effect.status", "", "부여하는 상태", "c,s"),
        ("effect.stat", "", "변동시키는 능력치", "c,s"),
        ("effect.ratio", "", "변동률. 만분율. 음수는 하락", "c,s"),
        ("effect.duration", "", "지속 턴", "c,s"),
    ]
    rows = []
    for sid, el, name, role, scope, cd, kind in SKILLS:
        for order, (variant, chance, members) in enumerate(EFFECT_SPEC[kind]):
            rows.append([
                sid, order, variant, chance,
                members.get("power", ""), members.get("status", ""),
                members.get("stat", ""), members.get("ratio", ""),
                members.get("duration", ""),
            ])
    return table("SkillEffect",
                 "스킬 하나가 일으키는 것이다. 판별자가 그 행의 형태를 정한다.",
                 cols, rows, meta='key="skill_id,order"')


def build_skill_growth():
    cols = [
        ("skill_id", "foreign Skill", "어느 스킬인가", "c,s"),
        ("level", "int (min=1, max=10)", "스킬 레벨", "c,s"),
        ("power_factor", "int (min=10000)", "효과 배수. 만분율", "c,s"),
        ("costs[0].currency_id", "Cost", "소모 재화", "c,s"),
        ("costs[0].amount", "", "첫째 소모 수량", "c,s"),
        ("costs[1].currency_id", "", "둘째 소모 재화", "c,s"),
        ("costs[1].amount", "", "둘째 소모 수량", "c,s"),
    ]
    rows = []
    for sid, el, name, role, scope, cd, kind in SKILLS:
        for level in range(1, 11):
            factor = 10000 + (level - 1) * 1200
            gold = 600 * level * level
            mat = 4 * level
            rows.append([sid, level, factor, "gold", gold, "food", mat])
    return table("SkillGrowth", "스킬 레벨별 효과와 재료이다.", cols, rows,
                 meta='key="skill_id,level"')


def build_monster_skill():
    cols = [
        ("monster_id", "foreign Monster", "어느 단계인가", "c,s"),
        ("skill_id", "foreign Skill", "어느 스킬인가", "c,s"),
        ("slot_kind", "SlotKind", "액티브인지 패시브인지", "c,s"),
        ("unlock_stage", "int (min=1, max=3)", "이 단계부터 사용할 수 있다", "c,s"),
    ]
    by_element = {}
    for s in SKILLS:
        by_element.setdefault(s[1], []).append(s)
    rows = []
    for sid, el, grade, role, stages, names in SPECIES:
        pool = [s for s in by_element.get(el, []) if s[3] == role] or by_element.get(el, [])
        plain = [s for s in SKILLS if s[1] == ""]
        candidates = pool + [s for s in by_element.get(el, []) if s not in pool] + plain
        for stage in range(1, stages + 1):
            count = 3 + (stage - 1)
            chosen, seen = [], set()
            for s in candidates:
                if s[0] in seen:
                    continue
                seen.add(s[0])
                chosen.append(s)
                if len(chosen) == count:
                    break
            for s in chosen:
                rows.append([monster_id(sid, stage), s[0], "Active", stage])
            passive = plain[stage % len(plain)]
            if passive[0] not in seen:
                rows.append([monster_id(sid, stage), passive[0],
                             "Passive" if stage >= 2 else "Active", stage])
    return table("MonsterSkill", "단계별로 사용할 수 있는 스킬이다.", cols, rows,
                 meta='key="monster_id,skill_id"')


# ---------------------------------------------------------------- 전투

def build_affinity():
    """속성 상성. 매트릭스 판독은 이름 기반 레이아웃의 기능이므로 일반 표로 적는다."""
    strong = {"Flame": ["Leaf"], "Tide": ["Flame"], "Leaf": ["Tide"],
              "Arc": ["Tide", "Leaf"], "Umbra": ["Arc"]}
    weak = {"Flame": ["Tide"], "Tide": ["Leaf"], "Leaf": ["Flame"],
            "Arc": ["Umbra"], "Umbra": []}
    cols = [
        ("attacker", "Element", "공격 속성", "c,s"),
        ("defender", "Element", "방어 속성", "c,s"),
        ("factor", "int (min=1)", "피해 배수. 만분율", "c,s"),
        ("#", "", "", ""),
    ]
    rows = []
    for el in ELEMENTS:
        for other in ELEMENTS:
            v = 13500 if other in strong[el] else (7500 if other in weak[el] else 10000)
            note = "유리" if v > 10000 else ("불리" if v < 10000 else "")
            rows.append([el, other, v, note])
    return table("ElementAffinity",
                 "속성 상성 배수이다. 조합이 키이므로 복합 기본 인덱스이다.",
                 cols, rows, meta='key="attacker,defender"')


BOSS_ABILITY = ["Enrage", "Shield", "Summon", "Purge", "Reflect"]


def build_boss():
    cols = [
        ("boss_id", "string", "식별자", "c,s"),
        ("monster_id", "foreign Monster", "외형과 기본 능력치의 출처", "c,s"),
        ("stat_factor.hp", "StatBlock", "계수. 만분율", "c,s"),
        ("stat_factor.attack", "", "공격 계수", "c,s"),
        ("stat_factor.defense", "", "방어 계수", "c,s"),
        ("stat_factor.speed", "", "행동 순서 계수", "c,s"),
        ("stat_factor.crit_rate", "", "치명타 확률 계수", "c,s"),
        ("stat_factor.crit_power", "", "치명타 배수 계수", "c,s"),
        ("ability", "BossAbility", "특수 능력", "c,s"),
        ("ability_pattern[0][0]", "int (min=0, max=3)", "페이즈별 행동 순서", "c,s"),
        ("ability_pattern[0][1]", "", "1페이즈 2번째 행동", "c,s"),
        ("ability_pattern[0][2]", "", "1페이즈 3번째 행동", "c,s"),
        ("ability_pattern[1][0]", "", "2페이즈 1번째 행동", "c,s"),
        ("ability_pattern[1][1]", "", "2페이즈 2번째 행동", "c,s"),
        ("ability_pattern[1][2]", "", "2페이즈 3번째 행동", "c,s"),
        ("ability_pattern[2][0]", "", "3페이즈 1번째 행동", "c,s"),
        ("ability_pattern[2][1]", "", "3페이즈 2번째 행동", "c,s"),
        ("ability_pattern[2][2]", "", "3페이즈 3번째 행동", "c,s"),
        ("spawn_rotation", "quat?", "등장 연출의 회전", "c"),
        ("tint", "color32", "실루엣 색", "c"),
        ("effects[].$type", "Effect", "특수 능력의 효과. 원소가 행에서 온다", "c,s"),
        ("effects[].chance", "", "발동 확률. 만분율", "c,s"),
        ("effects[].power", "", "피해 또는 회복 배수", "c,s"),
        ("effects[].status", "", "부여하는 상태", "c,s"),
        ("effects[].stat", "", "변동시키는 능력치", "c,s"),
        ("effects[].ratio", "", "변동률. 만분율", "c,s"),
        ("effects[].duration", "", "지속 턴", "c,s"),
    ]
    guardians = ["elder_bark_2", "abyss_whale_1", "pyre_lion_1", "thunder_stag_1", "void_seraph_3"]
    tints = ["#3E7A45FF", "#2F6E8CFF", "#A63A22FF", "#5C7FA8FF", "#3B2A5AFF"]
    rows = []
    for i, (rid, rname, el) in enumerate(REGIONS):
        pattern = [[(j + k) % 3 for k in range(3)] for j in range(3)]
        flat = [v for row in pattern for v in row]
        rows.append([
            "boss_%s" % rid, guardians[i],
            18000 + i * 2000, 13000 + i * 1000, 14000, 11000, 10000, 10000,
            BOSS_ABILITY[i],
        ] + flat + [
            "(0,0,0,1)" if i % 2 == 0 else "", tints[i],
            "DamageEffect", 10000, 22000 + i * 2000, "", "", "", "",
        ])
        rows.append([""] * 20 + ["StatusEffect", 3500, "", "Stun", "", "", 1])
    return table("Boss",
                 "지역 수호자이다. 효과가 멀티 로우 다형 배열이므로 5단계를 기다린다.",
                 cols, rows)


# ---------------------------------------------------------------- 세계

def build_region():
    cols = [
        ("region_id", "string", "식별자", "c,s"),
        ("name", "string (text=World)", "표시 이름", "c"),
        ("order", "int (min=1)", "진행 순서", "c,s"),
        ("state", "RegionState", "해금 상태의 초기값", "c,s"),
        ("theme_element", "Element", "그 지역에 많은 속성", "c,s"),
        ("background", "string (asset=model)", "배경", "c"),
        ("fog_color", "color", "안개 색", "c"),
        ("requirement_group_id", "foreign RequirementGroup?", "해금 조건. 첫 지역은 비운다", "c,s"),
    ]
    fogs = ["#C8DCC0", "#BFD6E4", "#E0BFAE", "#D2DEE8", "#C3B8D6"]
    rows = []
    for i, (rid, rname, el) in enumerate(REGIONS):
        rows.append([rid, rname, i + 1, "Open" if i == 0 else "Locked", el,
                     "bg_%s" % rid, fogs[i],
                     "" if i == 0 else "req_region_%s" % rid])
    return table("Region", "지역이다. 장기 진행 구조를 만든다.", cols, rows)


def build_region_yield():
    cols = [
        ("region_id", "foreign Region", "어느 지역인가", "c,s"),
        ("hour_band", "int (min=0, max=7)", "누적 시간대", "c,s"),
        ("gold_per_hour", "int (min=0)", "시간당 골드", "c,s"),
        ("food_per_hour", "int (min=0)", "시간당 먹이", "c,s"),
        ("reward_group_id", "foreign RewardGroup", "재료 드랍 묶음", "c,s"),
    ]
    rows = []
    for i, (rid, rname, el) in enumerate(REGIONS):
        for band in range(8):
            decay = 100 - band * 6
            rows.append([rid, band,
                         (900 + i * 700) * decay // 100,
                         (12 + i * 6) * decay // 100,
                         "rg_%s_material" % rid])
    return table("RegionYield",
                 "지역과 시간대별 산출이다. 뒤로 갈수록 줄어든다.",
                 cols, rows, meta='key="region_id,hour_band"')


def build_stage():
    cols = [
        ("stage_id", "string", "식별자", "c,s"),
        ("region_id", "foreign Region", "어느 지역인가", "c,s"),
        ("index", "int (min=1, max=18)", "지역 안의 순번", "c,s"),
        ("stage_kind", "StageKind", "일반 · 관측 · 수호자", "c,s"),
        ("wave_monster_ids", "foreign Monster[]", "등장 목록. 셀 안의 참조 배열", "c,s"),
        ("wave_levels", "int[] (size=1..5)", "각 등장의 레벨", "c,s"),
        ("reward_group_id", "foreign RewardGroup", "클리어 보상", "c,s"),
    ]
    by_region = {}
    for sid, el, grade, role, stages, names in SPECIES:
        r = REGION_OF_ELEMENT[el]
        by_region.setdefault(r, []).append(monster_id(sid, 1))
    rows = []
    for i, (rid, rname, el) in enumerate(REGIONS):
        pool = by_region.get(i, ["sprout_deer_1"])
        for index in range(1, 19):
            kind = "Guardian" if index == 18 else ("Observation" if index in (3, 9, 15) else "Normal")
            count = 1 + (index // 7)
            wave = [pool[(index + k) % len(pool)] for k in range(count)]
            level = 3 + index * 2 + i * 12
            rows.append([
                "%s_%02d" % (rid, index), rid, index, kind,
                ";".join(wave), ";".join([str(level)] * count),
                "rg_%s_stage_%02d" % (rid, index),
            ])
    return table("Stage", "스테이지이다. 첫 키가 단일이므로 참조 대상이 된다.",
                 cols, rows, meta='key="stage_id; region_id,index"')


def build_encounter():
    cols = [
        ("encounter_id", "string", "식별자", "c,s"),
        ("region_id", "foreign Region", "어느 지역인가", "c,s"),
        ("requirement_group_id", "foreign RequirementGroup?", "은둔 슬롯의 조건", "c,s"),
        ("entries[].monster_id", "foreign Monster", "어느 단계가 나오는가", "c,s"),
        ("entries[].weight", "int (min=1)", "가중치", "c,s"),
        ("entries[].encounter_slot", "EncounterSlot", "어느 슬롯에서 나오는가", "c,s"),
    ]
    by_region = {}
    for sid, el, grade, role, stages, names in SPECIES:
        by_region.setdefault(REGION_OF_ELEMENT[el], []).append((sid, grade, stages))
    rows = []
    for i, (rid, rname, el) in enumerate(REGIONS):
        pool = list(by_region.get(i, []))
        order = ["Mythic", "Legendary", "Epic", "Rare", "Common"]
        rarest = min(pool, key=lambda e: order.index(e[1]))[0] if pool else ""
        for n in (1, 2, 3):
            pool += [(a, b, 1) for a, b, c in by_region.get((i + n) % 5, [])]
        first = True
        for sid, grade, stages in pool:
            slot = {"Common": "Normal", "Rare": "Normal", "Epic": "Rare",
                    "Legendary": "Rare", "Mythic": "Hidden"}[grade]

            # 그 지역에서 가장 희귀한 종을 은둔 슬롯으로. 등급만으로 정하면 그 등급의 종이
            # 없는 지역에는 은둔이 하나도 없게 됩니다 — 기획서 §11.3이 요구하는 것입니다.
            if sid == rarest:
                slot = "Hidden"
            head = ["enc_%s" % rid, rid, "req_hidden_%s" % rid] if first else ["", "", ""]
            rows.append(head + [monster_id(sid, 1), GRADE_WEIGHT[grade], slot])
            first = False
            if stages >= 2:
                rows.append(["", "", "", monster_id(sid, 2),
                             max(1, GRADE_WEIGHT[grade] // 6), slot])
    return table("EncounterTable",
                 "지역별 출현 목록이다. 확률은 클라이언트가 알 필요가 없다.",
                 cols, rows, meta="side=s")


# ---------------------------------------------------------------- 성장

def build_growth_curve():
    cols = [
        ("grade", "Grade", "등급마다 곡선이 다르다", "c,s"),
        ("level", "int (min=1, max=70)", "레벨", "c,s"),
        ("hp_factor", "int (min=10000)", "체력 배수. 만분율", "c,s"),
        ("attack_factor", "int (min=10000)", "공격 배수", "c,s"),
        ("defense_factor", "int (min=10000)", "방어 배수", "c,s"),
        ("bonus_factor", "int?", "구간 보너스. 없으면 비운다", "c,s"),
        ("costs[0].currency_id", "Cost", "소모 재화", "c,s"),
        ("costs[0].amount", "", "첫째 소모 수량", "c,s"),
        ("costs[1].currency_id", "", "둘째 소모 재화", "c,s"),
        ("costs[1].amount", "", "둘째 소모 수량", "c,s"),
    ]
    slope = {"Common": 116, "Rare": 128, "Epic": 140, "Legendary": 154, "Mythic": 170}
    rows = []
    for grade in ["Common", "Rare", "Epic", "Legendary", "Mythic"]:
        for level in range(1, 71):
            k = slope[grade]
            hp = 10000 + (level - 1) * k * 12
            atk = 10000 + (level - 1) * k * 10
            dfn = 10000 + (level - 1) * k * 9
            bonus = 2000 if level % 10 == 0 else ""
            gold = 220 * level * level // 10
            food = 2 + level // 3
            rows.append([grade, level, hp, atk, dfn, bonus, "gold", gold, "food", food])
    return table("GrowthCurve", "등급과 레벨별 능력치 배수와 소모 재화이다.", cols, rows,
                 meta='key="grade,level"')


def build_resonance():
    cols = [
        ("grade", "Grade", "등급", "c,s"),
        ("rank", "int (min=1, max=5)", "공명 등급", "c,s"),
        ("stat_factor", "int (min=10000)", "능력치 배수. 만분율", "c,s"),
        ("shard_cost", "int (min=1)", "필요한 울림 조각", "c,s"),
        ("unlock_note", "string? (text=Growth)", "3 · 5등급의 추가 효과", "c"),
    ]
    rows = []
    for grade in ["Common", "Rare", "Epic", "Legendary", "Mythic"]:
        for rank in range(1, 6):
            note = ""
            if rank == 3:
                note = "액티브 슬롯 하나가 열린다."
            elif rank == 5:
                note = "패시브 슬롯 하나가 열린다."
            rows.append([grade, rank, 10000 + rank * 900,
                         GRADE_SHARD[grade] * rank * 6, note])
    return table("ResonanceRank", "공명 등급별 배수와 필요 조각이다.", cols, rows,
                 meta='key="grade,rank"')


# ---------------------------------------------------------------- 보상과 조건

REWARD_GROUPS = []
REWARD_ENTRIES = []


def add_reward(group, note, entries):
    REWARD_GROUPS.append([group, note])
    for order, e in enumerate(entries):
        REWARD_ENTRIES.append([group, order] + e)


def build_rewards():
    guardians_reward = ["elder_bark_1", "abyss_whale_1", "pyre_lion_1",
                       "thunder_stag_1", "void_seraph_1"]
    for i, (rid, rname, el) in enumerate(REGIONS):
        add_reward("rg_%s_material" % rid, "%s의 재료 드랍" % rname, [
            # 수액은 확정입니다 — 재료 묶음에 확정 항목이 하나도 없으면 방치 보상이 빈 채로 나옵니다.
            ["ItemReward", 3, "mat_%s_resin" % rid, "", "", 10000, 100],
            ["ItemReward", 1, "mat_%s_core" % rid, "", "", 2500, 40],
            # 각성 재료는 **최소 2개 지역**에서 나옵니다 — 기획서 §10.4. 한 지역에만 나오면
            # 그 지역을 반복하는 것 외에 할 일이 없어집니다.
            ["ItemReward", 1,
             "mat_%s_core" % REGIONS[(i + 1) % len(REGIONS)][0], "", "", 800, 12],
            ["CurrencyReward", 5, "", "gem", "", 300, 5],
        ])
        for index in range(1, 19):
            add_reward("rg_%s_stage_%02d" % (rid, index),
                       "%s %d번 스테이지" % (rname, index), [
                ["CurrencyReward", (300 + i * 200) + index * 40, "", "gold", "", 10000, ""],
                ["CurrencyReward", 6 + i * 2 + index // 4, "", "food", "", 5000, ""],
                ["ItemReward", 1, "mat_%s_dust" % rid, "", "", 3000, 20],
            ])
        add_reward("rg_%s_guardian" % rid, "%s 수호자 격파" % rname, [
            ["CurrencyReward", 2000 + i * 1500, "", "gold", "", 10000, ""],
            ["MonsterReward", 1, "", "", guardians_reward[i], 10000, ""],
            ["CurrencyReward", 80, "", "gem", "", 10000, ""],
        ])
        for t in (30, 60, 100):
            add_reward("rg_codex_%s_%d" % (rid, t), "%s 기록부 %d%%" % (rname, t), [
                ["CurrencyReward", 30 * t, "", "gold", "", 10000, ""],
            ])
    for t in (25, 50, 75, 100):
        add_reward("rg_codex_global_%d" % t, "전체 기록부 %d%%" % t, [
            ["CurrencyReward", t * 4, "", "gem", "", 10000, ""],
        ])
    for cycle, n in (("Daily", 3), ("Weekly", 1)):
        for k in range(n):
            add_reward("rg_mission_%s_%d" % (cycle.lower(), k), "%s 의뢰 보상" % cycle, [
                ["CurrencyReward", 800 if cycle == "Daily" else 4000, "", "gold", "", 10000, ""],
                ["CurrencyReward", 10 if cycle == "Daily" else 60, "", "gem", "", 10000, ""],
            ])
    for name, gold, gem in (("ad_idle", 0, 0), ("ad_explore", 1200, 0),
                            ("ad_free", 400, 5), ("ad_retry", 0, 0)):
        add_reward("rg_%s" % name, "광고 보상", [
            ["CurrencyReward", max(gold, 1), "", "gold", "", 10000, ""],
        ] + ([["CurrencyReward", gem, "", "gem", "", 10000, ""]] if gem else []))
    for k, (gem, price) in enumerate([(60, "1"), (330, "2"), (1100, "3"), (2400, "4")]):
        add_reward("rg_package_%d" % k, "원석 패키지", [
            ["CurrencyReward", gem, "", "gem", "", 10000, ""],
        ])
    add_reward("rg_pass_daily", "월간 통행증 일일 지급", [
        ["CurrencyReward", 50, "", "gem", "", 10000, ""],
        ["CurrencyReward", 20, "", "food", "", 10000, ""],
    ])
    for i in range(12):
        add_reward("rg_shop_%02d" % i, "상점 판매 항목", [
            ["ItemReward", 1 + i % 3, "mat_weir_forest_resin", "", "", 10000, ""],
        ])
    for i in range(8):
        add_reward("rg_shop_season_%02d" % i, "시즌 상점 항목", [
            ["ItemReward", 2, "mat_salt_coast_core", "", "", 10000, ""],
        ])
    for i in range(6):
        add_reward("rg_shop_package_%02d" % i, "패키지 상점 항목", [
            ["CurrencyReward", 300, "", "gold", "", 10000, ""],
        ])

    gcols = [
        ("reward_group_id", "string", "식별자", "c,s"),
        ("note", "string?", "이 묶음이 무엇인지", ""),
    ]
    ecols = [
        ("reward_group_id", "foreign RewardGroup", "어느 묶음인가", "c,s"),
        ("order", "int (min=0, max=15)", "표시 순서", "c,s"),
        ("reward.$type", "Reward", "이 행의 보상이 어떤 형태인가", "c,s"),
        ("reward.amount", "", "수량", "c,s"),
        ("reward.item_id", "", "아이템 보상의 대상", "c,s"),
        ("reward.currency_id", "", "재화 보상의 대상", "c,s"),
        ("reward.monster_id", "", "와일드링 또는 조각 보상의 대상", "c,s"),
        ("rate", "int (min=1, max=10000)", "확률. 만분율", "c,s"),
        ("server_weight", "int?", "서버만 쓰는 가중치", "s"),
    ]
    return (table("RewardGroup", "보상 묶음이다. 여러 테이블이 이것을 가리킨다.", gcols,
                  REWARD_GROUPS),
            table("RewardEntry", "묶음 하나의 항목이다. 판별자가 형태를 정한다.", ecols,
                  REWARD_ENTRIES, meta='key="reward_group_id,order"'))


REQ_GROUPS = []
REQ_ENTRIES = []


def add_req(group, note, entries):
    REQ_GROUPS.append([group, note])
    for order, e in enumerate(entries):
        REQ_ENTRIES.append([group, order] + e)


def build_requirements():
    for sid, el, grade, role, stages, names in SPECIES:
        for stage in range(2, stages + 1):
            level = 20 if stage == 2 else 40
            entries = [
                ["LevelRequirement", level, "", "", ""],
                ["CodexRequirement", "", "Studied", "", ""],
            ]
            if stage == 3:
                entries.append(["StageRequirement", "", "", "", "ashen_crater_12"])
                entries.append(["ItemRequirement", "", "", "mat_ashen_crater_core", 4])
            add_req("req_awaken_%s_%d" % (sid, stage), "%s %d단 각성" % (names[0], stage), entries)
    for i, (rid, rname, el) in enumerate(REGIONS):
        if i:
            add_req("req_region_%s" % rid, "%s 해금" % rname, [
                ["StageRequirement", "", "", "", "%s_18" % REGIONS[i - 1][0]],
                ["CodexRequirement", "", "Recorded", "", ""],
            ])
        add_req("req_hidden_%s" % rid, "%s 은둔 슬롯" % rname, [
            ["CodexRequirement", "", "Studied", "", ""],
        ])
    for cycle, n in (("Daily", 3), ("Weekly", 1)):
        for k in range(n):
            add_req("req_mission_%s_%d" % (cycle.lower(), k), "%s 의뢰 조건" % cycle, [
                ["LevelRequirement", 5 + k * 5, "", "", ""],
            ])

    gcols = [
        ("requirement_group_id", "string", "식별자", "c,s"),
        ("note", "string?", "이 묶음이 무엇인지", ""),
    ]
    ecols = [
        ("requirement_group_id", "foreign RequirementGroup", "어느 묶음인가", "c,s"),
        ("order", "int (min=0, max=7)", "검사 순서", "c,s"),
        ("req.$type", "Requirement", "이 행의 조건이 어떤 형태인가", "c,s"),
        ("req.level", "", "요구 레벨", "c,s"),
        ("req.codex_state", "", "요구 기록 상태", "c,s"),
        ("req.item_id", "", "요구 아이템", "c,s"),
        ("req.amount", "", "요구 수량", "c,s"),
        ("req.stage_id", "", "요구 스테이지", "c,s"),
    ]
    # 컬럼 순서를 선언과 맞춘다 — level · codex_state · item_id · amount · stage_id
    entries = []
    for g, order, variant, level, codex, item, amount in [
            (e[0], e[1], e[2], e[3], e[4], e[5], e[6]) for e in REQ_ENTRIES]:
        entries.append([g, order, variant, level, codex, item, amount, ""])
    fixed = []
    for e, src in zip(entries, REQ_ENTRIES):
        if src[2] == "StageRequirement":
            e[3] = e[4] = e[5] = e[6] = ""
            e[7] = src[6]
        fixed.append(e)
    return (table("RequirementGroup", "조건 묶음이다.", gcols, REQ_GROUPS),
            table("RequirementEntry", "묶음 하나의 조건이다.", ecols, fixed,
                  meta='key="requirement_group_id,order"'))


def build_drop_table():
    cols = [
        ("drop_group_id", "string", "식별자", "c,s"),
        ("region_id", "foreign Region", "어느 지역인가", "c,s"),
        ("reward_group_id", "foreign RewardGroup", "무엇이 나오는가", "c,s"),
        ("roll_count", "int (min=1, max=8)", "한 번에 몇 번 굴리는가", "c,s"),
    ]
    rows = []
    for i, (rid, rname, el) in enumerate(REGIONS):
        for k, band in enumerate(["early", "mid", "late"]):
            rows.append(["drop_%s_%s" % (rid, band), rid,
                         "rg_%s_material" % rid, 1 + k])
    return table("DropTable", "지역별 드랍 묶음이다.", cols, rows)


def build_stage_reward():
    cols = [
        ("stage_id", "foreign Stage", "어느 스테이지인가", "c,s"),
        ("reward_group_id", "foreign RewardGroup", "반복 보상", "c,s"),
        ("first_clear_group_id", "foreign RewardGroup?", "첫 클리어 보상", "c,s"),
    ]
    rows = []
    for i, (rid, rname, el) in enumerate(REGIONS):
        for index in range(1, 19):
            first = "rg_%s_guardian" % rid if index == 18 else ""
            rows.append(["%s_%02d" % (rid, index), "rg_%s_stage_%02d" % (rid, index), first])
    return table("StageReward", "스테이지 보상이다.", cols, rows)


def build_codex_reward():
    cols = [
        ("codex_reward_id", "string", "식별자", "c,s"),
        ("codex_scope", "CodexScope", "지역별인지 전체인지", "c,s"),
        ("region_id", "foreign Region?", "전체 보상은 비운다", "c,s"),
        ("threshold", "int (min=1, max=100)", "완성률", "c,s"),
        ("reward_group_id", "foreign RewardGroup", "보상", "c,s"),
    ]
    rows = []
    for rid, rname, el in REGIONS:
        for t in (30, 60, 100):
            rows.append(["codex_%s_%d" % (rid, t), "Region", rid, t,
                         "rg_codex_%s_%d" % (rid, t)])
    for t in (25, 50, 75, 100):
        rows.append(["codex_global_%d" % t, "Global", "", t, "rg_codex_global_%d" % t])
    return table("CodexReward", "기록부 완성률 보상이다.", cols, rows)


def build_mission():
    cols = [
        ("mission_id", "string", "식별자", "c,s"),
        ("cycle", "MissionCycle", "일일인지 주간인지", "c,s"),
        ("name", "string (text=Mission)", "표시 이름", "c"),
        ("target_id", 'string (refs="Monster;Region;Item")', "대상. 참조가 아니라 검사이다", "c,s"),
        ("goal_count", "int (min=1)", "목표 횟수", "c,s"),
        ("requirement_group_id", "foreign RequirementGroup", "수령 조건", "c,s"),
        ("reward_group_id", "foreign RewardGroup", "보상", "c,s"),
    ]
    targets = ["sprout_deer_1", "weir_forest", "mat_weir_forest_resin",
               "ember_fox_1", "salt_coast", "mat_salt_coast_core"]
    rows = []
    n = 0
    for cycle, count in (("Daily", 27), ("Weekly", 9)):
        for k in range(count):
            group = "%s_%d" % (cycle.lower(), k % (3 if cycle == "Daily" else 1))
            rows.append(["mission_%s_%02d" % (cycle.lower(), k), cycle,
                         "%s 의뢰 %d" % ("일일" if cycle == "Daily" else "주간", k + 1),
                         targets[n % len(targets)], 3 + (k % 5) * 2,
                         "req_mission_%s" % group, "rg_mission_%s" % group])
            n += 1
    return table("Mission", "일일과 주간 의뢰이다.", cols, rows)


# ---------------------------------------------------------------- 경제

def build_currency():
    cols = [
        ("currency_id", "string", "식별자", "c,s"),
        ("name", "string (text=Item)", "표시 이름", "c"),
        ("icon", "string (asset=icon)", "아이콘", "c"),
        ("cap", "int (min=1)", "보유 상한", "c,s"),
        ("tradable", "bool", "상점에서 살 수 있는가", "c,s"),
    ]
    rows = [
        ["gold", "은편", "cur_gold", 999999999, "TRUE"],
        ["gem", "원석", "cur_gem", 999999, "TRUE"],
        ["food", "먹이", "cur_food", 99999, "TRUE"],
        ["shard", "울림 조각", "cur_shard", 99999, "FALSE"],
    ]
    return table("Currency", "재화이다. 조각은 상점에서 팔지 않는다.", cols, rows)


def build_item():
    cols = [
        ("category@1", "ItemCategory", "첫 컬럼이지만 인덱스가 아니다", "c,s"),
        ("item_id@2", "string", "식별자", "c,s"),
        ("name@3", "string (text=Item)", "표시 이름", "c"),
        ("grade@4", "Grade", "등급", "c,s"),
        ("icon@6", "string (asset=icon)", "아이콘", "c"),
        ("stack_max@7", "int (min=1, notDefault)", "한 칸에 쌓이는 최대", "c,s"),
        ("#old_price@5", "", "가격을 상점으로 옮기기 전의 컬럼. 5번을 예약한다", ""),
        ("#", "", "", ""),
    ]
    rows = []
    for i, (rid, rname, el) in enumerate(REGIONS):
        for kind, ko, grade in (("resin", "수액", "Common"), ("core", "핵", "Rare"),
                                ("relic", "유물", "Epic"), ("dust", "가루", "Common"),
                                ("sigil", "인장", "Legendary")):
            rows.append(["Material", "mat_%s_%s" % (rid, kind),
                         "%s %s" % (rname, ko), grade,
                         "it_%s_%s" % (rid, kind), 9999, "", ""])
    for el in ELEMENTS:
        for tier in range(1, 4):
            rows.append(["Material", "mat_awaken_%s_%d" % (el.lower(), tier),
                         "%s 각성석 %d" % (ELEMENT_KO[el], tier),
                         ["Common", "Rare", "Epic"][tier - 1],
                         "it_awaken_%s_%d" % (el.lower(), tier), 999, "", ""])
    for i in range(24):
        rows.append(["Consumable", "use_food_%02d" % i, "먹이 꾸러미 %d" % (i + 1),
                     "Common" if i < 16 else "Rare", "it_food_%02d" % i, 999, "", ""])
    for i in range(12):
        rows.append(["Ticket", "tkt_retry_%02d" % i, "재도전 표 %d" % (i + 1),
                     "Rare", "it_ticket_%02d" % i, 99, "", ""])
    for i in range(16):
        rows.append(["Consumable", "use_boost_%02d" % i, "탐사 촉진제 %d" % (i + 1),
                     "Common" if i % 2 else "Rare", "it_boost_%02d" % i, 99, "", ""])
    return table("Item", "아이템이다.", cols, rows, meta="key=item_id")


def build_shop():
    scols = [
        ("shop_id", "string", "식별자", "c,s"),
        ("name", "string (text=Shop)", "표시 이름", "c"),
        ("refresh_hours", "int (min=0)", "갱신 주기. 0이면 갱신하지 않는다", "c,s"),
    ]
    srows = [["shop_main", "상시 상점", 24],
             ["shop_season", "시즌 상점", 168],
             ["shop_package", "패키지", 0]]

    def slot_cols():
        return [
            ("shop_slot_id", "string", "식별자", "c,s"),
            ("shop_id", "foreign Shop", "어느 상점인가", "c,s"),
            ("slot_index", "int (min=0, max=11)", "표시 자리", "c,s"),
            ("reward_group_id", "foreign RewardGroup", "판매 내용", "c,s"),
            ("cost", "Cost", "가격. 셀 하나에 `재화,수량`", "c,s"),
            ("stock", "int (min=1, max=99)", "재고", "c,s"),
        ]

    def slots(prefix, shop, count, group_prefix):
        return [["%s_%02d" % (prefix, i), shop, i, "%s_%02d" % (group_prefix, i),
                 "gold,%d" % (500 + i * 250), 3 + i % 4] for i in range(count)]

    return [
        table("Shop", "상점이다.", scols, srows),
        table("ShopSlot", "상시 상점의 판매 항목이다.", slot_cols(),
              slots("slot_main", "shop_main", 12, "rg_shop")),
        table("ShopSlot_Season", "시즌 상점의 판매 항목이다. 스키마가 같은 다른 벌이다.",
              slot_cols(), slots("slot_season", "shop_season", 8, "rg_shop_season")),
        table("ShopSlot_Package", "패키지 상점의 판매 항목이다.", slot_cols(),
              slots("slot_package", "shop_package", 6, "rg_shop_package")),
    ]


def build_package():
    cols = [
        ("package_id", "string", "식별자", "c,s"),
        ("name", "string (text=Shop)", "표시 이름", "c"),
        ("reward_group_id", "foreign RewardGroup", "지급 내용", "c,s"),
        ("price_display", "string", "표시 가격", "c"),
        ("price_display", "", "", "c"),
        ("price_display", "", "", "c"),
        ("sort_order", "int (min=0)", "표시 순서", "c,s"),
    ]
    variants = ["", "us", "jp"]
    rows = []
    prices = [("1,500원", "$1.99", "300円"), ("7,900원", "$5.99", "900円"),
              ("25,000원", "$19.99", "3,000円"), ("59,000원", "$49.99", "7,300円")]
    for k, (kr, us, jp) in enumerate(prices):
        rows.append(["pkg_gem_%d" % k, "원석 꾸러미 %d" % (k + 1),
                     "rg_package_%d" % k, kr, us, jp, k])
    out = table("Package", "판매 상품이다. 표시 가격만 국가별로 갈린다.", cols, rows)
    # `:variant` 행을 헤더에 끼운다 — 기본 변형은 빈 칸이다.
    variant_row = [":variant", "", "", "", "", "us", "jp", ""]
    out.insert(4, variant_row)
    return out


def build_pass_and_ads():
    pcols = [
        ("benefit_id", "string", "식별자", "c,s"),
        ("name", "string (text=Shop)", "표시 이름", "c"),
        ("reward_group_id", "foreign RewardGroup?", "일일 지급", "c,s"),
        ("idle_hours_bonus", "int (min=0, max=8)", "탐사 상한 연장", "c,s"),
        ("ad_free", "bool", "광고 제거 포함", "c,s"),
    ]
    prows = [
        ["pass_monthly", "월간 통행증", "rg_pass_daily", 2, "TRUE"],
        ["pass_ad_free", "광고 제거", "", 0, "TRUE"],
    ]
    acols = [
        ("ad_reward_id", "string", "식별자", "c,s"),
        ("name", "string (text=Shop)", "표시 이름", "c"),
        ("daily_limit", "int (min=1, max=10)", "일일 한도", "c,s"),
        ("reward_group_id", "foreign RewardGroup", "보상", "c,s"),
        ("doubles_idle", "bool", "방치 보상을 2배로", "c,s"),
    ]
    arows = [
        ["ad_idle", "방치 보상 2배", 3, "rg_ad_idle", "TRUE"],
        ["ad_explore", "추가 탐사", 2, "rg_ad_explore", "FALSE"],
        ["ad_free", "무료 보상", 5, "rg_ad_free", "FALSE"],
        ["ad_retry", "수호자 즉시 재도전", 3, "rg_ad_retry", "FALSE"],
    ]
    return [table("PassBenefit", "월간 통행증과 광고 제거이다.", pcols, prows),
            table("AdReward", "선택형 광고 보상이다. 강제 광고는 없다.", acols, arows)]


# ---------------------------------------------------------------- enum · 상수셋

def build_enums():
    return [
        enum("Role", "전투에서의 역할이다.", [
            ("Vanguard", 1, "피해를 받는 자리", "선봉"),
            ("Breaker", 2, "단일 대상 피해", "파격"),
            ("Warden", 3, "회복과 보호", "수호"),
            ("Tuner", 4, "능력치 변동과 상태 부여", "조율"),
        ]),
        enum("RegionState", "지역의 초기 상태이다.", [
            ("Locked", 0, "해금되지 않음", "잠김"),
            ("Open", 1, "해금됨", "열림"),
        ]),
        enum("MissionCycle", "의뢰의 주기이다.", [
            ("Daily", 1, "매일 05:00 초기화"),
            ("Weekly", 2, "월요일 05:00 초기화"),
        ]),
        enum("ItemCategory", "아이템의 분류이다.", [
            ("Material", 1, "성장 재료"),
            ("Consumable", 2, "소모품"),
            ("Ticket", 3, "입장권과 재도전 표"),
        ]),
        enum("SlotKind", "스킬 슬롯의 종류이다.", [
            ("Active", 1, "턴마다 순환해 사용"),
            ("Passive", 2, "상시 적용"),
        ]),
        enum("StageKind", "스테이지의 종류이다.", [
            ("Normal", 1, "일반"),
            ("Observation", 2, "클리어 시 미기록 종 1종이 목격 상태가 된다"),
            ("Guardian", 3, "수호자"),
        ]),
        enum("TargetScope", "스킬의 대상 범위이다.", [
            ("Single", 1, "적 1"),
            ("AllEnemy", 2, "적 전체"),
            ("OneAlly", 3, "아군 1"),
            ("AllAlly", 4, "아군 전체"),
            ("Self", 5, "자신"),
        ]),
        enum("BossAbility", "수호자의 특수 능력이다.", [
            ("Enrage", 1, "체력 50% 이하에서 공격 상승"),
            ("Shield", 2, "일정 턴마다 피해 흡수막"),
            ("Summon", 3, "하위 개체 1체 소환"),
            ("Purge", 4, "아군 상태 효과 제거"),
            ("Reflect", 5, "받은 피해의 일부 반사"),
        ]),
        enum("CodexScope", "기록부 보상의 범위이다.", [
            ("Region", 1, "지역별"),
            ("Global", 2, "전체"),
        ]),
    ]


def build_consts():
    battle = const("BattleConst", "전투 계산의 계수이다.", [
        ("DefenseFactor", "int", 60, "방어가 피해를 깎는 비율. 만분율"),
        ("MaxTurn", "int", 30, "이 턴에 결착이 없으면 체력 비율로 판정한다"),
        ("SpeedTiebreak", "bool", "TRUE", "속도가 같으면 배치 순서"),
        ("NeutralAffinity", "int", 10000, "상성이 없을 때의 배수"),
    ])
    battle_speed = table("BattleSpeed",
                         "배속 단계이다. 전투 상수만 쓰는 작은 표라 같은 탭에 둔다.",
                         [("at", "int (min=0, max=3)", "자리", "c,s"),
                          ("multiplier", "int (min=1)", "배수", "c,s"),
                          ("label", "string (text=Battle)", "표시", "c")],
                         [[0, 1, "1배"], [1, 2, "2배"], [2, 4, "4배"]])
    growth = const("GrowthConst", "성장 상한이다.", [
        ("LevelCapStage1", "int", 20, "1단의 레벨 상한"),
        ("LevelCapStage2", "int", 40, "2단의 레벨 상한"),
        ("LevelCapStage3", "int", 70, "3단의 레벨 상한"),
        ("SkillLevelCap", "int", 10, "스킬 레벨 상한"),
        ("ResonanceCap", "int", 5, "공명 등급 상한"),
    ])
    awakening = const("AwakeningConst", "각성이 늘리는 것이다.", [
        ("ActiveSlotsStage1", "int", 2, "1단의 액티브 슬롯"),
        ("ActiveSlotsStage2", "int", 3, "2단"),
        ("PassiveSlotsStage2", "int", 1, "2단의 패시브 슬롯"),
        ("PassiveSlotsStage3", "int", 2, "3단"),
    ])
    idle = const("IdleConst", "방치의 상한과 계수이다.", [
        ("CapHours", "int", 8, "누적 상한"),
        ("ProgressFactor", "int", 10000, "스테이지 진척도 계수. 만분율"),
        ("AdDoubleTargets", "string[]", "gold;food;material", "2배 대상. 발견과 조각은 아니다"),
    ])
    party = const("PartyConst", "파티의 규격이다.", [
        ("PartySize", "int", 3, "파티 인원"),
        ("SavedParties", "int", 3, "저장 슬롯"),
        ("ColumnNames", "string[]", "Front;Middle;Back", "열의 이름. 배치 순서 그대로"),
        ("VanguardColumns", "string[]", "Front", "선봉이 설 수 있는 열"),
        ("BreakerColumns", "string[]", "Middle;Back", "파격"),
        ("WardenColumns", "string[]", "Back", "수호"),
        ("TunerColumns", "string[]", "Middle;Back", "조율"),
    ])
    codex = const("CodexConst", "기록부의 계수이다.", [
        ("ObserveCapCommon", "int", GRADE_OBSERVE["Common"], "일반 등급의 관측 상한"),
        ("ObserveCapRare", "int", GRADE_OBSERVE["Rare"], "희귀"),
        ("ObserveCapEpic", "int", GRADE_OBSERVE["Epic"], "영웅"),
        ("ObserveCapLegendary", "int", GRADE_OBSERVE["Legendary"], "전설"),
        ("ObserveCapMythic", "int", GRADE_OBSERVE["Mythic"], "신화"),
        ("BattleObserveFactor", "int", 5000, "전투 승리의 관측 계수. 만분율"),
    ])
    collection = const("CollectionConst", "수집의 계수이다.", [
        ("UnrecordedBoost", "int", 16000, "미기록 종의 가중치 보정. 만분율"),
        ("ShardCap", "int", 9999, "조각 보유 상한"),
    ])
    mission = const("MissionConst", "의뢰의 개수와 초기화이다.", [
        ("DailyCount", "int", 3, "일일 의뢰 개수"),
        ("WeeklyCount", "int", 1, "주간 의뢰 개수"),
        ("ResetHour", "int", 5, "초기화 시각"),
    ])
    return {
        "Const_Battle.tsv": [battle, battle_speed],
        "Const_Growth.tsv": [growth, awakening],
        "Const_Idle.tsv": [idle],
        "Const_Party.tsv": [party],
        "Const_Codex.tsv": [codex, collection],
        "Const_Mission.tsv": [mission],
    }


# ---------------------------------------------------------------- 문자열

def build_strings():
    cols = [
        ("string_id", "string", "식별자", "c,s"),
        ("ko", "string", "한국어", "c"),
        ("en", "string", "영어", "c"),
        ("ja", "string", "일본어", "c"),
    ]
    rows = []
    for sid, el, grade, role, stages, names in SPECIES:
        for stage in range(1, stages + 1):
            key = monster_id(sid, stage)
            rows.append(["monster.%s.name" % key, names[stage - 1],
                         key.replace("_", " ").title(), names[stage - 1]])
            rows.append(["monster.%s.desc" % key,
                         "%s 와일드링이다." % ELEMENT_KO[el],
                         "A %s wildling." % el.lower(),
                         "%s のワイルドリング。" % el])
    for s in SKILLS:
        rows.append(["skill.%s.name" % s[0], s[2], s[0].replace("_", " ").title(), s[2]])
        rows.append(["skill.%s.desc" % s[0], "%s 계열이다." % s[2], "A %s skill." % s[6], s[2]])
    for rid, rname, el in REGIONS:
        rows.append(["region.%s.name" % rid, rname, rid.replace("_", " ").title(), rname])
    for i in range(2100):
        rows.append(["ui.label_%04d" % i, "라벨 %d" % i, "Label %d" % i, "ラベル %d" % i])
    return table("StringTable", "번역 대조본이다.", cols, rows)


# ---------------------------------------------------------------- 실행

def main():
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    total = 0
    total += emit("Monster.tsv", [build_monster()])
    total += emit("MonsterAwakening.tsv", [build_awakening()])
    total += emit("MonsterSkill.tsv", [build_monster_skill()])
    total += emit("Skill.tsv", [build_skill()])
    total += emit("SkillEffect.tsv", [build_skill_effect()])
    total += emit("SkillGrowth.tsv", [build_skill_growth()])
    total += emit("ElementAffinity.tsv", [build_affinity()])
    total += emit("Boss.tsv", [build_boss()])
    total += emit("Region.tsv", [build_region()])
    total += emit("RegionYield.tsv", [build_region_yield()])
    total += emit("Stage.tsv", [build_stage()])
    total += emit("StageReward.tsv", [build_stage_reward()])
    total += emit("EncounterTable.tsv", [build_encounter()])
    total += emit("GrowthCurve.tsv", [build_growth_curve()])
    total += emit("ResonanceRank.tsv", [build_resonance()])
    rg, re_ = build_rewards()
    total += emit("RewardGroup.tsv", [rg])
    total += emit("RewardEntry.tsv", [re_])
    qg, qe = build_requirements()
    total += emit("RequirementGroup.tsv", [qg])
    total += emit("RequirementEntry.tsv", [qe])
    total += emit("DropTable.tsv", [build_drop_table()])
    total += emit("CodexReward.tsv", [build_codex_reward()])
    total += emit("Mission.tsv", [build_mission()])
    total += emit("Currency.tsv", [build_currency()])
    total += emit("Item.tsv", [build_item()])
    for t in build_shop():
        total += emit(t[0][0].split()[1].split("(")[0] + ".tsv", [t])
    total += emit("Package.tsv", [build_package()])
    for t in build_pass_and_ads():
        total += emit(t[0][0].split()[1].split("(")[0] + ".tsv", [t])
    total += emit("Enums.tsv", build_enums())
    for filename, entities in sorted(build_consts().items()):
        total += emit(filename, entities)
    total += emit("StringTable.tsv", [build_strings()])
    print("-" * 40)
    print("%-28s %5d rows" % ("합계", total))


if __name__ == "__main__":
    main()
