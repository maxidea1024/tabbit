// 카드의 뒷면.
//
// **뒷면은 바탕색 하나가 아닙니다.** 몇백 년째 같은 모양이 있습니다 — 크림 바탕에 한 가지
// 색으로 그린 선화이고, 굵은 테두리 · 점선 띠 · 가는 테두리가 겹겹이 두르고, 가운데에는
// 동심원 여럿이 겹쳐 만드는 상하좌우 대칭 문양이 있습니다. 그 겹침이 곧 「이것은 트럼프
// 뒷면이다」이고, 색만 칠한 사각형은 아무리 예뻐도 그렇게 보이지 않습니다.
//
// 그림 파일이 아닌 이유는 앞면과 같습니다 — 크기가 여럿(손패 · 덱 더미 · 남은 카드 보기)이고,
// 선화라 어느 크기에서도 다시 그리는 편이 낫습니다.

import { Graphics } from 'pixi.js'

/** 뒷면의 두 색. **둘뿐입니다** — 셋째 색이 들어가면 선화가 그림이 됩니다. */
export interface BackLook {
  ground: number
  ink: number
}

/**
 * 뒷면 하나를 그립니다. `(0, 0)` 이 왼쪽 위입니다.
 *
 * 안쪽 문양은 **원 가족 셋이 겹쳐** 만듭니다. 가운데 하나와 위아래 하나씩이고, 그 셋이
 * 서로를 지나가며 생기는 무늬가 렌즈 모양과 꽃잎이 됩니다 — 그 모양을 하나씩 그리려 하면
 * 좌표가 수십 개가 되고, 크기가 바뀌면 전부 어긋납니다.
 */
export function drawCardBack(g: Graphics, width: number, height: number,
                             radius: number, look: BackLook): void {
  const cx = width / 2
  const cy = height / 2

  // 1. 크림 바탕.
  g.roundRect(0, 0, width, height, radius).fill(look.ground)

  // 2. 굵은 바깥 테두리.
  const edge = Math.max(2, Math.round(width * 0.045))
  g.roundRect(edge / 2, edge / 2, width - edge, height - edge, radius - edge / 2)
    .stroke({ color: look.ink, width: edge })

  // 3. 점선 띠. **작은 네모가 줄지어 있는 것이 이 뒷면의 표식입니다.**
  const bandInset = edge * 1.9
  const dot = Math.max(1, Math.round(width * 0.018))
  const step = dot * 2.6
  const bandL = bandInset
  const bandR = width - bandInset
  const bandT = bandInset
  const bandB = height - bandInset

  for (let x = bandL; x <= bandR - dot; x += step) {
    g.rect(x, bandT, dot, dot).fill(look.ink)
    g.rect(x, bandB - dot, dot, dot).fill(look.ink)
  }
  for (let y = bandT + step; y <= bandB - dot - step; y += step) {
    g.rect(bandL, y, dot, dot).fill(look.ink)
    g.rect(bandR - dot, y, dot, dot).fill(look.ink)
  }

  // 4. 가는 안쪽 테두리와 네 변 가운데의 표식.
  const panel = bandInset + dot * 2.4
  const panelW = width - panel * 2
  const panelH = height - panel * 2
  const hair = Math.max(1, Math.round(width * 0.013))
  g.rect(panel, panel, panelW, panelH).stroke({ color: look.ink, width: hair })

  const mark = dot * 1.8
  g.rect(cx - mark / 2, panel - mark / 2, mark, mark).fill(look.ground)
  g.rect(cx - mark / 2, panel - mark / 2, mark, mark).stroke({ color: look.ink, width: hair })
  g.rect(cx - mark / 2, panel + panelH - mark / 2, mark, mark).fill(look.ground)
  g.rect(cx - mark / 2, panel + panelH - mark / 2, mark, mark)
    .stroke({ color: look.ink, width: hair })
  g.rect(panel - mark / 2, cy - mark / 2, mark, mark).fill(look.ground)
  g.rect(panel - mark / 2, cy - mark / 2, mark, mark).stroke({ color: look.ink, width: hair })
  g.rect(panel + panelW - mark / 2, cy - mark / 2, mark, mark).fill(look.ground)
  g.rect(panel + panelW - mark / 2, cy - mark / 2, mark, mark)
    .stroke({ color: look.ink, width: hair })

  // 5. 가운데 문양. 원 가족 둘을 안쪽 판에 가둡니다.
  //
  // **셋이면 뭉칩니다.** 88 × 124 에서 원 셋이 겹치면 가운데가 붉게 메워져 무늬가 아니라
  // 얼룩으로 보입니다 — 위아래 하나씩이면 가운데에 렌즈 하나가 서고 그것이 문양이 됩니다.
  const field = new Graphics()
  const ring = Math.max(6, width * 0.098)
  const reach = Math.hypot(panelW, panelH) / 2
  const offset = panelH * 0.3

  for (const center of [cy - offset, cy + offset]) {
    for (let r = ring; r <= reach; r += ring) {
      field.circle(cx, center, r).stroke({ color: look.ink, width: hair })
    }
  }

  // 위아래 끝의 마름모. **문양에 시작과 끝이 있어야** 겹친 원이 무늬로 읽힙니다.
  for (const center of [panel + panelH * 0.13, panel + panelH * 0.87]) {
    const size = panelW * 0.15
    field.moveTo(cx, center - size).lineTo(cx + size, center)
      .lineTo(cx, center + size).lineTo(cx - size, center).closePath()
      .fill(look.ground)
    field.moveTo(cx, center - size).lineTo(cx + size, center)
      .lineTo(cx, center + size).lineTo(cx - size, center).closePath()
      .stroke({ color: look.ink, width: hair })
  }

  const clip = new Graphics()
  clip.rect(panel + hair, panel + hair, panelW - hair * 2, panelH - hair * 2).fill(0xffffff)
  field.mask = clip
  g.addChild(clip, field)
}

/**
 * 그려 둔 것을 치웁니다.
 *
 * `Graphics.clear()` 는 그린 선만 지웁니다 — 문양을 담은 통은 자식이라 그대로 남고, 다시
 * 그릴 때마다 한 겹씩 쌓입니다.
 */
export function clearCardBack(g: Graphics): void {
  g.removeChildren().forEach(child => child.destroy())
}
