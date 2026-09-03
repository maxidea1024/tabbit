# -*- coding: utf-8 -*-
"""배경음을 가져와 이음매 없는 한 바퀴로 자릅니다.

**곡을 통째로 담으면 한 바퀴마다 조용해집니다.** 발표된 곡에는 앞뒤에 무음이 있고 끝이
페이드아웃이라, `loop = true` 로 돌리면 그 자리가 「화면이 바뀌었다」보다 크게 들립니다.

그래서 곡의 가운데에서 **마디 수가 정수인 구간** 하나를 떼고, 그 구간 다음에 실제로 이어지던
소리를 구간의 머리에 겹칩니다. 머리로 돌아온 자리에 진짜 이어지던 소리가 잦아들며 깔리므로
끊긴 자리가 생기지 않습니다.

    python samples/clover/design-data/tools/music.py --fetch   # 받고 자릅니다
    python samples/clover/design-data/tools/music.py --bake    # 받아 둔 것으로 다시 자릅니다
    python samples/clover/design-data/tools/music.py           # 지금 놓인 것을 재어 봅니다

받은 것은 `web/public/music/<화면>.ogg` 로 놓입니다. **파일 이름이 곧 그 화면입니다** —
효과음이 신호의 이름을 쓰는 것과 같습니다.

`numpy` 와 `soundfile` 이 필요합니다. 저장소의 다른 도구와 달리 의존성이 있고, 그것은 마디를
찾고 vorbis 로 굽는 일을 손으로 할 수 없기 때문입니다. 자른 결과는 저장소에 담기므로 이
도구를 돌리지 않아도 게임은 돕니다.
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
SAMPLE = os.path.dirname(DESIGN)
OUT = os.path.join(SAMPLE, 'web', 'public', 'music')
CACHE = os.path.join(DESIGN, 'out', 'music-source')

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ARTIST = 'holiznacc0'
ALBUM = 'waves-of-nostalgia-2'

# 화면 하나에 곡 하나.
#
# **밝기로 갈라 두었습니다.** 타이틀은 저역이 두툼하고 고역이 낮은 야경, 판은 기복이 가장
# 작아 효과음이 그 위에 올라서는 것, 상점은 고역이 가장 밝아 넘어간 것이 곧바로 들리는 것.
TRACKS = {
    'title': 'night-life',
    'round': 'cyber-anxiety',
    'shop': 'machines-with-feelings',
}

#: 한 바퀴의 목표 길이. 짧으면 같은 구절이 자주 돌아오고, 길면 파일이 커집니다.
WANT_SECONDS = 78.0
#: 겹치는 길이를 마디로 셉니다.
CROSS_BARS = 0.5
#: 한 마디의 박 수. 이 앨범은 전부 4박입니다.
BEATS_PER_BAR = 4
#: 자른 뒤 봉우리가 멈추는 자리.
#:
#: **1.0 이 아니라 0.85 인 이유가 있습니다.** vorbis 는 되풀 때 원래보다 살짝 넘겨 나오고,
#: 브라우저에서 재면 0.6dB 위입니다 — 1.0 에 붙여 구우면 그만큼 깎입니다. 원본 셋도 그렇게
#: 1.04 로 깎여 있었습니다.
PEAK = 0.85
#: 자른 뒤 맞추는 체감 음량(dBFS).
#:
#: **봉우리만 맞추면 화면마다 크기가 다릅니다.** 봉우리는 드럼 한 대가 정하고 사람이 듣는
#: 크기는 그 아래의 평균이 정하므로, 봉우리를 같게 맞춘 셋이 3dB 씩 벌어집니다. 그래서
#: 평균을 맞추고 봉우리는 무르게 누릅니다 — 효과음을 `audio.ts` 에서 맞추는 것과 같은
#: 방식이고, 값은 효과음의 `TARGET_RMS` 0.09 보다 낮게 두어 배경이 자리를 비켜 줍니다.
TARGET_RMS = -15.5
#: 이 위로만 누릅니다. 여기 아래는 손대지 않습니다.
KNEE = 0.55
#: vorbis 압축. 0 이 가장 좋고 1 이 가장 작습니다.
VORBIS = 0.55


def need():
    try:
        import numpy
        import soundfile
        return numpy, soundfile
    except ImportError:
        print('numpy 와 soundfile 이 필요합니다: python -m pip install numpy soundfile')
        raise SystemExit(1)


def fetch():
    """앨범 쪽에서 곡의 주소를 찾아 받습니다. 이미 있으면 그대로 씁니다."""
    import urllib.request

    if not os.path.isdir(CACHE):
        os.makedirs(CACHE)
    want = [slug for slug in TRACKS.values()
            if not os.path.exists(os.path.join(CACHE, slug + '.mp3'))]
    for slug in TRACKS.values():
        if slug not in want:
            print('있음 %s.mp3' % slug)
    if not want:
        return

    # **파일 이름이 해시라 여기 적어 두면 언젠가 어긋납니다.** 앨범 쪽에서 그때그때 찾습니다.
    url = 'https://freemusicarchive.org/music/%s/%s/' % (ARTIST, ALBUM)
    print('조회 %s' % url)
    request = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    page = urllib.request.urlopen(request, timeout=90).read().decode('utf-8', 'replace')
    page = page.replace('\\/', '/')

    order = list(dict.fromkeys(
        re.findall(r'music/%s/%s/([a-z0-9-]+)' % (ARTIST, ALBUM), page)))
    files = list(dict.fromkeys(
        re.findall(r'files\.freemusicarchive\.org[^"&]*\.mp3', page)))
    found = dict(zip(order, files))

    for slug in want:
        if slug not in found:
            print('앨범 쪽에서 찾지 못했습니다: %s' % slug)
            raise SystemExit(1)
        print('받는 중 %s' % slug)
        request = urllib.request.Request('https://' + found[slug],
                                         headers={'User-Agent': 'Mozilla/5.0'})
        data = urllib.request.urlopen(request, timeout=180).read()
        with open(os.path.join(CACHE, slug + '.mp3'), 'wb') as handle:
            handle.write(data)


#: 마디를 찾을 때 쓰는 표본율과 창. **원본 그대로 재면 몇 GB가 됩니다** — 3분짜리를
#: 44.1kHz 로 20ms 마다 자르면 창이 겹쳐 표본이 스무 배로 늘어납니다. 박은 0.5초 안팎이므로
#: 11kHz 로 23ms 마다면 박 하나에 스물여섯 줄이고, 그것으로 충분합니다.
LOOK_RATE = 11025
LOOK_HOP = 256
LOOK_WIN = 1024


def bands(np, wave, sr):
    """23ms 마다 스펙트럼 한 줄. 마디를 찾는 것도 이음매를 재는 것도 이 위에서 합니다."""
    mono = wave.mean(axis=1)
    step = max(1, int(round(sr / LOOK_RATE)))
    mono = np.ascontiguousarray(mono[::step], dtype='float32')

    count = (len(mono) - LOOK_WIN) // LOOK_HOP
    index = (np.arange(LOOK_WIN, dtype='int64')[None, :]
             + LOOK_HOP * np.arange(count, dtype='int64')[:, None])
    frames = mono[index] * np.hanning(LOOK_WIN).astype('float32')[None, :]
    spectra = np.abs(np.fft.rfft(frames, axis=1)).astype('float32')
    return spectra, LOOK_HOP / (sr / step)


def grid(np, spectra, hop):
    """박과 마디의 길이, 그리고 마디의 첫 박이 어디인지."""
    flux = np.diff(spectra, axis=0).clip(min=0).sum(axis=1)
    flux = flux - flux.mean()

    ac = np.correlate(flux, flux, 'full')[len(flux) - 1:]
    lo, hi = int(0.30 / hop), int(1.00 / hop)      # 60~200 BPM
    beat = lo + int(np.argmax(ac[lo:hi]))
    bar = beat * BEATS_PER_BAR

    # 마디의 첫 박. 마디 간격으로 세운 빗으로 훑어 온셋이 가장 많이 걸리는 자리입니다.
    reach = (len(flux) - bar) // bar
    scores = [flux[phase::bar][:reach].sum() for phase in range(bar)]
    return beat, bar, int(np.argmax(scores))


def loop(np, spectra, hop, bar, phase, sr):
    """마디 수가 정수인 구간 가운데, 머리와 뒤가 가장 닮은 것을 고릅니다."""
    total = len(spectra)

    # 도입은 건너뜁니다. 소리가 다 차기 전에서 시작하면 한 바퀴마다 거기서 얇아집니다.
    level = np.sqrt((spectra ** 2).mean(axis=1))
    filled = int(np.argmax(level > np.percentile(level, 80) * 0.5))
    # 끝의 페이드아웃도 뺍니다.
    ending = total - int(np.argmax(level[::-1] > np.percentile(level, 80) * 0.5))

    cross = int(round(CROSS_BARS * bar))
    want = int(round(WANT_SECONDS / hop))
    # 4마디 단위로만 봅니다 — 구절이 4마디나 8마디이므로 그 배수라야 머리가 뒤와 맞습니다.
    step = bar * 4
    lengths = [n for n in range(step, total, step)
               if WANT_SECONDS * 0.7 < n * hop < WANT_SECONDS * 1.35]
    if not lengths:
        lengths = [max(step, want // step * step)]

    # 스펙트럼을 몇 개의 띠로 접습니다. 정밀한 파형이 아니라 「비슷하게 들리는가」를 봅니다.
    width = spectra.shape[1]
    edges = np.unique(np.geomspace(1, width, 25).astype(int))
    folded = np.stack([spectra[:, a:b].sum(axis=1)
                       for a, b in zip(edges[:-1], edges[1:])], axis=1)
    folded = np.log1p(folded)
    loud = 20 * np.log10(level + 1e-9)

    best = None
    starts = range(filled + (phase - filled) % bar, ending, bar)
    for start in starts:
        for length in lengths:
            after = start + length
            if after + cross > ending:
                continue
            gap = float(np.abs(folded[start:start + cross]
                               - folded[after:after + cross]).mean())
            # **음량도 맞아야 합니다.** 띠의 생김새가 닮아도 한쪽이 몇 dB 작으면, 한 바퀴
            # 돌 때마다 거기서 소리가 한 번 커집니다.
            gap += abs(loud[start:start + cross].mean()
                       - loud[after:after + cross].mean()) * 0.08
            # 같은 길이끼리 견주도록, 목표에서 멀어진 만큼만 살짝 얹습니다.
            gap += abs(length * hop - WANT_SECONDS) / WANT_SECONDS * 0.05
            if best is None or gap < best[0]:
                best = (gap, start, length)

    gap, start, length = best
    return gap, int(start * hop * sr), int(length * hop * sr), int(cross * hop * sr)


def fit(np, body):
    """체감 음량을 맞추고, 그러다 1을 넘긴 봉우리만 무르게 눌러 내립니다."""
    mono = body.mean(axis=1)
    span = max(1, len(mono) // 400)
    blocks = mono[:len(mono) // span * span].reshape(-1, span)
    loud = np.sqrt((blocks ** 2).mean(axis=1))
    # 조용한 구간은 빼고 잽니다. 무음이 섞이면 평균이 실제보다 작게 나옵니다.
    live = loud[loud > loud.max() * 0.1]
    now = 20 * np.log10(float(live.mean()) + 1e-9)

    lift = TARGET_RMS - now
    body = body * (10 ** (lift / 20))

    # 무릎 위만 눌립니다. `tanh` 라 꺾이는 자리가 없고, `PEAK` 를 넘지 않습니다.
    #
    # **누른 뒤에 봉우리로 다시 맞추지 않습니다.** 그렇게 하면 방금 맞춘 체감 음량이
    # 되돌아갑니다 — 누르는 폭이 곡마다 다르므로 되돌아가는 폭도 곡마다 달라서, 셋이 다시
    # 벌어집니다. 그래서 누르는 식 자체가 `PEAK` 에서 멈추도록 두었습니다.
    over = np.abs(body) > KNEE
    squash = float(np.abs(body).max())
    if over.any():
        rest = PEAK - KNEE
        body[over] = np.sign(body[over]) * (
            KNEE + rest * np.tanh((np.abs(body[over]) - KNEE) / rest))
    return body.astype('float32'), lift, 20 * np.log10(max(squash, 1e-9) / PEAK)


def bake(np, sf, slug, screen):
    path = os.path.join(CACHE, slug + '.mp3')
    if not os.path.exists(path):
        print('원본이 없습니다: %s — --fetch 를 먼저' % path)
        return None

    wave, sr = sf.read(path, always_2d=True, dtype='float32')
    spectra, hop = bands(np, wave, sr)
    beat, bar, phase = grid(np, spectra, hop)
    gap, start, length, cross = loop(np, spectra, hop, bar, phase, sr)

    body = wave[start:start + length].copy()
    tail = wave[start + length:start + length + cross]

    # **겹치는 것은 그 구간 다음에 실제로 이어지던 소리입니다.** 머리로 돌아온 자리에 그것이
    # 잦아들며 깔리므로, 돌아온 것이 아니라 이어진 것으로 들립니다.
    ramp = np.linspace(0.0, np.pi / 2, cross, dtype='float32')[:, None]
    body[:cross] = body[:cross] * np.sin(ramp) + tail * np.cos(ramp)

    body -= body.mean(axis=0)
    raw = float(np.abs(body).max())
    body, lift, squash = fit(np, body)

    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    target = os.path.join(OUT, screen + '.ogg')
    # **한 번에 다 넘기면 vorbis 인코더가 스택을 넘깁니다.** 1분이 넘는 것을 통째로 주면
    # libsndfile 이 그 자리에서 죽으므로, 나누어 넣습니다.
    kwargs = dict(mode='w', samplerate=sr, channels=body.shape[1],
                  format='OGG', subtype='VORBIS')
    try:
        handle = sf.SoundFile(target, compression_level=VORBIS, **kwargs)
    except TypeError:
        handle = sf.SoundFile(target, **kwargs)
    with handle:
        for at in range(0, len(body), sr):
            handle.write(body[at:at + sr])

    return dict(screen=screen, slug=slug, bpm=60.0 / (beat * hop),
                bars=(length / sr) / (bar * hop), seconds=length / sr, gap=gap,
                size=os.path.getsize(target), at=start / sr, cross=cross / sr,
                raw=raw, lift=lift, squash=squash)


def measure(np, sf):
    """지금 놓인 것을 재어 봅니다.

    **앞뒤 무음이 0이어야 하고, 이음매의 층이 그 곡 자신의 기복 안에 들어야 합니다.**
    한 바퀴 도는 자리에서 음량이 3dB 바뀌었더라도 그 곡이 원래 마디마다 5dB 씩 오르내리는
    것이면 거기만 들리지 않습니다 — 그래서 층을 그 곡의 90퍼센타일과 나란히 적습니다.

    파형의 층은 따로 봅니다. 마지막 표본과 첫 표본이 튀면 한 바퀴마다 「딱」 소리가 납니다.
    """
    print('%-8s %8s %7s %8s %8s %9s %8s'
          % ('화면', '길이', '봉우리', '앞 무음', '뒤 무음', '이음매 층', '그 곡'))
    for screen in TRACKS:
        path = os.path.join(OUT, screen + '.ogg')
        if not os.path.exists(path):
            print('%-8s 없음' % screen)
            continue
        wave, sr = sf.read(path, always_2d=True, dtype='float32')
        mono = wave.mean(axis=1)
        peak = float(np.abs(mono).max())
        quiet = peak * 0.005
        lead = int(np.argmax(np.abs(mono) > quiet)) / sr
        trail = int(np.argmax(np.abs(mono[::-1]) > quiet)) / sr

        span = int(sr * 0.2)
        blocks = mono[:len(mono) // span * span].reshape(-1, span)
        loud = 20 * np.log10(np.sqrt((blocks ** 2).mean(axis=1)) + 1e-9)
        seam = abs(float(loud[0] - loud[-1]))
        usual = float(np.percentile(np.abs(np.diff(loud)), 90))
        print('%-8s %7.1fs %7.3f %7.0fms %7.0fms %8.1fdB %7.1fdB'
              % (screen, len(mono) / sr, peak, lead * 1000, trail * 1000, seam, usual))


def main():
    np, sf = need()

    if '--fetch' in sys.argv:
        fetch()
    if '--fetch' in sys.argv or '--bake' in sys.argv:
        total = 0
        for screen, slug in TRACKS.items():
            report = bake(np, sf, slug, screen)
            if report is None:
                return 1
            total += report['size']
            print('%-8s %-24s %5.1f BPM  %5.1fs  %6.1f초부터  이음매 %.3f  '
                  '%+.1fdB 올리고 %.1fdB 누름  %.0fKB'
                  % (screen, slug, report['bpm'], report['seconds'],
                     report['at'], report['gap'], report['lift'],
                     report['squash'], report['size'] / 1024))
        print('%s 에 %d개 · %.0fKB' % (OUT, len(TRACKS), total / 1024))

    measure(np, sf)
    return 0


if __name__ == '__main__':
    sys.exit(main())
