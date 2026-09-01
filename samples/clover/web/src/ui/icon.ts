// 아이콘.
//
// **가져온 것입니다.** Lucide 의 SVG 이고 어디서 왔는지는
// [`public/icon/readme.md`](../../public/icon/readme.md) 에 있습니다 — 물음표와 톱니를
// 직접 그려 보았는데, 톱니가 해처럼 보였습니다. 이런 것은 이미 잘 그려진 것이 있습니다.
//
// **흰색으로 구워 두고 화면에서 물을 들입니다.** 색을 곱하는 것이므로 흰 것에만 제대로
// 걸립니다.

import { Assets, Texture } from 'pixi.js'

/** 쓰는 아이콘들. 파일 이름은 Lucide 의 이름 그대로입니다. */
export type IconName = 'circle-question-mark' | 'settings'

const NAMES: IconName[] = ['circle-question-mark', 'settings']

const ready = new Map<IconName, Texture>()

/**
 * 아이콘을 미리 읽습니다.
 *
 * **화면을 세우기 전에 읽습니다.** 그리는 자리에서 읽기 시작하면 첫 프레임에는 없어서
 * 빈 칸이 한 번 보입니다 — 두 파일이고 합쳐서 1KB 남짓입니다.
 */
export async function loadIcons(base = './icon'): Promise<void> {
  await Promise.all(NAMES.map(async name => {
    try {
      // **크게 구웁니다.** SVG 는 굽는 크기가 곧 해상도이고, 24픽셀로 구우면 58픽셀 자리에
      // 놓았을 때 뭉갭니다.
      const texture = await Assets.load<Texture>({
        src: `${base}/${name}.svg`,
        data: { resolution: 6 },
      })
      ready.set(name, texture)
    } catch {
      // 없으면 아이콘 없이 갑니다. 버튼은 그대로 눌립니다.
    }
  }))
}

export function iconFor(name: IconName): Texture | undefined {
  return ready.get(name)
}
