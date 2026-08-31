// 칩과 배수가 타오르는 것.
//
// **이 게임에서 가장 중요한 두 숫자입니다.** 배수가 3일 때와 300일 때 같은 칸에 같은
// 모습으로 앉아 있으면, 300이 큰 값이라는 것이 화면 어디에도 없습니다.
//
// 그래서 값이 커지면 칸 뒤에서 불이 오릅니다. 세기는 `juice.ts` 의 `intensityOf` 이므로
// 흔들림 · 숫자 크기 · 음높이와 **같은 하나의 값**을 봅니다 — 채널마다 따로 재면 어느
// 하나만 세게 반응하는 화면이 됩니다.
//
// 불은 값싼 층 노이즈 셋입니다. 위로 흐르는 좌표에 노이즈를 얹고, 아래가 밝고 위가 사라지는
// 기울기로 잘라 냅니다.

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
/** 0..1. 배수가 클수록 올라갑니다. */
uniform float uHeat;
/** 불의 색. 칩은 푸르고 배수는 붉습니다. */
uniform vec3 uCool;
uniform vec3 uHot;

float hash(vec2 p) {
  return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

/** 값 노이즈 하나. 격자의 네 귀를 부드럽게 섞습니다. */
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

/** 층 셋. 넷째 층부터는 눈에 보이지 않고 값만 듭니다. */
float layers(vec2 p) {
  float sum = noise(p) * 0.55;
  sum += noise(p * 2.1 + 11.0) * 0.30;
  sum += noise(p * 4.3 + 27.0) * 0.15;
  return sum;
}

void main(void) {
  if (uHeat <= 0.002) {
    finalColor = vec4(0.0);
    return;
  }

  vec2 uv = vTextureCoord;

  // 위로 흐릅니다. 빠르기가 세기를 따라가야 큰 값이 사납게 보입니다.
  vec2 flow = vec2(uv.x * 3.2, uv.y * 2.6 + uTime * (1.1 + uHeat * 1.6));
  float n = layers(flow);

  // 아래가 뿌리이고 위로 갈수록 사라집니다. **세기가 곧 불길의 높이입니다.**
  //
  // 걸러내기의 세로 좌표는 **위가 0** 입니다. 그것을 뒤집지 않으면 뿌리가 위에 서고 불이
  // 아래로 사라져, 칸 위쪽에 얼룩 하나가 떠 있는 것으로 보입니다.
  //
  // 기울기는 곧게 내려갑니다. 부드러운 계단으로 두면 뿌리 근처에만 몰려, 칸을 지나기 전에
  // 다 사라집니다.
  float reach = mix(0.40, 1.0, uHeat);
  float up = (1.0 - uv.y) / reach;
  float body = clamp(1.0 - up * 0.82, 0.0, 1.0);

  // 좌우 끝은 좁힙니다. 칸을 넘어 번지면 불이 아니라 색판이 됩니다.
  float sides = smoothstep(0.0, 0.16, uv.x) * smoothstep(1.0, 0.84, uv.x);

  // 맨 아래도 사라집니다. **끊긴 자리가 있으면 불이 아니라 잘린 그림으로 보입니다.**
  float foot = smoothstep(1.0, 0.90, uv.y);

  float fire = clamp(n * body * sides * foot * (0.9 + uHeat * 1.3) - 0.12, 0.0, 1.0);
  if (fire <= 0.002) {
    finalColor = vec4(0.0);
    return;
  }

  // 뿌리가 뜨겁고 끝이 식습니다.
  vec3 tint = mix(uCool, uHot, clamp(fire * 1.6, 0.0, 1.0));
  float alpha = clamp(fire * (0.55 + uHeat * 0.75), 0.0, 0.95);

  // 더해지는 빛입니다. 알파를 곱한 채로 내보냅니다.
  finalColor = vec4(tint * alpha, alpha);
}
`

export class FlameFilter extends Filter {
  constructor(cool: [number, number, number], hot: [number, number, number]) {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      resources: {
        flameUniforms: {
          uTime: { value: 0, type: 'f32' },
          uHeat: { value: 0, type: 'f32' },
          uCool: { value: new Float32Array(cool), type: 'vec3<f32>' },
          uHot: { value: new Float32Array(hot), type: 'vec3<f32>' },
        },
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.flameUniforms.uniforms as Record<string, number | Float32Array>
  }

  /** 지금 얼마나 뜨거운가. 0 이면 아무것도 그리지 않습니다. */
  set heat(value: number) {
    this.uniforms.uHeat = Math.max(0, Math.min(1, value))
  }

  get heat(): number {
    return this.uniforms.uHeat as number
  }

  advance(seconds: number): void {
    this.uniforms.uTime = (this.uniforms.uTime as number) + seconds
  }
}
