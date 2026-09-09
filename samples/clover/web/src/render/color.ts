// 색을 계산하는 법.
//
// **여기에는 색이 하나도 없습니다.** 색을 만드는 계산만 있습니다 — 팔레트는 `palette.ts`
// 이고, 이 파일은 그쪽이 「이 바탕 위에서 대비 2.3인 색」 같은 것을 물을 때 답을 내는
// 자리입니다.
//
// **두 좌표계를 씁니다.** 대비는 sRGB 의 상대휘도로 재는 것이 규격이고(WCAG), 색상각과
// 채도를 고정한 채 밝기만 옮기려면 지각적으로 고른 좌표계가 필요합니다 — OKLCH 입니다.
// HSL 로 밝기를 옮기면 색상각이 같아도 노랑과 파랑의 체감 밝기가 크게 어긋납니다.

/** 8비트 채널 하나를 선형광으로. */
function toLinear(channel: number): number {
  const unit = channel / 255
  return unit <= 0.04045 ? unit / 12.92 : ((unit + 0.055) / 1.055) ** 2.4
}

function toChannel(linear: number): number {
  const unit = linear <= 0.0031308
    ? linear * 12.92
    : 1.055 * linear ** (1 / 2.4) - 0.055
  return Math.round(Math.min(1, Math.max(0, unit)) * 255)
}

export function rgbOf(color: number): [number, number, number] {
  return [(color >> 16) & 0xff, (color >> 8) & 0xff, color & 0xff]
}

/**
 * 상대휘도. **대비를 재는 값입니다.**
 *
 * 0이 검정, 1이 흰색입니다. 눈에 보이는 밝기가 아니라 빛의 양이므로, 이 값이 반이라고
 * 해서 절반 밝기로 보이지는 않습니다 — 그것이 OKLCH 를 함께 쓰는 이유입니다.
 */
export function luminance(color: number): number {
  const [r, g, b] = rgbOf(color)
  return 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b)
}

/** 두 색의 대비비. 1이 같은 색이고 21이 검정과 흰색입니다. */
export function contrast(a: number, b: number): number {
  const first = luminance(a)
  const second = luminance(b)
  return (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05)
}

/**
 * 이 바탕 위에서 대비가 `ratio` 가 되는 휘도.
 *
 * **바탕보다 밝은 쪽입니다.** 어두운 쪽은 `ratio` 를 1보다 작게 넘기면 나옵니다 — 그래서
 * 팔레트의 표가 「판을 1로 두고 몇 배」 하나로 적힙니다.
 *
 * 대비비는 (휘도 + 0.05) 의 비이므로, 같은 바탕을 기준으로 잰 두 값의 대비는 두 배수를
 * 나눈 것과 정확히 같습니다. **표에 적힌 배수끼리 나누면 그 둘의 대비가 나옵니다.**
 */
export function luminanceFor(base: number, ratio: number): number {
  return ratio * (base + 0.05) - 0.05
}

// ── OKLab ──────────────────────────────────────────────────────────────────
//
// Björn Ottosson 의 변환입니다. 색상각과 채도를 잡아 둔 채 밝기만 옮기는 데 씁니다.

function oklabOf(r: number, g: number, b: number): [number, number, number] {
  const lr = toLinear(r)
  const lg = toLinear(g)
  const lb = toLinear(b)
  const l = Math.cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb)
  const m = Math.cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb)
  const s = Math.cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb)
  return [
    0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
  ]
}

/** OKLab 하나를 선형 sRGB 셋으로. 범위를 벗어난 값도 그대로 돌려줍니다. */
function linearOf(lightness: number, a: number, b: number): [number, number, number] {
  const l = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s = (lightness - 0.0894841775 * a - 1.2914855480 * b) ** 3
  return [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s,
  ]
}

