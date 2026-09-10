// 카드가 다른 카드가 되는 순간.
//
// **「왔다」와 다른 몸짓이어야 합니다.** 자리에 닿는 것은 카드 전체가 한 번 하얗게 번쩍이는
// 것이고(`arrive.ts`), 바뀌는 것은 있던 것이 다른 것이 되는 일입니다 — 같은 번쩍임으로
// 두면 카드가 새로 온 것인지 갈린 것인지 화면에서 갈리지 않습니다.
//
// **테두리만 답니다. 지나가는 빛줄기가 아닙니다.**
//
// 처음에는 줄기 하나가 위에서 아래로 지나가게 했는데, 뒤집기는 8분의 1초이고 줄기는
// 0.42초였습니다 — 다 뒤집혀 자리에 앉은 카드 위로 빛 막대 하나가 계속 기어갔고, 그 앞
// 8분의 1초 동안은 카드가 좁아져 있어 그 막대가 눌린 채로 그려졌습니다. **뒤집는 몸짓이
// 이미 「갈렸다」를 말하고 있으므로**, 여기서 할 일은 그 순간을 짚는 것 하나입니다.
//
// 종이의 끝에서 안쪽으로 빛이 물들고 잦아듭니다. 얼굴이 갈리는 그 절반에서 시작합니다.

import { Filter, GlProgram } from 'pixi.js'

const VERTEX = `#version 300 es
in vec2 aPosition;
out vec2 vTextureCoord;
out vec2 vLimit;

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
  vLimit = uOutputFrame.zw * uInputSize.zw;
  vTextureCoord = aPosition * vLimit;
}
`

const FRAGMENT = `#version 300 es
precision highp float;

in vec2 vTextureCoord;
in vec2 vLimit;
out vec4 finalColor;

uniform sampler2D uTexture;
/** 얼마나 세게. 1 에서 0 으로 잦아듭니다. */
uniform float uAmount;
/** 테의 색. */
uniform vec3 uTint;

void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  if (uAmount <= 0.001 || src.a < 0.004) {
    finalColor = src;
    return;
  }

  // 알파를 곱해 둔 그림입니다. 나누고 섞고 다시 곱합니다.
  vec3 color = src.rgb / src.a;

  // 카드 안에서의 자리. 0..1 입니다.
  vec2 uv = vTextureCoord / max(vLimit, vec2(0.0001));
  // **끝에서 안쪽으로.** 네 변에서의 거리 가운데 가장 가까운 것이 그 픽셀의 깊이입니다.
  float depth = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
  // 안쪽 0.16 까지만 물듭니다. 통째로 물들이면 카드가 그 색이 됩니다.
  float edge = 1.0 - smoothstep(0.0, 0.16, depth);
  // 가장자리 한 줄은 더 밝습니다 — 종이의 끝이 빛나는 것입니다.
  float lip = 1.0 - smoothstep(0.0, 0.035, depth);

  float lit = (edge * 0.55 + lip * 0.85) * uAmount;
  color = mix(color, vec3(1.0), lit * 0.35);
  color += uTint * lit;

  finalColor = vec4(color * src.a, src.a);
}
`

/**
 * 카드가 다른 카드가 되는 순간의 필터.
 *
 * **걸릴 때 만들고 잦아들면 뗍니다.** 필터 하나가 곧 렌더 텍스처 하나이고, 손패의 여덟
 * 장이 그것을 내내 들고 있을 이유가 없습니다 — `arrive.ts` 와 같은 규약입니다.
 */
export class ImprintFilter extends Filter {
  constructor(tint: [number, number, number] = [1.0, 0.82, 0.42]) {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      padding: 3,
      resolution: 'inherit',
      resources: {
        imprintUniforms: {
          uAmount: { value: 0, type: 'f32' },
          uTint: { value: new Float32Array(tint), type: 'vec3<f32>' },
        },
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.imprintUniforms.uniforms as Record<string, number | Float32Array>
  }

  /** 얼마나 세게. */
  set amount(value: number) {
    this.uniforms.uAmount = Math.max(0, value)
  }

  setTint(r: number, g: number, b: number): void {
    const tint = this.uniforms.uTint as Float32Array
    tint[0] = r
    tint[1] = g
    tint[2] = b
  }
}
