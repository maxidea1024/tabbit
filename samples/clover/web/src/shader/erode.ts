// 모래로 삭아 없어지는 것.
//
// **없어지는 것에도 자리가 있어야 합니다.** 소모품을 쓰거나 카드가 부서질 때 그냥 없어지면
// 무엇이 없어진 것인지 눈이 따라가지 못하고, 미끄러져 나가는 것은 「치웠다」이지 「없앴다」가
// 아닙니다.
//
// **불이 아닙니다.** 타는 것도 · 빛나는 것도 · 검게 남는 것도 없습니다. 소멸선이 위에서
// 아래로 훑고 지나가며 표면의 칸이 하나씩 모래알로 풀리고, 풀린 알갱이는 바람에 실려
// 아래로 흩어집니다. 그것이 전부입니다.
//
// **이 필터는 아직 붙어 있는 판만 그립니다.** 흩어지는 것은 `motes.ts` 의 알갱이 겹이
// 그립니다 — 필터는 제 사각형 밖을 그릴 수 없고, 카드가 지워진 뒤에도 남을 수 없습니다.
// 두 겹은 칸의 소멸선 값(`frontAt`) 하나만 약속하고, 그래서 칸이 여기서 꺼지는 프레임이
// 저기서 뜨는 프레임입니다.
//
// **노이즈는 그림으로 읽습니다.** 어디서 온 그림인지는 `public/noise/readme.md` 에 있습니다.

import { Filter, GlProgram } from 'pixi.js'

import { grainTexels, noiseResources } from './noise'

/** 알갱이 한 칸의 크기. 픽셀입니다. */
export const MOTE_PX = 3

/**
 * 판을 훑는 시간. 초입니다.
 *
 * **세 자리가 같은 값을 씁니다** — 소모품을 쓰는 것 · 카드가 부서지는 것 · 딱지를 파는 것.
 * 같은 연출이 자리마다 다른 길이면 눈이 그것을 다른 일로 읽습니다.
 */
export const ERODE_SWEEP = 0.85

/** 판을 몇 칸으로 나누는가. **두 겹이 같은 격자를 써야 합니다.** */
export function moteGrid(width: number, height: number): [number, number] {
  return [Math.max(1, Math.round(width / MOTE_PX)), Math.max(1, Math.round(height / MOTE_PX))]
}

/**
 * 소멸선. **필터와 알갱이 겹이 나눠 쓰는 조각입니다.**
 *
 * 칸이 풀리는 때를 이 조각 하나가 정합니다 — 필터는 `passAt` 이 0 을 넘는 칸을 끄고,
 * 알갱이 겹은 같은 부등식으로 그 칸을 띄웁니다. 값이 둘로 갈리면 판에서 사라진 칸이 아직
 * 뜨지 않거나, 이미 뜬 칸이 판에 남습니다.
 */
export const ERODE_FIELD_GLSL = `
/** 얼마나 삭았는가. 0 이면 성한 판, 1 이면 판이 하나도 없습니다. */
uniform float uErode;
/** 소멸선이 훑는 쪽. +1 이면 위에서 아래로, -1 이면 아래에서 위로. */
uniform float uDown;
/** 판을 나눈 칸 수. */
uniform vec2 uGrid;
/** 모래알 그림이 몇 텍셀인가. 칸 하나를 텍셀 하나로 짚으려면 이 수가 필요합니다. */
uniform float uGrainTexels;
uniform sampler2D uSoft;
uniform sampler2D uGrain;

/** 소멸선이 1 에 닿은 뒤 마지막 칸까지 풀리는 데 두는 여유. 소멸선의 단위입니다. */
const float BAND = 0.16;
/** 그 여유가 이만큼까지 늘어납니다. */
const float LIFE_MAX = 1.45;
/** 경계 앞의 폭. 이만큼 앞에서부터 빛깔이 빠지고 알갱이 결이 드러납니다. */
const float EDGE = 0.09;
/** uErode 1 에서 마지막 칸까지 꺼집니다. **이 값이 없으면 끝에 한 조각이 남습니다.** */
const float SPAN = 1.0 + BAND * LIFE_MAX;

/** 매끄러운 소멸선. 픽셀마다 읽으므로 경계 앞의 그라디언트가 이것입니다. */
float frontSmooth(vec2 uv) {
  float big = (textureLod(uSoft, uv * 0.62 + 0.17, 0.0).r - 0.45) * 2.2 + 0.5;
  float along = uDown > 0.0 ? uv.y : 1.0 - uv.y;
  // 훑는 쪽에 무게를 둡니다. 노이즈의 몫이 크면 선이 아니라 얼룩이 번지는 것이 됩니다.
  return clamp(big * 0.34 + along * 0.66, 0.0, 1.0);
}

/** 칸의 씨앗. **모래알 그림의 텍셀 하나가 이 칸입니다.** */
float seedAt(vec2 cell, vec2 off) {
  return textureLod(uGrain, (cell + off) / uGrainTexels, 0.0).r;
}

/** 칸의 소멸선. 칸 안에서 한 값이라야 칸이 통째로 풀립니다 — 톱니가 여기서 나옵니다. */
float frontAt(vec2 cell) {
  vec2 uv = (cell + 0.5) / uGrid;
  return clamp(frontSmooth(uv) + (seedAt(cell, vec2(0.5)) - 0.5) * 0.085, 0.0, 1.0);
}

/** 이 칸이 풀린 뒤 얼마나 지났는가. 0 보다 작으면 아직 붙어 있습니다. */
float passAt(float front) { return uErode * SPAN - front; }

`