/** 이 밝기·채도·색상각이 sRGB 안에 들어오는가. */
function inGamut(lightness: number, chroma: number, hue: number): boolean {
  const radian = (hue * Math.PI) / 180
  const [r, g, b] = linearOf(lightness, Math.cos(radian) * chroma,
                             Math.sin(radian) * chroma)
  const slack = 1 / 512
  return r >= -slack && r <= 1 + slack && g >= -slack && g <= 1 + slack
    && b >= -slack && b <= 1 + slack
}

/** 그 밝기에서 낼 수 있는 가장 높은 채도. */
function fitChroma(lightness: number, chroma: number, hue: number): number {
  if (inGamut(lightness, chroma, hue)) return chroma
  let low = 0
  let high = chroma
  for (let i = 0; i < 16; i++) {
    const mid = (low + high) / 2
    if (inGamut(lightness, mid, hue)) low = mid
    else high = mid
  }
  return low
}

/**
 * OKLCH 하나를 색으로. **범위를 벗어나면 채도를 줄여 들어옵니다.**
 *
 * 채널을 자르지 않는 이유는 자르면 색상각이 함께 움직이기 때문입니다 — 어두운 자리의
 * 진한 파랑을 잘라 넣으면 보라로 옮겨 갑니다.
 */
export function oklch(lightness: number, chroma: number, hue: number): number {
  const fitted = fitChroma(lightness, chroma, hue)
  const radian = (hue * Math.PI) / 180
  const [r, g, b] = linearOf(lightness, Math.cos(radian) * fitted,
                             Math.sin(radian) * fitted)
  return (toChannel(r) << 16) | (toChannel(g) << 8) | toChannel(b)
}

/**
 * 이 색상각·채도로, 상대휘도가 `target` 인 색.
 *
 * **팔레트가 쓰는 단 하나의 만드는 함수입니다.** 색을 손으로 적는 대신 「바탕 대비 몇
 * 배」를 적고, 그 배수가 휘도가 되어 여기로 들어옵니다 — 겉면을 더할 때 맞출 것이 색상각과
 * 채도 둘뿐이 되는 것이 요점입니다.
 *
 * 밝기에 따라 휘도가 단조증가하므로 이분법으로 찾습니다. 채도가 범위를 넘는 자리에서는
 * 줄여 넣으므로 휘도가 조금 어긋날 수 있고, 그래서 24번 돌립니다.
 */
export function solveLevel(target: number, chroma: number, hue: number): number {
  const want = Math.min(1, Math.max(0, target))
  let low = 0
  let high = 1
  for (let i = 0; i < 24; i++) {
    const mid = (low + high) / 2
    if (luminance(oklch(mid, chroma, hue)) < want) low = mid
    else high = mid
  }
  return (low + high) / 2
}

export function solve(target: number, chroma: number, hue: number): number {
  return oklch(solveLevel(target, chroma, hue), chroma, hue)
}

/**
 * 바탕 위에서 대비 `ratio` 인 색 하나.
 *
 * `ratio` 가 1보다 크면 바탕보다 밝고, 작으면 어둡습니다.
 */
export function against(base: number, ratio: number, chroma: number,
                        hue: number): number {
  return solve(luminanceFor(luminance(base), ratio), chroma, hue)
}

/** 두 색 사이. `t` 가 0이면 앞, 1이면 뒤입니다. */
export function mix(a: number, b: number, t: number): number {
  const channel = (shift: number): number => {
    const first = (a >> shift) & 0xff
    const second = (b >> shift) & 0xff
    return Math.round(first + (second - first) * t) & 0xff
  }
  return (channel(16) << 16) | (channel(8) << 8) | channel(0)
}

/** 그 색의 색상각. 무채색이면 `undefined` 입니다. */
export function hueOf(color: number): number | undefined {
  const [, a, b] = oklabOf(...rgbOf(color))
  if (Math.hypot(a, b) < 1e-4) return undefined
  return ((Math.atan2(b, a) * 180) / Math.PI + 360) % 360
}

/** 그 색의 채도(OKLCH). */
export function chromaOf(color: number): number {
  const [, a, b] = oklabOf(...rgbOf(color))
  return Math.hypot(a, b)
}
