// 말을 바꾸면 글이 바뀌는가.
//
// 둘을 봅니다 — 기계의 언어를 여섯 중 하나로 맞추는 것과, 고른 말로 글을 찾는 차례입니다.
// **번역이 아직 없는 칸은 한국어로 떨어집니다.** 그것도 여기서 확인합니다.
//
//     npx tsx tools/check-lang.ts

import * as path from 'path'
import { fileURLToPath } from 'url'

import { loadFromDisk } from '../src/core/load-node'
import { detectLanguage, fill, setLanguage, text } from '../src/core/strings'

const HERE = path.dirname(fileURLToPath(import.meta.url))

function main(): number {
  let failed = 0
  const verdict = (good: boolean, line: string) => {
    if (!good) failed++
    console.log(`  ${good ? '✓' : '✗'} ${line}`)
  }

  console.log('기계의 언어 맞추기')
  for (const [tag, want] of [
    ['ko-KR', 'ko'], ['ja', 'ja'], ['de-DE', 'de'], ['en-GB', 'en'],
    ['zh-TW', 'zh-Hant'], ['zh-HK', 'zh-Hant'], ['zh-MO', 'zh-Hant'],
    ['zh-CN', 'zh-Hans'], ['zh-Hant-CN', 'zh-Hant'], ['fr', 'en'],
  ] as const) {
    const got = detectLanguage([tag])
    verdict(got === want, `${tag} → ${got}`)
  }

  const data = loadFromDisk(path.resolve(HERE, '..', 'public', 'data'))

  console.log('\n찾는 차례')
  setLanguage('ko')
  const ko = text(data, 'joker.twig.name')
  setLanguage('en')
  const en = text(data, 'joker.twig.name')
  setLanguage('ja')
  const ja = text(data, 'joker.twig.name')
  console.log(`  ko=${ko}  en=${en}  ja=${ja}`)
  verdict(ko !== en, '말을 바꾸면 이름이 바뀝니다')
  verdict(ja === ko, '번역이 없으면 한국어로 떨어집니다')
  verdict(text(data, 'nope.nope') === 'nope.nope', '열쇠가 없으면 열쇠가 그대로 보입니다')

  console.log('\n조사')
  setLanguage('ko')
  for (const [word, want] of [
    ['클로버', '클로버는'], ['잔가지', '잔가지는'], ['별', '별은'],
  ] as const) {
    const filled = fill('{it:은}', { it: word })
    verdict(filled === want, `${word} → ${filled}`)
  }

  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

process.exit(main())
