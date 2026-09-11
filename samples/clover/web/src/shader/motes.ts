// 바람에 흩어지는 모래알.
//
// 판에서 풀려 나간 알갱이입니다. **판은 필터가 그립니다**(`erode.ts`) — 이 겹은 풀려서
// 날아가는 것만 맡습니다. 그렇게 가른 이유가 셋입니다.
//
// - 알갱이는 카드 사각형 밖으로 나갑니다. 필터는 제 사각형 안만 그립니다
// - 알갱이는 카드가 지워진 뒤에도 남습니다. 필터는 걸린 것이 없어지면 함께 없어집니다
// - 알갱이는 속도 방향으로 늘어납니다. 픽셀마다 되짚는 필터로는 그것이 늘어난 줄무늬가 됩니다
//
// **소용돌이가 아닙니다.** 축을 중심으로 도는 것을 넣지 않았습니다 — 그것은 판이 돌아
// 빨려 들어가는 것으로 읽히고, 모래가 흩어지는 모습이 아닙니다. 풀어야 할 문제는 「아래로만
// 고르게 내리면 밋밋하다」이고, 그것을 흩어짐 다섯으로 풉니다 — 실리는 정도가 다른 바람 ·
// 무게가 다른 낙하 · 바람의 결 두 겹 · 살랑임.
//
// **인스턴스 속성이 없습니다.** 격자의 칸은 `gl_InstanceID` 로 셈하고, 그 칸의 성질은
// 모래알 그림을 칸의 텍셀 하나로 짚어 읽습니다 — 알갱이 3천 개에 붙는 데이터가 한 바이트도
// 없습니다.

import { Geometry, Mesh, Renderer, Shader, Texture } from 'pixi.js'

import { ERODE_FIELD_GLSL, ERODE_SWEEP, moteGrid } from './erode'
import { grainTexels, noiseResources } from './noise'

/** 알갱이의 기준 수명. 초입니다. 칸마다 0.55배에서 1.45배까지 다릅니다. */
export const MOTE_TAIL = 0.95
/** 먼지의 수명이 모래알의 몇 배인가. */
const DUST_SPAN = 1.5
/** 마지막 알갱이가 꺼질 때까지. **겹을 이만큼 들고 있어야 합니다.** */
export const MOTES_HOLD = ERODE_SWEEP + MOTE_TAIL * 1.45 * DUST_SPAN

