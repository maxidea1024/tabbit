// 한 방 먹었을 때의 화면.
//
// **색수차입니다.** 큰 값이 들어올 때 화면의 빨강과 파랑이 잠깐 어긋났다가 돌아옵니다 —
// 흔들림만으로는 「크다」가 덜 읽히고, 이것이 붙으면 한 방이 됩니다.
//
// 세기의 상한은 `Const_Feel` 의 `ChromaticMaxPx` 이므로 데이터입니다.

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
uniform float uAmount;   // 픽셀
uniform vec2 uSize;

void main(void) {
  if (uAmount < 0.01) {
    finalColor = texture(uTexture, vTextureCoord);
    return;
  }

  // 가운데에서 멀수록 크게 어긋납니다. 화면 밖으로 밀려나는 느낌이 됩니다.
  vec2 middle = vTextureCoord - 0.5;
  vec2 shift = middle * (uAmount / uSize.x) * 6.0;

  float r = texture(uTexture, vTextureCoord + shift).r;
  vec4 g = texture(uTexture, vTextureCoord);
  float b = texture(uTexture, vTextureCoord - shift).b;

  finalColor = vec4(r, g.g, b, g.a);
}
`

export class PunchFilter extends Filter {
  constructor(width: number, height: number) {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      resources: {
        punchUniforms: {
          uAmount: { value: 0, type: 'f32' },
          uSize: { value: new Float32Array([width, height]), type: 'vec2<f32>' },
        },
      },
    })
  }

  private get uniforms(): Record<string, number> {
    return this.resources.punchUniforms.uniforms as Record<string, number>
  }

  /** 한 방. 값이 클수록 크게 어긋납니다. */
  hit(amount: number): void {
    this.uniforms.uAmount = Math.max(this.uniforms.uAmount, amount)
  }

  advance(seconds: number): void {
    this.uniforms.uAmount = Math.max(0, this.uniforms.uAmount - seconds * 14)
  }

  get quiet(): boolean {
    return this.uniforms.uAmount < 0.01
  }
}
