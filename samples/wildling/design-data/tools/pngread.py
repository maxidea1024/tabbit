# -*- coding: utf-8 -*-
"""PNG 를 읽는다. 쓰는 쪽은 `raster.py` 에 있다.

의존성 없이 8비트 RGB · RGBA · 회색 · 팔레트를 읽습니다. 인터레이스는 다루지 않습니다 —
이 저장소가 다루는 그림에 그런 것이 없습니다.
"""
import io
import struct
import zlib

CHANNELS = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}


def read(path):
    """`(폭, 높이, RGBA 바이트)` 를 낸다."""
    data = io.open(path, "rb").read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("PNG 가 아닙니다: %s" % path)

    width = height = depth = color = 0
    idat = bytearray()
    palette = b""
    trns = b""

    i = 8
    while i < len(data):
        length = struct.unpack(">I", data[i:i + 4])[0]
        tag = data[i + 4:i + 8]
        body = data[i + 8:i + 8 + length]

        if tag == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if depth != 8 or interlace:
                raise ValueError("8비트 · 인터레이스 없는 것만 읽습니다: %s" % path)
        elif tag == b"PLTE":
            palette = body
        elif tag == b"tRNS":
            trns = body
        elif tag == b"IDAT":
            idat += body
        elif tag == b"IEND":
            break

        i += 12 + length

    raw = zlib.decompress(bytes(idat))
    channels = CHANNELS[color]
    stride = width * channels

    lines = []
    previous = bytearray(stride)
    at = 0
    for _ in range(height):
        kind = raw[at]
        at += 1
        line = bytearray(raw[at:at + stride])
        at += stride
        unfilter(kind, line, previous, channels)
        lines.append(line)
        previous = line

    return width, height, to_rgba(lines, width, height, color, palette, trns, channels)


def unfilter(kind, line, previous, channels):
    """PNG 의 다섯 가지 줄 필터를 되돌린다."""
    if kind == 0:
        return

    stride = len(line)
    if kind == 1:
        for x in range(channels, stride):
            line[x] = (line[x] + line[x - channels]) & 255
    elif kind == 2:
        for x in range(stride):
            line[x] = (line[x] + previous[x]) & 255
    elif kind == 3:
        for x in range(stride):
            left = line[x - channels] if x >= channels else 0
            line[x] = (line[x] + ((left + previous[x]) >> 1)) & 255
    elif kind == 4:
        for x in range(stride):
            left = line[x - channels] if x >= channels else 0
            up = previous[x]
            corner = previous[x - channels] if x >= channels else 0
            guess = left + up - corner
            dl, du, dc = abs(guess - left), abs(guess - up), abs(guess - corner)
            near = left if (dl <= du and dl <= dc) else (up if du <= dc else corner)
            line[x] = (line[x] + near) & 255
    else:
        raise ValueError("모르는 줄 필터 %d" % kind)


def to_rgba(lines, width, height, color, palette, trns, channels):
    out = bytearray(width * height * 4)
    for y in range(height):
        line = lines[y]
        row = y * width * 4
        for x in range(width):
            src = x * channels
            dst = row + x * 4
            if color == 6:
                out[dst:dst + 4] = line[src:src + 4]
            elif color == 2:
                out[dst:dst + 3] = line[src:src + 3]
                out[dst + 3] = 255
            elif color == 0:
                value = line[src]
                out[dst] = out[dst + 1] = out[dst + 2] = value
                out[dst + 3] = 255
            elif color == 4:
                value = line[src]
                out[dst] = out[dst + 1] = out[dst + 2] = value
                out[dst + 3] = line[src + 1]
            else:  # 팔레트
                index = line[src]
                out[dst:dst + 3] = palette[index * 3:index * 3 + 3]
                out[dst + 3] = trns[index] if index < len(trns) else 255
    return out


def resize(width, height, rgba, to_width, to_height):
    """상자 평균으로 줄인다. 늘리는 데는 쓰지 않는다."""
    out = bytearray(to_width * to_height * 4)
    for y in range(to_height):
        y0 = y * height // to_height
        y1 = max(y0 + 1, (y + 1) * height // to_height)
        for x in range(to_width):
            x0 = x * width // to_width
            x1 = max(x0 + 1, (x + 1) * width // to_width)

            r = g = b = a = n = 0
            for sy in range(y0, y1):
                base = (sy * width) * 4
                for sx in range(x0, x1):
                    i = base + sx * 4
                    alpha = rgba[i + 3]
                    r += rgba[i] * alpha
                    g += rgba[i + 1] * alpha
                    b += rgba[i + 2] * alpha
                    a += alpha
                    n += 1

            o = (y * to_width + x) * 4
            if a:
                out[o] = r // a
                out[o + 1] = g // a
                out[o + 2] = b // a
            out[o + 3] = a // max(1, n)
    return out
