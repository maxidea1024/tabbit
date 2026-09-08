// 판에 들어가기 전의 배경.
//
// **로그인 화면과 타이틀만 이것입니다.** 판이 도는 동안의 배경은 도메인 워핑
// [프랙탈](background.ts)이고 국면마다 색이 바뀝니다 — 그 무늬는 카드 뒤에 깔릴 것이라
// 대비가 낮고 어두운데, 카드가 없는 화면에서는 그 어두움이 화면 전체가 됩니다.
//
// **겹 넷입니다.**
//
// |겹|무엇|
// |--|--|
// |바탕|위에서 아래로 흐르는 두 색. 위가 밝고 아래가 어둡습니다|
// |안개|바탕이 단색으로 눌리지 않게 하는 큰 결 둘|
// |빛살|이름 뒤의 한 점에서 퍼지는 결. 각도를 좌표로 삼아 노이즈 그림을 읽습니다|
// |무리|그 점을 둘러싼 둥근 빛. 이름이 그 위에 섭니다|
//
// **그림을 화면보다 크게 잡습니다.** 배율을 1보다 크게 두면 512픽셀짜리 한 장이 화면 안에서
// 여러 번 되풀이되고, 그 되풀이는 무늬가 아니라 격자로 보입니다 — 겹이 둘이면 서로 돌려
// 놓습니다. 잔 빛알을 뿌리던 겹이 그 격자를 가장 크게 보이게 하여 걷었습니다.
//
// **빛살의 이음매가 없습니다.** 각도를 한 바퀴로 나눈 값에 **정수**를 곱해 읽기 때문입니다 —
// 각도는 반 바퀴에서 부호가 뒤집히므로 정수가 아닌 배수를 곱하면 그 선을 따라 무늬가
// 어긋납니다.
//
// **띠를 지우는 잔 알갱이가 한 겹 있습니다.** 어두운 그라디언트는 8비트로 담기면 층이
// 보이고, 화면의 절반이 그 그라디언트입니다.

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
uniform float uAspect;
uniform float uLevel;    // 0..1. 화면 전체의 세기입니다.
uniform vec2  uOrigin;   // 빛이 나오는 자리. 이름 뒤입니다.
uniform vec3  uDeep;     // 아래 바탕
uniform vec3  uHigh;     // 위 바탕
uniform vec3  uRay;      // 빛살
uniform vec3  uHalo;     // 무리

uniform sampler2D uSoft;

// 노이즈 그림 한 번. **값을 펴서 씁니다** — 그림은 0.17~0.61 에 몰려 있고, 아래의 문턱들은
// 0..1 에 고르게 퍼진 값을 전제로 잡은 것입니다.
float wave(vec2 p) {
  return clamp((texture(uSoft, p).r - 0.384) * 2.0 + 0.5, 0.0, 1.0);
}

// **겹치는 두 겹을 서로 돌려 놓습니다.** 그림 한 장을 배율만 달리해 겹치면 같은 얼룩이
// 같은 방향으로 두 번 지나가고, 그 되풀이가 화면에서 격자로 보입니다.
const mat2 TURN = mat2(0.8776, -0.4794, 0.4794, 0.8776);

