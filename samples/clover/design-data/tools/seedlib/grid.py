# -*- coding: utf-8 -*-
"""격자를 `.tsv` 로 쓰는 것과, 효과 행을 만드는 것.

효과 테이블 8개가 **같은 컬럼 구성**입니다. 다른 것은 `owner` 가 무엇을 가리키는가뿐이므로,
그 구성을 여기 한 번 적고 전부가 이것을 씁니다.
"""

import io
import os

DATA = os.path.normpath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'data'))


# ---------------------------------------------------------------------------
# 쓰기
# ---------------------------------------------------------------------------

def write(name, *blocks):
    """격자 하나를 `.tsv` 로 씁니다. 블록이 둘 이상이면 빈 줄로 나눕니다."""
    lines = []
    for i, block in enumerate(blocks):
        if i:
            lines.append('')
        lines.extend(block)
    if not os.path.isdir(DATA):
        os.makedirs(DATA)
    with io.open(os.path.join(DATA, name + '.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    rows = sum(1 for line in lines if line.startswith('\t'))
    print('%-24s %5d행' % (name + '.tsv', rows))


def table(decl, note, fields, types, descs, rows):
    """`:table` 선언 하나와 그 격자."""
    out = [':table %s\t%s' % (decl, note)]
    out.append('\t'.join([':field'] + [cell(v) for v in fields]))
    out.append('\t'.join([':type'] + [cell(v) for v in types]))
    out.append('\t'.join([':desc'] + [cell(v) for v in descs]))
    for row in rows:
        out.append('\t'.join([''] + [cell(v) for v in row]))
    return out


def constants(name, note, rows):
    """`:const` 선언 하나. 행마다 이름 · 타입 · 값 · 설명입니다."""
    out = [':const %s\t%s' % (name, note)]
    out.append('\t'.join([':field', 'name', 'type', 'value', 'desc']))
    for row in rows:
        out.append('\t'.join([''] + [cell(v) for v in row]))
    return out


def dash(v):
    """선언된 옵셔널 컬럼의 빈 값. 이 도구는 빈 칸을 값으로 보지 않습니다."""
    return '-' if v is None else v


def cell(v):
    if v is None:
        return ''
    if v is True:
        return 'true'
    if v is False:
        return 'false'
    if isinstance(v, (list, tuple)):
        return ';'.join(str(x) for x in v)
    return str(v)


# ---------------------------------------------------------------------------
# 효과 행
# ---------------------------------------------------------------------------

COND_FIELDS = ['hand', 'suit', 'enhancement', 'seal', 'edition',
               'blind', 'consumable', 'target', 'counter', 'consume',
               'n', 'compare', 'num', 'den']

OP_FIELDS = ['chips', 'mult', 'money', 'cap', 'unit', 'mode', 'value', 'base_value',
             'min', 'max', 'times', 'counter', 'step', 'init', 'floor', 'reset',
             'hand_pick', 'levels', 'create', 'count', 'card_class', 'edition', 'rarity',
             'enhancement', 'seal', 'suit', 'modify', 'trait', 'debuff',
             'pick', 'rule', 'absolute', 'duration', 'free', 'random', 'ref_id', 'handler']

# 조건과 연산이 나눠 쓰는 칸. 한 행에서 둘 다 쓰는 경우가 없으므로 한 칸입니다.
SHARED_FIELDS = ['ranks', 'suits']


def C(variant, **kw):
    """조건 하나."""
    for key in kw:
        assert key in COND_FIELDS + SHARED_FIELDS, 'condition 에 없는 칸: ' + key
    return ('Cond' + variant, kw)


def O(variant, **kw):
    """연산 하나."""
    for key in kw:
        assert key in OP_FIELDS + SHARED_FIELDS, 'operation 에 없는 칸: ' + key
    return ('Op' + variant, kw)


def E(trigger, cond, op, scope='Run', count=None, chance=None, first=None):
    """효과 행 하나. `chance` 는 `(분자, 분모)` 입니다."""
    return (trigger, cond, op, scope, count, chance, first)


ALWAYS = C('Always')


def j(jid, ko, en, rarity, cost, effects, blueprint=True, unlock=None, pool='Base'):
    """조커 한 종. `pool` 이 기본 대조본인지 확장인지를 가릅니다."""
    return (jid, ko, en, rarity, cost, blueprint, unlock, effects, pool)


# 자주 쓰는 연산. 배수는 만분율이므로 여기서 한 번만 곱합니다.

def AM(n):
    """배수 가산."""
    return O('AddMult', mult=int(round(n * 10000)))


def AC(n):
    """칩 가산."""
    return O('AddChips', chips=n)


def XM(n):
    """배수 곱."""
    return O('MulMult', mult=int(round(n * 10000)))


def MONEY(n, cap=None):
    return O('AddMoney', money=n, cap=cap)


def RULE(rule, value=1, **kw):
    return O('ChangeRule', rule=rule, value=value, **kw)


def GROW(counter, step, **kw):
    kw.setdefault('reset', 'Never')
    return O('GrowSelf', counter=counter, step=step, **kw)


def PER(unit, mode, value, **kw):
    return O('PerUnit', unit=unit, mode=mode, value=value, **kw)


def HC(hand):
    return C('HandContains', hand=hand)


def RANKS(*ranks):
    return C('CardRankSet', ranks=list(ranks))


FACE = C('CardIsFace')

EFFECT_HEAD = ['owner', 'order', 'trigger', 'chance_num', 'chance_den', 'first_only',
               'ranks', 'suits', 'scope', 'scope_count', 'condition.$type']

EFFECT_HEAD_TYPES = ['%s', 'int (min=0, max=15)', 'Trigger', 'int?', 'int?', 'bool?',
                     'RankKind[]?', 'SuitKind[]?', 'Scope', 'int?', 'Condition']

EFFECT_HEAD_DESCS = ['이 효과를 가진 것', '같은 소유자 안의 순서', '언제 보는가',
                     '확률의 분자', '확률의 분모', '조건을 만족한 첫 대상만',
                     '어느 랭크들인가', '어느 무늬들인가',
                     '누구에게', '대상 개수', '무엇이 참이어야 하는가']


def effect_grid(name, owner_type, note, effects):
    """효과 격자 하나. `effects` 는 `(owner_id, [E…])` 의 목록입니다."""
    fields = (EFFECT_HEAD + ['condition.' + f for f in COND_FIELDS]
              + ['operation.$type'] + ['operation.' + f for f in OP_FIELDS])
    types = ([EFFECT_HEAD_TYPES[0] % owner_type] + EFFECT_HEAD_TYPES[1:]
             + [None] * len(COND_FIELDS) + ['Operation'] + [None] * len(OP_FIELDS))
    descs = (EFFECT_HEAD_DESCS + [None] * len(COND_FIELDS)
             + ['무엇을 하는가'] + [None] * len(OP_FIELDS))

    rows = []
    for owner, items in effects:
        for order, item in enumerate(items):
            trigger, cond, op, scope, count, chance, first = item
            cond_name, cond_kw = cond[0], dict(cond[1])
            op_name, op_kw = op[0], dict(op[1])
            num, den = chance if chance else (None, None)

            # 나눠 쓰는 칸은 행으로 올립니다. 한 행에서 조건과 연산이 둘 다 쓰면 그것은
            # 설계가 어긋난 것이므로 여기서 멈춥니다.
            shared = {}
            for key in SHARED_FIELDS:
                in_cond, in_op = cond_kw.pop(key, None), op_kw.pop(key, None)
                assert in_cond is None or in_op is None, \
                    '%s 의 %d번 효과가 `%s` 를 조건과 연산 양쪽에서 씁니다' % (owner, order, key)
                shared[key] = in_cond if in_cond is not None else in_op

            # 선언된 옵셔널 컬럼은 빈 칸이 아니라 `-` 입니다. 빈 칸은 「적는 것을 잊었다」와
            # 구분되지 않으므로 이 도구가 값을 요구합니다.
            row = [owner, order, trigger, dash(num), dash(den), dash(first),
                   dash(shared['ranks']), dash(shared['suits']), scope,
                   dash(count), cond_name]
            row += [cond_kw.get(f) for f in COND_FIELDS]
            row += [op_name]
            row += [op_kw.get(f) for f in OP_FIELDS]
            rows.append(row)

    write(name, table('%s(key="owner,order")' % name, note, fields, types, descs, rows))
    return rows
