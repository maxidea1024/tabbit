// 고운 재. **알갱이 하나가 원본 화면의 한 칸입니다.**
//
// 앞서 이것을 프래그먼트 셰이더 안에서 거꾸로 찾아 그렸습니다. 그 방법으로는 고운 재가
// 되지 않습니다 — **3픽셀짜리를 500픽셀 안에서 찾으려면 짚는 자리가 150개**여야 하고, 줄이면
// 찾는 띠가 넓어져 알갱이가 늘어난 선이 되고, 알갱이를 키우면 찢어진 종이가 됩니다. 실제로
// 그 둘을 차례로 얻었습니다.
//
// **그래서 앞으로 보냅니다.** 지워지는 화면을 3.6픽셀 격자로 나누고 칸마다 알갱이 하나를
// 두면, 8만 개가 저마다 제 색을 들고 바람에 실려 갑니다 — 찾을 것이 없으므로 늘어나지도
// 뭉치지도 않고, 알갱이가 격자 칸만 하므로 고운 재입니다.
//
// **기법은 가져온 것입니다.** 텔레그램의 dust 효과로 알려진 방법이고, 규격이
// [ThanosEffect](https://github.com/Aghajari/ThanosEffect) 의 README 에 수식으로 적혀
// 있습니다 — **코드를 옮겨 오지 않았습니다.** 그 저장소에 라이선스 파일이 없어서 옮길 수
// 없고, 여기 있는 것은 그 규격을 읽고 이 판에 맞게 적은 것입니다. 얼개는 넷입니다.
//
// |무엇|왜|
// |--|--|
// |칸마다 알갱이 하나, 색은 그 칸의 색|화면이 **자기 자신의 알갱이로** 흩어집니다|
// |바람 쪽으로, 그리고 가운데에서 바깥으로|퍼지면서 실려 갑니다. 한 방향으로만 밀면 화면이 밀린 것입니다|
// |가면서 잘아지고 옅어집니다|재가 잦아드는 것이 이것입니다|
// |떠나는 때가 자리마다 다릅니다|셰이더와 같은 앞을 쓰므로 판이 뚫리는 그 자리에서 떠납니다|
//
// **컴퓨트 셰이더가 아닙니다.** 상태가 없고, 정점 셰이더가 `gl_InstanceID` 와 지워진 정도만으로
// 자리를 냅니다 — 어느 프레임이든 같은 식으로 그립니다. 인스턴스 속성도 없습니다.
//
// **기계가 못 하면 없습니다.** 정점에서 그림을 읽고 인스턴스로 그리므로 WebGL2 가 있어야
// 하고, 셰이더가 컴파일되는지를 **쓰기 전에** 봅니다 — Pixi 는 컴파일에 실패한 셰이더를
// 조용히 넘어가므로 그 뒤에는 알 길이 없습니다.

import { Geometry, Mesh, Shader, Texture } from 'pixi.js'
import type { Renderer } from 'pixi.js'

import {
  ASH_DEFAULTS, ASH_FIELD_GLSL, ASH_PARAMS_GLSL, ashUniforms, refreshWind, tuneUniforms,
} from './ash'
import type { AshParams } from './ash'
import { noiseResources } from './noise'

/**
 * 알갱이 한 칸의 크기. 화면 높이의 몫입니다.
 *
 * **격자를 다시 만들지 않습니다.** 판은 1280 × 800 하나에 맞춰 그려지므로 가로세로 비가 늘
 * 1.6 이고, 격자는 그 비로 한 번 세웁니다 — 창의 크기가 바뀌면 `layout` 이 그리는 크기만
 * 바꿉니다.
 */
const MOTE = 0.0045
const BOARD_ASPECT = 1.6

/**
 * 정점 셰이더가 알갱이 하나의 자리를 냅니다.
 *
 * **인스턴스 속성이 없습니다.** `gl_InstanceID` 로 격자의 칸을 셈하고, 그 칸의 성질은 모래알
 * 그림을 칸의 텍셀 하나로 짚어 읽습니다 — 알갱이 8만 개에 붙는 데이터가 한 바이트도 없습니다.
 *
 * Pixi 의 메시 규약을 따릅니다 — `uProjectionMatrix` · `uWorldTransformMatrix` ·
 * `uTransformMatrix` 는 Pixi 가 채우고, 자리는 층의 픽셀입니다.
 */
