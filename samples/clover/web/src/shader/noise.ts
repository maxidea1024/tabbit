// 셰이더가 읽는 노이즈 그림.
//
// **셰이더 안에서 노이즈를 만들지 않습니다.** 격자 노이즈 한 번이 해시 넷이고, 층 셋을
// 겹치면 열둘입니다 — 그것을 픽셀마다, 재의 셰이더에서는 걸음마다 부르고 있었습니다. 그림
// 한 번 읽는 것이 그보다 훨씬 싸고, 미리 만든 무늬는 해시로 만든 것보다 결이 좋습니다.
//
// **그림은 가져온 것입니다.** 어디서 왔고 무엇인지는 `public/noise/readme.md` 에 있습니다.
// 생성하지 않습니다.
//
// **되풀이해 읽습니다.** 그림은 전부 이어 붙여도 이음이 없는 것이고, 셰이더는 좌표에 배율을
// 곱해 읽으므로 한 장을 여러 배율로 읽으면 여러 장으로 보입니다 — 핸드폰이 석 장으로 재의
// 셰이더를 도는 것이 그 방법입니다.
//
// **`grain` 은 칸마다 한 값으로도 읽습니다.** 픽셀마다 독립인 그림이므로 텍셀 하나가 곧
// 알갱이 하나이고, 좌표를 텍셀의 가운데에 맞춰 읽으면 그 칸 안에서 한 값이 나옵니다 —
// 셰이더의 `speckAt` 이 그것입니다. 그래서 **몇 개의 텍셀인지를 셰이더가 알아야 합니다.**

import { Assets, Texture } from 'pixi.js'

import { coarsePointer } from './device'

/**
 * 그림의 자리.
 *
 * |이름|무엇|
 * |--|--|
 * |`soft`|낮은 대비의 부드러운 결. 배경의 프랙탈과 재의 연기|
 * |`large`|대비가 큰 큰 얼룩. **부서지는 앞이 선으로 보이지 않게 하는 것**|
 * |`grain`|모래알. 픽셀마다 독립인 잔 결에 큰 뭉침이 겹쳐 있습니다 — 알갱이·조각·구멍이 다 이것입니다|
 * |`crack`|금. 가는 어두운 선이고 칸이 30픽셀쯤입니다|
 * |`flow`|바람. 기울기를 90도 돌려 흐름으로 씁니다 — **매끄러워야 기울기가 매끄럽습니다**|
 */
export type NoiseName = 'soft' | 'large' | 'grain' | 'crack' | 'flow'

/**
 * 파일과, 값을 아끼는 기계가 읽을 작은 판.
 *
 * **작은 판이 없는 것은 그 기계의 셰이더가 쓰지 않는 것입니다.** 금과 바람은 데스크탑의
 * 셰이더에만 있습니다.
 */
const FILES: Record<NoiseName, { full: string; small: string | null }> = {
  soft: { full: 'perlin-21.png', small: 'perlin-21-256.png' },
  large: { full: 'super-perlin-12.png', small: 'super-perlin-12-256.png' },
  grain: { full: 'grainy-1.png', small: 'grainy-1-256.png' },
  crack: { full: 'cracks-9.png', small: null },
  flow: { full: 'swirl-11.png', small: null },
}

const ready = new Map<NoiseName, Texture>()

/**
 * 노이즈 그림을 미리 읽습니다.
 *
 * **화면을 세우기 전에 읽습니다.** 필터는 만들어질 때 그림을 잡으므로, 그 뒤에 도착한 그림은
 * 아무 필터에도 들어가지 않습니다. 다섯을 합쳐 495KB 입니다.
 *
 * **핸드폰은 석 장만 읽습니다.** 그쪽 셰이더가 쓰는 것이 부드러운 결·큰 얼룩·모래알이고
 * 256판입니다 — 그 화면에서 512판과 구분되지 않고, 읽는 것도 GPU 에 올리는 것도 4분의
 * 1입니다(98KB).
 *
 * **읽지 못하면 흰 그림입니다.** 필터는 그대로 만들어지고 화면은 갈립니다 — 무늬가 없는
 * 채로 갈릴 뿐입니다.
 */
export async function loadNoise(base = './noise', lite = coarsePointer()): Promise<void> {
  const names = (Object.keys(FILES) as NoiseName[])
    .filter(name => !lite || FILES[name].small !== null)
  await Promise.all(names.map(async name => {
    const file = lite ? FILES[name].small ?? FILES[name].full : FILES[name].full
    try {
      const texture = await Assets.load<Texture>({
        src: `${base}/${file}`,
        data: {
          // **이어 붙여 읽습니다.** 셰이더가 좌표에 배율을 곱하므로 0..1 밖을 읽습니다.
          addressMode: 'repeat',
          // **밉맵을 만듭니다.** 잔 결을 큰 배율로 읽으면 픽셀보다 잔 무늬가 되어 프레임마다
          // 반짝이고, 밉맵이 그 자리에서 알맞은 크기의 것을 냅니다. 칸마다 한 값으로 읽는
          // 자리는 셰이더가 `textureLod` 로 0층을 짚습니다 — 그러지 않으면 칸의 경계에서
          // 기울기가 커져 GPU 가 흐린 층을 골라 칸 안이 한 값이 아니게 됩니다.
          autoGenerateMipmaps: true,
          scaleMode: 'linear',
        },
      })
      ready.set(name, texture)
    } catch {
      // 없으면 흰 그림으로 갑니다.
    }
  }))
}

/** 이 자리의 그림. 읽지 못했으면 흰 그림입니다. */
export function noise(name: NoiseName): Texture {
  return ready.get(name) ?? Texture.WHITE
}

/**
 * `grain` 이 몇 개의 텍셀인가. **칸마다 한 값으로 읽는 자리가 이 수를 씁니다.**
 *
 * 읽지 못했으면 512 입니다 — 흰 그림에서는 어느 수를 넣어도 값이 같습니다.
 */
export function grainTexels(): number {
  const source = ready.get('grain')?.source
  return source ? source.pixelWidth : 512
}

/**
 * 필터의 `resources` 에 넣는 꼴로.
 *
 * Pixi 는 `uX` 이름의 그림과 `uXSampler` 이름의 읽는 법을 짝으로 찾습니다 —
 * `editions.ts` 의 `uShape` 가 같은 꼴입니다.
 */
export function noiseResources(bind: Partial<Record<string, NoiseName>>): Record<string, unknown> {
  const out: Record<string, unknown> = {}
  for (const [uniform, name] of Object.entries(bind)) {
    if (!name) continue
    const texture = noise(name)
    out[uniform] = texture.source
    out[`${uniform}Sampler`] = texture.source.style
  }
  return out
}
