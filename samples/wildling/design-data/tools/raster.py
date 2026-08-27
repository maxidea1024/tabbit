# -*- coding: utf-8 -*-
"""의존성 없이 PNG 를 쓰는 작은 래스터라이저이다.

`art.py` 가 쓴다. 밖에서 가져오는 것은 표준 라이브러리의 `zlib` 뿐이고, 그래서 이 저장소를
클론한 사람이 무엇을 설치하지 않고도 그림을 다시 만들 수 있다.

**3배로 그리고 줄인다.** 도형마다 경계를 계산하는 대신 3배 해상도에서 하드 엣지로 그린 뒤
상자 평균으로 줄이면 계단이 눈에 띄지 않는다. 128 픽셀 아이콘 하나가 384 픽셀로 그려진다.

좌표는 전부 최종 크기 기준이다 — 3배는 이 파일 안에서만 쓰인다.
"""
import io
import math
import struct
import zlib

SS = 3  # 초과 표본 배수


class Canvas(object):
    """RGBA 캔버스이다. 좌표는 최종 크기 기준이고 내부는 `SS` 배로 갖는다."""

    def __init__(self, width, height):
        self.width = width
        self.height = height
        self.w = width * SS
        self.h = height * SS
        self.buf = bytearray(self.w * self.h * 4)

    # ------------------------------------------------------------ 합성

    def _blend(self, x, y, r, g, b, a):
        if a <= 0.0 or x < 0 or y < 0 or x >= self.w or y >= self.h:
            return
        i = (y * self.w + x) * 4
        buf = self.buf
        if a >= 1.0:
            buf[i] = r
            buf[i + 1] = g
            buf[i + 2] = b
            buf[i + 3] = 255
            return
        da = buf[i + 3] / 255.0
        out = a + da * (1.0 - a)
        if out <= 0.0:
            return
        buf[i] = int((r * a + buf[i] * da * (1.0 - a)) / out + 0.5)
        buf[i + 1] = int((g * a + buf[i + 1] * da * (1.0 - a)) / out + 0.5)
        buf[i + 2] = int((b * a + buf[i + 2] * da * (1.0 - a)) / out + 0.5)
        buf[i + 3] = int(out * 255.0 + 0.5)

    def _span(self, y, x0, x1, color, alpha):
        r, g, b = color
        for x in range(max(0, x0), min(self.w, x1 + 1)):
            self._blend(x, y, r, g, b, alpha)

    # ------------------------------------------------------------ 도형

    def clear(self, color=None, alpha=0.0):
        if color is None or alpha <= 0.0:
            self.buf = bytearray(self.w * self.h * 4)
            return
        r, g, b = color
        a = int(alpha * 255.0 + 0.5)
        self.buf = bytearray([r, g, b, a] * (self.w * self.h))

    def rect(self, x0, y0, x1, y1, color, alpha=1.0, radius=0.0):
        """모서리를 둥글게 깎은 사각형이다."""
        X0, Y0, X1, Y1 = x0 * SS, y0 * SS, x1 * SS, y1 * SS
        rad = radius * SS
        for py in range(int(math.floor(Y0)), int(math.ceil(Y1))):
            cy = py + 0.5
            if cy < Y0 or cy > Y1:
                continue
            inset = 0.0
            if rad > 0.0:
                dy = 0.0
                if cy < Y0 + rad:
                    dy = Y0 + rad - cy
                elif cy > Y1 - rad:
                    dy = cy - (Y1 - rad)
                if dy > 0.0:
                    if dy >= rad:
                        continue
                    inset = rad - math.sqrt(max(0.0, rad * rad - dy * dy))
            self._span(py, int(X0 + inset + 0.5), int(X1 - inset - 0.5), color, alpha)

    def gradient_rect(self, x0, y0, x1, y1, top, bottom, radius=0.0, alpha=1.0):
        """위에서 아래로 색이 변하는 둥근 사각형이다."""
        X0, Y0, X1, Y1 = x0 * SS, y0 * SS, x1 * SS, y1 * SS
        rad = radius * SS
        height = max(1.0, Y1 - Y0)
        for py in range(int(math.floor(Y0)), int(math.ceil(Y1))):
            cy = py + 0.5
            if cy < Y0 or cy > Y1:
                continue
            t = (cy - Y0) / height
            color = mix(top, bottom, t)
            inset = 0.0
            if rad > 0.0:
                dy = 0.0
                if cy < Y0 + rad:
                    dy = Y0 + rad - cy
                elif cy > Y1 - rad:
                    dy = cy - (Y1 - rad)
                if dy > 0.0:
                    if dy >= rad:
                        continue
                    inset = rad - math.sqrt(max(0.0, rad * rad - dy * dy))
            self._span(py, int(X0 + inset + 0.5), int(X1 - inset - 0.5), color, alpha)

    def ellipse(self, cx, cy, rx, ry, color, alpha=1.0, rot=0.0):
        """타원이다. `rot` 는 라디안이다."""
        CX, CY, RX, RY = cx * SS, cy * SS, rx * SS, ry * SS
        if RX <= 0.0 or RY <= 0.0:
            return
        cos, sin = math.cos(rot), math.sin(rot)
        reach = math.sqrt((RX * cos) ** 2 + (RY * sin) ** 2), \
                math.sqrt((RX * sin) ** 2 + (RY * cos) ** 2)
        r, g, b = color
        for py in range(int(CY - reach[1] - 1), int(CY + reach[1] + 2)):
            dy = py + 0.5 - CY
            for px in range(int(CX - reach[0] - 1), int(CX + reach[0] + 2)):
                dx = px + 0.5 - CX
                u = (dx * cos + dy * sin) / RX
                v = (-dx * sin + dy * cos) / RY
                if u * u + v * v <= 1.0:
                    self._blend(px, py, r, g, b, alpha)

    def ring(self, cx, cy, radius, width, color, alpha=1.0):
        """테두리 원이다."""
        CX, CY = cx * SS, cy * SS
        outer = (radius + width * 0.5) * SS
        inner = (radius - width * 0.5) * SS
        r, g, b = color
        for py in range(int(CY - outer - 1), int(CY + outer + 2)):
            dy = py + 0.5 - CY
            for px in range(int(CX - outer - 1), int(CX + outer + 2)):
                dx = px + 0.5 - CX
                d2 = dx * dx + dy * dy
                if inner * inner <= d2 <= outer * outer:
                    self._blend(px, py, r, g, b, alpha)

    def poly(self, points, color, alpha=1.0):
        """볼록·오목 다각형이다. 짝수-홀수 규칙으로 채운다."""
        pts = [(x * SS, y * SS) for x, y in points]
        if len(pts) < 3:
            return
        top = int(math.floor(min(p[1] for p in pts)))
        bottom = int(math.ceil(max(p[1] for p in pts)))
        n = len(pts)
        for py in range(top, bottom + 1):
            cy = py + 0.5
            xs = []
            for i in range(n):
                x0, y0 = pts[i]
                x1, y1 = pts[(i + 1) % n]
                if (y0 <= cy < y1) or (y1 <= cy < y0):
                    xs.append(x0 + (cy - y0) * (x1 - x0) / (y1 - y0))
            xs.sort()
            for i in range(0, len(xs) - 1, 2):
                self._span(py, int(xs[i] + 0.5), int(xs[i + 1] - 0.5), color, alpha)

    def capsule(self, x0, y0, x1, y1, width, color, alpha=1.0):
        """양 끝이 둥근 두꺼운 선이다."""
        dx, dy = x1 - x0, y1 - y0
        length = math.hypot(dx, dy)
        if length < 1e-6:
            self.ellipse(x0, y0, width * 0.5, width * 0.5, color, alpha)
            return
        nx, ny = -dy / length * width * 0.5, dx / length * width * 0.5
        self.poly([(x0 + nx, y0 + ny), (x1 + nx, y1 + ny),
                   (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)], color, alpha)
        self.ellipse(x0, y0, width * 0.5, width * 0.5, color, alpha)
        self.ellipse(x1, y1, width * 0.5, width * 0.5, color, alpha)

    def radial(self, cx, cy, radius, inner, outer, steps=24, alpha=1.0):
        """가운데에서 바깥으로 색이 변하는 원이다."""
        for i in range(steps, 0, -1):
            t = i / float(steps)
            self.ellipse(cx, cy, radius * t, radius * t, mix(inner, outer, t), alpha)

    # ------------------------------------------------------------ 쓰기

    def downsample(self):
        """`SS` 배 버퍼를 최종 크기의 RGBA 바이트로 줄인다."""
        w, h, ss = self.width, self.height, SS
        src, out = self.buf, bytearray(w * h * 4)
        area = ss * ss
        srow = self.w * 4
        for y in range(h):
            base = y * ss
            for x in range(w):
                r = g = b = a = 0
                for sy in range(ss):
                    i = (base + sy) * srow + (x * ss) * 4
                    for _ in range(ss):
                        pa = src[i + 3]
                        r += src[i] * pa
                        g += src[i + 1] * pa
                        b += src[i + 2] * pa
                        a += pa
                        i += 4
                o = (y * w + x) * 4
                if a:
                    out[o] = r // a
                    out[o + 1] = g // a
                    out[o + 2] = b // a
                out[o + 3] = a // area
        return out

    def write_png(self, path):
        rgba = self.downsample()
        w, h = self.width, self.height
        raw = bytearray()
        for y in range(h):
            raw.append(0)  # 필터 없음
            raw += rgba[y * w * 4:(y + 1) * w * 4]

        def chunk(tag, data):
            body = tag + data
            return struct.pack(">I", len(data)) + body + \
                struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

        png = b"\x89PNG\r\n\x1a\n"
        png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
        png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        png += chunk(b"IEND", b"")
        with io.open(path, "wb") as f:
            f.write(png)


