// 겉면 하나가 어떻게 만들어지는가.
//
// **색을 적지 않고 관계를 적습니다.** 겉면마다 손으로 적는 것은 중립 계열의 색상각 하나와
// 채도 하나와 판의 밝기 하나입니다 — 나머지 50 남짓은 「판 위에서 대비 몇 배」 라는 표
// 하나에서 나옵니다.
//
// 그렇게 하는 이유는 손으로 적던 동안 실제로 무너졌기 때문입니다. 겉면 8종의 단추와 잠긴
// 단추의 대비가 1.19~1.31 이었고 — 두 색이 다르게 보이려면 1.5 안팎이 필요합니다 — 켜진
// 단추가 잠긴 단추와 같은 색이었습니다. 구분선은 판 위에서 1.40~2.02 였고, 굵기 1의 선은
// 넓은 면보다 높은 대비가 필요하므로 그중 일곱은 나타나지 않았습니다.
//
// **대비비는 같은 바탕을 기준으로 잰 두 배수를 나눈 것과 정확히 같습니다.** 그래서 아래 표의
// 숫자끼리 나누면 그 둘의 대비가 나오고, 겉면이 몇 개든 그 관계가 유지됩니다 — 「단추
// 2.30 · 잠긴 단추 1.30」 은 어느 겉면에서나 둘의 대비가 1.77 이라는 뜻입니다.
//
// 게이트는 `test/palette.test.ts` 입니다.

import { luminance, luminanceFor, oklch, solveLevel } from './color'

/**
 * 겉면 하나가 손으로 적는 것.
 *
 * **다섯 개뿐입니다.** 겉면을 더하는 일이 색 50개를 고르는 일이 아니라 색상각 하나와 밝기
 * 하나를 고르는 일이어야, 더한 겉면이 나머지와 같은 규칙을 따릅니다.
 */
export interface SurfaceSeed {
  /** 중립 계열의 색상각(OKLCH). 판 · 칸 · 단추 · 선이 전부 이 각을 씁니다. */
  hue: number
  /** 중립 계열의 채도. 0이면 무채색입니다. */
  chroma: number
  /** 판의 상대휘도. **겉면의 밝기가 이 하나입니다.** */
  level: number
  /** 판이 배경 위에서 얼마나 비치는가. */
  alpha: number
  /**
   * 바깥 테의 색상각.
   *
   * **적지 않으면 중립과 같습니다.** 기본 겉면만 남흑색 판에 따뜻한 갈색 테이고, 그것이
   * 이 항목이 있는 이유입니다.
   */
  edgeHue?: number
  /** 강조색의 채도 배율. 1이 표에 적힌 그대로입니다. */
  vivid?: number
}

/**
 * 판 위에서의 대비 배수.
 *
 * **1보다 크면 판보다 밝고 작으면 어둡습니다.** 표 안의 숫자끼리 나눈 값이 그 둘의
 * 대비이므로, 고칠 때는 옆 칸과의 비를 함께 봅니다.
 */
interface Ratio {
  /** 판을 1로 둔 배수. */
  ratio: number
  /** 중립 채도에 곱하는 값. 강조색은 `chroma` 를 대신 적습니다. */
  tint?: number
  /** 채도를 직접 정합니다. 강조색이 씁니다. */
  chroma?: number
  /** 색상각. 적지 않으면 중립입니다. */
  hue?: number
  /** OKLCH 밝기의 위 한계. **밝은 겉면에서 강조색이 흰색으로 바래는 것을 막습니다.** */
  max?: number
  /** OKLCH 밝기의 아래 한계. */
  min?: number
}

/** 밝기를 직접 정하는 것. **바탕과 무관하게 절대적인 자리에만 씁니다.** */
interface Level {
  level: number
  tint?: number
  chroma?: number
  hue?: number
}

