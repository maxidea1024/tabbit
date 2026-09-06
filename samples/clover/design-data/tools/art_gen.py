# -*- coding: utf-8 -*-
"""`art-prompts.tsv` 의 빠진 그림을 굽습니다.

`art.py` 는 프롬프트만 뽑습니다. 그림 하나를 부르는 것은 MCP 도구로도 되지만, **350장을
하나씩 부르는 것은 도구 호출 350번**입니다 — 그래서 같은 API 를 여기서 직접 부릅니다.

    python samples/clover/design-data/tools/art_gen.py            # 빠진 것 전부
    python samples/clover/design-data/tools/art_gen.py --limit 10 # 10장만
    python samples/clover/design-data/tools/art_gen.py --only ash_rose,dusk_iris
    python samples/clover/design-data/tools/art_gen.py --redo ash_rose

**이미 있는 파일은 건너뜁니다.** 중간에 끊겨도 다시 돌리면 이어서 굽고, 다시 구우려면
`--redo` 로 식별자를 적습니다.

자격증명은 `CF_ACCOUNT_ID` · `CF_API_TOKEN` 입니다. 프로세스 환경변수 → MCP 서버 폴더의
`.env` → 레지스트리 순으로 찾습니다 — MCP 서버와 같은 순서입니다.
"""

import base64
import csv
import io
import json
import os
import sys
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
# **크기는 `art.py` 가 정합니다.** 여기에 하나를 적어 두었더니 카드의 비율이 모든 갈래에
# 걸렸고, 정사각으로 뽑아야 하는 태그와 보스가 세로로 긴 채로 구워졌습니다 — 그것들은
# 동그라미로 오려 내어 쓰므로, 세로로 길면 화면에서 타원이 됩니다.
from art import size_for  # noqa: E402
DESIGN = os.path.dirname(HERE)
SAMPLE = os.path.dirname(DESIGN)

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

MODEL = '@cf/leonardo/lucid-origin'
#: 실패했을 때 다시 시도하는 횟수. 429 와 5xx 는 기다렸다 다시 부릅니다.
TRIES = 4
MCP_DIR = os.path.join(os.path.expanduser('~'), '.claude', 'mcp-servers', 'cf-image')


def credentials():
    """계정과 토큰. **값을 찍지 않습니다.**"""
    account = os.environ.get('CF_ACCOUNT_ID')
    token = os.environ.get('CF_API_TOKEN')

    env_path = os.path.join(MCP_DIR, '.env')
    if (not account or not token) and os.path.isfile(env_path):
        for line in io.open(env_path, encoding='utf-8'):
            line = line.strip()
            if not line or line.startswith('#') or '=' not in line:
                continue
            key, value = line.split('=', 1)
            value = value.strip().strip('"').strip("'")
            if key.strip() == 'CF_ACCOUNT_ID' and not account:
                account = value
            if key.strip() == 'CF_API_TOKEN' and not token:
                token = value

    if not account or not token:
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, 'Environment') as key:
                if not account:
                    account = winreg.QueryValueEx(key, 'CF_ACCOUNT_ID')[0]
                if not token:
                    token = winreg.QueryValueEx(key, 'CF_API_TOKEN')[0]
        except Exception:
            pass

    if not account or not token:
        raise SystemExit('CF_ACCOUNT_ID · CF_API_TOKEN 이 없습니다.')
    return account, token


def generate(account, token, prompt, size):
    """그림 하나의 바이트. 실패하면 예외입니다."""
    url = ('https://api.cloudflare.com/client/v4/accounts/%s/ai/run/%s'
           % (account, MODEL))
    payload = json.dumps({
        'prompt': prompt, 'width': size[0], 'height': size[1],
    }).encode('utf-8')
    request = urllib.request.Request(url, data=payload, method='POST')
    request.add_header('Authorization', 'Bearer %s' % token)
    request.add_header('Content-Type', 'application/json')

    with urllib.request.urlopen(request, timeout=180) as response:
        ctype = response.headers.get('Content-Type') or ''
        body = response.read()

    # 응답의 모양이 둘입니다 — base64 를 담은 JSON 이거나 이미지 바이트 그대로입니다.
    if 'application/json' in ctype:
        envelope = json.loads(body.decode('utf-8'))
        if not envelope.get('success', True):
            raise RuntimeError(str(envelope.get('errors'))[:300])
        result = envelope.get('result') or {}
        encoded = result.get('image') or (result.get('images') or [None])[0]
        if not encoded:
            raise RuntimeError('응답에 이미지가 없습니다: %s' % str(envelope)[:200])
        return base64.b64decode(encoded)
    if not body:
        raise RuntimeError('빈 이미지')
    return body