const VERTEX = `#version 300 es
precision highp float;

in vec2 aPosition;

uniform mat3 uProjectionMatrix;
uniform mat3 uWorldTransformMatrix;
uniform mat3 uTransformMatrix;
/** 층의 크기. 픽셀 */
uniform vec2 uBox;
/** 격자. 가로·세로 알갱이 수 */
uniform vec2 uGrid;
/** 알갱이 한 칸의 크기. 화면 높이의 몫 */
uniform float uMote;
/** 지워지는 화면. 알갱이의 색이 여기서 옵니다. */
uniform sampler2D uShot;
/** 바람. 이 그림의 기울기를 90도 돌려 씁니다. */
uniform sampler2D uFlow;
uniform vec3 uInk;
${ASH_PARAMS_GLSL}
${ASH_FIELD_GLSL}

out vec2 vCorner;
out vec3 vTint;
out float vFade;

void main(void) {
  float a = clamp(uAmount, 0.0, 1.0);
  float id = float(gl_InstanceID);
  vec2 cell = vec2(mod(id, uGrid.x), floor(id / uGrid.x));
  vec2 uv = (cell + 0.5) / uGrid;
  vec2 origin = vec2(uv.x * uAspect, uv.y);

  // 알갱이마다의 성질. **모래알 그림의 텍셀 하나가 이 칸입니다.**
  float r1 = textureLod(uGrain, (cell + 0.5) / uGrainTexels, 0.0).r;
  float r2 = textureLod(uGrain, (cell + vec2(101.5, 37.5)) / uGrainTexels, 0.0).r;
  float r3 = textureLod(uGrain, (cell + vec2(211.5, 149.5)) / uGrainTexels, 0.0).r;

  // 떠나는 때는 셰이더와 같은 앞입니다. **판이 뚫리는 그 자리에서 떠납니다.**
  float s = a - front(origin);
  float life = uAshLife * (0.35 + 1.05 * r1);
  float f = s / life;
  // **칸의 절반만 재가 되어 뜹니다.** 전부 띄우면 화면의 칸 수만큼이 한꺼번에 날고, 그것은
  // 재가 아니라 안개입니다 — 나머지는 판이 삭으면서 그대로 없어집니다.
  if (s <= 0.0 || f >= 1.0 || r3 < 0.46) {
    // 아직 떠나지 않았거나 이미 사라진 것. **화면 밖에 둡니다** — 그리지 않는 것과 같습니다.
    vFade = 0.0;
    gl_Position = vec4(2.0, 2.0, 0.0, 1.0);
    return;
  }

  // 간 거리. 알갱이마다 다섯 배까지 다릅니다 — **어떤 것은 날아가고 어떤 것은 떠 있습니다.**
  float reach = run(s) * uAshSpeed * (0.35 + 2.30 * r2);
  vec2 w = uWindUnit;
  // **바람 쪽으로, 그리고 가운데에서 바깥으로.** 한 방향으로만 밀면 화면이 밀린 것입니다.
  vec2 away = origin - vec2(uAspect * 0.5, 0.5);
  vec2 at = origin + w * (reach * uWindStrength) + away * (reach * 0.55);
  // 맴돌이. **흐름 그림의 기울기를 90도 돌린 것은 발산이 없습니다** — 재가 한 자리에
  // 모이거나 솟지 않고 휩니다.
  vec2 q = origin * uNoiseScale * 0.5 - w * s * uNoiseSpeed * 0.5 + 0.2;
  float e = 0.012;
  float c0 = texture(uFlow, q).r;
  vec2 curl = vec2(texture(uFlow, q + vec2(0.0, e)).r - c0,
                   -(texture(uFlow, q + vec2(e, 0.0)).r - c0)) / e;
  at += curl * (reach * 0.45 * uTurbulence);

  // **뜬 뒤 잠깐 커지고 그다음 줄어듭니다.** 갓 떠난 것이 눈에 들어야 「지금 떠났다」가
  // 보이고, 줄어드는 것이 재가 잦아드는 모습입니다.
  float grow = smoothstep(0.0, 0.06, f);
  float size = uMote * uBox.y * mix(0.9, 1.35, grow) * (1.0 - f * 0.80) * (0.55 + 0.9 * r1);
  vec2 px = vec2(at.x / uAspect * uBox.x, at.y * uBox.y) + aPosition * size;

  vCorner = aPosition;
  vec4 src = texture(uShot, uv);
  vec3 color = src.a > 0.003 ? src.rgb / src.a : uInk;
  // **떠나온 자리의 색을 오래 들고 갑니다.** 곧바로 재의 색이 되면 화면이 회색 안개로
  // 덮이고, 「그 화면이 재가 되었다」가 아니라 「회색이 덮였다」가 됩니다.
  vTint = ashen(color, f * 0.75);
  // 잦아듭니다. **끝에서 뚝 끊기지 않습니다.**
  // **빨리 잦아듭니다.** 오래 또렷하면 알갱이가 쌓여 벽이 되고, 재는 흩어지며 옅어지는
  // 것입니다.
  vFade = pow(1.0 - f, 1.6) * 0.85 * uAshAmount;
  gl_Position = vec4((uProjectionMatrix * uWorldTransformMatrix * uTransformMatrix
                      * vec3(px, 1.0)).xy, 0.0, 1.0);
}
`

