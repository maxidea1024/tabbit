// 진입점.
//
// 데이터를 읽고 화면을 세우는 것이 전부입니다. **규칙은 `core/` 에 있고 이 파일은 그것을
// 모릅니다.**

import { Application } from 'pixi.js'

import { loadFromUrl } from './core/load'
import { setLanguage, useStrings } from './core/strings'
import { loadFonts, useFont } from './ui/font'
import { chosen, loadOptions } from './ui/options'
import { loadArtIndex } from './render/art'
import { Game } from './render/game'
import { COLOR } from './render/theme'

async function main(): Promise<void> {
  const canvas = document.getElementById('stage') as HTMLCanvasElement
  const boot = document.getElementById('boot')

  const app = new Application()
  await app.init({
    canvas,
    background: COLOR.ground,
    antialias: true,
    // 글씨가 뿌옇지 않게 화면의 픽셀 밀도를 그대로 씁니다.
    resolution: Math.min(3, window.devicePixelRatio || 1),
    autoDensity: true,
    resizeTo: window,
    preference: 'webgl',
  })

  const data = await loadFromUrl('./data')
  // **화면을 만들기 전에 글 표를 넘깁니다.** 클래스의 필드는 생성자 본문보다 먼저 만들어지고,
  // 그 자리에서 이미 글을 읽습니다 — 생성자 안에서 넘기면 그것들이 열쇠를 그대로 답니다.
  useStrings(data)
  const language = chosen(loadOptions())
  setLanguage(language)
  // **글꼴을 다 읽고 나서 화면을 세웁니다.** 글을 그리는 것은 글자를 그림으로 굽는 일이고,
  // 그때 글꼴이 없으면 대체 글꼴로 구워져 그대로 남습니다.
  await loadFonts()
  useFont(language)
  // 그림 목록. 없으면 문양으로 갑니다.
  await loadArtIndex('./art')

  // 시드는 주소에서 받습니다 — 같은 주소를 열면 같은 판입니다. 대조할 때 그 편이 편합니다.
  const seed = new URLSearchParams(location.search).get('seed')
    ?? `CLOVER-${Math.floor(Math.random() * 1e6).toString().padStart(6, '0')}`

  const game = new Game(app, data, seed)

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
    const density = Math.min(3, window.devicePixelRatio || 1)
    if (Math.abs(app.renderer.resolution - density) > 0.01) {
      app.renderer.resolution = density
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
  boot?.classList.add('gone')
  document.title = `clover — ${seed}`
}

main().catch((error: unknown) => {
  const boot = document.getElementById('boot')
  if (boot) boot.textContent = `열지 못했습니다: ${String(error)}`
  console.error(error)
})
