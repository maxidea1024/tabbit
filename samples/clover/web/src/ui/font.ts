// 화면의 글꼴.
//
// **여섯 말이 한 벌로 나와야 합니다.** 기계의 글꼴에 맡기면 없는 기계에서 네모가 보이고,
// 있는 기계에서도 저마다 다른 글꼴로 보입니다.
//
// 한자는 일본어·간체·번체의 자형이 다릅니다. 한 벌로 합치면 일본어 화면에 중국 자형이
// 나오므로 **말마다 다른 글꼴을 겁니다.**
//
// 글꼴은 `design-data/tools/font.py` 가 **쓰는 글자만큼만** 잘라 둡니다 — 시트에 글이 다
// 있어서 셀 수 있고, 통째로 담으면 40MB 인 것이 773KB 로 끝납니다.

import { TextStyle, type StrokeStyle, type Text } from 'pixi.js'

import { language, type Language } from '../core/strings'

/** 말마다 쓰는 글꼴 이름. 라틴은 한 벌로 족합니다. */
const FAMILY: Record<Language, string> = {
  ko: 'clover-kr',
  ja: 'clover-jp',
  'zh-Hans': 'clover-sc',
  'zh-Hant': 'clover-tc',
  en: 'clover-latin',
  de: 'clover-latin',
}

/** 어느 파일이 어느 이름인가. 굵기 둘씩입니다. */
const FILES: { family: string; file: string; weight: number }[] = [
  { family: 'clover-kr', file: 'noto-sans-kr', weight: 400 },
  { family: 'clover-kr', file: 'noto-sans-kr', weight: 700 },
  { family: 'clover-jp', file: 'noto-sans-jp', weight: 400 },
  { family: 'clover-jp', file: 'noto-sans-jp', weight: 700 },
  { family: 'clover-sc', file: 'noto-sans-sc', weight: 400 },
  { family: 'clover-sc', file: 'noto-sans-sc', weight: 700 },
  { family: 'clover-tc', file: 'noto-sans-tc', weight: 400 },
  { family: 'clover-tc', file: 'noto-sans-tc', weight: 700 },
  { family: 'clover-latin', file: 'noto-sans', weight: 400 },
  { family: 'clover-latin', file: 'noto-sans', weight: 700 },
]

/**
 * 글꼴을 다 읽습니다.
 *
 * **화면을 세우기 전에 기다립니다.** 글을 그리는 것은 글자를 그림으로 굽는 일이고, 그때
 * 글꼴이 아직 없으면 대체 글꼴로 구워져 그대로 남습니다 — 나중에 글꼴이 와도 다시 굽지
 * 않습니다.
 */
export async function loadFonts(base = './font'): Promise<void> {
  if (typeof document === 'undefined' || document.fonts === undefined) return

  await Promise.all(FILES.map(async one => {
    const face = new FontFace(one.family,
      `url(${base}/${one.file}-${one.weight}.woff2) format('woff2')`,
      { weight: String(one.weight), display: 'block' })
    try {
      document.fonts.add(await face.load())
    } catch {
      // 하나가 없어도 나머지는 씁니다. 그 말만 대체 글꼴로 갑니다.
    }
  }))
}

/**
 * 숫자에 쓰는 글꼴의 이름.
 *
 * **글의 글꼴과 다릅니다.** 판을 읽는 사람이 보는 것은 수이고, 본문용 글꼴의 숫자는 어느
 * 화면에서나 같은 모습이라 이 게임의 것으로 보이지 않습니다 — 두꺼운 표제용 글꼴은 자릿수가
 * 늘어도 덩어리로 읽힙니다.
 *
 * 라틴 글꼴을 뒤에 둡니다. 숫자만 잘라 둔 글꼴이라 `1 / 8` 의 빗금 같은 것이 없으면 그것만
 * 뒤의 글꼴로 갑니다.
 */
export const NUMERALS = ['clover-num', 'clover-latin', 'system-ui', 'sans-serif']

/**
 * 숫자 글꼴의 파일 이름.
 *
 * **Bungee 입니다.** 간판용 글꼴이라 획이 굵고 각져서, 작은 칸에서도 각이 살아 범용 글꼴
 * 느낌이 빠집니다 — Archivo Black 과 Titan One 도 놓고 봤는데, 앞의 것은 Noto 의 굵은
 * 숫자와 실루엣이 비슷해 바꾼 표가 덜 나고 뒤의 것은 `0` 이 좁아 「점수 0」 이 작게
 * 보였습니다.
 */
const NUMERAL_FILE = 'bungee-700'

/**
 * 숫자 글꼴을 읽습니다.
 *
 * **본문 글꼴과 함께 읽습니다.** 글자를 그림으로 굽는 것은 한 번뿐이라, 나중에 오면 그
 * 숫자는 대체 글꼴로 구워진 채 남습니다.
 */
export async function loadNumerals(base = './font'): Promise<void> {
  if (typeof document === 'undefined' || document.fonts === undefined) return
  const face = new FontFace('clover-num',
    `url(${base}/${NUMERAL_FILE}.woff2) format('woff2')`,
    { weight: '700', display: 'block' })
  try {
    document.fonts.add(await face.load())
  } catch {
    // 없으면 본문 글꼴의 숫자로 갑니다.
  }
}

