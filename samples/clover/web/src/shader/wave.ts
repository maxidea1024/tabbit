// 칩과 배수의 바탕에 흐르는 파형.
//
// **음악 플레이어의 비주얼과 같은 그로우 라인입니다.** 선 하나를 그리는 것이 아니라 가는 흰
// 심과 그 바깥의 넓은 색 번짐 두 겹이고, 그 두 겹이 겹쳐야 winamp·sonic 의 모습이 됩니다 —
// 굵기를 늘린 선을 여러 겹 쌓는 방법으로는 밝기의 falloff 가 단으로 끊깁니다.
//
// **세기는 두 가지가 더해진 것입니다.**
//
// |성분|어디서|어떻게 변하는가|
// |--|--|--|
// |얹히는 것|값이 더해진 순간|더해질 때마다 얹히고 잦아듭니다. 연달아 더해지면 그만큼 쌓입니다|
// |바닥|지금의 배당|잦아들지 않습니다. 배당이 크면 계속 요동칩니다|
//
// 값이 줄어드는 동안에는 아무것도 하지 않습니다. 그 판정은 칸이 하고(`Slot.surge`) 여기는
// 받은 값으로 그림만 냅니다.
//
// **상자 둘을 사각형 하나로 그립니다.** 둘은 `splitX(block, [1, 사이, 1])` 로 나온 대칭이므로
// 가운데에서 접으면 한 번의 셈으로 둘이 나옵니다 — 접으면 오른쪽 상자의 가로 좌표가 오른쪽에서
// 왼쪽으로 자라므로, **방향이 하나인 식이 칩에서는 왼쪽으로 배수에서는 오른쪽으로 흐릅니다.**
// 방향 유니폼이 없습니다. 대가는 배수 쪽 무늬가 칩 쪽의 좌우 뒤집힌 것이라는 점이고, 두 칸의
// 세기가 다르므로 같은 모습이 되는 것은 둘이 같은 세기일 때뿐입니다.
//
// **필터가 아니라 메시입니다.** 필터 하나가 프레임마다 렌더 타깃 하나이고, 타일 기반 GPU 에서
// 타깃 전환은 픽셀 수에 비례하지 않는 값입니다. 둥근 모서리도 마스크가 아니라 SDF 로 자릅니다 —
// 마스크 그림도 스텐실도 없습니다.

import { Geometry, Mesh, Shader } from 'pixi.js'

import { coarsePointer } from './device'
import { noiseResources } from './noise'

/**
 * 배당의 크기를 0..1 로.
 *
 * **`euphoria` 의 사다리를 그대로 씁니다.** 그쪽이 `chips × mult / 10,000` 을 40 · 400 ·
 * 4,000 · 40,000 의 네 단으로 세므로, 이 식은 첫 단에서 0 이고 마지막 단에서 1 입니다 —
 * 상수를 새로 정하지 않으면 **파형이 요동치기 시작하는 배당과 배경에 기가 모이기 시작하는
 * 배당이 같은 값**입니다.
 */
/**
 * 수가 생겨 나타나는 데와 0 이 되어 사라지는 데 걸리는 시간.
 *
 * **나타나는 것이 더 빠릅니다.** 나타나는 순간은 곧 첫 수가 더해지는 순간이므로 밝아지는
 * 겹과 함께 와야 하고, 사라지는 것은 판이 끝나 정리되는 대목이라 조금 여유가 있습니다.
 */
const LIVE_RISE_MS = 90
const LIVE_FALL_MS = 200

export function payoutLevel(chips: number, mult: number): number {
  const product = chips * mult / 10_000
  if (product <= 40) return 0
  return Math.min(1, Math.log10(product / 40) / 3)
}

const VERTEX = `#version 300 es
precision highp float;

in vec2 aPosition;

uniform mat3 uProjectionMatrix;
uniform mat3 uWorldTransformMatrix;
uniform mat3 uTransformMatrix;
/** 이 층이 덮는 사각형. 판의 픽셀입니다 — 상자 둘과 그 사이를 합친 것입니다 */
uniform vec2 uBlock;

out vec2 vPx;

void main(void) {
  vPx = aPosition * uBlock;
  gl_Position = vec4((uProjectionMatrix * uWorldTransformMatrix * uTransformMatrix
                      * vec3(vPx, 1.0)).xy, 0.0, 1.0);
}
`

