// 고른 카드와 고르지 않은 카드.
//
// **고른 것이 눈에 띄는 것만으로는 부족합니다** — 고르지 않은 것이 물러나야 몇 장을 골랐는지가
// 한눈에 읽힙니다. 그래서 한 필터가 두 가지를 합니다.
//
//     uMode = 1   고른 것. 밝아지고 테두리에 빛이 돌고 사선 광택이 흐릅니다
//     uMode = -1  고르지 않은 것. 어두워지고 색이 빠집니다
//     uMode = 2   득점하는 것. 카드 둘레로 빛이 번집니다
//
// **득점의 빛만 카드 밖으로 나갑니다.** 그래서 이 필터에는 여백이 있고, 둘레를 훑는 표본이
// 있습니다 — 카드 안에서만 밝히면 종이 색이 조금 변한 것으로 보일 뿐입니다.
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
uniform vec4  uInputSize;   // 너비 · 높이 · 1/너비 · 1/높이
uniform float uMode;    // 1 고름 · -1 고르지 않음 · 2 득점 · 0 그대로
uniform float uTime;
uniform vec3  uTint;
uniform float uGlow;    // 0..1. 득점의 빛이 잦아드는 정도입니다.

/**
 * 카드 모양을 뭉갠 것.
 *
 * **알파의 기울기만으로는 빛이 나지 않습니다** — 카드는 안쪽이 전부 불투명하고 바깥이 전부
 * 투명해서, 그 기울기는 모서리의 1px 뿐입니다. 그래서 카드 모양 자체를 흐리게 뭉개고, 그
 * 뭉개진 그림을 빛으로 씁니다. 안쪽에서 1 이고 바깥으로 부드럽게 0 이 되는 값입니다.
 *
 * 표본은 원판 위에 황금각으로 흩습니다. 고리를 몇 겹 두르면 그 고리가 그대로 줄무늬로
 * 보이고, 격자로 두면 네모가 비칩니다.
 */
float blurredShape(vec2 uv, vec2 texel) {
  float sum = 0.0;
  float total = 0.0;
  for (int i = 0; i < 28; i++) {
    float t = (float(i) + 0.5) / 28.0;
    float radius = sqrt(t) * 26.0;
    float angle = float(i) * 2.39996323;
    float weight = exp(-radius * radius / 320.0);
    sum += texture(uTexture, uv + vec2(cos(angle), sin(angle)) * radius * texel).a * weight;
    total += weight;
  }
  return sum / total;
}

void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);

  // **득점의 빛은 카드가 없는 자리에도 그립니다.** 카드 안에서만 밝히면 종이 색이 조금
  // 변한 것으로 보일 뿐이고, 한 장이 지금 점수를 내고 있다는 것이 읽히지 않습니다.
  if (uMode > 1.5) {
    float soft = blurredShape(vTextureCoord, uInputSize.zw);
    // 빛이 숨을 쉽니다. 잦아드는 동안 부풀었다 꺼집니다.
    float breath = 0.84 + 0.16 * sin(uTime * 8.0);
    float glow = uGlow * breath;

    // 카드 밖. 뭉갠 그림이 그대로 빛의 모양입니다.
    float outer = pow(soft, 1.25) * (1.0 - src.a) * 3.6 * glow;

    // 카드 안. **테두리를 긋지 않습니다** — 같은 뭉갠 그림을 뒤집어 쓰면 가장자리에서
    // 안쪽으로 빛이 스며들고, 선 하나가 얹힌 것으로 보이지 않습니다.
    vec3 color = src.a < 0.004 ? vec3(0.0) : src.rgb / src.a;
    color += uTint * ((1.0 - soft) * 1.5 + 0.10) * glow;

    finalColor = vec4(color * src.a + uTint * min(outer, 1.6),
                      min(1.0, src.a + outer * 0.9));
    return;
  }

  if (src.a < 0.004) {
    finalColor = src;
    return;
  }

  vec3 color = src.rgb / max(src.a, 0.004);
  float gray = dot(color, vec3(0.299, 0.587, 0.114));

  if (uMode > 0.5) {
    // 고른 카드.
    //
    // **얼굴은 거의 건드리지 않습니다.** 카드의 종이는 이미 밝아서, 거기에 빛과 색을 더하면
    // 무늬와 숫자가 씻겨 나갑니다 — 작은 화면에서 특히 그렇습니다. 고른 것은 카드가 위로
    // 올라오는 것으로 이미 보이므로, 셰이더는 **테두리 하나**만 맡습니다.
    color = color * 1.04 + uTint * 0.05;

    float band = fract((vTextureCoord.x + vTextureCoord.y) * 1.6 - uTime * 0.55);
    float sheen = smoothstep(0.46, 0.5, band) - smoothstep(0.5, 0.56, band);
    color += uTint * sheen * 0.16;

    // 알파의 기울기가 곧 테두리입니다 — 카드 모서리가 둥글어도 따라갑니다.
    float edge = 1.0 - smoothstep(0.35, 0.98, src.a);
    color += uTint * edge * 1.3;
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
      // **빛이 카드 밖으로 나가므로 자리를 넓힙니다.** 여백이 없으면 둘레의 빛이 카드
      // 경계에서 잘려 네모난 테가 보입니다.
      padding: 32,
      resources: {
        pickUniforms: {
          uMode: { value: 0, type: 'f32' },
          uTime: { value: 0, type: 'f32' },
          uTint: { value: new Float32Array([0.55, 0.95, 0.7]), type: 'vec3<f32>' },
          uGlow: { value: 0, type: 'f32' },
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

  /** 득점의 빛. 1 에서 0 으로 잦아듭니다. */
  set glow(value: number) {
    this.uniforms.uGlow = value
  }

  setTint(r: number, g: number, b: number): void {
    (this.uniforms.uTint as Float32Array).set([r, g, b])
  }
}
