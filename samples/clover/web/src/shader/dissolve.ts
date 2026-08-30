// 타서 사라지는 것.
//
// **사라지는 것에도 자리가 있어야 합니다.** 조커를 팔거나 카드가 부서질 때 그냥 없어지면
// 무엇이 없어진 것인지 눈이 따라가지 못하고, 미끄러져 나가는 것은 「치웠다」이지 「없앴다」가
// 아닙니다.
//
// 노이즈 하나를 문턱으로 깎습니다. 문턱이 올라가면 구멍이 뚫리고 넓어지며, 그 가장자리가
// 잠깐 타오릅니다 — 종이가 타는 모습이 그렇습니다.

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
/** 0 이면 그대로, 1 이면 다 타서 없습니다. */
uniform float uBurn;
/** 불의 색. 안쪽이 밝고 바깥이 붉습니다. */
uniform vec3 uEmber;

float hash(vec2 p) {
  vec3 q = fract(vec3(p.xyx) * 0.1031);
  q += dot(q, q.yzx + 33.33);
  return fract((q.x + q.y) * q.z);
}

float noise(vec2 p) {
  vec2 cell = floor(p);
  vec2 f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  float a = hash(cell);
  float b = hash(cell + vec2(1.0, 0.0));
  float c = hash(cell + vec2(0.0, 1.0));
  float d = hash(cell + vec2(1.0, 1.0));
  return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

/** 층 셋. 구멍의 가장자리가 톱니처럼 되어야 종이가 탄 것으로 보입니다. */
float layers(vec2 p) {
  return noise(p) * 0.6 + noise(p * 2.3 + 7.0) * 0.28 + noise(p * 5.1 + 19.0) * 0.12;
}

void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  if (uBurn <= 0.001 || src.a < 0.004) {
    finalColor = src;
    return;
  }

  // 아래에서 위로 번집니다. **불은 아래에서 붙습니다.**
  float grain = layers(vTextureCoord * 9.0);
  float rise = 1.0 - vTextureCoord.y;
  float level = grain * 0.72 + rise * 0.28;

  // 문턱을 조금 넘겨 잡습니다 — 다 타고 나서도 한 조각이 남아 있으면 안 됩니다.
  float edge = uBurn * 1.12;
  if (level < edge - 0.10) {
    finalColor = vec4(0.0);
    return;
  }

  vec3 color = src.rgb / src.a;

  // 구멍의 가장자리. 안쪽이 밝고 바깥으로 갈수록 붉어집니다.
  float ring = 1.0 - smoothstep(0.0, 0.10, level - (edge - 0.10));
  color = mix(color, uEmber, ring * 0.85);
  color += uEmber * ring * 0.9;

  float alpha = src.a * (1.0 - uBurn * 0.25);
  finalColor = vec4(color * alpha, alpha);
}
`

export class DissolveFilter extends Filter {
  constructor(ember: [number, number, number] = [1.0, 0.55, 0.18]) {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      resources: {
        dissolveUniforms: {
          uBurn: { value: 0, type: 'f32' },
          uEmber: { value: new Float32Array(ember), type: 'vec3<f32>' },
        },
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.dissolveUniforms.uniforms as Record<string, number | Float32Array>
  }

  /** 얼마나 탔는가. 0 에서 1 입니다. */
  set burn(value: number) {
    this.uniforms.uBurn = Math.max(0, Math.min(1, value))
  }

  get burn(): number {
    return this.uniforms.uBurn as number
  }
}
