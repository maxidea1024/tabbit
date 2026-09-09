// 타이틀과 로그인 화면의 배경 그림.
//
// **두 화면이 같은 그림 한 장을 씁니다.** 전에는 타이틀이 자기가 어둡게 낮춘 무늬를 깔고
// 로그인은 씬 밖의 셰이더 판을 썼습니다 — 두 자리가 갈라져 있어 브랜드 그림을 고칠 때마다
// 두 곳을 고치게 됩니다.
//
// **판 밖의 셰이더는 건드리지 않습니다.** 그것은 판이 도는 동안 쓰는 것이므로, 이 그림은
// 두 씬의 통 맨 아래에 깔아 그 위를 덮습니다.
//
// 그림의 규격은 [리브랜딩 계획](../../../../notes/hypephoria-rebrand-plan.md) 의 「배경
// 그림의 규격」에 있습니다 — 왼쪽 위는 로고 자리로 비어 있고, 오른쪽 3분의 1은 단추가
// 세로로 내려오는 자리이므로 넓고 어둡습니다.

import { Assets, Container, Sprite, Texture } from 'pixi.js'

import { SIZE } from '../render/theme'

let ready: Texture | undefined

/**
 * 배경 그림을 미리 읽습니다.
 *
 * **화면을 세우기 전에 읽습니다.** 그리는 자리에서 읽기 시작하면 첫 프레임에 검은 화면이
 * 한 번 보입니다.
 */
export async function loadSceneArt(base = './ui'): Promise<void> {
  try {
    ready = await Assets.load<Texture>(`${base}/title-bg.webp`)
  } catch {
    // 없으면 그림 없이 갑니다. 두 화면은 지금까지의 배경으로 그려집니다.
  }
}

export function sceneArtReady(): boolean {
  return ready !== undefined
}

/**
 * 화면을 덮는 배경 스프라이트. 없으면 `undefined` 입니다.
 *
 * **넓이에 맞추고 남는 세로를 가운데에서 자릅니다.** 그림이 화면과 같은 비율(16 대 10)로
 * 구워져 있으므로 실제로는 잘리지 않지만, 화면 크기가 바뀌어도 여백이 생기지 않습니다.
 */
export function sceneArt(): Container | undefined {
  if (ready === undefined) return undefined

  const node = new Container()
  const sprite = new Sprite(ready)
  const scale = Math.max(SIZE.width / ready.width, SIZE.height / ready.height)
  sprite.width = ready.width * scale
  sprite.height = ready.height * scale
  sprite.position.set((SIZE.width - sprite.width) / 2, (SIZE.height - sprite.height) / 2)
  node.addChild(sprite)
  return node
}
