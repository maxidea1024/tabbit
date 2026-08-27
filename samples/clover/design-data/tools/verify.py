# -*- coding: utf-8 -*-
"""되는지가 아니라 맞는지를 봅니다.

변환이 성공으로 끝나도 확인되지 않는 것이 있습니다 — 격자와 산출물의 개수가 맞는지,
`doc/tool-findings.md` 의 우회가 아직 남아 있는지는 변환의 어느 검사에도 걸리지 않습니다.

    python samples/clover/design-data/tools/verify.py
"""

import io
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
SAMPLE = os.path.dirname(DESIGN)
ROOT = os.path.dirname(os.path.dirname(SAMPLE))

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

def data_checks():
    grids = [f for f in os.listdir(os.path.join(DESIGN, 'data')) if f.endswith('.tsv')]
    check('격자 40개', len(grids) == 40, '%d개' % len(grids))

    plan = read(os.path.join(HERE, 'workbooks.tsv')).splitlines()
    mapped = [line for line in plan if line and not line.startswith('#')]
    check('workbooks.tsv 가 격자 전부를 배치', len(mapped) == len(grids),
          '%d개 배치' % len(mapped))

    check('조커 150종', count_rows('Joker') == 150, '%d종' % count_rows('Joker'))
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


def main():
    print('데이터')
    data_checks()
    print('\n변환')
    convert()
    output_checks()
    print('\n문서')
    doc_checks()
    print('\n우회')
    workaround_checks()

    print('\n' + '-' * 60)
    print('통과 %d · 실패 %d' % (len(PASS), len(FAIL)))
    return 1 if FAIL else 0


if __name__ == '__main__':
    raise SystemExit(main())