/**
 * 판때기의 층.
 *
 * **칸이 판보다 어둡습니다.** 값이 들어가는 자리는 파인 것으로 보여야 하고, 판과 같은
 * 밝기면 테 하나로만 갈립니다 — 그 테도 흐렸습니다(판 대비 1.05~1.24 였습니다).
 */
const SURFACES: Record<string, Ratio> = {
  /** 판 뒤. 배경이 셰이더로 덮이지 않는 자리에 보입니다. */
  ground: { ratio: 0.58 },
  /** 값 칸 · 입력 · 물건 칸. 판 대비 1.35 입니다. */
  cell: { ratio: 0.741 },
  /** 진행 바의 바탕. 칸보다 한 단 더 팹니다. */
  well: { ratio: 0.62 },
  /** 설명 쪽지의 바탕. 판 위에 뜨는 것이라 판보다 어둡습니다. */
  tipBack: { ratio: 0.69 },
  /** 뒤를 덮는 막. */
  scrim: { ratio: 0.20, tint: 0.5 },
}

/**
 * 선.
 *
 * **면보다 한 단 높은 대비를 씁니다.** 굵기 1~1.5의 선은 같은 대비에서 넓은 면보다 흐리게
 * 보입니다 — 배수를 면과 같이 맞추면 선만 사라집니다.
 *
 * **채도가 면보다 높습니다.** 선과 단추가 같은 밝기 띠에 놓이는 것은 어두운 중립 계열에서
 * 피할 수 없고, 그 둘을 가르는 것은 채도와 — 단추에만 있는 — 어두운 테입니다.
 */
const LINES: Record<string, Ratio> = {
  /** 구획 머리 아래의 선. **이름에 딸린 선이므로 가장 뚜렷합니다.** */
  rule: { ratio: 2.60, tint: 2.4 },
  /**
   * 무리를 가르는 줄(`groove`).
   *
   * **구획선보다 한 단 낮습니다.** 이름 없이 위아래를 가르기만 하므로 약한 표시이고,
   * 대시가 그 차이를 한 번 더 알립니다.
   */
  groove: { ratio: 2.05, tint: 2.4 },
  /** 칸의 테. **칸을 바탕으로 재어 1.95 입니다**(0.741 × 1.95). */
  hairline: { ratio: 1.445, tint: 2.0 },
  /** 판의 바깥 테. */
  panelEdge: { ratio: 3.35, tint: 3.0 },
  /** 쪽지의 테. **쪽지를 바탕으로 재어 2.85 입니다**(0.69 × 2.85). */
  tipEdge: { ratio: 1.967, tint: 2.6 },
}

/**
 * 누를 수 있는 것의 바탕.
 *
 * **잠긴 것과 1.77 벌어져 있습니다.** 손으로 적던 동안은 1.19~1.31 이었고, 그래서 켜진
 * 단추가 잠긴 단추로 보였습니다 — 잠김을 알리는 것이 글자의 알파 하나뿐이었습니다.
 *
 * 채도는 면보다 낮습니다. 단추는 누르는 것이지 색을 알리는 것이 아닙니다.
 */
const CONTROLS: Record<string, Ratio> = {
  /** 잠긴 단추. **판 쪽으로 당겨 둡니다** — 잠긴 것이 판보다 먼저 보이면 안 됩니다. */
  locked: { ratio: 1.30, tint: 0.7 },
  /** 보통 단추. */
  btn: { ratio: 2.35, tint: 0.7 },
  /** 판 위에 조용히 놓이는 단추. 칸과 같은 층입니다. */
  quiet: { ratio: 1.72, tint: 0.7 },
  /** 스크롤 막대의 홈. */
  track: { ratio: 1.50, tint: 0.8 },
  /** 스크롤 막대의 손잡이. */
  grip: { ratio: 3.20, tint: 0.8 },
}