const FRAGMENT = `#version 300 es
precision highp float;

in vec2 vPx;
out vec4 finalColor;

uniform vec2  uBlock;    // 층이 덮는 사각형
uniform vec2  uBox;      // 상자 하나
uniform float uHalf;     // 접는 자리. 층의 절반입니다
uniform float uRadius;   // 상자의 모서리
uniform vec2  uSurge;    // 두 칸의 세기. 얹히는 것 + 바닥입니다. x 가 칩, y 가 배수입니다
uniform vec2  uPulse;    // 얹히는 것만. **상자 전체가 밝아지는 겹이 이것을 봅니다**
uniform vec2  uAlive;    // 칸마다 0..1. 수가 없으면 0 으로 갑니다
uniform float uLevel;    // 배당. **주파수와 진폭이 이것을 따릅니다**
uniform float uPhase;    // 파형의 위상. **시간이 아니라 위상을 누적한 값입니다**
uniform float uDrift;    // 노이즈를 읽는 자리
uniform float uJag;      // 잔 떨림을 얹는가. 0 이면 없습니다
uniform vec3  uChips;
uniform vec3  uMult;

uniform sampler2D uSoft;

const float TAU = 6.28318530718;

void main(void) {
  // **접습니다.** 이 자리가 오른쪽 상자이면 가로 좌표를 뒤집어 왼쪽 상자의 자로 셈합니다 —
  // 그러면 아래의 식 하나가 두 상자에서 서로 반대로 흐릅니다.
  bool right = vPx.x >= uHalf;
  vec2 local = vec2(right ? uBlock.x - vPx.x : vPx.x, vPx.y);
  float surge = right ? uSurge.y : uSurge.x;
  float pulse = right ? uPulse.y : uPulse.x;
  float alive = right ? uAlive.y : uAlive.x;
  vec3 ink = right ? uMult : uChips;

  float u = local.x / uBox.x;

  // **배당이 크면 파형이 촘촘해집니다.** 빠르기만 올리면 같은 파형이 빨리 지나가는 것이고,
  // 주기가 함께 촘촘해져야 요동치는 것으로 보입니다 — 한 자리를 스쳐 가는 주파수는
  // uPhase 의 오르는 빠르기가 정하고, 이 값은 한 화면에 보이는 산의 수를 정합니다.
  float freq = 1.0 + uLevel * 1.6;

  // 파형. **주기와 빠르기가 서로 나누어떨어지지 않습니다** — 그래야 되풀이가 보이지 않습니다.
  float w = sin(u * TAU * 1.0 * freq + uPhase * 1.00)
          + sin(u * TAU * 2.3 * freq - uPhase * 0.68 + 1.7) * 0.647
          + sin(u * TAU * 4.7 * freq + uPhase * 1.42 + 4.1) * 0.324;
  w /= 1.971;

  // 노이즈. **값이 오를 때만 얹힙니다.**
  //
  // n 과 jag 의 곱이 요점입니다 — 잔 떨림의 진폭을 노이즈가 정하므로 거친 자리가 가로로
  // 뭉쳐서 옵니다. jag 만 얹으면 상자 전체에 같은 굵기의 빗살이 서고, 그것은 파형이 아니라
  // 무늬로 보입니다.
  float n = texture(uSoft, vec2(u * 2.6 - uDrift, uDrift * 0.31)).r - 0.5;
  float jag = sin(u * TAU * 19.0 - uPhase * 4.4) * uJag;
  // **진폭이 주파수를 따라 함께 커집니다.** 촘촘해지기만 하면 잔 물결이 되고, 진폭이 함께
  // 커져야 요동칩니다. 배당의 몫이 세기의 몫과 따로인 이유는 **잦아들지 않아야** 하기
  // 때문입니다 — 세기 쪽은 더해진 뒤 0.42초에 빠집니다.
  float amp = 2.5 + surge * 7.0 + uLevel * 6.0;
  float y = uBox.y * 0.5 + w * amp + surge * (n * 26.0 + n * jag * 11.0);

  // **기울기 보정입니다.** 세로 거리만 쓰면 파형이 급한 자리에서 줄이 굵어 보입니다.
  // 노이즈 항의 해석적 미분은 구할 수 없고, 도함수 명령 하나가 두 항을 함께 처리합니다.
  float sx = max(1e-4, abs(dFdx(vPx.x)));
  float slope = dFdx(y) / sx;
  float d = abs(vPx.y - y) * inversesqrt(1.0 + slope * slope);

  // 두 겹. 가는 흰 심과 넓은 색 번짐입니다. 심의 절반 밝기가 약 1.1픽셀, 번짐의 절반
  // 밝기가 약 4.7픽셀입니다 — 번짐은 15픽셀에서 이미 0.09 이므로 상자의 위아래 변에
  // 닿기 전에 잦아듭니다.
  float core = exp(-d * d * 0.62);
  float halo = 1.0 / (1.0 + d * d * 0.045);
  // **번짐의 상한을 지킵니다.** 이 위에 34픽셀 숫자가 앉고 그 테가 2.55픽셀이므로, 더
  // 올리면 숫자의 테가 빛에 묻힙니다.
  vec3 lit = ink * (halo * (0.30 + surge * 0.55))
           + vec3(1.0) * (core * (0.22 + surge * 0.30));

  // **더해진 순간에는 상자 전체가 밝아집니다.**
  //
  // 줄만 밝히면 그것은 줄이 굵어진 것입니다 — 이 겹이 있어야 「이 칸에 무언가 더해졌다」가
  // 칸의 밝기로 옵니다. 절반 밝기가 15.8픽셀이므로 상자의 위아래 변까지 닿고, 평평한 몫
  // 0.16이 파형에서 먼 네 귀퉁이를 채웁니다.
  //
  // **바닥이 아니라 얹히는 것만 봅니다.** 바닥까지 보면 고배당에서는 늘 밝아 있고, 그러면
  // 더해진 순간이 드러나지 않습니다.
  float wash = 1.0 / (1.0 + d * d * 0.004);
  lit += ink * (pulse * (0.16 + 0.34 * wash));

  // 둥근 사각형 하나로 자릅니다. **접은 자리가 상자의 밖(곱셈표가 서는 사이)이므로 접힘의
  // 이음매는 잘려 나갑니다** — 위의 좌우 선택이 그 자리에서 어긋나도 화면에 나오지 않습니다.
  vec2 h = uBox * 0.5;
  vec2 q = abs(local - h) - (h - uRadius);
  float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - uRadius;
  // **fwidth 로 나눕니다.** 흐림이 화면 픽셀 1개이고, 판 좌표로 적으면 화면 배율이
  // 2.7배인 자리에서 2.7픽셀이 됩니다.
  float mask = clamp(0.5 - sd / max(1e-4, fwidth(sd)), 0.0, 1.0) * alive;

  // 알파를 미리 곱해 냅니다. 섞기가 add 이므로 잘린 자리는 아무것도 더하지 않습니다 —
  // **수가 없는 칸도 그렇습니다.** discard 를 쓰지 않는 이유는 위의 도함수가 2 × 2 조각을
  // 함께 보기 때문입니다. 둘 다 사라진 뒤에는 층 자체가 화면에서 빠집니다.
  finalColor = vec4(lit * mask, mask);
}
`

