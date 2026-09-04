// 기를 모으는 배경.
//
// **환희의 순간에 [배경](background.ts)을 대신하는 겹입니다.** 칩과 배수의 곱이 문턱을
// 넘으면 프랙탈 위에 이 겹이 겹쳐 오르고, 점수가 정산되는 순간에 터진 뒤 물러납니다.
// 문턱과 상태는 `render/euphoria.ts` 가 정하고 여기는 그림만 냅니다.
//
// **그림 파일이 아닙니다.** 같은 연출을 영상으로 두면 파일 하나가 배경 셰이더 전체보다
// 크고, 라운드의 색(보스는 붉고 상점은 푸릅니다)을 따라가지 못합니다 — 모이는 자리도
// 낸 카드가 놓인 자리여야 하므로 고정된 화면으로는 맞지 않습니다.
//
// 네 가지가 겹쳐 있습니다 — 안쪽으로 흐르는 집중선 · 가운데의 핵 · 핵에서 퍼지는 고리 ·
// 터질 때의 방사선.

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
uniform float uCharge;   // 0..1. 모으는 정도. 문턱을 넘은 뒤로 계속 오릅니다.
uniform float uBurst;    // 0..1. 터진 순간 1이 되고 잦아듭니다.
uniform float uFade;     // 0..1. 이 겹의 짙기. 프랙탈과 겹쳐 넘어가는 값입니다.
uniform vec2  uCenter;   // 기가 모이는 자리. 0..1 의 화면 좌표입니다.
uniform vec3  uInk;      // 바탕색. 배경과 같은 값을 받습니다.
uniform vec3  uGlow;     // 기의 색
uniform float uAspect;

float hash(vec2 p) {
  vec3 q = fract(vec3(p.xyx) * 0.1031);
  q += dot(q, q.yzx + 33.33);
  return fract((q.x + q.y) * q.z);
}

float noise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
             mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

const float TAU = 6.28318530718;

