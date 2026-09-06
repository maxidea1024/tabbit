// 진입점.
//
// 데이터를 읽고 화면을 세우는 것이 전부입니다. **규칙은 `core/` 에 있고 이 파일은 그것을
// 모릅니다.**
//
// **씬 셋 중 첫째가 여기 있습니다** — 로딩 · 타이틀 · 판 순서이고, 이 파일은 로딩을 맡아
// 다 읽고 나서 `Game` 에 넘깁니다. 나머지 둘은 `Game` 안에서 오갑니다. 그래서 판을 접고
// 타이틀로 가는 데 이 파일을 다시 지날 일이 없습니다 — **다시 지나면 데이터를 처음부터
// 읽고 로딩이 한 번 더 보입니다.**

import { Application } from 'pixi.js'

import { loadFromUrl } from './core/load'
import { setLanguage, useStrings } from './core/strings'
import { loadFonts, loadNumerals, useFont } from './ui/font'
import { randomSeed } from './ui/title'
import { chosen, loadOptions } from './ui/options'
import { loadArtIndex } from './render/art'
import { Boot } from './ui/boot'
import { loadIcons } from './ui/icon'
import { JokerPool } from './generated/enums/joker-pool'
import { Game } from './render/game'
import { COLOR, setUiTheme } from './render/theme'

/**
 * 몇 배로 그릴 것인가.
 *
 * **2 에서 끊습니다.** 화면의 밀도를 그대로 쓰면 손전화에서 3이 되는데, 판은 1280 × 800
 * 하나에 맞춰 그려지고 그 판이 손전화에서는 0.6배로 들어갑니다 — 화면에 실제로 필요한
 * 것은 그 곱이고, 3은 그보다 훨씬 큽니다. 값은 픽셀 수만큼 늘어납니다.
 */
function density(): number {
  return Math.min(2, window.devicePixelRatio || 1)
}

/** 손가락으로 짚는 화면인가. */
function coarsePointer(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches
}

async function main(): Promise<void> {
  const canvas = document.getElementById('stage') as HTMLCanvasElement
  const boot = new Boot()

  const app = new Application()
  await app.init({
    canvas,
    // **판 밖에 보이는 색입니다.** 판은 기준 해상도 하나에 맞춰 그려지고 그 밖은
    // 잘라 내므로, 지우는 색이 곧 잘라 낸 자리의 색입니다.
    background: COLOR.crop,
    // **손가락으로 짚는 화면에서는 끕니다.** 카드의 앞뒤와 글은 이미 그림으로 구워
    // 쓰므로 MSAA 가 다듬을 것이 판때기의 모서리뿐인데, 그 값은 화면 전체에 걸립니다 —
    // 손전화는 그 화면이 200만 픽셀이 넘고 GPU 는 데스크탑의 것이 아닙니다.
    antialias: !coarsePointer(),
    resolution: density(),
    autoDensity: true,
    resizeTo: window,
    preference: 'webgl',
  })
  boot.step('data')
  const data = await loadFromUrl('./data')
  // **화면을 만들기 전에 글 표를 넘깁니다.** 클래스의 필드는 생성자 본문보다 먼저 만들어지고,
  // 그 자리에서 이미 글을 읽습니다 — 생성자 안에서 넘기면 그것들이 열쇠를 그대로 답니다.
  useStrings(data)
  const saved = loadOptions()
  // **프레임 상한은 사람이 정합니다.** 옵션의 「화면」 탭이고, 처음 값은 「무제한」입니다 —
  // 걸어 두었더니 120Hz 화면에서 움직임이 절반이 되었고, 그것은 배터리를 아낀 것이 아니라
  // 눈이 먼저 알아채는 손해였습니다. 판이 서고 나서는 `Game.applyOptions` 가 겁니다.
  app.ticker.maxFPS = saved.frameCap
  const language = chosen(saved)
  setLanguage(language)
  // **판의 겉면도 화면을 세우기 전에 정합니다.** 판때기는 그릴 때의 색으로 삼각화되므로,
  // 세운 뒤에 정하면 첫 화면만 기본 겉면으로 남습니다.
  setUiTheme(saved.uiTheme)
  // **글꼴을 다 읽고 나서 화면을 세웁니다.** 글을 그리는 것은 글자를 그림으로 굽는 일이고,
  // 그때 글꼴이 없으면 대체 글꼴로 구워져 그대로 남습니다.
  boot.step('font')
  // **숫자 글꼴을 함께 읽습니다.** 글자를 그림으로 굽는 것은 한 번뿐이라, 나중에 오면 그
  // 숫자는 대체 글꼴로 구워진 채 남습니다.
  await Promise.all([loadFonts(), loadNumerals()])
  useFont(language)
  // 그림 목록. 없으면 문양으로 갑니다.
  boot.step('art')
  await loadArtIndex('./art')
  // 아이콘 둘. 화면을 세우기 전에 읽습니다 — 그리는 자리에서 읽으면 첫 프레임에 빈 칸이
  // 한 번 보입니다.
  await loadIcons('./icon')

  // 시드는 주소에서 받습니다 — 같은 주소를 열면 같은 판입니다. 대조할 때 그 편이 편합니다.
  const seed = new URLSearchParams(location.search).get('seed') ?? randomSeed()

  // 확장 350종을 켜는 자리입니다. 덱 선택 화면이 생기면 그쪽으로 엮깁니다 — 지금은
  // 시드와 같은 방식이 유지보수가 적은 자리입니다.
  const pools = new URLSearchParams(location.search).get('expansion') === '1'
    ? [JokerPool.Base, JokerPool.Greenhouse]
    : [JokerPool.Base]

  // 검증 도구의 수동 틱. 시간이 `__clover.advance` 로만 흐릅니다.
  const manualTick = new URLSearchParams(location.search).get('tick') === 'manual'

  const game = new Game(app, data, seed, pools, manualTick)

  /**
   * 화면 크기가 바뀔 때 다시 배치합니다.
   *
   * **렌더러가 다시 잰 뒤에 부릅니다.** 창의 `resize` 만 듣고 계산하면 아직 갱신되지 않은
   * 크기를 읽습니다 — 전체 화면을 켜고 끌 때 배치가 어긋나던 것이 그것입니다.
   *
   * **논리 크기를 씁니다.** `renderer.width` 는 픽셀 밀도가 곱해진 물리 크기라, 그것으로 재면
   * 배율이 커져 화면 오른쪽과 아래에 빈 곳이 남습니다.
   */
  const relayout = () => {
    // 전체 화면으로 가면 다른 모니터의 밀도가 될 수 있습니다. 밀도가 바뀌었으면 렌더러의
    // 것도 함께 바꿉니다 — 그러지 않으면 전체 화면에서 글씨가 뿌옇게 됩니다.
    const want = density()
    if (Math.abs(app.renderer.resolution - want) > 0.01) {
      app.renderer.resolution = want
    }
    game.layout(app.renderer.screen.width, app.renderer.screen.height)
  }

  app.renderer.on('resize', relayout)
  relayout()

  // 제대로 섰으니 다시 읽기 셈을 지웁니다. index.html 의 감시가 이것을 봅니다.
  try {
    sessionStorage.removeItem('clover.retry')
  } catch {
    // 저장소가 막힌 브라우저에서는 셀 것이 없습니다.
  }
  // 로딩이 끝났습니다. **타이틀이 이 자리를 이어받습니다.**
  boot.done()
  document.title = `clover — ${seed}`
}

main().catch((error: unknown) => {
  new Boot().fail(`열지 못했습니다: ${String(error)}`)
  console.error(error)
})