/**
 * 알갱이 하나.
 *
 * **둥근 점 하나입니다.** 3~4픽셀이므로 모양을 깎을 것이 없고, 깎으면 그 값이 8만 배로
 * 붙습니다 — 고운 재는 알갱이의 모양이 아니라 알갱이의 수로 보입니다.
 */
const FRAGMENT = `#version 300 es
precision highp float;

in vec2 vCorner;
in vec3 vTint;
in float vFade;
out vec4 finalColor;

void main(void) {
  if (vFade <= 0.004) discard;
  float alpha = (1.0 - smoothstep(0.24, 0.50, length(vCorner))) * vFade;
  if (alpha <= 0.004) discard;
  finalColor = vec4(vTint * alpha, alpha);
}
`

/**
 * 이 기계가 알갱이를 그릴 수 있는가.
 *
 * **셋을 봅니다** — WebGL2 인가 · 정점에서 그림을 읽을 자리가 있는가 · 이 셰이더가 실제로
 * 컴파일되는가. 셋째가 있는 이유는 Pixi 가 컴파일에 실패한 셰이더를 조용히 넘어가기
 * 때문입니다 — 그 뒤에는 「알갱이가 없다」와 「알갱이가 안 된다」를 가릴 길이 없습니다.
 */
export function embersSupported(renderer: Renderer | undefined): boolean {
  const gl = (renderer as unknown as { gl?: WebGL2RenderingContext } | undefined)?.gl
  if (!gl || typeof WebGL2RenderingContext === 'undefined'
      || !(gl instanceof WebGL2RenderingContext)) {
    return false
  }
  // 정점 셰이더가 지워지는 화면과 큰 얼룩과 모래알과 흐름을 읽습니다.
  if ((gl.getParameter(gl.MAX_VERTEX_TEXTURE_IMAGE_UNITS) as number) < 4) return false
  return compiles(gl, VERTEX, FRAGMENT)
}

/** 두 셰이더가 이 컨텍스트에서 컴파일되고 이어지는가. 만든 것은 바로 버립니다. */
function compiles(gl: WebGL2RenderingContext, vertex: string, fragment: string): boolean {
  const one = (kind: number, source: string): WebGLShader | null => {
    const shader = gl.createShader(kind)
    if (!shader) return null
    gl.shaderSource(shader, source)
    gl.compileShader(shader)
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      // **왜 안 되는지를 남깁니다.** 조용히 빠지면 「알갱이가 없다」만 남고 고칠 길이 없습니다.
      console.warn(`[embers] 컴파일되지 않았습니다: ${gl.getShaderInfoLog(shader) ?? ''}`)
      gl.deleteShader(shader)
      return null
    }
    return shader
  }
  const vs = one(gl.VERTEX_SHADER, vertex)
  const fs = one(gl.FRAGMENT_SHADER, fragment)
  let ok = false
  if (vs && fs) {
    const program = gl.createProgram()
    if (program) {
      gl.attachShader(program, vs)
      gl.attachShader(program, fs)
      gl.linkProgram(program)
      ok = gl.getProgramParameter(program, gl.LINK_STATUS) === true
      if (!ok) console.warn(`[embers] 이어지지 않았습니다: ${gl.getProgramInfoLog(program) ?? ''}`)
      gl.deleteProgram(program)
    }
  }
  if (vs) gl.deleteShader(vs)
  if (fs) gl.deleteShader(fs)
  return ok
}

