// 에디션 셰이더 4종.
//
// **파라미터는 데이터입니다** — `EditionVisual` 테이블의 `strength` · `flow_speed` · `noise`
// 를 그대로 받습니다. 세기를 고치는 것이 코드가 아니라 시트입니다.
//
// 수식은 `doc/art.md` 에 적혀 있고 여기가 그 구현입니다.
//
// **네 셰이더가 `main` 하나를 나눠 씁니다.** 에디션마다 다른 것은 색을 어떻게 바꾸는가
// 뿐이고, 알파를 다루는 방법은 넷이 같아야 합니다 — 그것이 갈라져서 홀로그래픽과 네거티브가
// 카드 밖까지 칠했습니다.

import { Filter, GlProgram, Texture } from 'pixi.js'

/**
 * Pixi v8 의 필터가 요구하는 정점 셰이더. 네 필터가 공유합니다.
 *
 * `vShapeCoord` 가 **필터가 도는 사각형 위의 0..1** 입니다. 모양 그림을 그 좌표로 읽으므로,
 * 필터를 카드 넓이에 딱 맞춰 두면 그림과 카드가 정확히 겹칩니다.
 */
const VERTEX = `#version 300 es
in vec2 aPosition;
out vec2 vTextureCoord;
out vec2 vShapeCoord;

uniform vec4 uInputSize;
uniform vec4 uOutputFrame;
uniform vec4 uOutputTexture;

vec4 filterVertexPosition(void) {
  vec2 position = aPosition * uOutputFrame.zw + uOutputFrame.xy;
  position.x = position.x * (2.0 / uOutputTexture.x) - 1.0;
  position.y = position.y * (2.0 * uOutputTexture.z / uOutputTexture.y) - uOutputTexture.z;
  return vec4(position, 0.0, 1.0);
}

vec2 filterTextureCoord(void) {
  return aPosition * (uOutputFrame.zw * uInputSize.zw);
}

void main(void) {
  gl_Position = filterVertexPosition();
  vTextureCoord = filterTextureCoord();
  vShapeCoord = aPosition;
}
`

/**
 * 네 셰이더가 공유하는 머리와 `main`.
 *
 * **알파를 다루는 자리가 여기 한 곳입니다.**
 *
 * 1. 화면의 색은 알파가 곱해진 채로 들어옵니다. 그대로 섞으면 투명한 자리의 색이 0 인 것을
 *    「검정」으로 읽어 밝은 색이 얹히고, 알파가 0인데 색이 남아 발광하는 사각형이 됩니다.
 *    그래서 나누고, 바꾸고, 다시 곱합니다.
 * 2. 모양 그림의 알파를 마지막에 곱합니다. 카드의 둥근 모서리가 여기서 지켜집니다.
 *
 * 에디션마다 다른 것은 `tone` 하나뿐입니다.
 */
const HEAD = `#version 300 es
precision highp float;

in vec2 vTextureCoord;
in vec2 vShapeCoord;
out vec4 finalColor;

uniform sampler2D uTexture;
uniform sampler2D uShape;
uniform float uTime;
uniform float uStrength;
uniform float uFlow;
uniform float uNoise;
uniform float uTilt;

float hash(vec2 p) {
  return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

vec3 rainbow(float t) {
  return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.0, 0.33, 0.67)));
}

vec3 tone(vec3 color);

void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  float shape = texture(uShape, vShapeCoord).a;

  if (src.a < 0.004 || shape < 0.004) {
    finalColor = vec4(0.0);
    return;
  }

  vec3 color = tone(src.rgb / src.a);
  finalColor = vec4(color * src.a, src.a) * shape;
}
`

/**
 * 포일 — 카드 좌표에 시간 위상을 더한 사인파로 밝기 띠 하나.
 *
 * **무늬의 좌표는 `vShapeCoord` 입니다.** `vTextureCoord` 는 필터가 받은 텍스처 위의
 * 자리이고, 그 텍스처는 Pixi 가 2의 거듭제곱으로 잡아 줍니다 — 조커가 늘 흔들려 화면에서
 * 차지한 사각형이 프레임마다 1픽셀씩 달라지므로, 그 나눗셈의 값도 함께 달라집니다.
 *
 * 실측으로 그 떨림이 **의도한 흐름의 20배**였습니다. 흐름이 프레임당 0.005 rad 인데 사각형
 * 1픽셀이 0.09 rad 을 주고, 사각형의 폭이 2의 거듭제곱 경계를 넘으면 배율이 절반이 되어
 * 위상이 6 rad 뛰었습니다 — 띠가 지나가는 것이 아니라 제자리에서 덜컹거린 것이 그것입니다.
 *
 * `vShapeCoord` 는 필터가 도는 사각형 위의 0..1 이므로 그 나눗셈이 없습니다.
 */
const FOIL = `${HEAD}
vec3 tone(vec3 color) {
  float band = sin((vShapeCoord.x + vShapeCoord.y) * 12.0 + uTime * uFlow + uTilt * 0.6);
  float light = smoothstep(0.55, 1.0, band) * clamp(uStrength, 0.0, 1.0) * 0.6;
  return color + light * 0.55;
}
`

