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

/**
 * 프랙탈 잡음.
 *
 * **층마다 칸이 절반이고 세기가 절반입니다.** 큰 결 위에 작은 결이 얹히고 그 위에 더 작은
 * 결이 얹히므로, 가까이 봐도 멀리 봐도 같은 결이 보입니다 — 불꽃과 연기와 구름이 그렇게
 * 생겼습니다.
 *
 * 층마다 좌표를 조금 옮깁니다. 옮기지 않으면 층들의 격자가 같은 자리에서 겹쳐, 그 자리에
 * 가로세로 줄이 보입니다.
 */
float fbm(vec2 p) {
  float sum = 0.0;
  float amp = 0.5;
  for (int i = 0; i < 5; i++) {
    sum += noise(p) * amp;
    p = p * 2.03 + vec2(11.3, 7.7);
    amp *= 0.5;
  }
  return sum;
}

void main(void) {
  if (uHeat <= 0.002) {
    finalColor = vec4(0.0);
    return;
  }

  vec2 uv = vTextureCoord;

  // 위로 흐릅니다. 빠르기가 세기를 따라가야 큰 값이 사납게 보입니다.
  // 위로 흐릅니다. **가로를 촘촘하게, 세로를 느슨하게** — 가로로 칸이 여럿이어야 혀가
  // 여럿 서고, 세로가 느슨해야 그 혀가 길게 늘어납니다.
  vec2 p = vec2(uv.x * 4.6, uv.y * 1.6 - uTime * (1.5 + uHeat * 1.1));

  // **좌표를 좌표로 휩니다.** 잡음 하나로 다른 잡음의 자리를 밀면 결이 말리고 갈라집니다 —
  // 프랙탈 불꽃이 곱슬거리는 것이 이것이고, **층만 쌓아서는 나오지 않습니다.** 층만 쌓은
  // 것은 결이 고르게 깔린 얼룩이고, 그것을 문턱으로 자르면 톱니 달린 껍질이 됩니다.
  //
  // 미는 두 잡음이 서로 다른 빠르기로 흐릅니다. 같이 흐르면 그림 하나가 미끄러질 뿐입니다.
  vec2 warp = vec2(
    fbm(p * 0.7 + vec2(0.0, uTime * 0.8)),
    fbm(p * 0.7 + vec2(5.2, uTime * 1.25 + 1.3)));
  float n = fbm(p + warp * (1.15 + uHeat * 0.6));

  // 아래가 뿌리이고 위로 갈수록 사라집니다. **세기가 곧 불길의 높이입니다.**
  //
  // 걸러내기의 세로 좌표는 **위가 0** 입니다. 그것을 뒤집지 않으면 뿌리가 위에 서고 불이
  // 아래로 사라져, 칸 위쪽에 얼룩 하나가 떠 있는 것으로 보입니다.
  //
  // 기울기는 곧게 내려갑니다. 부드러운 계단으로 두면 뿌리 근처에만 몰려, 칸을 지나기 전에
  // 다 사라집니다.
  float reach = mix(0.40, 1.0, uHeat);
  float up = (1.0 - uv.y) / reach;
  // **위로 갈수록 빠르게 죽습니다.** 느리게 죽으면 문턱을 넘는 자리가 넓은 판이 되고,
  // 그것이 혀가 아니라 두꺼운 껍질로 보이던 까닭입니다 — 빠르게 죽으면 잡음의 봉우리만
  // 위까지 살아남아 그것이 혀가 됩니다.
  // **제곱으로 죽습니다.** 곧게 내려가면 문턱을 넘는 자리가 넓은 판이 되고, 제곱이면
  // 봉우리만 위까지 살아남아 끝이 뾰족해집니다.
  float body = clamp(1.0 - up * up * 0.72 - up * 0.34, 0.0, 1.0);

  // 좌우 끝만 살짝 좁힙니다. **넓게 좁히면 불이 상자 가운데에만 서서 얹은 것으로 보입니다** —
  // 상자의 윗변 전체에 붙어야 그 상자가 타는 것입니다.
  float sides = smoothstep(0.0, 0.05, uv.x) * smoothstep(1.0, 0.95, uv.x);

  // 맨 아래도 사라집니다. **끊긴 자리가 있으면 불이 아니라 잘린 그림으로 보입니다.**
  float foot = smoothstep(1.0, 0.90, uv.y);

  // **여기서 층을 끊습니다.** 부드럽게 옅어지는 불은 화면에서 흐리멍덩한 얼룩입니다 —
  // 게임의 불은 테두리가 분명한 혀 여럿이고, 안쪽이 밝은 층 셋으로 갈립니다. 문턱 하나로
  // 자르고 층마다 색을 갈아 끼우면 그 모습이 됩니다.
  float field = n * body * sides * foot * (0.9 + uHeat * 1.3);

  // 층의 문턱 셋. 바깥이 붉고 가운데가 주황이고 안쪽이 흽니다.
  // **문턱이 높아야 혀가 갈라집니다.** 낮으면 잡음의 대부분이 문턱을 넘어 아랫부분이
  // 통째로 채워지고, 그러면 혀가 아니라 상자 위에 얹은 납작한 뚜껑이 됩니다.
  float edge = 0.42;
  float mid = 0.51;
  float core = 0.62;
  // 가장자리를 아주 좁게만 부드럽게 합니다 — 0 이면 톱니가 서고, 넓으면 도로 흐려집니다.
  float soft = 0.026;

  float lit = smoothstep(edge - soft, edge + soft, field);
  if (lit <= 0.002) {
    finalColor = vec4(0.0);
    return;
  }

  // 층. **섞지 않고 갈아 끼웁니다** — 섞으면 그 사이가 다시 그라디언트가 됩니다.
  vec3 tint = uCool;
  tint = mix(tint, uHot, smoothstep(mid - soft, mid + soft, field));
  // 안쪽 심지. 가장 뜨거운 자리는 색이 빠져 흽니다.
  tint = mix(tint, mix(uHot, vec3(1.0), 0.72),
             smoothstep(core - soft, core + soft, field));

  // **알파도 층입니다.** 층마다 고르게 진해야 혀의 테두리가 보입니다.
  float alpha = lit * (0.72 + uHeat * 0.28);

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