/**
 * 뜻이 있는 색.
 *
 * **색상각은 고정이고 밝기만 겉면을 따릅니다.** 「돈은 노랑」 이 약속인 것은 맞지만, 약속인
 * 것은 노랑이라는 계열이지 `0xf5c518` 이라는 값 하나가 아닙니다 — 값으로 고정해 두면 밝은
 * 겉면에서 대비가 7.57 까지 떨어지고 어두운 겉면에서 11.59 까지 올라가, 같은 노랑이 겉면마다
 * 다른 무게로 놓입니다.
 *
 * `max` 는 밝은 겉면에서 강조색이 흰색으로 바래지 않게 하는 한계입니다.
 */
const INTENTS: Record<string, Ratio> = {
  /** 값 · 돈 · 나아가는 단추. */
  yellow: { ratio: 8.0, chroma: 0.168, hue: 88, max: 0.88 },
  /** 돈의 금색. 노랑보다 반 단 밝습니다. */
  money: { ratio: 8.4, chroma: 0.158, hue: 85, max: 0.9 },
  /** 진행 바 · 요구 점수. */
  bar: { ratio: 6.4, chroma: 0.131, hue: 223, max: 0.84 },
  /** 칩. */
  chips: { ratio: 4.4, chroma: 0.19, hue: 251, max: 0.78 },
  /** 배수. */
  mult: { ratio: 4.6, chroma: 0.196, hue: 27, max: 0.78 },
  /** 고른 것. 목록의 줄과 물건 칸의 테입니다. */
  pick: { ratio: 3.8, chroma: 0.168, hue: 253, max: 0.76 },
  /** 승리 · 핸드 수. */
  green: { ratio: 8.0, chroma: 0.132, hue: 160, max: 0.88 },
  /** 된 것. */
  good: { ratio: 7.2, chroma: 0.147, hue: 154, max: 0.86 },
  /** 되돌릴 수 없는 것 · 버리기. */
  red: { ratio: 5.0, chroma: 0.148, hue: 29, max: 0.8 },
  /** 안 된 것 · 모자란 값. */
  bad: { ratio: 5.4, chroma: 0.163, hue: 22, max: 0.82 },
  /** 걸어 보는 것. 블라인드를 건너뜁니다. */
  dare: { ratio: 4.3, chroma: 0.147, hue: 53, max: 0.78 },
  /**
   * 남은 버리기.
   *
   * **주황이되 걸어 보는 것보다 밝습니다.** 둘 다 주황인 것은 「쓰면 줄어드는 것」이라는
   * 같은 갈래이기 때문이고, 하나는 왼쪽 판의 수이고 하나는 단추이므로 밝기로 갈립니다.
   */
  discard: { ratio: 6.0, chroma: 0.150, hue: 50, max: 0.84 },
  /** 묻는 판의 「그렇게 합니다」. 되돌릴 수 있는 쪽입니다. */
  confirm: { ratio: 3.7, chroma: 0.11, hue: 155, max: 0.74 },
  /**
   * 되돌릴 수 없는 일의 첫 누름.
   *
   * **붉음의 어두운 쪽입니다.** 두 번 눌러야 지워지는 단추가 처음부터 붉으면 그 판에서
   * 가장 먼저 보이는 것이 「지운다」가 됩니다 — 두 번째 누름에서 `danger` 로 갑니다.
   */
  caution: { ratio: 2.6, chroma: 0.10, hue: 29, max: 0.68 },
  /** 글 속의 수. 칩과 같은 계열입니다. */
  accentNumber: { ratio: 7.0, chroma: 0.11, hue: 246, max: 0.86 },
  /** 글 속의 이름. */
  accentTerm: { ratio: 9.2, chroma: 0.12, hue: 85, max: 0.93 },
  /** 희귀도 넷. 상점과 조커의 테가 씁니다. */
  common: { ratio: 5.4, chroma: 0.032, hue: 256, max: 0.82 },
  uncommon: { ratio: 6.4, chroma: 0.125, hue: 168, max: 0.84 },
  rare: { ratio: 4.6, chroma: 0.196, hue: 27, max: 0.78 },
  legendary: { ratio: 5.2, chroma: 0.166, hue: 300, max: 0.8 },
}

