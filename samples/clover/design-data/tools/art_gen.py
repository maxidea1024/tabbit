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
DESIGN = os.path.dirname(HERE)
SAMPLE = os.path.dirname(DESIGN)

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

MODEL = '@cf/leonardo/lucid-origin'
# `art.py` 의 `SIZE` 와 같아야 합니다. 카드의 비율입니다.
WIDTH, HEIGHT = 640, 960
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


def generate(account, token, prompt):
    """그림 하나의 바이트. 실패하면 예외입니다."""
    url = ('https://api.cloudflare.com/client/v4/accounts/%s/ai/run/%s'
           % (account, MODEL))
    payload = json.dumps({
        'prompt': prompt, 'width': WIDTH, 'height': HEIGHT,
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


def save(raw, path):
    """webp 로 굽습니다. `art.py` 의 `target()` 이 정한 확장자입니다."""
    from PIL import Image
    image = Image.open(io.BytesIO(raw)).convert('RGB')
    if image.size != (WIDTH, HEIGHT):
        image = image.resize((WIDTH, HEIGHT), Image.LANCZOS)
    folder = os.path.dirname(path)
    if not os.path.isdir(folder):
        os.makedirs(folder)
    image.save(path, 'WEBP', quality=88, method=6)
    return os.path.getsize(path)


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

    print('구울 것 %d장 · %s · %d × %d' % (len(todo), MODEL, WIDTH, HEIGHT))
    if not todo:
        return 0

    account, token = credentials()
    done, failed, started = 0, [], time.time()

    for index, (kind, identifier, prompt, path) in enumerate(todo, 1):
        for attempt in range(1, TRIES + 1):
            try:
                size = save(generate(account, token, prompt), path)
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