/**
 * 칩과 배수의 파형 한 벌.
 *
 * 만드는 것은 한 번이고 그 뒤는 유니폼만 바뀝니다. 그리기 1회입니다.
 */
export class ScoreWave {
  readonly view: Mesh<Geometry, Shader>
  private readonly shader: Shader
  /** 흐르는 빠르기가 이것을 따릅니다. `advance` 가 읽습니다. */
  private payout = 0

  constructor(jag = !coarsePointer()) {
    const geometry = new Geometry({
      attributes: {
        aPosition: { buffer: new Float32Array([0, 0, 1, 0, 1, 1, 0, 1]), format: 'float32x2' },
      },
      indexBuffer: new Uint16Array([0, 1, 2, 0, 2, 3]),
    })
    this.shader = Shader.from({
      gl: { vertex: VERTEX, fragment: FRAGMENT, name: 'score-wave' },
      resources: {
        waveUniforms: {
          uBlock: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
          uBox: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
          uHalf: { value: 0.5, type: 'f32' },
          uRadius: { value: 6, type: 'f32' },
          uSurge: { value: new Float32Array([0, 0]), type: 'vec2<f32>' },
          uPulse: { value: new Float32Array([0, 0]), type: 'vec2<f32>' },
          // **처음에는 없습니다.** 판이 서기 전의 두 칸은 0 이고, 0 인 칸은 보이지
          // 않는 것이 이 연출의 기본 자리입니다.
          uAlive: { value: new Float32Array([0, 0]), type: 'vec2<f32>' },
          uLevel: { value: 0, type: 'f32' },
          uPhase: { value: 0, type: 'f32' },
          uDrift: { value: 0, type: 'f32' },
          // **핸드폰에서는 잔 떨림을 빼습니다.** 19주기가 115픽셀이므로 한 주기가 6픽셀이고,
          // 판이 0.5배로 들어가는 화면에서는 화면 픽셀 3개입니다 — 그 크기의 무늬는 프레임마다
          // 반짝입니다.
          uJag: { value: jag ? 1 : 0, type: 'f32' },
          uChips: { value: new Float32Array([1, 1, 1]), type: 'vec3<f32>' },
          uMult: { value: new Float32Array([1, 1, 1]), type: 'vec3<f32>' },
        },
        ...noiseResources({ uSoft: 'soft' }),
      },
    })
    this.view = new Mesh({ geometry, shader: this.shader })
    // **빛으로 얹힙니다.** 아래에 상자의 채움과 값이 움직이는 동안의 바탕이 있습니다.
    this.view.blendMode = 'add'
    this.view.eventMode = 'none'
    // 두 칸이 다 0 이므로 처음에는 화면에 없습니다.
    this.view.visible = false
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.shader.resources.waveUniforms.uniforms as Record<string, number | Float32Array>
  }