/**
 * 홀로그래픽 — 무지개 그라디언트를 UV 와 기울기로 이동시킵니다.
 *
 * **원본을 덮지 않습니다.** 노이즈를 무지개의 위상에 더하면 픽셀마다 색이 달라져 카드가
 * 무지개 잡티로 덮이고 이름을 읽을 수 없게 됩니다 — 노이즈는 밝기에만 얹고, 섞는 양은
 * 데이터가 어떤 값이어도 절반을 넘지 않게 묶습니다.
 */
const HOLO = `${HEAD}
vec3 tone(vec3 color) {
  float t = vShapeCoord.y * 1.4 + vShapeCoord.x * 0.35 + uTime * uFlow + uTilt * 0.6;
  vec3 tint = rainbow(t);

  // **잡티도 같은 좌표입니다.** 필터 텍스처의 좌표로 두면 조커가 흔들릴 때마다 잡티가
  // 자리를 옮겨, 종이의 결이 아니라 화면의 잡음으로 보입니다.
  //
  // 셰이더 소스는 템플릿 문자열 안이므로 **주석에 백틱을 쓸 수 없습니다** — 하나가
  // 문자열을 끊고, 끊긴 자리부터 GLSL 이 아니라 자바스크립트로 읽힙니다.
  float grain = (hash(floor(vShapeCoord * 90.0)) - 0.5) * clamp(uNoise, 0.0, 1.0) * 0.14;
  float amount = clamp(uStrength, 0.0, 1.0) * 0.45;

  vec3 sheet = color * 0.55 + tint * 0.8;
  return mix(color, sheet, amount) + grain;
}
`

/**
 * 폴리크롬 — **밝은 부분에만** 무지개를 얹습니다.
 *
 * 섞는 양을 묶습니다. `strength` 가 1을 넘으면 밝은 자리가 통째로 무지개가 되어 글씨가
 * 사라집니다.
 */
const POLY = `${HEAD}
vec3 tone(vec3 color) {
  float luma = dot(color, vec3(0.299, 0.587, 0.114));
  float t = luma * 1.2 + uTime * uFlow + vShapeCoord.x * 0.7 + uTilt * 0.6;
  vec3 tint = rainbow(t);
  float amount = smoothstep(0.5, 0.95, luma) * clamp(uStrength, 0.0, 1.0) * 0.62;
  return mix(color, tint, clamp(amount, 0.0, 1.0));
}
`

/** 네거티브 — 색을 뒤집고 어두운 테두리를 발광시킵니다. */
const NEGATIVE = `${HEAD}
vec3 tone(vec3 color) {
  vec3 flipped = vec3(1.0) - color;
  float edge = 1.0 - smoothstep(0.0, 0.35, dot(color, vec3(0.333)));
  vec3 glow = vec3(0.45, 0.15, 0.75) * edge * (0.6 + 0.4 * sin(uTime * uFlow));
  return mix(color, flipped, clamp(uStrength, 0.0, 0.85)) + glow;
}
`

export type EditionShader = 'foil' | 'holo' | 'poly' | 'negative'

const SOURCE: Record<EditionShader, string> = {
  foil: FOIL,
  holo: HOLO,
  poly: POLY,
  negative: NEGATIVE,
}

export interface EditionParams {
  /** 만분율. `EditionVisual.strength` 그대로입니다. */
  strength: number
  flowSpeed: number
  noise: number
  /** 카드의 모양. 이 그림의 알파가 곧 칠해도 되는 자리입니다. */
  shape: Texture
}

/** 에디션 필터 하나. 시간과 기울기는 매 프레임 갱신합니다. */
export class EditionFilter extends Filter {
  constructor(shader: EditionShader, params: EditionParams) {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: SOURCE[shader] }),
      // **화면의 배율로 굽습니다.** 필터의 기본값은 1배이고 화면은 2~3배이므로, 기본값으로
      // 두면 카드가 1배로 구워진 뒤 늘려져 그 위의 것이 전부 뿌옇게 됩니다. 한 통에 걸린
      // 필터 가운데 하나라도 1배이면 통째로 1배가 되므로, 카드에 걸리는 필터는 전부 같습니다.
      resolution: 'inherit',
      resources: {
        editionUniforms: {
          uTime: { value: 0, type: 'f32' },
          uStrength: { value: params.strength / 10_000, type: 'f32' },
          uFlow: { value: params.flowSpeed / 10_000, type: 'f32' },
          uNoise: { value: params.noise / 10_000, type: 'f32' },
          uTilt: { value: 0, type: 'f32' },
        },
        uShape: params.shape.source,
        uShapeSampler: params.shape.source.style,
      },
    })
  }

  /**
   * 지금이 몇 초인가.
   *
   * **자기 시계를 갖지 않습니다.** 초를 받아 스스로 더하면 필터를 다시 만들 때마다 0에서
   * 시작하고, 그러면 화면을 다시 그리는 것만으로 무늬의 흐름이 끊깁니다 — 패를 한 장 깔
   * 때마다 조커의 무늬가 처음으로 돌아가던 것이 그것입니다.
   *
   * 판 전체의 시계를 그대로 받습니다. 게임 엔진이 엔진 시간을 넘기는 것과 같습니다 —
   * 카드마다 따로 돌 이유가 없고, 같이 돌면 줄에 선 것들의 무늬가 한 결로 흐릅니다.
   */
  at(time: number, tilt: number): void {
    const uniforms = this.resources.editionUniforms.uniforms as Record<string, number>
    uniforms.uTime = time
    uniforms.uTilt = tilt
  }
}
