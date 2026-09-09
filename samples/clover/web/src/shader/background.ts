// 배경.
//
// **화면이 멈춰 있으면 게임이 죽어 보입니다.** 배경은 늘 흐르고, 국면에 따라 색과 세기가
// 바뀝니다 — 보스 라운드에서 붉어지고, 점수가 커지면 빨라집니다.
//
// 도메인 워핑 노이즈입니다. 프랙탈처럼 보이는 것은 노이즈를 자기 자신으로 두 번
// 접기 때문이고, 그것이 화면 전체를 채우면서도 프레임을 먹지 않는 방법입니다.
//
// **노이즈는 그림으로 읽습니다.** 격자 노이즈 한 번이 해시 넷이고 옥타브 넷을 일곱 번
// 접으면 픽셀마다 해시 112번이었습니다 — 화면 전체에 매 프레임입니다. 같은 자리를 그림
// 열네 번 읽는 것으로 바꿨고, 그림이 이미 결을 여러 겹 가지고 있어 옥타브는 둘로 됩니다.
// 어디서 온 그림인지는 `public/noise/readme.md` 에 있습니다.

import { Filter, GlProgram } from 'pixi.js'

import { noiseResources } from './noise'

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
  vTextureCoord = aPosition;
}
`

const FRAGMENT = `#version 300 es
precision highp float;

in vec2 vTextureCoord;
out vec4 finalColor;

uniform float uTime;
uniform float uHeat;       // 0..1. 점수가 클수록 올라갑니다.
uniform float uPulse;      // 0..1. 한 방 먹으면 1이 되고 곧 줄어듭니다.
uniform vec3  uInk;        // 바탕색
uniform vec3  uGlow;       // 무늬의 색
uniform float uAspect;
uniform vec2  uCenter;     // 고리가 퍼져 나가는 자리. 0..1 의 화면 좌표입니다.

uniform sampler2D uSoft;

// 노이즈 그림 한 번. **값을 펴서 씁니다** — 그림은 0.17~0.61 에 몰려 있고, 아래의 등고선
// 문턱은 0..1 에 고르게 퍼진 값을 전제로 잡은 것입니다.
float noise(vec2 p) {
  return clamp((texture(uSoft, p * 0.16).r - 0.384) * 2.0 + 0.5, 0.0, 1.0);
}

// 옥타브 둘. **여기가 프랙탈로 보이는 자리입니다** — 같은 그림을 배율을 올려 겹칩니다.
// 그림이 이미 결을 여러 겹 가지고 있어 둘로 충분합니다.
float fbm(vec2 p) {
  return noise(p) * 0.64 + noise(p * 2.03 + 3.7) * 0.36;
}

