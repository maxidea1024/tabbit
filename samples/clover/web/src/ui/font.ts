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

import { TextStyle } from 'pixi.js'

import type { Language } from '../core/strings'

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
