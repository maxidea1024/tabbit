// 화면의 글.
//
// **찾는 차례는 「고른 언어 → 한국어 → 열쇠 그대로」입니다.** 빠진 것을 영어로 대신 채우면
// 번역이 덜 된 자리가 화면에서 보이지 않고, 그러면 영영 덜 된 채로 남습니다. 열쇠가 그대로
// 보이면 그 자리가 눈에 띕니다.
//
// 한국어가 둘째인 것은 이 게임의 원본이 한국어이기 때문입니다 — 시트의 표시 이름이 한국어이고
// 나머지가 그 번역입니다.

import type { Data } from './data'

/** 이 게임이 아는 언어. */
export const LANGUAGES = ['ko', 'en', 'ja', 'zh-Hans', 'zh-Hant', 'de'] as const

export type Language = (typeof LANGUAGES)[number]

/** 고르는 자리에 적히는 이름. **그 언어로 적습니다** — 찾는 사람이 그 언어의 사람입니다. */
export const LANGUAGE_NAMES: Record<Language, string> = {
  ko: '한국어',
  en: 'English',
  ja: '日本語',
  'zh-Hans': '简体中文',
  'zh-Hant': '繁體中文',
  de: 'Deutsch',
}

/** 그 언어가 `StringTable` 의 어느 칸인가. */
const COLUMN: Record<Language, 'ko' | 'en' | 'ja' | 'zhHans' | 'zhHant' | 'de'> = {
  ko: 'ko',
  en: 'en',
  ja: 'ja',
  'zh-Hans': 'zhHans',
  'zh-Hant': 'zhHant',
  de: 'de',
}

let current: Language = 'ko'

export function setLanguage(language: Language): void {
  current = language
}

export function language(): Language {
  return current
}

/**
 * 브라우저가 말하는 언어를 여섯 중 하나로.
 *
 * **중국어는 지역이 아니라 글자로 갈립니다.** `zh-TW` 만 번체로 보면 번체를 쓰는
 * 홍콩·마카오가 간체로 떨어집니다.
 */
export function detectLanguage(tags: readonly string[]): Language {
  for (const raw of tags) {
    const tag = raw.toLowerCase()
    if (tag.startsWith('ko')) return 'ko'
    if (tag.startsWith('ja')) return 'ja'
    if (tag.startsWith('de')) return 'de'
    if (tag.startsWith('zh')) {
      const hant = tag.includes('hant') || /-(tw|hk|mo)\b/.test(tag)
      return hant ? 'zh-Hant' : 'zh-Hans'
    }
    if (tag.startsWith('en')) return 'en'
  }
  return 'en'
}

/**
 * 열쇠 하나의 글.
 *
 * 값이 있는 첫 칸을 씁니다. 어느 칸도 비어 있으면 열쇠를 그대로 돌려주어, 그 자리가 화면에서
 * 눈에 띄게 합니다.
 */
export function text(data: Data, key: string): string {
  const row = data.tables.stringTable.findByStringId(key)
  if (!row) return key
  const mine = row[COLUMN[current]]
  if (mine !== '') return mine
  return row.ko !== '' ? row.ko : key
}

/**
 * 열쇠 하나의 글에 값을 채웁니다.
 *
 * `{name}` 자리에 값이 들어갑니다. 한국어의 조사는 `{name:은}` 처럼 짝으로 적습니다 —
 * 앞 글자의 받침에 따라 갈리므로, 손으로 박으면 눈앞의 값에만 맞습니다.
 */
export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)(?::([은는이가을를와과로])?)?\}/g,
    (whole, name: string, particle: string | undefined) => {
      if (!(name in values)) return whole
      const value = String(values[name])
      return particle === undefined ? value : value + particleFor(value, particle)
    })
}

export function textWith(data: Data, key: string,
                         values: Record<string, string | number>): string {
  return fill(text(data, key), values)
}

/**
 * 그 낱말 뒤에 붙는 조사.
 *
 * 받침이 있으면 앞의 것, 없으면 뒤의 것입니다. `로` 는 ㄹ 받침도 받침 없는 쪽입니다 —
 * 「물로」이지 「물으로」가 아닙니다.
 */
function particleFor(word: string, want: string): string {
  const last = word.trimEnd().slice(-1)
  const code = last.charCodeAt(0)
  // 한글 음절이 아니면 조사를 고를 수 없습니다. 받침이 있는 것으로 봅니다 — 숫자와 로마자는
  // 읽는 사람이 그렇게 읽는 쪽이 많습니다.
  const syllable = code >= 0xac00 && code <= 0xd7a3
  const tail = syllable ? (code - 0xac00) % 28 : 1
  const rieul = tail === 8

  switch (want) {
    case '은': case '는': return tail === 0 ? '는' : '은'
    case '이': case '가': return tail === 0 ? '가' : '이'
    case '을': case '를': return tail === 0 ? '를' : '을'
    case '와': case '과': return tail === 0 ? '와' : '과'
    case '로': return tail === 0 || rieul ? '로' : '으로'
    default: return want
  }
}
