// 진입점.
//
// 데이터를 읽고 화면을 세우는 것이 전부입니다. **규칙은 `core/` 에 있고 이 파일은 그것을
// 모릅니다.**

import { Application } from 'pixi.js'

import { loadFromUrl } from './core/load'
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

  // 시드는 주소에서 받습니다 — 같은 주소를 열면 같은 판입니다. 대조할 때 그 편이 편합니다.
  const seed = new URLSearchParams(location.search).get('seed')
    ?? `CLOVER-${Math.floor(Math.random() * 1e6).toString().padStart(6, '0')}`

  const game = new Game(app, data, seed)
  // **논리 크기를 씁니다.** `renderer.width` 는 픽셀 밀도가 곱해진 물리 크기라,
  // 그것으로 재면 배율이 커져 화면 오른쪽과 아래에 빈 곳이 남습니다.
  const relayout = () => game.layout(app.renderer.screen.width, app.renderer.screen.height)
  relayout()
  window.addEventListener('resize', relayout)

  boot?.classList.add('gone')
  document.title = `clover — ${seed}`
}

main().catch((error: unknown) => {
  const boot = document.getElementById('boot')
  if (boot) boot.textContent = `열지 못했습니다: ${String(error)}`
  console.error(error)
})
