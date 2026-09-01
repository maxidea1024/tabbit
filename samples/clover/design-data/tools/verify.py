# -*- coding: utf-8 -*-
"""되는지가 아니라 맞는지를 봅니다.

변환이 성공으로 끝나도 확인되지 않는 것이 있습니다 — 격자와 산출물의 개수가 맞는지,
`doc/tool-findings.md` 의 우회가 아직 남아 있는지는 변환의 어느 검사에도 걸리지 않습니다.

    python samples/clover/design-data/tools/verify.py
"""

import io
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
SAMPLE = os.path.dirname(DESIGN)
ROOT = os.path.dirname(os.path.dirname(SAMPLE))

# 콘솔이 cp949 이면 「—」 하나에 이 스크립트가 멈춥니다. 출력만 UTF-8 로 돌립니다.
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

PASS, FAIL = [], []


def check(name, ok, detail=''):
    (PASS if ok else FAIL).append((name, detail))
    print('%s  %-46s %s' % ('OK  ' if ok else 'FAIL', name, detail))


def read(path):
    with io.open(path, encoding='utf-8') as f:
        return f.read()


def count_rows(grid):
    path = os.path.join(DESIGN, 'data', grid + '.tsv')
    return sum(1 for line in read(path).splitlines() if line.startswith('\t'))


# ---------------------------------------------------------------------------
# 데이터 자체
# ---------------------------------------------------------------------------

def grid_rows(grid):
    """격자의 데이터 행을 컬럼 목록으로. 선언 3줄은 빼고 돌려줍니다."""
    path = os.path.join(DESIGN, 'data', grid + '.tsv')
    lines = read(path).splitlines()
    fields = lines[1].split('\t')[1:]
    out = []
    for line in lines[4:]:
        if not line.startswith('\t'):
            continue
        cells = line.split('\t')[1:]
        out.append(dict(zip(fields, cells)))
    return out


def joker_checks():
    """조커 500종의 배분과 확장의 판정 기준. 규격은 `doc/expansion.md` 입니다."""
    sys.path.insert(0, HERE)
    from seedlib import expansion

    rows = grid_rows('Joker')
    check('조커 500종', len(rows) == 500, '%d종' % len(rows))

    pools = {}
    for row in rows:
        pools[row['pool']] = pools.get(row['pool'], 0) + 1
    check('풀이 기본 150 · 확장 350',
          pools.get('Base') == 150 and pools.get('Greenhouse') == 350,
          '기본 %s · 확장 %s' % (pools.get('Base'), pools.get('Greenhouse')))

    want = {'Common': 181, 'Uncommon': 214, 'Rare': 85, 'Legendary': 20}
    got = {}
    for row in rows:
        got[row['rarity']] = got.get(row['rarity'], 0) + 1
    check('희귀도가 181 · 214 · 85 · 20', got == want,
          ' · '.join('%s %d' % (r, got.get(r, 0))
                     for r in ('Common', 'Uncommon', 'Rare', 'Legendary')))

    sizes = sorted(set(len(entries) for _, entries in expansion.FAMILIES))
    check('계열 14개가 각 25종',
          len(expansion.FAMILIES) == 14 and sizes == [25],
          '계열 %d개 · 종수 %s' % (len(expansion.FAMILIES), sizes))

    ids = [row['joker_id'] for row in rows]
    check('식별자 중복 0', len(set(ids)) == len(ids),
          '%d개 중 %d개' % (len(ids), len(set(ids))))
    names = [row['name'] for row in rows]
    check('표시 이름 중복 0', len(set(names)) == len(names),
          '%d개 중 %d개' % (len(names), len(set(names))))

    effects = {}
    for row in grid_rows('JokerEffect'):
        effects.setdefault(row['owner'], []).append(row)

    # 확장 조커마다 효과가 한 행 이상. 효과 없는 조커는 상점에서 값이 없습니다.
    orphan = [row['joker_id'] for row in rows
              if row['pool'] == 'Greenhouse' and row['joker_id'] not in effects]
    check('확장 조커 전부에 효과 행이 있음', not orphan,
          '없는 것 %d종 %s' % (len(orphan), ', '.join(orphan[:5])))

    # 판정 기준 — 조건 없는 배수 가산 하나뿐인 것은 목록에 들어오지 않습니다.
    bare = []
    for jid in expansion.FAMILY_OF:
        mine = effects.get(jid, [])
        if (len(mine) == 1 and mine[0]['condition.$type'] == 'CondAlways'
                and mine[0]['operation.$type'] == 'OpAddMult'):
            bare.append(jid)
    check('판정 기준에 걸리는 확장 조커 0종', not bare,
          '%d종 %s' % (len(bare), ', '.join(bare[:5])))


