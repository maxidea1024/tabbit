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

/** 밝기 하나를 회색 하나로. 물들이기는 색을 곱하는 것이므로 이것이 그림을 누릅니다. */
function gray(level: number): number {
  const one = Math.max(0, Math.min(255, Math.round(level * 255)))
  return (one << 16) | (one << 8) | one
}

let ready: Texture | undefined

/** 타이틀의 세 큰 판에 들어가는 실제 삽화입니다. 임시 줄무늬나 막대는 두지 않습니다. */
export type TitleCardArt = 'start' | 'collection' | 'leaderboard'
export type RunCardArt = 'new' | 'resume' | 'challenge'

const titleReady = new Map<TitleCardArt, Texture>()
const runReady = new Map<RunCardArt, Texture>()
const TITLE_FILE: Record<TitleCardArt, string> = {
  start: 'title-start.webp',
  collection: 'title-collection.webp',
  leaderboard: 'title-leaderboard.webp',
}
const RUN_FILE: Record<RunCardArt, string> = {
  new: 'run-new.webp',
  resume: 'run-resume.webp',
  challenge: 'run-challenge.webp',
}

/**
 * 배경 그림을 미리 읽습니다.
 *
 * **화면을 세우기 전에 읽습니다.** 그리는 자리에서 읽기 시작하면 첫 프레임에 검은 화면이
 * 한 번 보입니다.
 */
export async function loadSceneArt(base = './ui'): Promise<void> {
  await Promise.all([
    Assets.load<Texture>(`${base}/title-bg.webp`).then(texture => { ready = texture }).catch(() => {
      // 없으면 그림 없이 갑니다. 두 화면은 지금까지의 배경으로 그려집니다.
    }),
    ...Object.entries(TITLE_FILE).map(async ([name, file]) => {
      try {
        titleReady.set(name as TitleCardArt, await Assets.load<Texture>(`${base}/${file}`))
      } catch {
        // 그림이 없는 판에는 대체 도형을 만들지 않습니다. 원화 누락을 그대로 드러냅니다.
      }
    }),
    ...Object.entries(RUN_FILE).map(async ([name, file]) => {
      try {
        runReady.set(name as RunCardArt, await Assets.load<Texture>(`${base}/${file}`))
      } catch {
        // 삽화가 없으면 색 면이나 기호로 둘러대지 않습니다.
      }
    }),
  ])
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
export function sceneArt(dim = 1): Container | undefined {
  if (ready === undefined) return undefined

  const node = new Container()
  const sprite = new Sprite(ready)
  // **그림을 눌러 깝니다.** 로그인 화면은 글이 여섯 줄이고 그림의 밝기가 2에서 246까지를
  // 다 가지므로, 글마다 테두리를 두르는 것으로는 읽히지 않습니다 — 뒤에 사각형을 깔면
  // 그림 위에 판이 하나 놓인 것이 되므로, 그림 자체를 어둡게 합니다.
  // **누르지 않을 때는 손대지 않습니다.** 흰색을 적어 두면 색을 손으로 적은 것이 되고,
  // 게이트가 그것을 잡습니다 — 물들이지 않는 것이 곧 원래 색입니다.
  if (dim < 1) sprite.tint = gray(dim)
  const scale = Math.max(SIZE.width / ready.width, SIZE.height / ready.height)
  sprite.width = ready.width * scale
  sprite.height = ready.height * scale
  sprite.position.set((SIZE.width - sprite.width) / 2, (SIZE.height - sprite.height) / 2)
  node.addChild(sprite)
  return node
}

/**
 * 타이틀의 큰 판을 채우는 삽화입니다.
 *
 * 원화가 그림 자리와 같은 3:2 비율이므로 잘라내는 도형 마스크가 필요 없습니다. 픽셀 한두
 * 줄의 비율 차이만 화면 크기에 맞춥니다. 누락됐을 때 선이나 막대로 대신하지 않습니다.
 */
export function titleCardArt(name: TitleCardArt, width: number, height: number): Sprite | undefined {
  const texture = titleReady.get(name)
  if (texture === undefined) return undefined
  const sprite = new Sprite(texture)
  sprite.width = width
  sprite.height = height
  return sprite
}

/** 런 시작의 세 선택지를 설명하는 삽화입니다. 누락됐을 때 대체 도형을 만들지 않습니다. */
export function runCardArt(name: RunCardArt, width: number, height: number): Sprite | undefined {
  const texture = runReady.get(name)
  if (texture === undefined) return undefined
  const sprite = new Sprite(texture)
  sprite.width = width
  sprite.height = height
  return sprite
}