void main(void) {
  vec2 uv = vTextureCoord;
  vec2 p = vec2((uv.x - uCenter.x) * uAspect, uv.y - uCenter.y);
  float r = length(p);
  float a = atan(p.y, p.x);

  // 핵의 둘레가 고르게 둥글면 그려 놓은 원으로 보입니다. 각도에 따라 조금 일그러뜨립니다.
  float wobble = noise(vec2(a * 2.4, uTime * 1.3)) - 0.5;
  float rc = r * (1.0 + 0.16 * wobble);

  // **집중선.** 각도를 96칸으로 나누고 칸마다 다른 빠르기로 안쪽으로 흐릅니다.
  //
  // 안쪽으로 흐르게 하려면 위상에 시간을 **더합니다** — 빼면 같은 무늬가 바깥으로
  // 흐르고, 그러면 기를 모으는 것이 아니라 뿜는 것으로 보입니다.
  float band = a / TAU + 0.5;
  float slot = floor(band * 96.0);
  float seed = hash(vec2(slot, 3.0));
  float across = abs(fract(band * 96.0) - 0.5) * 2.0;
  float thin = smoothstep(1.0, 0.35, across);
  float flow = fract(r * 1.7 + uTime * (0.75 + seed * 1.5) + seed);
  // 꼬리 하나. 앞이 밝고 뒤로 길게 늘어집니다.
  float lines = thin * pow(flow, 5.0) * smoothstep(0.05, 0.42, r);

  // **핵.** 모으는 정도만큼 자라고 가운데가 흽니다.
  //
  // **판을 덮지 않는 크기입니다.** 화면의 높이가 1 이므로 0.2 는 160픽셀이고, 낸 카드가
  // 놓인 자리를 감싸는 데 그만하면 됩니다 — 더 키우면 그 카드가 흰 덩어리 안으로 들어가
  // 무엇을 냈는지가 보이지 않습니다.
  float grown = 0.030 + 0.050 * uCharge;
  float core = smoothstep(grown * 2.6, 0.0, rc);
  float hot = smoothstep(grown * 0.7, 0.0, rc);

  // **핵에서 퍼지는 고리 둘.** 모으는 동안에도 무언가가 나가야 갇혀 있는 힘으로 보입니다.
  float rings = 0.0;
  for (int i = 0; i < 2; i++) {
    float phase = fract(uTime * 0.62 + float(i) * 0.5);
    rings += smoothstep(0.03, 0.0, abs(r - phase * 0.85)) * (1.0 - phase);
  }

  // **번개.** 핵 둘레에서 실 하나가 지직거립니다.
  //
  // **각도로 잘게 흔듭니다.** 낮은 배율로 두면 화면을 가로지르는 굵은 곡선 하나가 되고,
  // 그것은 번개가 아니라 그려 놓은 줄로 보입니다.
  float bolt = noise(vec2(a * 13.0, uTime * 3.2));
  float arc = smoothstep(0.014, 0.0, abs(r - (grown * 1.8 + 0.20 * bolt))) * uCharge;

  // 기의 색. 라운드의 색에 흰빛을 섞습니다 — 순색만 쓰면 붉은 라운드에서 피처럼 보입니다.
  vec3 energy = mix(uGlow, vec3(1.0), 0.34);

  // 바탕은 배경보다 어둡습니다. **모이는 것이 밝게 읽히려면 그 밖이 물러나야 합니다.**
  vec3 color = uInk * (0.34 + 0.22 * uCharge);
  color += energy * lines * (0.45 + 0.75 * uCharge);
  color += energy * rings * 0.8;
  color += energy * arc * 1.3;
  color += mix(energy, vec3(1.0), 0.45) * core * (0.55 + 1.0 * uCharge);
  color += hot * (0.45 + 0.8 * uCharge);

  // **터짐.** 고리 하나가 화면 밖으로 나가고 방사선이 함께 돕니다.
  if (uBurst > 0.002) {
    float front = 1.0 - uBurst;
    float shock = smoothstep(0.13, 0.0, abs(r - front * 1.5));
    float rays = pow(abs(sin(a * 22.0 + uTime * 2.4)), 8.0) * smoothstep(0.0, 0.55, r);
    color += energy * (rays * uBurst * 0.8 + shock * 1.4);
    // **흰빛은 아낍니다.** 정산하는 순간에는 화면 번쩍임과 흔들림이 이미 함께 오고,
    // 여기서 화면을 흰색으로 채우면 그 한 순간에 판이 통째로 사라집니다.
    color += vec3(uBurst * uBurst * 0.45);
  }

  float vignette = 1.0 - smoothstep(0.40, 1.00, r);
  color *= 0.52 + 0.72 * vignette;

  // **미리 곱한 알파입니다.** 이 겹이 프랙탈 위에 얹히므로 짙기를 스스로 들고 있어야
  // 하고, Pixi 가 그리는 그림은 알파를 미리 곱한 것입니다.
  finalColor = vec4(color * uFade, uFade);
}
`

export class SurgeFilter extends Filter {
  constructor() {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      resources: {
        surgeUniforms: {
          uTime: { value: 0, type: 'f32' },
          uCharge: { value: 0, type: 'f32' },
          uBurst: { value: 0, type: 'f32' },
          uFade: { value: 0, type: 'f32' },
          uAspect: { value: 16 / 9, type: 'f32' },
          uCenter: { value: new Float32Array([0.5, 0.5]), type: 'vec2<f32>' },
          uInk: { value: new Float32Array([0.031, 0.075, 0.055]), type: 'vec3<f32>' },
          uGlow: { value: new Float32Array([0.25, 0.85, 0.55]), type: 'vec3<f32>' },
        },
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.surgeUniforms.uniforms as Record<string, number | Float32Array>
  }

  advance(seconds: number): void {
    this.uniforms.uTime = (this.uniforms.uTime as number) + seconds
  }

  /** 모으는 정도 · 터짐 · 짙기. 세 값을 한 번에 받습니다. */
  setLevels(charge: number, burst: number, fade: number): void {
    this.uniforms.uCharge = charge
    this.uniforms.uBurst = burst
    this.uniforms.uFade = fade
  }

  /** 배경과 같은 색을 받습니다. 보스 라운드의 기는 붉습니다. */
  setMood(ink: [number, number, number], glow: [number, number, number]): void {
    (this.uniforms.uInk as Float32Array).set(ink)
    ;(this.uniforms.uGlow as Float32Array).set(glow)
  }

  /** 기가 모이는 자리. 낸 카드가 놓인 자리입니다. */
  setCenter(x: number, y: number): void {
    (this.uniforms.uCenter as Float32Array).set([x, y])
  }

  setAspect(aspect: number): void {
    this.uniforms.uAspect = aspect
  }
}
