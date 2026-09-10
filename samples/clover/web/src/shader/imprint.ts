// 카드가 다른 카드가 되는 순간.
//
// **「왔다」와 다른 몸짓이어야 합니다.** 자리에 닿는 것은 한 번 하얗게 번쩍이는 것이고
// (`arrive.ts`), 바뀌는 것은 있던 것이 다른 것이 되는 일입니다 — 같은 번쩍임으로 두면
// 카드가 새로 온 것인지 갈린 것인지 화면에서 갈리지 않습니다.
//
// 셋이 한 필터에 있습니다.
//
// |무엇|어떻게|
// |--|--|
// |가로지르는 빛줄기|위에서 아래로 한 번 지나갑니다. 지나간 자리가 밝습니다|
// |가장자리의 테|줄기가 지나는 동안 종이의 가장자리가 그 색으로 탑니다|
// |색이 갈리는 것|줄기의 앞뒤로 색이 아주 조금 어긋납니다 — 갈리는 순간의 흔적입니다|
//
// **뒤집기와 함께 씁니다.** 카드는 그 자리에서 한 번 뒤집혀 다른 얼굴로 돌아오고
// (`CardView.turnInto`), 이 필터가 그 절반에 맞춰 지나갑니다.

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
/** 0 이면 아무 일도 없고, 0 에서 1 로 지나가는 동안 줄기가 위에서 아래로 갑니다. */
uniform float uSweep;
/** 얼마나 세게. 0 이면 꺼집니다. */
uniform float uAmount;
/** 줄기와 테의 색. */
uniform vec3 uTint;

void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  if (uAmount <= 0.001 || src.a < 0.004) {
    finalColor = src;
    return;
  }

  // 카드 안에서의 세로 자리. 0 이 윗변이고 1 이 아랫변입니다.
  float y = vTextureCoord.y / max(vLimit.y, 0.0001);

  // **줄기는 카드 밖에서 들어와 카드 밖으로 나갑니다.** 0 에서 시작하면 윗변에서 태어나고
  // 1 에서 끝나면 아랫변에서 죽어, 지나간 것이 아니라 켜졌다 꺼진 것으로 보입니다.
  float head = uSweep * 1.4 - 0.2;
  float band = 1.0 - smoothstep(0.0, 0.16, abs(y - head));

  // 지나간 자리는 아직 조금 남아 있습니다. **꼬리가 없으면 줄 하나가 튀는 것으로 보입니다.**
  float tail = smoothstep(0.34, 0.0, head - y) * step(y, head);

  // 알파를 곱해 둔 그림입니다. 나누고 섞고 다시 곱합니다.
  vec3 color = src.rgb / src.a;

  // 줄기가 지나는 자리는 그 색으로 밝아지고, 한가운데는 흰빛까지 갑니다.
  float lit = (band * 0.9 + tail * 0.22) * uAmount;
  color = mix(color, vec3(1.0), lit * 0.55);
  color += uTint * lit * 0.8;

  // **가장자리의 테.** 종이의 끝에서 안쪽으로 조금만 — 통째로 물들이면 카드가 그 색이
  // 됩니다. 알파의 기울기가 곧 가장자리입니다.
  vec2 step2 = 1.5 / max(vLimit, vec2(1.0)) * vLimit;
  float around =
      texture(uTexture, vTextureCoord + vec2(step2.x, 0.0)).a
    + texture(uTexture, vTextureCoord - vec2(step2.x, 0.0)).a
    + texture(uTexture, vTextureCoord + vec2(0.0, step2.y)).a
    + texture(uTexture, vTextureCoord - vec2(0.0, step2.y)).a;
  float rim = clamp(src.a * 4.0 - around, 0.0, 1.0);
  color += uTint * rim * uAmount * 0.9;

  finalColor = vec4(color * src.a, src.a);
}
`

/**
 * 카드가 다른 카드가 되는 순간의 필터.
 *
 * **탈 때 만들고 다 지나가면 뗍니다.** 필터 하나가 곧 렌더 텍스처 하나이고, 손패의 여덟
 * 장이 그것을 내내 들고 있을 이유가 없습니다 — `arrive.ts` 와 같은 규약입니다.
 */
export class ImprintFilter extends Filter {
  constructor(tint: [number, number, number] = [1.0, 0.82, 0.42]) {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      padding: 4,
      resolution: 'inherit',
      resources: {
        imprintUniforms: {
          uSweep: { value: 0, type: 'f32' },
          uAmount: { value: 0, type: 'f32' },
          uTint: { value: new Float32Array(tint), type: 'vec3<f32>' },
        },
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.imprintUniforms.uniforms as Record<string, number | Float32Array>
  }

  /** 줄기가 어디까지 갔는가. 0 에서 1 로 한 번 지나갑니다. */
  set sweep(value: number) {
    this.uniforms.uSweep = Math.max(0, Math.min(1, value))
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