const VERTEX = `#version 300 es
precision highp float;

in vec2 aPosition;

uniform mat3 uProjectionMatrix;
uniform mat3 uWorldTransformMatrix;
uniform mat3 uTransformMatrix;

/** 판의 크기. 픽셀 */
uniform vec2 uPlatePx;
/** 삭기 시작한 뒤 지난 시간. 초 */
uniform float uAge;
/** 판을 훑는 시간. 초 */
uniform float uSweep;
/** 알갱이의 기준 수명. 초 */
uniform float uTail;
/** 바람. 판 너비를 1 로 본 속도. 오른쪽 아래입니다. */
uniform vec2 uWind;
/**
 * 내려가는 빠르기. 판 너비를 1 로 본 값입니다.
 *
 * **소멸선보다 빨라야 합니다.** 소멸선은 0.85초에 판을 훑으므로 카드에서 초당 판 높이
 * 하나를 지나갑니다 — 알갱이가 그보다 느리면 풀린 것이 선 위에 얹힌 층으로 보입니다.
 */
uniform float uFall;
/** 내려가며 붙는 가속. 끝에서 빨라지는 것이 이 값입니다. */
uniform float uGrav;
/** 바람의 결. **흩어짐의 대부분이 이 값입니다.** */
uniform float uGust;
/** 좌우로 살랑이는 폭 */
uniform float uSway;
/** 구운 판. 알갱이의 색이 여기서 옵니다. */
uniform sampler2D uShot;
/** 바람. 이 그림의 기울기를 90도 돌려 씁니다. */
uniform sampler2D uFlow;
${ERODE_FIELD_GLSL}

out vec2 vCorner;
out vec3 vTint;
out float vFade;
out float vDust;

/**
 * 바람의 결. **기울기를 90도 돌린 것은 발산이 없습니다** — 알갱이가 한 자리에 모이거나
 * 한 자리에서 솟지 않고 휩니다.
 */
vec2 curlAt(vec2 q) {
  float e = 0.012;
  float c0 = textureLod(uFlow, q, 0.0).r;
  vec2 c = vec2(textureLod(uFlow, q + vec2(0.0, e), 0.0).r - c0,
               -(textureLod(uFlow, q + vec2(e, 0.0), 0.0).r - c0)) / e;
  // **크기를 재어 맞춥니다.** 기울기의 크기는 그림이 얼마나 급한지에 딸려 있어서, 그대로
  // 쓰면 배율을 바꾸거나 그림을 바꾸는 것만으로 흩어지는 거리가 몇 배가 됩니다 — 처음
  // 값에서 알갱이가 판 너비의 두 배까지 날아가 위로 솟았고, 그것은 바람이 아니었습니다.
  //
  // **세로를 눌러 둡니다.** 결은 좌우로 퍼지게 하는 것이고, 세로로 같은 크기면 알갱이가
  // 솟습니다 — 바람에 날리는 모래는 옆으로 퍼지며 아래로 갑니다.
  return c / (1.0 + length(c)) * vec2(1.0, 0.4);
}

/**
 * 풀린 뒤 t 초에 어디 있는가. 판 너비를 1 로 본 좌표입니다.
 *
 * 항력은 닫힌 꼴로 넣습니다(1 - exp) — 풀린 자리에서 곧바로 바람의 속도가 되지 않고,
 * 끝에서는 바람에 실려 함께 갑니다.
 */
vec2 place(float t, vec2 home, float r1, float r2, float r3) {
  float ease = 1.0 - exp(-t * 2.2);
  vec2 at = home;

  // 1. 바람. **실리는 정도가 알갱이마다 세 배 넘게 다릅니다** — 고르면 판이 밀린 것입니다.
  at += uWind * (ease * (0.55 + 1.15 * r1));

  // 2. 내려감. 굵은 알갱이가 빨리 떨어집니다(크기도 r1 을 씁니다).
  //
  // **빠르기와 가속을 함께 둡니다.** 가속만 두면 풀린 뒤 0.2초 동안 내려가는 것이 몇
  // 픽셀뿐이고, 그동안 소멸선이 그보다 훨씬 빨리 내려가 알갱이가 선 위에 얹힙니다.
  at.y += (uFall * t + uGrav * t * t) * (0.45 + 1.10 * r1);

  // 3. 바람의 결. 그림이 시간에 따라 흐르므로 돌풍이 지나가고, 이웃한 알갱이가 서로 다른
  //    결에 실려 갈라집니다.
  vec2 q = home * 1.5 + vec2(-t * 0.55, t * 0.28) + 0.2;
  at += curlAt(q) * (uGust * ease * (0.35 + 1.30 * r2));
  // 결 하나로는 매끈합니다. 같은 그림을 3.7배로 한 겹 더 읽어 잔 결을 겹칩니다.
  at += curlAt(q * 3.7 + 0.6) * (uGust * 0.35 * ease * (0.30 + r3));

  // 4. 살랑임. 위상이 알갱이마다 달라 좌우로 흔들리며 내려갑니다.
  at.x += sin(6.2831 * r2 + t * (3.4 + 4.0 * r3)) * (uSway * ease * (0.3 + r1));
  return at;
}

void main(void) {
  float asp = uPlatePx.y / uPlatePx.x;
  float id = float(gl_InstanceID);
  vec2 cell = vec2(mod(id, uGrid.x), floor(id / uGrid.x));
  vec2 uv = (cell + 0.5) / uGrid;
  // 판 좌표. **가로세로가 같은 단위라야** 결과 살랑임이 두 축에서 같은 거리로 나옵니다.
  vec2 home = vec2(uv.x, uv.y * asp);

  float r1 = seedAt(cell, vec2(0.5));
  float r2 = seedAt(cell, vec2(97.5, 43.5));
  float r3 = seedAt(cell, vec2(191.5, 137.5));
  float r4 = seedAt(cell, vec2(311.5, 271.5));
  // **칸의 가운데에서 조금씩 비껴 둡니다.** 칸에 딱 맞춰 두면 알갱이가 격자에 줄을 서서
  // 흩어지는 것이 화면의 픽셀이 커진 것으로 보입니다.
  home += (vec2(r2, r4) - 0.5) * (0.8 / uGrid.x);

  // 풀리는 때. **필터가 그 칸을 끄는 그 시점입니다.**
  float t = uAge - frontAt(cell) / SPAN * uSweep;
  float dust = r3 < 0.07 ? 1.0 : 0.0;
  float span = uTail * (0.55 + 0.9 * r1) * (dust > 0.5 ? ${DUST_SPAN.toFixed(2)} : 1.0);
  float f = t / span;
  vec4 shot = textureLod(uShot, uv, 0.0);

  // 아직 붙어 있거나, 이미 없거나, 판이 없는 칸이거나, 뜨지 않고 그대로 없어지는 칸입니다.
  // **칸의 절반만 뜹니다** — 전부 띄우면 알갱이가 겹쳐 선 위의 층이 됩니다. 나머지는
  // 판이 풀리면서 그대로 없어집니다.
  if (t <= 0.0 || f >= 1.0 || shot.a < 0.02 || (dust < 0.5 && r4 < 0.55)) {
    // 화면 밖에 둡니다. 그리지 않는 것과 같습니다.
    vFade = 0.0;
    gl_Position = vec4(2.0, 2.0, 0.0, 1.0);
    return;
  }

  vec2 at = place(t, home, r1, r2, r4);
  // 속도는 한 걸음 앞과의 차이입니다. **늘어나는 방향이 이것입니다.**
  vec2 vel = (at - place(max(0.0, t - 0.02), home, r1, r2, r4)) / 0.02;
  float speed = length(vel);
  vec2 along = speed > 1.0e-4 ? vel / speed : vec2(0.0, 1.0);

  // 뜬 뒤 잠깐 커지고 그다음 줄어듭니다. 갓 풀린 것이 눈에 들어야 「지금 풀렸다」가 보입니다.
  float mote = 1.0 / uGrid.x;
  // **알갱이는 칸보다 작습니다.** 칸만큼 크게 그리면 이웃과 붙어 거품이 되고, 모래는
  // 알갱이의 크기가 아니라 알갱이의 수로 보입니다.
  float size = mote * mix(0.55, 0.95, smoothstep(0.0, 0.08, f))
             * (1.0 - 0.35 * f) * (0.6 + 0.9 * r1);
  if (dust > 0.5) size *= 3.2 + 2.0 * r2;
  // 빠른 것은 지나간 자리가 조금 남습니다. **조금입니다** — 2픽셀짜리를 1.5배로 늘이면
  // 모래알이 아니라 쌀알이 됩니다.
  float streak = dust > 0.5 ? 1.0 : 1.0 + min(0.55, speed * 0.16);
  vec2 corner = mat2(along.x, along.y, -along.y, along.x)
              * (aPosition * vec2(size * streak, size));

  // **알갱이는 제 칸의 색을 들고 갑니다.** 빛나지도 물들지도 않습니다 — 판이 자기
  // 자신의 모래로 흩어지는 것이고, 다른 색이 섞이면 그것은 다른 것이 덮인 것입니다.
  //
  // 칸마다 밝기가 조금 다릅니다. 알갱이가 저마다 다른 쪽으로 누워 있는 것이고, 고르면
  // 흩어지는 것이 한 장의 그림이 밀린 것으로 보입니다.
  vec3 own = shot.rgb / shot.a;
  vec3 tint = own * (0.86 + 0.24 * r2);

  vCorner = aPosition;
  vDust = dust;
  // 먼지는 빛깔이 빠져 있습니다. 잔 알갱이가 뭉쳐 있는 것이므로 한 알의 색이 아닙니다.
  vTint = dust > 0.5 ? mix(own, vec3(dot(own, vec3(0.299, 0.587, 0.114))), 0.75) : tint;
  // **수명의 앞 절반은 또렷합니다.** 나이로 곧 옅어지면 알갱이가 판을 떠나는 동안 이미
  // 사라져, 보이는 것이 소멸선 옆의 띠뿐입니다 — 흩어져 가는 것이 보여야 흩어진 것입니다.
  // 끝에서 뚝 끊기지 않고, 뜨는 첫 두 프레임에 밝아집니다.
  vFade = (dust > 0.5 ? 0.20 : 0.95)
        * (1.0 - smoothstep(0.45, 1.0, f)) * smoothstep(0.0, 0.04, f);
  // 멀어질수록 옅어집니다. **판 하나만큼 간 뒤부터입니다** — 그보다 이르면 알갱이가
  // 판을 떠나기 전에 사라져 소멸선에 얹힌 층만 보입니다.
  vFade *= 1.0 - smoothstep(1.20, 3.00, distance(at, home)) * 0.85;

  vec2 px = (at + corner) * uPlatePx.x;
  gl_Position = vec4((uProjectionMatrix * uWorldTransformMatrix * uTransformMatrix
                      * vec3(px, 1.0)).xy, 0.0, 1.0);
}
`