  /**
   * 층이 놓인 자리와 두 상자의 규격. **판의 좌표입니다.**
   *
   * 상자 둘을 받아 셈합니다 — 자리를 베껴 적으면 상자의 크기를 고친 자리에서 이것만 낡습니다.
   */
  layout(left: number, top: number, boxWidth: number, boxHeight: number,
         blockWidth: number, radius: number): void {
    this.view.position.set(left, top)
    ;(this.uniforms.uBlock as Float32Array).set([blockWidth, boxHeight])
    ;(this.uniforms.uBox as Float32Array).set([boxWidth, boxHeight])
    this.uniforms.uHalf = blockWidth / 2
    this.uniforms.uRadius = radius
    this.rect = [left, top, blockWidth, boxHeight]
  }

  /**
   * 이 층이 덮은 사각형. **판의 좌표입니다.**
   *
   * **`view.getBounds()` 로는 나오지 않습니다.** 기하가 단위 사각형이고 크기를 정점 셰이더가
   * 곱하므로 그쪽은 1 × 1 을 냅니다 — 그림을 오려 보는 도구가 그것을 받아 3 × 3 픽셀을
   * 구웠습니다.
   */
  private rect: [number, number, number, number] = [0, 0, 0, 0]

  get box(): [number, number, number, number] {
    return [...this.rect]
  }

  /**
   * 흐름을 잇습니다. **실제 초입니다.**
   *
   * **배당이 크면 빠르게 흐릅니다.** 세기만 올리면 요동치는 폭만 커지고 그 자리가 급한
   * 대목인지가 드러나지 않습니다 — 빠르기가 함께 올라야 「지금 이 판이 크다」로 읽힙니다.
   *
   * |무엇|조용한 자리|배당의 마지막 단|
   * |--|--|--|
   * |위상|초당 1.9 라디안. 상자 너비의 0.30배|초당 9.5. 1.51배 — **5.0배 빠릅니다**|
   * |노이즈가 흐르는 자리|초당 0.28|초당 1.78 — 6.4배|
   *
   * 상한은 프레임에 맞춰 정한 것입니다. 가장 잔 항(19주기)이 마지막 단에서 초당 40픽셀이므로
   * 60프레임에서 한 프레임이 0.7픽셀이고, 이보다 올리면 잔 무늬가 프레임마다 튑니다.
   *
   * **시간이 아니라 위상을 누적합니다.** `uv.x * f - uTime * speed` 로 적으면 `speed` 가
   * 바뀌는 순간 `uTime` 이 이미 큰 값이므로 위상이 통째로 뛰고, 배당이 오를 때마다 파형이
   * 한 번 끊깁니다. 누적하면 빠르기만 바뀝니다.
   */
  advance(seconds: number): void {
    const level = this.payout
    this.uniforms.uPhase = (this.uniforms.uPhase as number) + seconds * (1.9 + level * 7.6)
    this.uniforms.uDrift = (this.uniforms.uDrift as number) + seconds * (0.28 + level * 1.5)
  }