/**
 * 그 말의 글꼴을 화면 전체에 겁니다.
 *
 * **한 자리에서 겁니다.** 글 하나하나에 글꼴 이름을 적으면 새로 만드는 글에서 반드시
 * 하나가 빠지고, 그것만 다른 글꼴로 그려집니다.
 */
export function useFont(language: Language): void {
  TextStyle.defaultTextStyle.fontFamily = [
    FAMILY[language], 'clover-latin', 'system-ui', 'sans-serif',
  ]
}

/**
 * 글자에 두르는 테두리의 굵기가 글자 크기의 몇 배인가.
 *
 * **굵기를 상수로 적으면 안 됩니다.** 캔버스의 테두리는 윤곽선에 가운데를 맞춰 그려지므로
 * 굵기 `w` 는 바깥으로 `w/2`, 안으로 `w/2` 번집니다. 바깥 윤곽선의 안쪽 절반은 뒤이어
 * 그리는 채우기가 덮지만 **획과 획 사이의 틈은 덮이지 않고 `w` 만큼 좁아집니다** — 굵기가
 * 그 글자의 틈보다 넓으면 획 사이가 메워져 검게 뭉칩니다.
 *
 * 값은 재서 정하였습니다. `design-data/out/font-chars.json` 의 **실제로 쓰는 글자 전부**를
 * 8배로 구워 획 사이 틈을 세고, 글자마다의 중앙값을 모아 하위 10% 를 보았습니다:
 *
 * |말|글자 수|15px|17px|23px|34px|크기 대비|
 * |--|--|--|--|--|--|--|
 * |한글|602|0.75|0.88|1.25|2.00|0.050 ~ 0.059|
 * |일본어|703|0.75|0.88|1.13|1.88|0.049 ~ 0.055|
 * |간체|842|0.75|0.88|1.25|1.88|0.049 ~ 0.055|
 * |번체|848|0.63|0.75|1.00|1.63|0.042 ~ 0.048|
 * |라틴|77|1.13|1.38|1.75|2.63|0.075 ~ 0.081|
 *
 * **한글이 한자만큼 촘촘합니다.** 「률」·「홀」·「돌」처럼 받침이 `ㄹ` 인 글자가 그렇고,
 * 그래서 한글과 일본어·간체의 배수가 같습니다. 번체만 한 단계 아래입니다.
 *
 * 배수는 위의 하위 10% 를 **넘지 않게** 골랐습니다 — 굵기는 위쪽 한계이지 목표가
 * 아닙니다.
 */
const OUTLINE_RATIO: Record<Language, number> = {
  ko: 0.045,
  ja: 0.045,
  'zh-Hans': 0.045,
  'zh-Hant': 0.04,
  en: 0.075,
  de: 0.075,
}

/**
 * 숫자 글꼴의 배수. 어느 말에서나 라틴이므로 말을 보지 않습니다.
 *
 * Bungee 는 본문 글꼴보다 굵어 속이 좁습니다. 숫자 12자의 틈을 재니 크기 대비 0.075 ~
 * 0.082 로 라틴 본문과 같은 자리였습니다.
 */
const LATIN_RATIO = 0.075

/**
 * 이 크기의 글에 두르는 테두리의 굵기.
 *
 * `latin` 은 그 글이 고른 말과 무관하게 라틴·숫자인 자리입니다 — `NUMERALS` 를 쓰는 칸이
 * 그렇습니다.
 */
export function outlineWidth(fontSize: number, latin = false): number {
  return fontSize * (latin ? LATIN_RATIO : OUTLINE_RATIO[language()])
}

/**
 * 굵기를 정해 둔 테두리.
 *
 * **이음매가 `round` 입니다.** PixiJS 의 기본값은 `miter` 에 `miterLimit` 이 10이라,
 * 삐침의 뾰족한 끝 같은 예각에서 이음매가 굵기의 10배까지 뻗어 글자 위로 가시가 솟습니다.
 */
export function outlineOf(width: number, color: number): StrokeStyle {
  return { color, width, join: 'round', miterLimit: 2 }
}

/** 그 크기의 글에 두르는 테두리. */
export function outline(fontSize: number, color: number, latin = false): StrokeStyle {
  return outlineOf(outlineWidth(fontSize, latin), color)
}

/**
 * 테두리를 두른 글의 크기와 테두리를 함께.
 *
 * **크기를 한 번만 적기 위한 것입니다.** 테두리의 굵기가 크기에서 나오므로 둘을 따로
 * 적으면 크기만 고친 자리에서 굵기가 옛 크기의 것으로 남습니다.
 */
export function outlined(fontSize: number, color: number, latin = false):
    { fontSize: number; stroke: StrokeStyle } {
  return { fontSize, stroke: outline(fontSize, color, latin) }
}

/**
 * 이 글에 실제로 걸려 있는 테두리의 굵기.
 *
 * **검증 도구가 읽는 값입니다.** 말이 바뀔 때 굵기가 따라오는지는 화면으로 보이지
 * 않습니다 — 굵기 차이가 1픽셀 아래입니다.
 */
export function strokeWidthOf(node: Text): number {
  const stroke = node.style.stroke
  return stroke !== null && typeof stroke === 'object' && 'width' in stroke
    ? Number(stroke.width) : 0
}