# ---------------------------------------------------------------- 색

def hex_color(text):
    """`#RRGGBB` 를 세 값으로 읽는다."""
    text = text.strip().lstrip("#")
    return (int(text[0:2], 16), int(text[2:4], 16), int(text[4:6], 16))


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return (int(a[0] + (b[0] - a[0]) * t),
            int(a[1] + (b[1] - a[1]) * t),
            int(a[2] + (b[2] - a[2]) * t))


def shade(color, amount):
    """`amount` 가 음수면 어둡게, 양수면 밝게 한다. -1 에서 1 사이이다."""
    if amount < 0.0:
        return mix(color, (0, 0, 0), -amount)
    return mix(color, (255, 255, 255), amount)


class Rng(object):
    """씨앗에서 값을 내는 작은 난수기이다.

    파이썬의 `random` 을 쓰지 않는 것은 **판마다 같은 그림이 나와야 하기 때문**이다. 표준
    라이브러리의 알고리즘이 판 사이에 바뀌면 커밋된 그림과 다시 만든 그림이 달라진다.
    """

    def __init__(self, seed):
        if isinstance(seed, str):
            value = 2166136261
            for ch in seed.encode("utf-8"):
                value = ((value ^ ch) * 16777619) & 0xFFFFFFFF
            seed = value
        self.state = (seed | 1) & 0xFFFFFFFF

    def next(self):
        x = self.state
        x ^= (x << 13) & 0xFFFFFFFF
        x ^= x >> 17
        x ^= (x << 5) & 0xFFFFFFFF
        self.state = x & 0xFFFFFFFF
        return self.state

    def float(self, low=0.0, high=1.0):
        return low + (high - low) * (self.next() / 4294967296.0)

    def int(self, low, high):
        """`low` 이상 `high` 이하이다."""
        return low + self.next() % (high - low + 1)

    def pick(self, items):
        return items[self.next() % len(items)]

    def chance(self, probability):
        return self.float() < probability
