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
 * **`euphoria` 의 사다리를 쓰지 않습니다.** 처음에 그것을 그대로 가져왔고, 그 자는 첫 단이
 * `chips × mult` 로 400,000 입니다 — 그 아래가 전부 0 으로 눌려서, 보통 판에서 파형이
 * 요동치는 일이 아예 없었습니다. 안티 5~6에 가야 무언가 달라졌습니다.
 *
 * 환희는 「대단한 판」을 가리는 것이고 **파형은 「지금 쌓이고 있다」를 나타내는 것**이므로
 * 자가 달라야 합니다. 이 자는 한 런에서 실제로 지나가는 범위에 걸쳐 있습니다.
 *
 * |한 판의 곱|세기|
 * |--|--|
 * |60 (30 × 2, 초반 원페어)|0.00|
 * |240 (60 × 4)|0.09|
 * |4,500 (300 × 15, 중반)|0.38|
 * |30,000 (1,000 × 30)|0.58|
 * |2,000,000 (20,000 × 100) 이상|1.00|
 *
 * **득점하는 동안 이 값이 오릅니다.** 칩과 배수가 쌓여 가므로 한 판 안에서 0.1 에서 0.7 로
 * 지나가고, 그것이 파형이 점점 거칠어지는 모습입니다.
 */
const LEVEL_FLOOR = 100
const LEVEL_TOP = 2_000_000
const LEVEL_SPAN = Math.log10(LEVEL_TOP / LEVEL_FLOOR)
/**
 * 수가 생겨 나타나는 데와 0 이 되어 사라지는 데 걸리는 시간.
 *
 * **나타나는 것이 더 빠릅니다.** 나타나는 순간은 곧 첫 수가 더해지는 순간이므로 밝아지는
 * 겹과 함께 와야 하고, 사라지는 것은 판이 끝나 정리되는 대목이라 조금 여유가 있습니다.
 */
const LIVE_RISE_MS = 90
const LIVE_FALL_MS = 200

/**
 * 위상이 초당 얼마나 오르는가. **한 자리를 스쳐 가는 주파수가 이것입니다.**
 *
 * 무늬가 흐르는 빠르기는 이 값을 산의 수로 나눈 것입니다 — 조용한 자리에서 초당 57픽셀이고
 * 마지막 단에서 87픽셀입니다. 115픽셀 상자를 2.0초와 1.3초에 건넙니다.
 */
function phaseRate(level: number): number {
  return 7.5 + level * 21.0
}

/** 노이즈를 읽는 자리가 초당 얼마나 가는가. **파형과 같은 빠르기로 맞춘 값입니다.** */
function driftRate(level: number): number {
  return 1.30 + level * 0.85
}

