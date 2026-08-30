// 카드의 모양.
//
// **카드는 모서리가 둥근데 필터는 사각형입니다.** 필터가 도는 자리는 그 물체를 감싸는
// 사각형이고, 셰이더가 그 사각형을 전부 칠하면 둥근 모서리 밖까지 칠해집니다 — 홀로그래픽과
// 네거티브가 실제로 그렇게 보였습니다.
//
// 그래서 **모양을 그림 한 장으로 만들어 셰이더에 넘깁니다.** 셰이더는 그 그림의 알파를
// 곱하므로, 모서리가 둥글면 둥근 대로 칠해집니다.
//
// 같은 크기의 카드가 수십 장이므로 크기마다 한 장만 만들어 두고 나눠 씁니다.

import { Texture } from 'pixi.js'

const cache = new Map<string, Texture>()

/**
 * 모서리가 둥근 사각형 한 장.
 *
 * **카드보다 촘촘하게 굽습니다** — 같은 크기로 구우면 모서리가 계단으로 보이고, 화면 배율이
 * 1보다 크면 그 계단이 그대로 커집니다.
 */
export function roundedMask(width: number, height: number, radius: number,
                            density = 3): Texture {
  const key = `${width}x${height}r${radius}@${density}`
  const found = cache.get(key)
  if (found) return found

  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, Math.round(width * density))
  canvas.height = Math.max(1, Math.round(height * density))

  const ctx = canvas.getContext('2d')
  if (!ctx) return Texture.WHITE

  ctx.fillStyle = '#ffffff'
  ctx.beginPath()
  ctx.roundRect(0, 0, canvas.width, canvas.height, radius * density)
  ctx.fill()

  const texture = Texture.from(canvas)
  cache.set(key, texture)
  return texture
}
