// 화면 하나를 지우고 되돌리는 것.
//
// **덮개를 그리는 것이 아니라 화면을 처리합니다.** 색을 칠한 판이 앞을 지나가면 그것은
// 화면 위에 놓인 다른 물건이고, 화면 자체가 뭉개지거나 밀려 나가면 그것은 그 화면에
// 일어난 일입니다 — 씬이 갈리는 것은 뒤의 것입니다.
//
// 그래서 이 필터는 **그림 한 장을 받아 그 그림을 고칩니다.** 나가는 쪽에서는 앞 화면을
// 구운 사진이 그 그림이고, 들어오는 쪽에서는 살아 있는 화면 그 자체입니다 — 같은 식에
// 값만 거꾸로 넣습니다.
//
// 규격은 `doc/ui/transition.md` 입니다.

import { Filter, GlProgram } from 'pixi.js'

/** 처리하는 방법 넷. 셰이더의 `uKind` 에 그대로 들어갑니다. */
export const CROSS = {
  /** 색으로 잦아듭니다. */
  fade: 0,
  /** 조각으로 뭉개집니다. */
  blocks: 1,
  /** 앞뒤로 밀리며 결이 늘어납니다. */
  push: 2,
  /** 노이즈 문턱으로 지워집니다. 가장자리가 탑니다. */
  burn: 3,
  /** 옆으로 밀려 나갑니다. 나간 자리에 결이 남습니다. */
  slide: 4,
} as const

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
/** 이 그림이 놓인 자리. 이 밖을 읽으면 옆의 그림이 딸려 옵니다. */
uniform vec4 uInputClamp;
/** 0 이면 그대로이고 1 이면 아무것도 남지 않습니다. */
uniform float uAmount;
/** 방법. 0 잦아듦 · 1 조각 · 2 밀림 · 3 탐 · 4 옆으로 */
uniform float uKind;
/** 남는 색. 다 지워진 자리가 이 색입니다. */
uniform vec3 uInk;
/** 미는 방향. 1 이면 다가오고(또는 오른쪽) -1 이면 물러납니다(또는 왼쪽). */
uniform float uPush;
/** 가로세로 비. 조각이 찌그러지지 않게 씁니다. */
uniform float uAspect;

vec4 grab(vec2 uv) {
  return texture(uTexture, clamp(uv, uInputClamp.xy, uInputClamp.zw));
}

/** 곱해 둔 알파를 풉니다. */
vec3 plain(vec4 src) {
  return src.a > 0.003 ? src.rgb / src.a : uInk;
}

float hash(vec2 p) {
  vec3 q = fract(vec3(p.xyx) * 0.1031);
  q += dot(q, q.yzx + 33.33);
  return fract((q.x + q.y) * q.z);
}

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

/** 층 셋. 지워지는 가장자리가 톱니처럼 되어야 종이가 탄 것으로 보입니다. */
float layers(vec2 p) {
  return noise(p) * 0.6 + noise(p * 2.3 + 7.0) * 0.28 + noise(p * 5.1 + 19.0) * 0.12;
}