void main(void) {
  vec2 uv = vTextureCoord;
  // 가로세로 비를 살린 좌표. 빛살이 옆으로 눌리지 않게 합니다.
  vec2 p = vec2((uv.x - uOrigin.x) * uAspect, uv.y - uOrigin.y);
  float dist = length(p);

  // 1. 바탕. **위가 밝습니다** — 이름과 단추가 위쪽 절반에 서므로 그 뒤가 밝아야
  //    글자의 테두리에 기대지 않고 읽힙니다.
  vec3 color = mix(uHigh, uDeep, smoothstep(0.02, 0.98, uv.y));

  // 2. 안개. 바탕이 단색으로 눌리지 않게 두 겹을 겹칩니다.
  //
  // **한 장이 화면을 넘게 잡습니다.** 배율을 올리면 그림이 화면 안에서 여러 번 되풀이되고,
  // 그 되풀이는 무늬가 아니라 격자로 보입니다 — 배율이 1보다 작으면 화면에 그림의 일부만
  // 들어옵니다.
  vec2 field = vec2(uv.x * uAspect, uv.y);
  float haze = wave(field * 0.34 + vec2(0.11, -uTime * 0.008)) * 0.62
             + wave(TURN * field * 0.71 + vec2(uTime * 0.006, 0.43)) * 0.38;
  color += uHigh * (haze - 0.44) * 0.55;

  // 3. 빛살. **각도가 좌표입니다.** 한 바퀴를 1로 잡고 정수를 곱해 읽으므로 반 바퀴에서
  //    이음매가 생기지 않습니다.
  float lane = atan(p.y, p.x) * 0.15915494;
  float wide = wave(vec2(lane * 4.0 + uTime * 0.010, dist * 0.07));
  float fine = wave(vec2(lane * 11.0 - uTime * 0.007, dist * 0.04 + 0.37));
  // 넓은 결이 자리를 정하고 가는 결이 그 안을 가릅니다.
  //
  // **문턱이 좁습니다.** 넓게 잡으면 빛살이 아니라 번진 얼룩이 되고, 그 얼룩은 배경에
  // 무엇이 있는지가 아니라 배경이 지저분하다는 것만 알립니다.
  float shafts = smoothstep(0.53, 0.96, wide * 0.62 + fine * 0.38);
  // 나오는 자리에서는 무리가 맡고, 멀리서는 잦아듭니다.
  float reach = smoothstep(0.02, 0.34, dist) * smoothstep(1.30, 0.22, dist);
  // **아래로 갈수록 잦아듭니다.** 단추가 아래쪽 절반에 서므로 그 뒤는 고요해야 합니다.
  float calm = mix(1.0, 0.24, smoothstep(0.36, 0.96, uv.y));
  color += uRay * shafts * reach * calm * 0.34;
  // 가는 결 하나가 그 위에 더 밝게 섭니다. **빛살이 몇 줄인지 세어집니다.**
  color += uRay * smoothstep(0.82, 1.0, fine) * reach * calm * 0.16;

  // 4. 무리. 이름이 이 위에 섭니다.
  color += mix(uHalo, uRay, 0.26) * exp(-dist * dist * 7.0) * 0.50;

  // 5. 가장자리를 떨굽니다. 눈이 가운데에 머무릅니다.
  float edge = length(vec2((uv.x - 0.5) * uAspect, uv.y - 0.5));
  color *= 1.0 - smoothstep(0.42, 1.06, edge) * 0.60;

  // 6. **띠를 지우는 잔 알갱이.** 어두운 그라디언트를 8비트로 담으면 층이 보이고, 이
  //    화면의 절반이 그 그라디언트입니다. 한 단계보다 작은 흔들림 하나면 층이 없어집니다.
  float grit = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);
  color += (grit - 0.5) * 0.0035;

  finalColor = vec4(max(color, vec3(0.0)) * uLevel, 1.0);
}
`

/** 색 한 벌. 로그인 화면과 타이틀이 같은 것을 씁니다. */
export interface FrontMood {
  deep: [number, number, number]
  high: [number, number, number]
  ray: [number, number, number]
  halo: [number, number, number]
}

/**
 * 기본 색.
 *
 * **초록 하나로 눌러 두지 않습니다.** 이름이 초록이므로 바탕까지 초록이면 이름이 바탕에
 * 묻히고, 어둡게 낮춘 초록은 색이라기보다 그냥 어두움입니다 — 바탕은 남보라이고 빛살이
 * 금색, 무리가 옥색입니다. 이름의 초록이 그 위에서 가장 밝은 색으로 남습니다.
 */
export const FRONT_MOOD: FrontMood = {
  deep: [0.020, 0.017, 0.048],
  high: [0.150, 0.086, 0.240],
  ray: [1.00, 0.80, 0.48],
  halo: [0.26, 0.88, 0.62],
}

export class FrontFilter extends Filter {
  constructor() {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      resources: {
        frontUniforms: {
          uTime: { value: 0, type: 'f32' },
          uLevel: { value: 1, type: 'f32' },
          uAspect: { value: 16 / 10, type: 'f32' },
          uOrigin: { value: new Float32Array([0.5, 0.34]), type: 'vec2<f32>' },
          uDeep: { value: new Float32Array(FRONT_MOOD.deep), type: 'vec3<f32>' },
          uHigh: { value: new Float32Array(FRONT_MOOD.high), type: 'vec3<f32>' },
          uRay: { value: new Float32Array(FRONT_MOOD.ray), type: 'vec3<f32>' },
          uHalo: { value: new Float32Array(FRONT_MOOD.halo), type: 'vec3<f32>' },
        },
        ...noiseResources({ uSoft: 'soft' }),
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.frontUniforms.uniforms as Record<string, number | Float32Array>
  }

  advance(seconds: number): void {
    this.uniforms.uTime = (this.uniforms.uTime as number) + seconds
  }

  /** 빛이 나오는 자리. 0..1 의 화면 좌표이고 이름 뒤입니다. */
  setOrigin(x: number, y: number): void {
    (this.uniforms.uOrigin as Float32Array).set([x, y])
  }

  setAspect(aspect: number): void {
    this.uniforms.uAspect = aspect
  }

  /** 화면 전체의 세기. 1이 그대로입니다. */
  setLevel(level: number): void {
    this.uniforms.uLevel = level
  }

  setMood(mood: FrontMood): void {
    (this.uniforms.uDeep as Float32Array).set(mood.deep)
    ;(this.uniforms.uHigh as Float32Array).set(mood.high)
    ;(this.uniforms.uRay as Float32Array).set(mood.ray)
    ;(this.uniforms.uHalo as Float32Array).set(mood.halo)
  }
}
