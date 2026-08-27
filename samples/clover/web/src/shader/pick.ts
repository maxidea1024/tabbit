// 고른 카드와 고르지 않은 카드.
//
// **고른 것이 눈에 띄는 것만으로는 부족합니다** — 고르지 않은 것이 물러나야 몇 장을 골랐는지가
// 한눈에 읽힙니다. 그래서 한 필터가 두 가지를 합니다.
//
//     uMode = 1   고른 것. 밝아지고 테두리에 빛이 돌고 사선 광택이 흐릅니다
//     uMode = -1  고르지 않은 것. 어두워지고 색이 빠집니다
//
// **족보 도움은 여기 없습니다.** 그것은 카드에 그린 외곽선 하나이고 `card-view.ts` 가
// 그립니다 — 필터를 걸면 카드가 매 프레임 그림으로 구워져 글씨가 흐려지고, 알파의 기울기로
// 만든 테두리는 그림자 색이 바뀐 것으로 보입니다.
//
// 카드 한 장에 하나씩 붙으므로, 손패 8장이면 필터가 8개입니다. 카드가 작고 셰이더가 짧아서
// 그 값은 무시할 수 있습니다.

import { Filter, GlProgram } from 'pixi.js'

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

void main(void) {
  gl_Position = filterVertexPosition();
  vTextureCoord = aPosition * (uOutputFrame.zw * uInputSize.zw);
}
`

const FRAGMENT = `#version 300 es
precision highp float;

in vec2 vTextureCoord;
out vec4 finalColor;

uniform sampler2D uTexture;
uniform float uMode;    // 1 고름 · -1 고르지 않음 · 0 그대로
uniform float uTime;
uniform vec3  uTint;

void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  if (src.a < 0.004) {
    finalColor = src;
    return;
  }

  vec3 color = src.rgb / max(src.a, 0.004);
  float gray = dot(color, vec3(0.299, 0.587, 0.114));

  if (uMode > 0.5) {
    // 고른 카드. 밝히고, 사선 광택을 흘리고, 테두리에 빛을 두릅니다.
    color = mix(color, color * 1.18 + uTint * 0.16, 1.0);

    float band = fract((vTextureCoord.x + vTextureCoord.y) * 1.6 - uTime * 0.55);
    float sheen = smoothstep(0.46, 0.5, band) - smoothstep(0.5, 0.56, band);
    color += uTint * sheen * 0.55;

    // 알파의 기울기가 곧 테두리입니다 — 카드 모서리가 둥글어도 따라갑니다.
    float edge = 1.0 - smoothstep(0.35, 0.98, src.a);
    color += uTint * edge * 1.1;
  } else if (uMode < -0.5) {
    // 고르지 않은 카드. 색을 빼고 어둡게 합니다. **물러나 있어야 고른 것이 보입니다.**
    color = mix(vec3(gray), color, 0.45) * 0.62;
  }

  finalColor = vec4(color * src.a, src.a);
}
`

export class PickFilter extends Filter {
  constructor() {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      resources: {
        pickUniforms: {
          uMode: { value: 0, type: 'f32' },
          uTime: { value: 0, type: 'f32' },
          uTint: { value: new Float32Array([0.55, 0.95, 0.7]), type: 'vec3<f32>' },
        },
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.pickUniforms.uniforms as Record<string, number | Float32Array>
  }

  /** 1 고름 · -1 고르지 않음 · 0 그대로. */
  set mode(value: number) {
    this.uniforms.uMode = value
  }

  get mode(): number {
    return this.uniforms.uMode as number
  }

  set time(value: number) {
    this.uniforms.uTime = value
  }

  setTint(r: number, g: number, b: number): void {
    (this.uniforms.uTint as Float32Array).set([r, g, b])
  }
}