def data_checks():
    grids = [f for f in os.listdir(os.path.join(DESIGN, 'data')) if f.endswith('.tsv')]
    check('격자 40개', len(grids) == 40, '%d개' % len(grids))

    plan = read(os.path.join(HERE, 'workbooks.tsv')).splitlines()
    mapped = [line for line in plan if line and not line.startswith('#')]
    check('workbooks.tsv 가 격자 전부를 배치', len(mapped) == len(grids),
          '%d개 배치' % len(mapped))

    joker_checks()

    check('보스 28종', count_rows('BossBlind') == 28)
    check('바우처 32종', count_rows('Voucher') == 32)
    check('태그 24종', count_rows('Tag') == 24)
    check('덱 15종', count_rows('Deck') == 15)
    check('소모품 52종',
          count_rows('Tarot') + count_rows('Planet') + count_rows('Spectral') == 52)

    effects = read(os.path.join(DESIGN, 'data', 'JokerEffect.tsv'))
    custom = effects.count('\tOpCustom\t')
    check('조커 효과의 `Custom` 이 1건', custom == 1, '%d건' % custom)


# ---------------------------------------------------------------------------
# 변환
# ---------------------------------------------------------------------------

def convert():
    # 캐시가 있으면 두 번째 실행이 일을 건너뛰고, 그러면 검증이 돌았는지 알 수 없습니다.
    cache = os.path.join(ROOT, '.tabbit')
    if os.path.isdir(cache):
        shutil.rmtree(cache, ignore_errors=True)

    recipe = 'samples/clover/design-data/recipe.jsonc'
    result = subprocess.run(
        ['dotnet', 'run', '--project', 'src/Tabbit.csproj', '--', '--recipe', recipe],
        cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')

    out = (result.stdout or '') + (result.stderr or '')
    check('변환이 끝까지 돕니다', result.returncode == 0,
          '' if result.returncode == 0 else '종료 코드 %d' % result.returncode)
    check('검증 규칙이 통과합니다', 'Validation: 0 error(s)' in out)
    return out


def output_checks():
    targets = [
        ('웹 TypeScript', 'web/src/generated/clover-data.ts'),
        ('웹 바이너리', 'web/public/data/Joker.tcb'),
        ('유니티 C#', 'unity/Assets/Clover/Generated/CloverData.cs'),
        ('유니티 바이너리', 'unity/Assets/StreamingAssets/tables/Joker.bytes'),
        ('사람이 읽는 문서', 'design-data/out/html/index.html'),
        ('인코딩 보고서', 'design-data/out/encoding-report.txt'),
        ('스키마 기준선', 'design-data/out/schema-baseline.json'),
    ]
    for name, rel in targets:
        check(name + ' 산출물', os.path.exists(os.path.join(SAMPLE, rel)), rel)

    tcb = [f for f in os.listdir(os.path.join(SAMPLE, 'web/public/data')) if f.endswith('.tcb')]
    bytes_ = [f for f in os.listdir(os.path.join(SAMPLE, 'unity/Assets/StreamingAssets/tables'))
              if f.endswith('.bytes')]
    check('두 플랫폼의 테이블 수가 같습니다', len(tcb) == len(bytes_),
          '%d개 / %d개' % (len(tcb), len(bytes_)))


# ---------------------------------------------------------------------------
# 우회
# ---------------------------------------------------------------------------

def workaround_checks():
    """`doc/tool-findings.md` 의 우회가 아직 있는가.

    **우회는 결함의 자리표입니다.** 지워지면 이 검사가 통과로 바뀌고 그 항목이 닫힙니다.
    """
    grid = read(os.path.join(HERE, 'seedlib', 'grid.py'))
    check('§1 우회 — 배열이 변종 밖에 있습니다',
          "SHARED_FIELDS = ['ranks', 'suits']" in grid)

    check('§5 우회 — 생성 코드가 자기 프로젝트에서 검사됩니다',
          os.path.exists(os.path.join(SAMPLE, 'web', 'src', 'generated', 'tsconfig.json')))
    check('§6 우회 — Node 로더가 따로 있습니다',
          os.path.exists(os.path.join(SAMPLE, 'web', 'src', 'core', 'load-node.ts')))

    consumables = read(os.path.join(HERE, 'seedlib', 'consumables.py'))
    check('§2 우회 — `Planet.hand` 가 `foreign` 이 아닙니다',
          "'PokerHandKind'" in consumables)

    effect = read(os.path.join(DESIGN, 'schemas', 'effect.tbs'))
    check('§3 우회 — 공유하는 `n` 에 제약이 없습니다',
          'field n       int\n' in effect or 'field n int\n' in effect)


def doc_checks():
    """문서가 선언을 따라오는가.

    변종을 하나 더하고 문서를 잊으면 두 구현이 문서만 보고 만들다가 갈라집니다.
    """
    effect = read(os.path.join(DESIGN, 'schemas', 'effect.tbs'))
    opcodes = read(os.path.join(SAMPLE, 'doc', 'effect-vm', 'opcodes.md'))

    declared = [line.split()[1] for line in effect.splitlines()
                if line.startswith('struct Cond') or line.startswith('struct Op')]
    missing = [name for name in declared
               if '`%s`' % name[4:] not in opcodes and '`%s`' % name[2:] not in opcodes]

    check('명령 목록이 선언 전부를 담습니다', not missing,
          '빠진 것: ' + ', '.join(missing[:5]) if missing else '%d개' % len(declared))

    conds = sum(1 for name in declared if name.startswith('Cond'))
    ops = len(declared) - conds
    check('조건 41종 · 연산 36종', conds == 41 and ops == 36, '%d · %d' % (conds, ops))


# ---------------------------------------------------------------------------
# 코어
# ---------------------------------------------------------------------------

WEB = os.path.join(SAMPLE, 'web')


def npm(*args):
    cmd = 'npm.cmd' if os.name == 'nt' else 'npm'
    return subprocess.run([cmd, *args], cwd=WEB, capture_output=True, text=True,
                          encoding='utf-8', errors='replace', shell=False)


def core_checks():
    if not os.path.isdir(os.path.join(WEB, 'node_modules')):
        check('웹 의존성이 설치되어 있습니다', False, 'npm install 을 먼저 돌리십시오')
        return

    built = npm('run', 'check')
    check('두 프로젝트가 타입 검사를 통과합니다', built.returncode == 0)

    tested = npm('test')
    out = (tested.stdout or '') + (tested.stderr or '')
    check('테스트가 통과합니다', tested.returncode == 0,
          next((line.strip() for line in out.splitlines() if 'Tests ' in line), ''))

    replays = [f for f in os.listdir(os.path.join(DESIGN, 'out', 'replay'))
               if f.endswith('.json')]
    check('구운 리플레이가 있습니다', len(replays) >= 10, '%d개' % len(replays))

    # 리플레이가 같은 해시를 다시 냅니다. **여기가 유니티와 대조할 자리입니다.**
    same = 0
    for name in sorted(replays):
        path = os.path.join(DESIGN, 'out', 'replay', name)
        replay = json.loads(read(path))
        result = subprocess.run(
            ['npx', 'tsx', 'src/headless.ts', '--replay', path],
            cwd=WEB, capture_output=True, text=True, encoding='utf-8',
            errors='replace', shell=(os.name == 'nt'))
        if result.returncode == 0 and replay['hashes'][-1] in (result.stdout or ''):
            same += 1

    check('리플레이가 같은 해시를 다시 냅니다', same == len(replays),
          '%d / %d' % (same, len(replays)))

    built = npm('run', 'build')
    check('웹 번들이 나옵니다', built.returncode == 0)

    shot = subprocess.run(
        ['npx', 'tsx', 'tools/shoot.ts'], cwd=WEB, capture_output=True, text=True,
        encoding='utf-8', errors='replace', shell=(os.name == 'nt'))
    out = (shot.stdout or '') + (shot.stderr or '')
    check('브라우저가 오류 없이 그립니다', shot.returncode == 0 and '오류 없음' in out)

    shots = [f for f in os.listdir(os.path.join(DESIGN, 'out', 'shot'))
             if f.endswith('.png')] if os.path.isdir(os.path.join(DESIGN, 'out', 'shot')) else []
    check('구운 화면이 있습니다', len(shots) >= 5, '%d장' % len(shots))


def main():
    print('데이터')
    data_checks()
    print('\n변환')
    convert()
    output_checks()
    print('\n코어')
    core_checks()
    print('\n문서')
    doc_checks()
    print('\n우회')
    workaround_checks()

    print('\n' + '-' * 60)
    print('통과 %d · 실패 %d' % (len(PASS), len(FAIL)))
    return 1 if FAIL else 0


if __name__ == '__main__':
    raise SystemExit(main())
