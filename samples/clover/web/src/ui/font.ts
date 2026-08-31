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