export function payoutLevel(chips: number, mult: number): number {
  const product = chips * mult
  if (product <= LEVEL_FLOOR) return 0
  return Math.min(1, Math.log10(product / LEVEL_FLOOR) / LEVEL_SPAN)
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

  // **한 화면에 보이는 산의 수입니다.** 조용한 자리에서도 2.4개이고 마지막 단에서 6.0개
  // 입니다 — 1.0개에서 시작하던 동안 조용한 자리가 완만한 굽이 하나여서 얌전했습니다.
  //
  // 한 자리를 스쳐 가는 주파수는 이 값이 아니라 uPhase 의 오르는 빠르기가 정합니다.
  float freq = 2.4 + uLevel * 3.6;

  // **잔 항만 덜 올립니다.** 세 항을 다 같은 배로 올리면 마지막 항이 4.7 × 6.0 = 28주기가
  // 되고, 그것은 115픽셀에서 한 주기가 4픽셀입니다 — 판이 작게 들어가는 화면에서 화면 픽셀
  // 두어 개의 무늬가 되어 프레임마다 반짝입니다. 0.45배로 올리면 마지막 단에서 7.5픽셀입니다.
  float fine = 1.0 + (freq - 1.0) * 0.45;

  // 항 셋의 주기.
  float k1 = TAU * 1.0 * freq;
  float k2 = TAU * 2.3 * freq;
  float k3 = TAU * 4.7 * fine;

  // 파형. **항 셋이 같은 빠르기로 한쪽으로 흐릅니다.**
  //
  // 여기에 두 가지가 잘못 있었습니다. 하나는 둘째 항의 위상이 **부호가 반대**여서 그 항이
  // 거꾸로 흐른 것이고(무게가 0.647이라 첫 항을 거의 상쇄했습니다), 또 하나는 위상 계수가
  // 그 항의 주기와 무관한 값이어서 **항마다 다른 빠르기로 흐른** 것입니다. 그 둘이 겹쳐
  // 파형이 어느 쪽으로도 가지 않고 제자리에서 들썩였습니다.
  //
  // **위상 속도는 위상 계수를 주기로 나눈 값입니다.** 셋이 같은 쪽으로 같은 빠르기로 가려면
  // 계수가 그 항의 주기에 비례해야 하고, 그래서 k2/k1 과 k3/k1 을 곱합니다.
  //
  // **0.94 와 1.06 은 일부러 어긋낸 것입니다.** 정확히 같은 빠르기로 두면 셋이 한 덩어리로
  // 굳어 그려 놓은 그림 하나가 미끄러지는 것이 되고, 6%를 어긋내면 흐르는 동안 모습이
  // 천천히 바뀝니다.
  //
  // 되풀이가 보이지 않는 것은 부호가 아니라 **주기 1 · 2.3 · 4.7 이 서로 나누어떨어지지
  // 않는 것**에서 옵니다.
  // 나눗셈 하나로 묶습니다. k2 / k1 은 정확히 2.3 이고, k3 / k1 은 4.7 * fine / freq
  // 입니다 — 셋 다 이 프레임 안에서 값이 같은데 픽셀마다 나누고 있었습니다.
  float invFreq = 1.0 / freq;
  float w = sin(u * k1 + uPhase)
          + sin(u * k2 + uPhase * 2.3 * 0.94 + 1.7) * 0.647
          + sin(u * k3 + uPhase * (4.7 * fine * invFreq) * 1.06 + 4.1) * 0.324;
  w /= 1.971;

  // 노이즈. **값이 오를 때만 얹힙니다.**
  //
  // n 과 jag 의 곱이 요점입니다 — 잔 떨림의 진폭을 노이즈가 정하므로 거친 자리가 가로로
  // 뭉쳐서 옵니다. jag 만 얹으면 상자 전체에 같은 굵기의 빗살이 서고, 그것은 파형이 아니라
  // 무늬로 보입니다.
  // **읽는 자리도 같은 쪽으로 갑니다.** 빼고 있었고, 그러면 무늬가 파형과 반대로 흐릅니다 —
  // 더해지는 동안에는 이 항이 가장 크게 보이므로 그 대목의 흐름이 통째로 거꾸로였습니다.
  float n = texture(uSoft, vec2(u * 2.6 + uDrift, uDrift * 0.31)).r - 0.5;
  // 잔 떨림도 같은 빠르기로. 19주기이므로 계수가 19 / freq 입니다.
  // **잔 떨림만 절반 빠르기입니다.** 19주기는 115픽셀에서 한 주기가 6픽셀이고, 같은
  // 빠르기로 흐르면 한 프레임에 0.26주기를 지나 되감기는 것으로 보입니다 — 이 항이 하는
  // 일은 거칠기이지 흐름이 아니므로 절반으로 둡니다.
  float jag = sin(u * TAU * 19.0 + uPhase * (19.0 * invFreq) * 0.5) * uJag;
  // **진폭이 주파수를 따라 함께 커집니다.** 촘촘해지기만 하면 잔 물결이 되고, 진폭이 함께
  // 커져야 요동칩니다. 배당의 몫이 세기의 몫과 따로인 이유는 **잦아들지 않아야** 하기
  // 때문입니다 — 세기 쪽은 더해진 뒤 0.42초에 빠집니다.
  //
  // 마지막 단에서 17.2픽셀입니다. 상자의 절반이 29픽셀이므로 **파형이 상자를 거의 채우고
  // 숫자를 통째로 지납니다** — 그 배당에서 의도한 모습입니다.
  float amp = 3.2 + surge * 5.0 + uLevel * 10.0;
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
   * |위상|초당 4.5 라디안. 한 자리를 초당 0.72회 스칩니다|초당 18.5. 2.94회 — **4.1배**|
   * |노이즈가 흐르는 자리|초당 0.60|초당 3.00 — 5.0배|
   *
   * **산의 수를 올리면 위상도 함께 올려야 합니다.** 같은 위상 빠르기에서 산이 촘촘해지면
   * 무늬가 지나가는 것이 그만큼 느려 보입니다 — 촘촘하게만 만들고 위상을 두면 얌전해집니다.
   *
   * 상한은 프레임에 맞춰 정한 것입니다. 가장 잔 항(19주기)이 마지막 단에서 초당 78픽셀이므로
   * 60프레임에서 한 프레임이 1.3픽셀이고, 이보다 올리면 잔 무늬가 프레임마다 튑니다.
   *
   * **시간이 아니라 위상을 누적합니다.** `uv.x * f - uTime * speed` 로 적으면 `speed` 가
   * 바뀌는 순간 `uTime` 이 이미 큰 값이므로 위상이 통째로 뛰고, 배당이 오를 때마다 파형이
   * 한 번 끊깁니다. 누적하면 빠르기만 바뀝니다.
   */
  advance(seconds: number): void {
    if (this.held) return
    const level = this.payout
    this.uniforms.uPhase = (this.uniforms.uPhase as number) + seconds * phaseRate(level)
    this.uniforms.uDrift = (this.uniforms.uDrift as number) + seconds * driftRate(level)
  }

  /** 위상을 손으로 잡고 있는가. **확인 도구만 잡습니다.** */
  private held = false

  /**
   * 위상을 이 값에 세우고 붙잡습니다.
   *
   * **흐르는 쪽을 확인하는 도구가 쓰는 자리입니다.** 그 도구는 컷 둘을 견주는데, 그림 한 장을
   * 굽는 데 1초쯤 들어서 시간으로는 컷 사이의 위상 차이를 정할 수 없습니다 — 800라디안이
   * 넘게 가고, 그것은 파장의 여러 배이므로 어느 골을 골랐는지 알 수 없습니다.
   *
   * **노이즈도 같은 비로 함께 세웁니다.** 그러지 않으면 더해지는 동안 가장 크게 보이는 항이
   * 저 혼자 흐릅니다.
   */
  hold(phase: number): void {
    this.held = true
    const level = this.payout
    this.uniforms.uPhase = phase
    this.uniforms.uDrift = phase * (driftRate(level) / phaseRate(level))
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