/** 글. **흐린 단계 둘은 판을 기준으로 재고, 본문은 밝기를 직접 정합니다.** */
const INKS: Record<string, Ratio> = {
  /** 흐린 글. 이름 · 딱지 · 단위입니다. */
  inkDim: { ratio: 5.6, tint: 1.2, max: 0.80 },
  /** 더 흐린 글. 곁들이는 수와 도움말입니다. */
  inkFaint: { ratio: 3.2, tint: 1.2, max: 0.66 },
}

const LEVELS: Record<string, Level> = {
  /** 본문 글. **거의 흰색입니다** — 겉면의 색상각이 아주 옅게만 섞입니다. */
  ink: { level: 0.955, tint: 0.5 },
  /** 구획 머리의 마름모. */
  mark: { level: 0.874, tint: 0.9 },
  /** 밝은 단추 · 고른 탭. */
  light: { level: 0.885, tint: 0.7 },
  /** 밝은 단추 위의 글. */
  onLight: { level: 0.240, tint: 1.0 },
  /** 모든 테의 잉크. 단추와 카드의 테입니다. */
  outline: { level: 0.160, tint: 0.6 },
}

/**
 * 가리켰을 때와 눌렸을 때.
 *
 * **밝기로 옮깁니다.** 대비 배수로 적으면 이미 밝은 단추에서 흰색에 부딪혀 아무 일도
 * 일어나지 않습니다 — 크림색 단추와 남흑색 단추가 같은 만큼 움직여야 합니다.
 */
const HOVER = 0.09
const PRESS = -0.06

/** 밝기의 위아래 끝. 넘으면 흰색과 검정에 붙어 움직이지 않습니다. */
function clampLevel(level: number): number {
  return Math.min(0.985, Math.max(0.02, level))
}

/** 만들어 둔 색 하나. 밝기를 함께 들고 있어야 가리킨 것과 눌린 것을 낼 수 있습니다. */
interface Made {
  color: number
  level: number
  chroma: number
  hue: number
}

function makeRatio(panel: number, seed: SurfaceSeed, spec: Ratio): Made {
  const hue = spec.hue ?? seed.hue
  const chroma = spec.chroma !== undefined
    ? spec.chroma * (seed.vivid ?? 1)
    : seed.chroma * (spec.tint ?? 1)
  const want = luminanceFor(panel, spec.ratio)
  let level = solveLevel(want, chroma, hue)
  if (spec.max !== undefined) level = Math.min(level, spec.max)
  if (spec.min !== undefined) level = Math.max(level, spec.min)
  level = clampLevel(level)
  return { color: oklch(level, chroma, hue), level, chroma, hue }
}

function makeLevel(seed: SurfaceSeed, spec: Level): Made {
  const hue = spec.hue ?? seed.hue
  const chroma = spec.chroma ?? seed.chroma * (spec.tint ?? 1)
  const level = clampLevel(spec.level)
  return { color: oklch(level, chroma, hue), level, chroma, hue }
}

/** 그 색에서 밝기만 옮긴 것. */
function shift(made: Made, by: number): number {
  return oklch(clampLevel(made.level + by), made.chroma, made.hue)
}

/**
 * 겉면 하나가 정하는 색 전부.
 *
 * **손으로 적는 자리가 아닙니다** — `buildSurface` 가 씨앗 하나에서 만듭니다. 이름을 하나
 * 더하려면 위의 표에 한 줄을 더합니다.
 */
export interface Surface {
  panel: number
  panelAlpha: number
  panelEdge: number
  ground: number
  cell: number
  well: number
  tipBack: number
  tipEdge: number
  scrim: number