void main(void) {
  vec2 uv = vTextureCoord;
  vec2 p = vec2((uv.x - 0.5) * uAspect, uv.y - 0.5) * 2.4;
  // 가운데에서의 거리. **아래의 가장자리 떨굼이 같은 길이를 다시 재고 있었습니다.**
  float middle = length(p) * (1.0 / 2.4);

  // 흐르는 빠르기. **열기가 올라도 조금만 빨라집니다** — 크게 걸면 점수가 오를 때마다
  // 배경이 딴 화면처럼 됩니다.
  float t = uTime * (0.05 + uHeat * 0.09);

  // 도메인 워핑 — 노이즈의 좌표를 노이즈로 밉니다. 무늬가 접히는 것이 이것입니다.
  vec2 q = vec2(fbm(p + vec2(0.0, t)), fbm(p + vec2(5.2, 1.3 - t)));
  vec2 r = vec2(fbm(p + 3.0 * q + vec2(1.7, 9.2) + 0.15 * t),
                fbm(p + 3.0 * q + vec2(8.3, 2.8) - 0.12 * t));
  float f = fbm(p + 3.0 * r);

  // 등고선 하나. 무늬가 「층」으로 보이게 합니다.
  float bands = smoothstep(0.42, 0.72, f) - smoothstep(0.74, 0.94, f);

  vec3 color = uInk;
  color += uGlow * (0.16 + 0.5 * uHeat) * bands;
  color += uGlow * 0.06 * f;

  // **한 방.** 무늬가 통째로 밝아지고 고리가 가운데에서 바깥으로 퍼집니다. 화면 흔들림만
  // 있으면 「움직였다」로 읽히고, 배경이 같이 밝아지면 「터졌다」로 읽힙니다.
  if (uPulse > 0.002) {
    // **화면의 가운데가 아니라 판의 가운데입니다.** 왼쪽 판이 280픽셀을 쓰므로 카드가
    // 놓이는 자리의 가운데는 화면의 가운데보다 오른쪽이고, 고리가 화면 가운데에서 퍼지면
    // 그 한 방이 카드에서 난 것으로 읽히지 않습니다 — 환희의 기가 모이는 자리와 같은
    // 자리이고, 부르는 쪽이 그 둘에 같은 값을 넘깁니다.
    float d = length(vec2((uv.x - uCenter.x) * uAspect, uv.y - uCenter.y));
    // 퍼져 나가는 끝은 1.25 입니다. **가운데가 옮겨진 만큼 늘립니다** — 판의 가운데에서
    // 화면의 왼쪽 아래 귀퉁이까지가 1.19 이고, 1.05 로는 고리가 그 앞에서 멈춥니다.
    float ring = smoothstep(0.09, 0.0, abs(d - (1.0 - uPulse) * 1.25));
    color += uGlow * (uPulse * (0.45 + 1.1 * bands) + ring * uPulse * 1.5);
  }

  // 가운데가 밝고 가장자리가 어둡습니다. 시선이 판에 머무릅니다.
  float vignette = 1.0 - smoothstep(0.35, 0.95, middle);
  color *= 0.55 + 0.75 * vignette;

  finalColor = vec4(color, 1.0);
}
`

export class BackgroundFilter extends Filter {
  constructor() {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      resources: {
        backgroundUniforms: {
          uTime: { value: 0, type: 'f32' },
          uHeat: { value: 0, type: 'f32' },
          uPulse: { value: 0, type: 'f32' },
          uAspect: { value: 16 / 9, type: 'f32' },
          uCenter: { value: new Float32Array([0.5, 0.5]), type: 'vec2<f32>' },
          uInk: { value: new Float32Array([0.031, 0.075, 0.055]), type: 'vec3<f32>' },
          uGlow: { value: new Float32Array([0.25, 0.85, 0.55]), type: 'vec3<f32>' },
        },
        ...noiseResources({ uSoft: 'soft' }),
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.backgroundUniforms.uniforms as Record<string, number | Float32Array>
  }

  advance(seconds: number): void {
    this.uniforms.uTime = (this.uniforms.uTime as number) + seconds
    this.uniforms.uPulse = Math.max(0, (this.uniforms.uPulse as number) - seconds * 2.4)
  }

  /**
   * 고리가 퍼져 나가는 자리. **판의 가운데입니다.**
   *
   * 화면의 좌표가 아니라 0..1 의 비율이고, 부르는 쪽이 판의 자리를 화면 크기로 나눠 넘깁니다.
   */
  setCenter(x: number, y: number): void {
    (this.uniforms.uCenter as Float32Array).set([x, y])
  }

  /** 한 방. 큰 값이 들어오면 배경이 밝아지고 고리가 퍼집니다. */
  pulse(amount: number): void {
    this.uniforms.uPulse = Math.min(1, Math.max(this.uniforms.uPulse as number, amount))
  }

  /** 국면이 색을 정합니다. 보스는 붉고 상점은 푸릅니다. */
  setMood(ink: [number, number, number], glow: [number, number, number]): void {
    (this.uniforms.uInk as Float32Array).set(ink)
    ;(this.uniforms.uGlow as Float32Array).set(glow)
  }

  /** 점수가 클수록 배경이 빨라지고 밝아집니다. */
  setHeat(heat: number): void {
    const current = this.uniforms.uHeat as number
    this.uniforms.uHeat = current + (heat - current) * 0.08
  }

  setAspect(aspect: number): void {
    this.uniforms.uAspect = aspect
  }
}
