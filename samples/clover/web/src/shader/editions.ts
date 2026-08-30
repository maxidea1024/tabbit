// 에디션 셰이더 4종.
//
// **파라미터는 데이터입니다** — `EditionVisual` 테이블의 `strength` · `flow_speed` · `noise`
// 를 그대로 받습니다. 유니티의 HLSL 이 같은 값을 같은 수식에 넣으므로, 두 화면이 같은
// 모양이 됩니다.
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

/** 포일 — 카드 좌표에 시간 위상을 더한 사인파로 밝기 띠 하나. */
const FOIL = `${HEAD}
vec3 tone(vec3 color) {
  float band = sin((vTextureCoord.x + vTextureCoord.y) * 12.0 + uTime * uFlow + uTilt * 2.0);
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
  float t = vTextureCoord.y * 1.4 + vTextureCoord.x * 0.35 + uTime * uFlow + uTilt;
  vec3 tint = rainbow(t);

  float grain = (hash(floor(vTextureCoord * 90.0)) - 0.5) * clamp(uNoise, 0.0, 1.0) * 0.14;
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
  float t = luma * 1.2 + uTime * uFlow + vTextureCoord.x * 0.7 + uTilt;
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

  advance(seconds: number, tilt: number): void {
    const uniforms = this.resources.editionUniforms.uniforms as Record<string, number>
    uniforms.uTime += seconds
    uniforms.uTilt = tilt
  }
}