#: 카드의 둥근 모서리. **카드 폭에 대한 비율입니다** — `SIZE.cardRadius / cardWidth` 이고,
#: 비율로 두므로 손패에서도 덱 보기의 작은 카드에서도 같은 모양이 됩니다.
CARD_RADIUS = 0.102


#: 그림의 바탕. **흰색입니다.**
#:
#: 화면이 그림에 종이색을 곱합니다 — 강화를 그림 위에 덧그리면 얼굴이 가려지므로 색을
#: 입혀 알리기 때문입니다. 그래서 바탕이 이미 크림이면 크림 × 크림이 되어 누렇게 뜨고,
#: 흰색이어야 그 곱이 정확히 종이색이 됩니다. **정본 52장의 얼굴이 흰색인 것이 그 이유입니다.**
CARD_GROUND = (0xff, 0xff, 0xff)
#: 바탕으로 볼 색의 거리. 이보다 가까운 색을 흰색으로 바꿉니다.
GROUND_TOLERANCE = 30


def is_cream(color):
    """크림이나 흰색인가. **색이 도는 것은 바탕이 아니라 생성기가 그린 판입니다.**

    문턱은 실측입니다 — 따뜻한 크림(`#fceccc`)이 48이고 생성기가 그린 주황 판(`#f8b17e`)이
    122이므로 그 사이입니다.
    """
    spread = max(color) - min(color)
    light = (0.299 * color[0] + 0.587 * color[1] + 0.114 * color[2]) / 255.0
    return spread < 65 and light > 0.84


