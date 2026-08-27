// 배경.
//
// **화면이 멈춰 있으면 게임이 죽어 보입니다.** 배경은 늘 흐르고, 국면에 따라 색과 세기가
// 바뀝니다 — 보스 라운드에서 붉어지고, 점수가 커지면 빨라집니다.
//
// 값싼 도메인 워핑 노이즈입니다. 프랙탈처럼 보이는 것은 노이즈를 자기 자신으로 두 번
// 접기 때문이고, 그것이 화면 전체를 채우면서도 프레임을 먹지 않는 방법입니다.

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

// 값싼 2D 노이즈. 격자에서 보간합니다.
float hash(vec2 p) {
  p = fract(p * vec2(123.34, 456.21));
  p += dot(p, p + 45.32);
  return fract(p.x * p.y);
}

float noise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
             mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

// 옥타브 넷. **여기가 프랙탈로 보이는 자리입니다** — 같은 노이즈를 배율을 올려 겹칩니다.
float fbm(vec2 p) {
  float value = 0.0;
  float amplitude = 0.5;
  for (int i = 0; i < 4; i++) {
    value += amplitude * noise(p);
    p *= 2.03;
    amplitude *= 0.5;
  }
  return value;
}

void main(void) {
  vec2 uv = vTextureCoord;
  vec2 p = vec2((uv.x - 0.5) * uAspect, uv.y - 0.5) * 2.4;

  float t = uTime * (0.06 + uHeat * 0.22);

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
    float d = length(vec2((uv.x - 0.5) * uAspect, uv.y - 0.5));
    float ring = smoothstep(0.09, 0.0, abs(d - (1.0 - uPulse) * 1.05));
    color += uGlow * (uPulse * (0.45 + 1.1 * bands) + ring * uPulse * 1.5);
  }

  // 가운데가 밝고 가장자리가 어둡습니다. 시선이 판에 머무릅니다.
  float vignette = 1.0 - smoothstep(0.35, 0.95, length(vec2((uv.x - 0.5) * uAspect, uv.y - 0.5)));
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
          uInk: { value: new Float32Array([0.031, 0.075, 0.055]), type: 'vec3<f32>' },
          uGlow: { value: new Float32Array([0.25, 0.85, 0.55]), type: 'vec3<f32>' },
        },
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