  /** 구획 머리 아래의 선. */
  rule: number
  /** 무리를 가르는 줄. */
  groove: number
  /** 칸의 테. */
  hairline: number

  /** 보통 단추. */
  btn: number
  btnHover: number
  btnPress: number
  /** 조용한 단추. */
  quiet: number
  quietHover: number
  quietPress: number
  /** 밝은 단추 · 고른 탭. */
  light: number
  lightHover: number
  lightPress: number
  /** 잠긴 단추. */
  locked: number

  /** 스크롤 막대. */
  track: number
  grip: number
  gripHot: number

  /** 모든 테의 잉크. */
  outline: number
  /** 밝은 단추 위의 글. */
  onLight: number
  /** 구획 머리의 마름모. */
  mark: number

  ink: number
  inkDim: number
  inkFaint: number

  yellow: number
  yellowHover: number
  yellowPress: number
  money: number
  bar: number
  chips: number
  mult: number
  pick: number
  green: number
  good: number
  red: number
  redHover: number
  redPress: number
  bad: number
  dare: number
  dareHover: number
  darePress: number
  discard: number
  confirm: number
  confirmHover: number
  confirmPress: number
  caution: number
  cautionHover: number
  cautionPress: number
  accentNumber: number
  accentTerm: number
  common: number
  uncommon: number
  rare: number
  legendary: number
}

/**
 * 씨앗 하나에서 겉면 하나를 만듭니다.
 *
 * **여기가 유일하게 색을 만드는 자리입니다.** 화면 어디에도 색을 손으로 적지 않는 것이
 * 목표이고, 그래야 겉면을 더할 때 빠지는 자리가 없습니다.
 */
export function buildSurface(seed: SurfaceSeed): Surface {
  const panel = seed.level
  const at = (spec: Ratio): Made => makeRatio(panel, seed, spec)
  const flat = (spec: Level): Made => makeLevel(seed, spec)

  const edgeSeed: SurfaceSeed = seed.edgeHue === undefined
    ? seed
    : { ...seed, hue: seed.edgeHue }

  const btn = at(CONTROLS.btn)
  const quiet = at(CONTROLS.quiet)
  const light = flat(LEVELS.light)
  const grip = at(CONTROLS.grip)
  const yellow = at(INTENTS.yellow)
  const red = at(INTENTS.red)
  const dare = at(INTENTS.dare)
  const confirm = at(INTENTS.confirm)
  const caution = at(INTENTS.caution)

  const plain = (spec: Ratio): number => at(spec).color

  return {
    panel: oklch(solveLevel(panel, seed.chroma, seed.hue), seed.chroma, seed.hue),
    panelAlpha: seed.alpha,
    panelEdge: makeRatio(panel, edgeSeed, LINES.panelEdge).color,
    ground: plain(SURFACES.ground),
    cell: plain(SURFACES.cell),
    well: plain(SURFACES.well),
    tipBack: plain(SURFACES.tipBack),
    tipEdge: plain(LINES.tipEdge),
    scrim: plain(SURFACES.scrim),

    rule: plain(LINES.rule),
    groove: plain(LINES.groove),
    hairline: plain(LINES.hairline),

    btn: btn.color,
    btnHover: shift(btn, HOVER),
    btnPress: shift(btn, PRESS),
    quiet: quiet.color,
    quietHover: shift(quiet, HOVER),
    quietPress: shift(quiet, PRESS),
    light: light.color,
    lightHover: shift(light, HOVER),
    lightPress: shift(light, PRESS),
    locked: plain(CONTROLS.locked),

    track: plain(CONTROLS.track),
    grip: grip.color,
    gripHot: shift(grip, HOVER + 0.05),

    outline: flat(LEVELS.outline).color,
    onLight: flat(LEVELS.onLight).color,
    mark: flat(LEVELS.mark).color,

    ink: flat(LEVELS.ink).color,
    inkDim: plain(INKS.inkDim),
    inkFaint: plain(INKS.inkFaint),

    yellow: yellow.color,
    yellowHover: shift(yellow, HOVER),
    yellowPress: shift(yellow, PRESS),
    money: plain(INTENTS.money),
    bar: plain(INTENTS.bar),
    chips: plain(INTENTS.chips),
    mult: plain(INTENTS.mult),
    pick: plain(INTENTS.pick),
    green: plain(INTENTS.green),
    good: plain(INTENTS.good),
    red: red.color,
    redHover: shift(red, HOVER),
    redPress: shift(red, PRESS),
    bad: plain(INTENTS.bad),
    dare: dare.color,
    dareHover: shift(dare, HOVER),
    darePress: shift(dare, PRESS),
    discard: plain(INTENTS.discard),
    confirm: confirm.color,
    confirmHover: shift(confirm, HOVER),
    confirmPress: shift(confirm, PRESS),
    caution: caution.color,
    cautionHover: shift(caution, HOVER),
    cautionPress: shift(caution, PRESS),
    accentNumber: plain(INTENTS.accentNumber),
    accentTerm: plain(INTENTS.accentTerm),
    common: plain(INTENTS.common),
    uncommon: plain(INTENTS.uncommon),
    rare: plain(INTENTS.rare),
    legendary: plain(INTENTS.legendary),
  }
}