const FRAGMENT = `#version 300 es
precision highp float;

in vec2 vCorner;
in vec3 vTint;
in float vFade;
in float vDust;
out vec4 finalColor;

void main(void) {
  if (vFade <= 0.004) discard;
  float d = length(vCorner);
  // 모래알은 테가 또렷하고, 먼지는 테가 없습니다.
  float soft = vDust > 0.5
    ? (1.0 - smoothstep(0.0, 0.50, d)) * 0.55
    : 1.0 - smoothstep(0.18, 0.50, d);
  float alpha = soft * vFade;
  if (alpha <= 0.003) discard;
  // 미리 곱한 알파. **1 을 넘는 색이 없습니다** — 알갱이는 빛이 아니라 모래입니다.
  finalColor = vec4(vTint * alpha, alpha);
}
`

/**
 * 이 기계가 알갱이를 그릴 수 있는가.
 *
 * **셋을 확인합니다** — WebGL2 인가 · 정점에서 그림을 읽을 자리가 넷 있는가 · 이 셰이더가
 * 실제로 컴파일되는가. 셋째가 있는 이유는 Pixi 가 컴파일에 실패한 셰이더를 조용히 넘어가기
 * 때문입니다 — 그 뒤에는 「알갱이가 없다」와 「알갱이가 안 된다」가 갈리지 않습니다.
 */