void main(void) {
  float amount = clamp(uAmount, 0.0, 1.0);
  vec2 uv = vTextureCoord;
  vec3 color = uInk;
  // 이 픽셀이 얼마나 지워졌는가. 1 이면 남는 색만 있습니다.
  float gone = amount;

  if (uKind < 0.5) {
    // 잦아듦. **가운데가 조금 늦게 남습니다** — 화면 전체가 한 값으로 어두워지면 그것은
    // 밝기를 내린 것이지 화면이 나간 것이 아닙니다.
    vec2 d = (uv - 0.5) * vec2(uAspect, 1.0);
    float far = length(d) / length(vec2(uAspect, 1.0) * 0.5);
    color = plain(grab(uv));
    gone = clamp(amount * 1.35 - far * 0.35, 0.0, 1.0);
  } else if (uKind < 1.5) {
    // 조각. 칸이 커지며 뭉개지고, 그다음에 색이 듭니다.
    float cells = mix(520.0, 11.0, pow(amount, 0.7));
    vec2 grid = vec2(cells, cells / uAspect);
    vec2 snapped = (floor(uv * grid) + 0.5) / grid;
    color = plain(grab(snapped));
    gone = smoothstep(0.45, 1.0, amount);
  } else if (uKind < 2.5) {
    // 밀림. 가운데에서 바깥으로 열두 번 읽어 결을 냅니다.
    //
    // **읽는 자리를 픽셀마다 조금씩 어긋냅니다.** 같은 간격으로만 읽으면 열두 벌의 그림이
    // 겹쳐 보이고, 그것은 늘어난 것이 아니라 여러 장이 겹친 것입니다.
    vec2 d = uv - 0.5;
    float reach = amount * 0.32 * uPush;
    float jitter = hash(uv * 811.0) - 0.5;
    vec3 sum = vec3(0.0);
    float total = 0.0;
    for (int i = 0; i < 12; i++) {
      float t = (float(i) + jitter) / 11.0;
      float weight = 1.0 - t * 0.55;
      sum += plain(grab(0.5 + d * (1.0 + reach * t))) * weight;
      total += weight;
    }
    color = sum / total;
    gone = smoothstep(0.30, 1.0, amount);
  } else if (uKind < 3.5) {
    // 탐. 아래에서 위로 번집니다.
    color = plain(grab(uv));
    float grain = layers(uv * 9.0);
    float rise = 1.0 - uv.y;
    float level = grain * 0.72 + rise * 0.28;
    float edge = amount * 1.12;
    gone = 1.0 - smoothstep(edge - 0.12, edge, level);
    // 지워지는 가장자리가 잠깐 탑니다.
    float ring = (1.0 - smoothstep(0.0, 0.10, level - (edge - 0.12))) * (1.0 - gone);
    color = mix(color, vec3(1.0, 0.55, 0.18), ring * 0.9);
    color += vec3(1.0, 0.55, 0.18) * ring * 0.5;
  } else {
    // 옆으로. **나간 자리에 결이 남습니다** — 그냥 옮기면 판때기 하나가 옆으로 미끄러지는
    // 것이고, 뒤로 늘어나야 화면이 지나간 것이 됩니다.
    float shift = amount * 1.15 * uPush;
    float jitter = hash(uv * 811.0) - 0.5;
    vec3 sum = vec3(0.0);
    float total = 0.0;
    for (int i = 0; i < 10; i++) {
      float t = (float(i) + jitter) / 9.0;
      float weight = 1.0 - t * 0.6;
      // 뒤로 조금씩 끌립니다. 앞머리가 진하고 꼬리가 옅습니다.
      sum += plain(grab(uv - vec2(shift * (1.0 - t * 0.18), 0.0))) * weight;
      total += weight;
    }
    color = sum / total;
    // 화면 밖에서 온 자리는 남는 색입니다.
    vec2 came = uv - vec2(shift, 0.0);
    float outside = came.x < 0.0 || came.x > 1.0 ? 1.0 : 0.0;
    gone = max(outside, smoothstep(0.85, 1.0, amount));
  }

  // **지워졌다는 것은 한 픽셀도 남지 않았다는 뜻입니다.** 어느 방법이든 마지막 6%에서
  // 남는 색으로 채웁니다 — 갈아 끼우는 프레임에 무언가가 비치면 그것은 덮인 것이 아닙니다.
  gone = max(clamp(gone, 0.0, 1.0), smoothstep(0.94, 1.0, amount));

  vec4 src = grab(uv);
  color = mix(color, uInk, gone);
  float alpha = max(src.a, gone);
  finalColor = vec4(color * alpha, alpha);
}
`

export class CrossFilter extends Filter {
  constructor() {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      // 화면의 배율로. 이유는 `editions.ts` 에 있습니다.
      resolution: 'inherit',
      resources: {
        crossUniforms: {
          uAmount: { value: 0, type: 'f32' },
          uKind: { value: 0, type: 'f32' },
          uInk: { value: new Float32Array([0.02, 0.03, 0.05]), type: 'vec3<f32>' },
          uPush: { value: 1, type: 'f32' },
          uAspect: { value: 1.6, type: 'f32' },
        },
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.crossUniforms.uniforms as Record<string, number | Float32Array>
  }

  /** 얼마나 지워졌는가. 0 에서 1 입니다. */
  set amount(value: number) {
    this.uniforms.uAmount = Math.max(0, Math.min(1, value))
  }

  set kind(value: number) {
    this.uniforms.uKind = value
  }

  /** 미는 방향. 참이면 다가오고 거짓이면 물러납니다. */
  set toward(value: boolean) {
    this.uniforms.uPush = value ? 1 : -1
  }

  set ink(color: number) {
    const into = this.uniforms.uInk as Float32Array
    into[0] = ((color >> 16) & 0xff) / 255
    into[1] = ((color >> 8) & 0xff) / 255
    into[2] = (color & 0xff) / 255
  }

  setAspect(value: number): void {
    this.uniforms.uAspect = value
  }
}
