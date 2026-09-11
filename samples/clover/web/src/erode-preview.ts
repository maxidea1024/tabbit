// 판이 모래로 삭는 것을 눈으로 보는 자리.
//
// **판에서는 0.85초에 지나갑니다.** 소모품을 한 장 쓰면 그 한 번뿐이고, 다시 보려면 판을
// 한 판 더 돌려야 합니다 — 소멸선의 톱니가 굵은지 · 칸이 꺼지는 프레임과 알갱이가 뜨는
// 프레임이 같은지 · 알갱이가 아래로 흩어지는지는 그 0.85초를 세워 두고 봐야 갈립니다.
//
//     npm run dev   →  /erode.html
//
// 스페이스로 멈추고, 좌우 화살표로 0.02초씩 끕니다. R 로 처음부터.

import { Application, Container, Graphics, Text } from 'pixi.js'

import { SuitKind } from './generated/enums/suit-kind'
import { COLOR } from './render/ink'
import { type MotesHandle, MotesLayer, startMotes, useMotesLayer } from './render/motes-layer'
import { drawSuit } from './render/pips'
import { SIZE, UI } from './render/theme'
import { ERODE_SWEEP, ErodeFilter } from './shader/erode'
import { MOTES_HOLD } from './shader/motes'
import { loadNoise } from './shader/noise'

/** 한 걸음. 멈춘 채로 끌 때의 단위입니다. */
const STEP = 0.02

/** 판 하나. 삭는 것을 스스로 돌립니다. */
class Sample {
  readonly node = new Container()
  private readonly filter = new ErodeFilter()
  private motes?: MotesHandle
  /**
   * 이미 시작했는가.
   *
   * **판을 굽는 것과 겹의 시계가 같은 프레임에 시작해야 합니다.** 겹은 제 나이를 스스로
   * 세므로, 먼저 굽고 나중에 삭기 시작하면 알갱이가 판보다 그만큼 앞서 뜹니다.
   */
  private started = false
  age: number

  constructor(readonly width: number, readonly height: number, private readonly offset: number,
              label: string, x: number, y: number) {
    this.age = -offset
    const paper = new Graphics()
    paper.roundRect(0, 0, width, height, 8).fill({ color: 0xf3ead9 })
    paper.roundRect(2.5, 2.5, width - 5, height - 5, 6).stroke({ color: 0x2b2f38, width: 2, alpha: 0.5 })
    this.node.addChild(paper)

    // 글과 무늬를 얹습니다. **알갱이는 제 칸의 색을 들고 가므로** 판이 한 색이면 흩어지는
    // 것이 한 색으로만 보이고, 그것으로는 색을 제대로 들고 가는지가 갈리지 않습니다.
    const pip = new Graphics()
    drawSuit(pip, SuitKind.Heart, width / 2, height * 0.44, Math.min(width, height) * 0.34, 0xd7343f)
    const rank = new Text({
      text: label,
      style: { fontFamily: 'serif', fontSize: 22, fill: 0x1f2024, fontWeight: 'bold' },
    })
    rank.position.set(6, 4)
    const foot = new Graphics()
    foot.rect(0, height - 16, width, 16).fill({ color: 0x2f6fc0, alpha: 0.85 })
    this.node.addChild(pip, foot, rank)
    this.node.position.set(x, y)
    this.node.filters = [this.filter]
    this.filter.fit(width, height)
  }

  advance(seconds: number): void {
    this.age += seconds
    if (this.age >= MOTES_HOLD + 0.4) {
      this.restart()
      return
    }
    if (this.age < 0) {
      this.filter.erode = 0
      return
    }
    if (!this.started) {
      this.started = true
      // **굽는 것이 거는 것보다 먼저입니다.** 뒤에 구우면 삭기 시작한 판이 구워집니다.
      this.node.filters = []
      this.motes = startMotes(this.node, this.width, this.height)
      this.node.filters = [this.filter]
    }
    this.filter.erode = this.age / ERODE_SWEEP
    this.motes?.place(this.node)
  }

  /** 처음부터. 제 차례가 오는 프레임에 다시 굽습니다. */
  restart(): void {
    this.age = -this.offset
    this.started = false
    this.motes = undefined
    this.filter.erode = 0
    this.node.filters = [this.filter]
  }
}

async function main(): Promise<void> {
  const canvas = document.getElementById('stage') as HTMLCanvasElement
  const app = new Application()
  await app.init({
    canvas,
    background: COLOR.crop,
    antialias: true,
    resolution: Math.min(2, window.devicePixelRatio || 1),
    autoDensity: true,
    resizeTo: window,
    preference: 'webgl',
  })
  // **그림이 먼저입니다.** 필터는 만들어질 때 노이즈 그림을 잡으므로, 그 뒤에 도착한
  // 그림은 아무 필터에도 들어가지 않습니다.
  await loadNoise('./noise')

  const plates = new Container()
  const layer = new MotesLayer()
  // 알갱이가 판 위에 그려집니다.
  app.stage.addChild(plates, layer)
  useMotesLayer(layer, app.renderer, 2)

  const w = SIZE.cardWidth
  const h = SIZE.cardHeight
  const samples = [
    new Sample(w, h, 0, 'A', 90, 150),
    new Sample(w, h, 0.22, 'K', 90 + w + 60, 150),
    new Sample(w, h, 0.44, 'Q', 90 + (w + 60) * 2, 150),
    new Sample(SIZE.jokerWidth, SIZE.jokerHeight, 0.1, 'J', 90, 150 + h + 90),
    new Sample(w * 2, h * 2, 0.3, '2', 90 + SIZE.jokerWidth + 80, 150 + h + 60),
  ]
  for (const one of samples) {
    plates.addChild(one.node)
    one.restart()
  }

  const readout = new Text({
    text: '',
    style: { fontFamily: 'monospace', fontSize: 13, fill: UI.ink },
  })
  readout.position.set(14, 12)
  app.stage.addChild(readout)

  let paused = false
  window.addEventListener('keydown', event => {
    if (event.code === 'Space') {
      paused = !paused
      event.preventDefault()
      return
    }
    if (event.code === 'KeyR') {
      for (const one of samples) one.restart()
      return
    }
    const step = event.code === 'ArrowRight' ? STEP : event.code === 'ArrowLeft' ? -STEP : 0
    if (step === 0) return
    paused = true
    for (const one of samples) one.advance(step)
    layer.advance(step)
  })

  app.ticker.add(ticker => {
    const seconds = paused ? 0 : Math.min(0.05, ticker.deltaMS / 1000)
    if (seconds > 0) {
      for (const one of samples) one.advance(seconds)
      layer.advance(seconds)
    }
    const first = samples[0]
    readout.text = [
      `age ${first.age.toFixed(2)}s`,
      `erode ${Math.max(0, Math.min(1, first.age / ERODE_SWEEP)).toFixed(2)}`,
      `sweep ${ERODE_SWEEP}s · hold ${MOTES_HOLD.toFixed(2)}s`,
      paused ? '멈춤 — 화살표로 0.02초씩' : '스페이스로 멈춤 · R 로 처음부터',
    ].join('   ')
  })
}

void main()