export function motesSupported(renderer: Renderer | undefined): boolean {
  const gl = (renderer as { gl?: WebGL2RenderingContext } | undefined)?.gl
  if (!gl || !(gl instanceof WebGL2RenderingContext)) return false
  if ((gl.getParameter(gl.MAX_VERTEX_TEXTURE_IMAGE_UNITS) as number) < 4) return false

  const one = (kind: number, source: string): WebGLShader | null => {
    const shader = gl.createShader(kind)
    if (!shader) return null
    gl.shaderSource(shader, source)
    gl.compileShader(shader)
    if (gl.getShaderParameter(shader, gl.COMPILE_STATUS)) return shader
    console.warn(`[motes] 컴파일되지 않았습니다: ${gl.getShaderInfoLog(shader) ?? ''}`)
    gl.deleteShader(shader)
    return null
  }

  const vs = one(gl.VERTEX_SHADER, VERTEX)
  const fs = one(gl.FRAGMENT_SHADER, FRAGMENT)
  if (vs) gl.deleteShader(vs)
  if (fs) gl.deleteShader(fs)
  return vs !== null && fs !== null
}

/**
 * 판 하나에서 풀려 나가는 알갱이.
 *
 * **하나를 여러 판이 돌려 씁니다**(`render/motes-layer.ts`) — 격자의 칸 수만 다시 세우면
 * 카드에도 딱지에도 같은 것을 씁니다.
 */