/**
 * 게이트가 확인하는 것.
 *
 * **표와 같은 자리에 둡니다.** 배수를 고치면 이 표도 함께 보게 되고, 게이트만 따로 두면
 * 배수를 낮춘 커밋이 게이트를 함께 낮춥니다.
 *
 * `line` 이 참인 줄은 굵기 1~1.5 의 선이고, 넓은 면보다 0.4 높은 값을 요구합니다.
 */
export const CONTRAST_GATE: {
  what: string; a: keyof Surface; b: keyof Surface; least: number; line?: boolean
}[] = [
  { what: '판과 칸', a: 'cell', b: 'panel', least: 1.30 },
  { what: '판과 진행 바의 바탕', a: 'well', b: 'panel', least: 1.35 },
  { what: '칸과 진행 바의 바탕', a: 'cell', b: 'well', least: 1.05 },
  { what: '판과 쪽지', a: 'tipBack', b: 'panel', least: 1.38 },
  { what: '판과 바깥 테', a: 'panelEdge', b: 'panel', least: 2.85, line: true },
  { what: '판과 구획선', a: 'rule', b: 'panel', least: 2.15, line: true },
  { what: '판과 가르는 줄', a: 'groove', b: 'panel', least: 1.60, line: true },
  { what: '칸과 칸의 테', a: 'hairline', b: 'cell', least: 1.50, line: true },
  { what: '쪽지와 쪽지의 테', a: 'tipEdge', b: 'tipBack', least: 2.35, line: true },
  // **선끼리는 더 얹지 않습니다.** 얇은 선에 얹는 0.4는 면 위에서 나타나기 위한 것이고,
  // 두 선을 가르는 데 필요한 값이 아닙니다.
  { what: '구획선과 가르는 줄', a: 'rule', b: 'groove', least: 1.20 },

  { what: '판과 단추', a: 'btn', b: 'panel', least: 2.10 },
  { what: '단추와 잠긴 단추', a: 'btn', b: 'locked', least: 1.65 },
  { what: '단추와 가리킨 단추', a: 'btnHover', b: 'btn', least: 1.35 },
  { what: '단추와 눌린 단추', a: 'btn', b: 'btnPress', least: 1.15 },
  { what: '판과 잠긴 단추', a: 'locked', b: 'panel', least: 1.20 },
  { what: '판과 조용한 단추', a: 'quiet', b: 'panel', least: 1.50 },
  { what: '조용한 단추와 잠긴 단추', a: 'quiet', b: 'locked', least: 1.20 },
  { what: '단추와 밝은 단추', a: 'light', b: 'btn', least: 2.70 },
  { what: '밝은 단추와 가리킨 것', a: 'lightHover', b: 'light', least: 1.10 },
  { what: '단추 테와 단추', a: 'btn', b: 'outline', least: 1.80, line: true },
  { what: '밝은 단추와 그 위의 글', a: 'light', b: 'onLight', least: 7.00 },

  { what: '판과 홈', a: 'track', b: 'panel', least: 1.35 },
  { what: '홈과 손잡이', a: 'grip', b: 'track', least: 1.70 },
  { what: '손잡이와 잡은 손잡이', a: 'gripHot', b: 'grip', least: 1.30 },

  { what: '판과 본문 글', a: 'ink', b: 'panel', least: 8.00 },
  { what: '판과 흐린 글', a: 'inkDim', b: 'panel', least: 5.00 },
  { what: '판과 더 흐린 글', a: 'inkFaint', b: 'panel', least: 3.00 },
  { what: '칸과 흐린 글', a: 'inkDim', b: 'cell', least: 6.00 },
  { what: '판과 마름모', a: 'mark', b: 'panel', least: 6.00 },

  { what: '판과 돈', a: 'money', b: 'panel', least: 7.00 },
  { what: '판과 값', a: 'yellow', b: 'panel', least: 6.50 },
  { what: '판과 진행 바', a: 'bar', b: 'panel', least: 5.50 },
  { what: '판과 칩', a: 'chips', b: 'panel', least: 4.00 },
  { what: '판과 배수', a: 'mult', b: 'panel', least: 4.00 },
  { what: '칸과 칩', a: 'chips', b: 'cell', least: 5.00 },
  { what: '칸과 배수', a: 'mult', b: 'cell', least: 5.00 },
  { what: '판과 고른 것', a: 'pick', b: 'panel', least: 3.30, line: true },
  { what: '칸과 고른 것', a: 'pick', b: 'cell', least: 4.00, line: true },
  { what: '판과 승리', a: 'green', b: 'panel', least: 6.50 },
  { what: '판과 된 것', a: 'good', b: 'panel', least: 6.00 },
  { what: '판과 붉음', a: 'red', b: 'panel', least: 4.50 },
  { what: '판과 안 된 것', a: 'bad', b: 'panel', least: 4.80 },
  { what: '판과 걸어 보는 것', a: 'dare', b: 'panel', least: 3.90 },
  { what: '판과 남은 버리기', a: 'discard', b: 'panel', least: 5.20 },
  { what: '남은 버리기와 걸어 보는 것', a: 'discard', b: 'dare', least: 1.20 },
  { what: '판과 그렇게 합니다', a: 'confirm', b: 'panel', least: 3.40 },
  { what: '판과 첫 누름', a: 'caution', b: 'panel', least: 2.30 },
  { what: '첫 누름과 붉음', a: 'red', b: 'caution', least: 1.60 },
  { what: '판과 글 속의 수', a: 'accentNumber', b: 'panel', least: 6.30 },
  { what: '판과 글 속의 이름', a: 'accentTerm', b: 'panel', least: 7.40 },

  { what: '칸과 흔한 것', a: 'common', b: 'cell', least: 5.00, line: true },
  { what: '칸과 드문 것', a: 'uncommon', b: 'cell', least: 5.50, line: true },
  { what: '칸과 귀한 것', a: 'rare', b: 'cell', least: 4.00, line: true },
  { what: '칸과 전설', a: 'legendary', b: 'cell', least: 4.20, line: true },

  { what: '붉음과 걸어 보는 것', a: 'red', b: 'dare', least: 1.10 },
  { what: '값과 진행 바', a: 'yellow', b: 'bar', least: 1.08 },
]

/** 판의 밝기를 상대휘도로 읽습니다. 씨앗을 고칠 때 견줄 값입니다. */
export function levelOf(color: number): number {
  return luminance(color)
}
