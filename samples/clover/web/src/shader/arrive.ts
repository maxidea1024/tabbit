// 산 것이 오는 동안.
//
// **두 가지가 한 필터에 있습니다** — 오는 동안 울렁이고, 자리에 닿을 때 한 번 하얗게
// 번쩍입니다. 둘이 잇달아 일어나는 한 몸짓이라 나누면 두 필터를 겹쳐 걸어야 하고, 그러면
// 같은 그림을 두 번 굽습니다.
//
// **조각을 터뜨리지 않습니다.** 조각 몇 개는 카드 뒤에서 흩어질 뿐이라 무엇을 산 것인지가
// 남지 않습니다 — 산 그 물건이 직접 움직이는 것이 「샀다」입니다.

import { Filter, GlProgram } from 'pixi.js'

const VERTEX = `#version 300 es
in vec2 aPosition;
out vec2 vTextureCoord;
/** 이 그림이 텍스처 안에서 차지하는 끝. 울렁이며 이 밖을 읽으면 옆의 것이 묻어 나옵니다. */
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
/** 울렁이는 세기. 오는 동안 1 에서 0 으로 잦아듭니다. */
uniform float uWarp;
/** 번쩍이는 세기. 닿는 순간 1 이 되고 곧 0 으로 갑니다. */
uniform float uFlash;
/** 번쩍임의 색. 흰빛에 이 색이 섞입니다. */
uniform vec3 uTint;
uniform float uTime;

void main(void) {
  vec2 uv = vTextureCoord;

  // 가로와 세로가 서로 다른 빠르기로 흔들립니다. **같은 빠르기면 통째로 미끄러지는
  // 것으로 보이고, 울렁이는 것은 안쪽이 서로 다르게 밀릴 때 보입니다.**
  if (uWarp > 0.001) {
    vec2 push = vec2(
      sin(uv.y * 17.0 + uTime * 15.0),
      sin(uv.x * 13.0 + uTime * 11.0));
    uv = clamp(uv + push * uWarp * 0.022 * vLimit, vec2(0.0), vLimit);
  }

  vec4 src = texture(uTexture, uv);
  if (uFlash <= 0.001 || src.a < 0.004) {
    finalColor = src;
    return;
  }

  // 알파를 곱해 둔 그림입니다. **나누고 섞고 다시 곱합니다** — 그대로 섞으면 반투명한
  // 자리가 먼저 하얘져서 테두리만 빛나는 것으로 보입니다.
  vec3 color = src.rgb / src.a;
  color = mix(color, vec3(1.0), uFlash * 0.88);
  // 흰빛 위에 색을 조금 더 얹습니다. 그래야 「빛났다」이지 「지워졌다」가 아닙니다.
  color += uTint * uFlash * 0.55;
  finalColor = vec4(color * src.a, src.a);
}
`

export class ArriveFilter extends Filter {
  constructor(tint: [number, number, number] = [1.0, 0.86, 0.36]) {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      // **울렁이면 그림이 자기 자리 밖으로 나갑니다.** 여백이 없으면 그만큼 잘립니다.
      padding: 6,
      // 화면의 배율로. 이유는 `editions.ts` 에 있습니다.
      resolution: 'inherit',
      resources: {
        arriveUniforms: {
          uWarp: { value: 0, type: 'f32' },
          uFlash: { value: 0, type: 'f32' },
          uTint: { value: new Float32Array(tint), type: 'vec3<f32>' },
          uTime: { value: 0, type: 'f32' },
        },
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.arriveUniforms.uniforms as Record<string, number | Float32Array>
  }

  set warp(value: number) {
    this.uniforms.uWarp = Math.max(0, value)
  }

  set flash(value: number) {
    this.uniforms.uFlash = Math.max(0, value)
  }

  /**
   * 시계를 넘깁니다.
   *
   * **전역 시간입니다.** 울렁이는 것은 처음과 끝이 있는 한 번짜리지만, 그 안의 물결은
   * 덧칠하는 것이라 각자 시계를 돌릴 이유가 없습니다.
   */
  at(time: number): void {
    this.uniforms.uTime = time
  }
}