export class CardMotes {
  readonly view: Mesh<Geometry, Shader>
  private readonly geometry: Geometry
  private readonly shader: Shader

  constructor() {
    this.geometry = new Geometry({
      attributes: {
        aPosition: {
          buffer: new Float32Array([-0.5, -0.5, 0.5, -0.5, 0.5, 0.5, -0.5, 0.5]),
          format: 'float32x2',
        },
      },
      indexBuffer: new Uint16Array([0, 1, 2, 0, 2, 3]),
      instanceCount: 1,
    })
    this.shader = Shader.from({
      gl: { vertex: VERTEX, fragment: FRAGMENT, name: 'motes' },
      resources: {
        motesUniforms: {
          uPlatePx: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
          uAge: { value: 0, type: 'f32' },
          uSweep: { value: ERODE_SWEEP, type: 'f32' },
          uTail: { value: MOTE_TAIL, type: 'f32' },
          uWind: { value: new Float32Array([0.22, 0.26]), type: 'vec2<f32>' },
          uFall: { value: 1.25, type: 'f32' },
          uGrav: { value: 0.95, type: 'f32' },
          uGust: { value: 0.16, type: 'f32' },
          uSway: { value: 0.035, type: 'f32' },
          // 소멸선의 조각이 함께 선언하는 것들. `uErode` 는 이 셰이더가 쓰지 않습니다 —
          // 알갱이는 제 나이로 살고, 판이 얼마나 삭았는지는 필터의 일입니다.
          uErode: { value: 1, type: 'f32' },
          uDown: { value: 1, type: 'f32' },
          uGrid: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
          uGrainTexels: { value: grainTexels(), type: 'f32' },
        },
        uShot: Texture.WHITE.source,
        uShotSampler: Texture.WHITE.source.style,
        ...noiseResources({ uSoft: 'soft', uGrain: 'grain', uFlow: 'flow' }),
      },
    })
    this.view = new Mesh({ geometry: this.geometry, shader: this.shader })
    // 미리 곱한 알파로 냅니다. Pixi 의 보통 섞기가 그것을 전제합니다.
    this.view.state.blendMode = 'normal'
    this.view.eventMode = 'none'
    this.view.visible = false
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.shader.resources.motesUniforms.uniforms as Record<string, number | Float32Array>
  }

  /**
   * 이 판을 흩습니다.
   *
   * @param shot 구운 판. **알갱이의 색이 여기서 옵니다** — 없으면 칸마다 색이 없습니다.
   */
  begin(shot: Texture, width: number, height: number, down: boolean): void {
    const [cols, rows] = moteGrid(width, height)
    this.geometry.instanceCount = cols * rows
    ;(this.uniforms.uGrid as Float32Array).set([cols, rows])
    ;(this.uniforms.uPlatePx as Float32Array).set([width, height])
    this.uniforms.uAge = 0
    this.uniforms.uDown = down ? 1 : -1
    this.shader.resources.uShot = shot.source
    this.shader.resources.uShotSampler = shot.source.style
    this.view.visible = true
  }

  /** 삭기 시작한 뒤 지난 시간. 초입니다. */
  set age(value: number) {
    this.uniforms.uAge = value
  }

  /**
   * 놓습니다.
   *
   * **흰 그림으로 되돌립니다** — 구운 판은 여기서 버리는 것이고, 버린 그림을 가리키는
   * 채로 그리면 안 됩니다.
   */
  end(): void {
    this.view.visible = false
    this.shader.resources.uShot = Texture.WHITE.source
    this.shader.resources.uShotSampler = Texture.WHITE.source.style
  }

  destroy(): void {
    this.end()
    this.view.destroy()
    this.shader.destroy()
    this.geometry.destroy()
  }
}
