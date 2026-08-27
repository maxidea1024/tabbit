// 에디션 셰이더 4종.
//
// **파라미터는 데이터입니다** — `EditionVisual` 테이블의 `strength` · `flow_speed` · `noise`
// 를 그대로 받습니다. 유니티의 HLSL 이 같은 값을 같은 수식에 넣으므로, 두 화면이 같은
// 모양이 됩니다.
//
// 수식은 `doc/art.md` 에 적혀 있고 여기가 그 구현입니다.

import { Filter, GlProgram } from 'pixi.js'

/** Pixi v8 의 필터가 요구하는 정점 셰이더. 네 필터가 공유합니다. */
const VERTEX = `#version 300 es
in vec2 aPosition;
out vec2 vTextureCoord;

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
}
`

/** 네 셰이더가 공유하는 머리. 값 셋이 데이터에서 옵니다. */
const HEAD = `#version 300 es
precision highp float;

in vec2 vTextureCoord;
out vec4 finalColor;

uniform sampler2D uTexture;
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
`

/** 포일 — 카드 좌표에 시간 위상을 더한 사인파로 밝기 띠 하나. */
const FOIL = `${HEAD}
void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  float band = sin((vTextureCoord.x + vTextureCoord.y) * 12.0 + uTime * uFlow + uTilt * 2.0);
  float light = smoothstep(0.55, 1.0, band) * uStrength;
  finalColor = vec4(src.rgb + light * 0.55 * src.a, src.a);
}
`

/** 홀로그래픽 — 무지개 그라디언트를 UV 와 기울기로 이동시키고 노이즈로 결을 냅니다. */
const HOLO = `${HEAD}
void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  float grain = (hash(floor(vTextureCoord * 220.0)) - 0.5) * uNoise;
  float t = vTextureCoord.y * 1.6 + vTextureCoord.x * 0.4 + uTime * uFlow + uTilt + grain;
  vec3 tint = rainbow(t);
  finalColor = vec4(mix(src.rgb, src.rgb * 0.4 + tint * 0.9, uStrength * src.a), src.a);
}
`

/** 폴리크롬 — 색상을 돌리고 **밝은 부분에만** 무지개를 얹습니다. */
const POLY = `${HEAD}
void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  float luma = dot(src.rgb, vec3(0.299, 0.587, 0.114));
  float t = luma * 1.2 + uTime * uFlow + vTextureCoord.x * 0.7 + uTilt;
  vec3 tint = rainbow(t);
  float mask = smoothstep(0.45, 0.95, luma) * uStrength;
  finalColor = vec4(mix(src.rgb, tint, mask * src.a), src.a);
}
`

/** 네거티브 — 색을 뒤집고 어두운 테두리를 발광시킵니다. */
const NEGATIVE = `${HEAD}
void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  vec3 flipped = vec3(1.0) - src.rgb;
  float edge = 1.0 - smoothstep(0.0, 0.35, dot(src.rgb, vec3(0.333)));
  vec3 glow = vec3(0.45, 0.15, 0.75) * edge * (0.6 + 0.4 * sin(uTime * uFlow));
  finalColor = vec4(mix(src.rgb, flipped, uStrength) + glow * src.a, src.a);
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
      },
    })
  }

  advance(seconds: number, tilt: number): void {
    const uniforms = this.resources.editionUniforms.uniforms as Record<string, number>
    uniforms.uTime += seconds
    uniforms.uTilt = tilt
  }
}