  /**
   * 두 칸의 세기. **얹히는 것과 바닥이 더해진 값입니다.**
   *
   * 바닥의 상한이 0.55 인 것은 얹히는 것이 들어갈 자리를 남기기 위한 것입니다 — 1 로 두면
   * 고배당에서 이미 최대이므로 새로 더해진 것이 화면에 드러나지 않습니다.
   */
  setSurge(chips: number, mult: number, level: number): void {
    this.payout = Math.max(0, Math.min(1, level))
    this.uniforms.uLevel = this.payout
    const floor = this.payout * 0.55
    const into = this.uniforms.uSurge as Float32Array
    into[0] = Math.min(1, floor + Math.max(0, chips))
    into[1] = Math.min(1, floor + Math.max(0, mult))
    // **얹히는 것만 따로 냅니다.** 상자 전체가 밝아지는 겹이 이것을 봅니다 — 바닥까지 보면
    // 고배당에서는 늘 밝아 있고, 그러면 더해진 순간이 드러나지 않습니다.
    const pulse = this.uniforms.uPulse as Float32Array
    pulse[0] = Math.max(0, Math.min(1, chips))
    pulse[1] = Math.max(0, Math.min(1, mult))
  }

  /**
   * 수가 있는 칸만 보입니다. **0 인 칸은 빠르게 사라집니다.**
   *
   * 0 은 아직 아무것도 없는 자리이고, 거기에 줄 하나가 흐르고 있으면 그 줄이 무엇을
   * 나타내는지가 없어집니다 — 파형은 쌓이는 수의 모습이므로 쌓인 것이 없으면 그림도
   * 없어야 합니다.
   *
   * **끄지 않고 사라집니다.** 알파를 한 프레임에 0 으로 두면 그것은 꺼진 것이고, 판이 끝날
   * 때마다 두 상자에서 무언가 사라지는 것이 눈에 걸립니다. 나타나는 것이 더 빠른 이유는
   * 그 순간이 곧 첫 수가 더해지는 순간이기 때문입니다 — 밝아지는 겹과 함께 와야 합니다.
   */
  setLive(stepMs: number, chips: boolean, mult: boolean): void {
    const into = this.uniforms.uAlive as Float32Array
    const rise = stepMs / LIVE_RISE_MS
    const fall = stepMs / LIVE_FALL_MS
    for (const [slot, on] of [[0, chips], [1, mult]] as const) {
      into[slot] = on
        ? Math.min(1, into[slot] + rise)
        : Math.max(0, into[slot] - fall)
    }
    // **둘 다 사라지면 층을 화면에서 뺍니다.** 조각을 셈하고 알파 0 으로 버리는 것이
    // 값으로는 작지만, 판이 도는 시간의 대부분이 그 자리입니다.
    this.view.visible = into[0] > 0 || into[1] > 0
  }

  /**
   * 지금 화면에 있는 세기. 값을 확인하는 도구가 읽습니다.
   *
   * **위상도 함께 냅니다.** 빠르기는 그림으로 확인되지 않습니다 — 두 컷의 위상 차이를
   * 그 사이의 시간으로 나눈 것이 빠르기입니다.
   */
  get surge(): {
    chips: number; mult: number; level: number; phase: number
    live: [number, number]
  } {
    const from = this.uniforms.uSurge as Float32Array
    const live = this.uniforms.uAlive as Float32Array
    return {
      chips: from[0], mult: from[1], level: this.payout,
      phase: this.uniforms.uPhase as number,
      live: [live[0], live[1]],
    }
  }

  ink(chips: number, mult: number): void {
    for (const [uniform, color] of [['uChips', chips], ['uMult', mult]] as const) {
      const into = this.uniforms[uniform] as Float32Array
      into[0] = ((color >> 16) & 0xff) / 255
      into[1] = ((color >> 8) & 0xff) / 255
      into[2] = (color & 0xff) / 255
    }
  }

  destroy(): void {
    this.view.destroy()
    this.shader.destroy()
  }
}