const VERTEX = `#version 300 es
in vec2 aPosition;
out vec2 vTextureCoord;
out vec2 vPlate;

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
  // 판 안쪽의 0..1. **여백이 0 이므로 이것이 곧 판의 좌표입니다.**
  //
  // 텍스처 좌표로 셈하면 어긋납니다 — 필터가 받는 그림은 풀에서 온 것이라 프레임보다
  // 크고, 그래서 vTextureCoord 의 끝이 1 이 아닙니다. GLSL 주석에 백틱을 쓰지 않습니다 —
  // 이 조각이 템플릿 문자열이라 백틱 하나가 그 자리에서 문자열을 끊습니다.
  vPlate = aPosition;
}
`

const FRAGMENT = `#version 300 es
precision highp float;

in vec2 vTextureCoord;
in vec2 vPlate;
out vec4 finalColor;

uniform sampler2D uTexture;
${ERODE_FIELD_GLSL}

void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  if (uErode <= 0.0005 || src.a < 0.003) { finalColor = src; return; }

  vec2 cell = floor(vPlate * uGrid);
  float s = passAt(frontAt(cell));

  // 이 칸은 풀렸습니다. **판에는 없습니다** — 떠난 것은 알갱이 겹이 그립니다.
  if (s >= 0.0) { finalColor = vec4(0.0); return; }

  // 아직 붙어 있는 칸. 경계가 가까울수록 빛깔이 빠지고 알갱이 결이 드러납니다.
  // **둘을 함께 씁니다** — 칸 단위는 톱니를 내고, 픽셀 단위는 그라디언트를 매끄럽게 합니다.
  float byCell = 1.0 - smoothstep(0.0, EDGE, -s);
  float byPixel = 1.0 - smoothstep(0.0, EDGE, -(uErode * SPAN - frontSmooth(vPlate)));
  float near = max(byCell, byPixel);

  vec3 face = src.rgb / src.a;
  // 빛깔이 빠집니다. **어두워지는 것이 아닙니다** — 검게 남는 것이 없어야 합니다.
  face = mix(face, vec3(dot(face, vec3(0.299, 0.587, 0.114))), near * 0.55);
  // 알갱이 결이 드러납니다. 칸마다 밝기가 갈리는 것이 「곧 풀린다」입니다.
  face *= 1.0 - byCell * 0.20 * seedAt(cell, vec2(53.5, 7.5));

  // 성기게 뚫립니다. 칸이 꺼지기 전에 이미 얇아져 있습니다.
  float alpha = src.a * (1.0 - near * 0.12 * seedAt(cell, vec2(17.5, 91.5)));
  finalColor = vec4(face * alpha, alpha);
}
`

export class ErodeFilter extends Filter {
  constructor() {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT, name: 'erode' }),
      // 화면의 배율로. 이유는 `editions.ts` 에 있습니다.
      resolution: 'inherit',
      // **여백이 없습니다.** 판 밖으로 나가는 것은 알갱이 겹의 몫이므로 이 필터는 제
      // 사각형 안만 그립니다 — 여백을 두면 `vPlate` 가 판의 좌표가 아니게 됩니다.
      padding: 0,
      resources: {
        erodeUniforms: {
          uErode: { value: 0, type: 'f32' },
          uDown: { value: 1, type: 'f32' },
          uGrid: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
          uGrainTexels: { value: grainTexels(), type: 'f32' },
        },
        ...noiseResources({ uSoft: 'soft', uGrain: 'grain' }),
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.erodeUniforms.uniforms as Record<string, number | Float32Array>
  }

  /**
   * 판이 몇 픽셀인가. **격자가 이것으로 정해집니다.**
   *
   * 카드와 딱지의 크기가 다르므로 걸기 전에 한 번 넘깁니다 — 넘기지 않으면 판 하나가
   * 한 칸이 되어 통째로 사라집니다.
   */
  fit(width: number, height: number): void {
    const [cols, rows] = moteGrid(width, height)
    ;(this.uniforms.uGrid as Float32Array).set([cols, rows])
  }

  /** 얼마나 삭았는가. 0 에서 1 입니다. */
  set erode(value: number) {
    this.uniforms.uErode = Math.max(0, Math.min(1, value))
  }

  get erode(): number {
    return this.uniforms.uErode as number
  }

  /** 훑는 쪽. 참이면 위에서 아래입니다. */
  set down(value: boolean) {
    this.uniforms.uDown = value ? 1 : -1
  }
}