/**
 * 고운 재 한 벌.
 *
 * 메시 하나에 인스턴스가 격자의 칸 수만큼입니다. 쓰는 쪽은 `shot` 에 지워지는 화면을 넣고
 * `amount` 를 매 프레임 넘기면 됩니다 — 만드는 것은 한 번이고 그 뒤는 유니폼만 바뀝니다.
 */
export class AshEmbers {
  readonly view: Mesh<Geometry, Shader>
  /** 알갱이가 몇 개인가. 값을 재는 도구가 읽습니다. */
  readonly count: number
  private readonly shader: Shader
  private direction: [number, number]

  /**
   * @param mote 알갱이 한 칸의 크기. 화면 높이의 몫이고, 작을수록 곱게 많아집니다.
   */
  constructor(mote = MOTE, params: Partial<AshParams> = {}) {
    const p = { ...ASH_DEFAULTS, ...params }
    this.direction = p.windDir
    const cols = Math.max(1, Math.round(BOARD_ASPECT / mote))
    const rows = Math.max(1, Math.round(1 / mote))
    this.count = cols * rows
    const geometry = new Geometry({
      attributes: {
        aPosition: {
          buffer: new Float32Array([-0.5, -0.5, 0.5, -0.5, 0.5, 0.5, -0.5, 0.5]),
          format: 'float32x2',
        },
      },
      indexBuffer: new Uint16Array([0, 1, 2, 0, 2, 3]),
      instanceCount: this.count,
    })
    this.shader = Shader.from({
      gl: { vertex: VERTEX, fragment: FRAGMENT, name: 'ash-embers' },
      resources: {
        embersUniforms: {
          ...ashUniforms(p),
          uBox: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
          uGrid: { value: new Float32Array([cols, rows]), type: 'vec2<f32>' },
          uMote: { value: mote, type: 'f32' },
          uInk: { value: new Float32Array([0.02, 0.03, 0.05]), type: 'vec3<f32>' },
        },
        uShot: Texture.WHITE.source,
        uShotSampler: Texture.WHITE.source.style,
        ...noiseResources({ uLarge: 'large', uGrain: 'grain', uFlow: 'flow' }),
      },
    })
    this.view = new Mesh({ geometry, shader: this.shader })
    // 미리 곱한 알파로 냅니다. Pixi 의 보통 섞기가 그것을 전제합니다.
    this.view.state.blendMode = 'normal'
    this.view.eventMode = 'none'
    refreshWind(this.uniforms)
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.shader.resources.embersUniforms.uniforms as Record<string, number | Float32Array>
  }

  /**
   * 지워지는 화면. 알갱이의 색이 여기서 옵니다.
   *
   * **놓기 전에 흰 그림으로 되돌립니다** — 그 화면은 전환이 끝나면 버려지는 것이고, 버려진
   * 그림을 가리키는 채로 그리면 안 됩니다.
   */
  set shot(texture: Texture | undefined) {
    const source = (texture ?? Texture.WHITE).source
    this.shader.resources.uShot = source
    this.shader.resources.uShotSampler = source.style
  }

  /** 층이 놓인 자리와 크기. 무대의 좌표입니다. */
  layout(x: number, y: number, width: number, height: number): void {
    this.view.position.set(x, y)
    ;(this.uniforms.uBox as Float32Array).set([width, height])
    this.uniforms.uAspect = width / Math.max(1, height)
    // 「span」 이 가로세로 비를 쓰므로 함께 다시 셈합니다.
    refreshWind(this.uniforms)
  }

  set amount(value: number) {
    this.uniforms.uAmount = Math.max(0, Math.min(1, value))
  }

  set toward(value: boolean) {
    const dir = this.uniforms.uWindDir as Float32Array
    dir[0] = Math.abs(this.direction[0]) * (value ? 1 : -1)
    dir[1] = this.direction[1]
    refreshWind(this.uniforms)
  }

  set ink(color: number) {
    const into = this.uniforms.uInk as Float32Array
    into[0] = ((color >> 16) & 0xff) / 255
    into[1] = ((color >> 8) & 0xff) / 255
    into[2] = (color & 0xff) / 255
  }

  tune(params: Partial<AshParams>): void {
    if (params.windDir) this.direction = params.windDir
    tuneUniforms(this.uniforms, params)
  }

  destroy(): void {
    this.shot = undefined
    this.view.destroy()
    this.shader.destroy()
  }
}