def ground_of(image):
    """테두리 안쪽 고리에서 가장 흔한 색. 바탕을 판정하는 데 씁니다."""
    rgba = image.convert('RGBA')
    w, h = rgba.size
    pixels = rgba.load()
    inset = max(4, int(round(w * 0.035)))
    counted = {}
    for x in range(inset, w - inset, 7):
        for one in (pixels[x, inset], pixels[x, h - 1 - inset]):
            if one[3] < 250:
                continue
            key = (one[0] // 8 * 8 + 4, one[1] // 8 * 8 + 4, one[2] // 8 * 8 + 4)
            counted[key] = counted.get(key, 0) + 1
    for y in range(inset, h - inset, 7):
        for one in (pixels[inset, y], pixels[w - 1 - inset, y]):
            if one[3] < 250:
                continue
            key = (one[0] // 8 * 8 + 4, one[1] // 8 * 8 + 4, one[2] // 8 * 8 + 4)
            counted[key] = counted.get(key, 0) + 1
    if not counted:
        return None
    return max(counted.items(), key=lambda kv: kv[1])[0]


def colored_grounds():
    """바탕에 색이 도는 그림을 셉니다. **그 장은 다시 구워야 합니다.**

        python art_gen.py --ground
    """
    from PIL import Image
    base = os.path.join(SAMPLE, 'web', 'public', 'art', 'card')
    bad = []
    for folder, _dirs, files in os.walk(base):
        for name in sorted(files):
            if not name.endswith('.webp'):
                continue
            path = os.path.join(folder, name)
            ground = ground_of(Image.open(path))
            if ground is None or is_cream(ground):
                continue
            bad.append((os.path.relpath(path, base), ground))
    for one, ground in bad:
        print('  %-34s #%02x%02x%02x' % (one, ground[0], ground[1], ground[2]))
    print('바탕에 색이 도는 것 %d장' % len(bad))
    return bad


def neutral_ground(image):
    """그림의 바탕을 흰색으로 바꿉니다.

    **이 게임에서 카드 바탕에 색이 들면 강화입니다.** `ENHANCEMENT_TINT` 가 그 색이고,
    따뜻한 크림 바탕은 `Gold` 강화와 같은 신호가 됩니다 — 겉모습을 고르는 것이 규칙의
    신호를 가져가면 안 됩니다.

    테두리 안쪽 고리에서 가장 흔한 색을 바탕으로 보고, 그 색에서 가까운 것을 전부 바꿉니다.
    **연결을 따라가지 않습니다** — 그림 안쪽의 같은 크림도 함께 바뀌어야 한 장이 한 색을
    씁니다.
    """
    from PIL import Image
    rgba = image.convert('RGBA')
    w, h = rgba.size
    pixels = rgba.load()

    # 테두리 바로 안쪽의 고리. 모서리는 둥글게 잘려 있을 수 있으므로 변의 가운데를 봅니다.
    inset = max(4, int(round(w * 0.035)))
    ring = []
    for x in range(inset, w - inset, 7):
        ring.append(pixels[x, inset])
        ring.append(pixels[x, h - 1 - inset])
    for y in range(inset, h - inset, 7):
        ring.append(pixels[inset, y])
        ring.append(pixels[w - 1 - inset, y])

    counted = {}
    for one in ring:
        if one[3] < 250:
            continue
        key = (one[0] // 8, one[1] // 8, one[2] // 8)
        counted[key] = counted.get(key, 0) + 1
    if not counted:
        return image

    # **바탕이 한 색이라고 보지 않습니다.** 생성기가 지시를 어겨 색 있는 판을 그리면 고리에
    # 그 색과 크림이 함께 잡히고, 가장 흔한 하나만 바꾸면 나머지가 그대로 남습니다 —
    # `baseball/club_jack` 이 실제로 주황 판으로 나왔습니다.
    #
    # **밝은 것만 바꿉니다.** 어두운 색이 고리에 잡히는 것은 인물이 변에 닿은 것이고,
    # 그것을 바꾸면 그림이 지워집니다.
    total = sum(counted.values())
    grounds = []
    for key, many in counted.items():
        if many * 100.0 / total < 12:
            continue
        one = (key[0] * 8 + 4, key[1] * 8 + 4, key[2] * 8 + 4)
        if not is_cream(one):
            # **색 있는 바탕은 지우지 않습니다.** 색 거리로 지우면 인물 안의 비슷한 색까지
            # 뚫려 누끼가 뜯깁니다 — 생성기가 지시를 어긴 한 장이므로 다시 굽는 것이 답이고,
            # `--ground` 가 그런 장을 셉니다.
            return image
        grounds.append(one)
    if not grounds:
        return image

    out = Image.new('RGBA', (w, h))
    write = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = pixels[x, y]
            hit = False
            for ground in grounds:
                if abs(r - ground[0]) + abs(g - ground[1])                         + abs(b - ground[2]) <= GROUND_TOLERANCE:
                    hit = True
                    break
            write[x, y] = CARD_GROUND + (a,) if hit else (r, g, b, a)
    return out


def round_card(image):
    """모서리를 둥글게 자릅니다. **테두리는 그리지 않습니다.**

    그림이 카드를 덮으므로 종이의 둥근 모서리가 그림에 가려지고, 그러면 네모난 종이 조각이
    둥근 카드 자리에 앉은 것으로 보입니다 — 그림이 자기 모서리를 가지고 있어야 합니다.

    테두리는 화면이 그림 위에 그립니다. **구워 넣었더니 어긋났습니다** — 자른 자리와 그린
    자리 사이에 바탕색 한 겹이 남아 종이의 2픽셀 선 밖에 흰 실선이 하나 더 보였고, 굽힌
    선은 디버프의 회색으로 바뀌지도 않습니다.
    """
    from PIL import Image, ImageDraw
    image = image.convert('RGBA')
    w, h = image.size
    radius = int(round(w * CARD_RADIUS))

    mask = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w - 1, h - 1), radius, fill=255)
    image.putalpha(mask)
    return image


def strip_edge(image):
    """구워 넣었던 테두리를 걷어냅니다.

    바깥 고리를 흰색으로 덮습니다. **거기는 어느 그림에서나 바탕입니다** — 인물이 가운데에
    서도록 프롬프트에 적어 두었습니다. 테두리가 없는 그림에는 아무것도 하지 않습니다.
    """
    from PIL import Image, ImageDraw
    rgba = image.convert('RGBA')
    w, h = rgba.size
    probe = rgba.getpixel((w // 2, max(2, int(w * 0.006))))
    if probe[3] < 250:
        return image
    if abs(probe[0] - CARD_GROUND[0]) + abs(probe[1] - CARD_GROUND[1])             + abs(probe[2] - CARD_GROUND[2]) < 60:
        return image

    ring = int(round(w * 0.030))
    radius = int(round(w * CARD_RADIUS))
    over = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(over)
    draw.rounded_rectangle((0, 0, w - 1, h - 1), radius, fill=CARD_GROUND + (255,))
    draw.rounded_rectangle((ring, ring, w - 1 - ring, h - 1 - ring),
                           max(1, radius - ring), fill=(0, 0, 0, 0))
    rgba.alpha_composite(over)
    return rgba


def is_card_set(path):
    """카드 세트의 그림인가. `art/card/<세트>/` 아래의 것입니다."""
    return os.sep + 'card' + os.sep in path


def save(raw, path, size):
    """webp 로 굽습니다. `art.py` 의 `target()` 이 정한 확장자입니다."""
    from PIL import Image
    image = Image.open(io.BytesIO(raw)).convert('RGB')
    if image.size != size:
        image = image.resize(size, Image.LANCZOS)
    if is_card_set(path):
        image = round_card(neutral_ground(image))
    folder = os.path.dirname(path)
    if not os.path.isdir(folder):
        os.makedirs(folder)
    image.save(path, 'WEBP', quality=88, method=6, lossless=False, exact=True)
    return os.path.getsize(path)


def round_existing():
    """이미 구워 둔 카드 세트의 그림을 손봅니다 — 바탕색과 모서리입니다.

    **여러 번 돌려도 같습니다.** 모서리는 이미 난 것을 다시 내지 않고, 바탕색은 이미 종이색인
    것을 다시 바꿔도 같은 값입니다 — 그림을 다시 굽는 데는 돈과 시간이 들고, 다시 구우면
    다른 그림이 나옵니다.

        python art_gen.py --round
    """
    from PIL import Image
    base = os.path.join(SAMPLE, 'web', 'public', 'art', 'card')
    done = 0
    for folder, _dirs, files in os.walk(base):
        for name in sorted(files):
            if not name.endswith('.webp'):
                continue
            path = os.path.join(folder, name)
            image = Image.open(path)
            image = round_card(strip_edge(neutral_ground(image)))
            image.save(path, 'WEBP', quality=88, method=6, exact=True)
            done += 1
            print('  %s' % os.path.relpath(path, base))
    print('모서리를 낸 것 %d장' % done)


def target(kind, identifier):
    return os.path.join(SAMPLE, 'web', 'public', 'art', kind, identifier + '.webp')


def rows():
    path = os.path.join(DESIGN, 'out', 'art-prompts.tsv')
    with io.open(path, encoding='utf-8') as handle:
        return list(csv.DictReader(handle, delimiter='\t'))


def arg(name):
    if name in sys.argv:
        at = sys.argv.index(name)
        if at + 1 < len(sys.argv):
            return sys.argv[at + 1]
    return None


def main():
    if '--ground' in sys.argv:
        colored_grounds()
        return 0

    if '--round' in sys.argv:
        round_existing()
        return 0

    limit = int(arg('--limit') or 0)
    only = set((arg('--only') or '').split(',')) - {''}
    redo = set((arg('--redo') or '').split(',')) - {''}

    todo = []
    for row in rows():
        if only and row['id'] not in only:
            continue
        path = target(row['kind'], row['id'])
        if os.path.exists(path) and row['id'] not in redo:
            continue
        todo.append((row['kind'], row['id'], row['prompt'], path))
    if limit:
        todo = todo[:limit]

    print('구울 것 %d장 · %s' % (len(todo), MODEL))
    if not todo:
        return 0

    account, token = credentials()
    done, failed, started = 0, [], time.time()

    for index, (kind, identifier, prompt, path) in enumerate(todo, 1):
        for attempt in range(1, TRIES + 1):
            try:
                want = size_for(kind)
                size = save(generate(account, token, prompt, want), path, want)
                done += 1
                spent = time.time() - started
                left = (spent / done) * (len(todo) - done)
                print('%4d/%d  %-24s %6.1fKB   남은 시간 %d분'
                      % (index, len(todo), identifier, size / 1024, left / 60))
                break
            except urllib.error.HTTPError as error:
                # 429 와 5xx 는 기다렸다 다시. 나머지는 프롬프트나 자격증명의 문제입니다.
                retryable = error.code == 429 or error.code >= 500
                detail = '%s %s' % (error.code, error.reason)
                if not retryable or attempt == TRIES:
                    failed.append((identifier, detail))
                    print('%4d/%d  %-24s 실패 — %s' % (index, len(todo), identifier, detail))
                    break
                time.sleep(min(60, 4 * attempt * attempt))
            except Exception as error:  # noqa: BLE001
                detail = str(error)[:120]
                if attempt == TRIES:
                    failed.append((identifier, detail))
                    print('%4d/%d  %-24s 실패 — %s' % (index, len(todo), identifier, detail))
                    break
                time.sleep(min(30, 3 * attempt))

    print('-' * 34)
    print('구운 것 %d장 · 실패 %d장 · %d분' % (done, len(failed), (time.time() - started) / 60))
    for identifier, detail in failed[:20]:
        print('  실패  %-24s %s' % (identifier, detail))
    return 1 if failed else 0


if __name__ == '__main__':
    raise SystemExit(main())
