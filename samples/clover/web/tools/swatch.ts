// 겉면 여덟을 한 장에 굽습니다.
//
// **게임을 켜지 않고 색만 봅니다.** 판 · 칸 · 선 · 단추의 네 상태 · 강조색을 겉면마다 한 줄로
// 놓으므로, 어느 겉면에서 무엇이 어긋나는지가 나란히 보입니다 — 게임을 켜서 겉면을 갈아
// 끼우며 보면 한 번에 하나씩만 보이고, 그 사이에 눈이 앞의 색을 잊습니다.
//
//     npx tsx tools/swatch.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'

import { shade } from '../src/render/color'
import { UI_THEMES, UI_THEME_KEYS } from '../src/render/theme'
import type { Surface } from '../src/render/palette'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')

const hex = (color: number): string => '#' + color.toString(16).padStart(6, '0')

/** 판 계열의 단추. **바탕에 붙은 채움과 밝은 테입니다.** */
function flat(fill: number, edge: number, label: string, ink: number): string {
  return `<div class="flat" style="background:${hex(fill)};border-color:${hex(edge)};
    color:${hex(ink)}">${label}</div>`
}

/** 뜻이 있는 색의 단추. **테 대신 두께를 가집니다.** */
function solid(fill: number, label: string, ink: number): string {
  return `<div class="btn" style="background:${hex(shade(fill, -0.13))};
    border-color:${hex(shade(fill, -0.24))}">
    <span style="background:linear-gradient(${hex(shade(fill, 0.05))},${hex(fill)});
      border-top:1px solid ${hex(shade(fill, 0.13))};color:${hex(ink)}">${label}</span>
  </div>`
}

function card(name: string, look: Surface): string {
  const dot = (color: number, label: string): string =>
    `<div class="dot"><i style="background:${hex(color)}"></i>${label}</div>`
  return `
<div class="panel" style="background:${hex(look.panel)};border-color:${hex(look.panelEdge)}">
  <div class="head" style="color:${hex(look.ink)}">${name}
    <span style="color:${hex(look.inkDim)}">${hex(look.panel)}</span></div>
  <div class="rule" style="background:${hex(look.rule)}"></div>
  <div class="groove" style="background:${hex(look.groove)}"></div>
  <div class="row">
    <div class="cell" style="background:${hex(look.cell)};border-color:${hex(look.hairline)};
      color:${hex(look.inkDim)}">라운드 점수<b style="color:${hex(look.ink)}">1,200</b></div>
    <div class="cell" style="background:${hex(look.cell)};border-color:${hex(look.hairline)};
      color:${hex(look.inkDim)}">금액<b style="color:${hex(look.money)}">$14</b></div>
  </div>
  <div class="row">
    <div class="well" style="background:${hex(look.well)};border-color:${hex(look.hairline)}">
      <i style="background:${hex(look.bar)}"></i></div>
  </div>
  <div class="row">
    ${flat(look.btn, look.btnEdge, '메뉴', look.ink)}
    ${flat(look.btnHover, look.btnEdgeHover, '가리킴', look.ink)}
    ${flat(look.btnPress, look.btnEdge, '눌림', look.ink)}
    ${flat(look.locked, look.lockedEdge, '잠김', look.inkDim)}
  </div>
  <div class="row">
    ${solid(look.yellow, '낸다', look.onLight)}
    ${solid(look.red, '버린다', look.onLight)}
    ${solid(look.dare, '건너뛴다', look.onLight)}
    ${solid(look.light, '고른 탭', look.onLight)}
  </div>
  <div class="row">
    ${flat(look.quiet, look.quietEdge, '조용한 것', look.ink)}
    ${solid(look.confirm, '그렇게', look.ink)}
    ${solid(look.caution, '지운다', look.ink)}
    <div class="tip" style="background:${hex(look.tipBack)};border-color:${hex(look.tipEdge)};
      color:${hex(look.ink)}">쪽지</div>
  </div>
  <div class="dots">
    ${dot(look.money, '돈')}${dot(look.chips, '칩')}${dot(look.mult, '배수')}
    ${dot(look.bar, '바')}${dot(look.pick, '고름')}${dot(look.green, '승리')}
    ${dot(look.red, '위험')}${dot(look.discard, '버리기')}${dot(look.legendary, '전설')}
  </div>
</div>`
}

const PAGE = `<!doctype html><meta charset="utf-8"><style>
  body { margin: 0; padding: 18px; background: #000; display: grid;
         grid-template-columns: repeat(4, 300px); gap: 18px;
         font: 12px/1.3 'Malgun Gothic', sans-serif; }
  .panel { border: 1.5px solid; border-radius: 8px; padding: 12px; }
  .head { font-size: 14px; font-weight: 800; margin-bottom: 8px;
          display: flex; justify-content: space-between; align-items: baseline; }
  .head span { font-size: 10px; font-weight: 400; }
  .rule { height: 1.5px; margin-bottom: 7px; }
  .groove { height: 1px; margin-bottom: 10px; }
  .row { display: flex; gap: 6px; margin-bottom: 8px; }
  .cell { flex: 1; border: 1px solid; border-radius: 6px; padding: 7px 9px;
          display: flex; justify-content: space-between; font-size: 11px; }
  .well { flex: 1; height: 10px; border: 1px solid; border-radius: 5px; overflow: hidden; }
  .well i { display: block; height: 100%; width: 62%; border-radius: 5px; }
  .btn { flex: 1; border: 1.5px solid; border-radius: 6px; }
  .flat { flex: 1; border: 1.5px solid; border-radius: 6px; padding: 8px 4px;
          text-align: center; font-weight: 800; font-size: 11px; }
  .btn span { display: block; border-radius: 5px; margin-bottom: 4px; padding: 8px 4px;
              text-align: center; font-weight: 800; font-size: 11px; }
  .tip { flex: 1; border: 1.5px solid; border-radius: 8px; padding: 8px 4px;
         text-align: center; font-size: 11px; }
  .dots { display: flex; flex-wrap: wrap; gap: 7px; margin-top: 10px; }
  .dot { display: flex; align-items: center; gap: 4px; font-size: 10px; color: #8a8a8a; }
  .dot i { width: 11px; height: 11px; border-radius: 3px; display: block; }
</style>
${UI_THEME_KEYS.map(key => card(key, UI_THEMES[key])).join('')}`

async function main(): Promise<void> {
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1290, height: 1200 } })
  await page.setContent(PAGE)
  await page.screenshot({ path: path.join(OUT, 'swatch.png'), fullPage: true })
  await browser.close()
  console.log('swatch.png')
}

main()
