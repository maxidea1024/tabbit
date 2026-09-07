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

/**
 * 값을 아껴야 하는 기계인가.
 *
 * **손가락으로 짚는 화면인지를 봅니다.** `main.ts` 가 MSAA 를 끄는 것과 같은 판정이고
 * 같은 이유입니다 — 핸드폰은 화면이 200만 픽셀이 넘고 GPU 는 데스크탑의 것이 아닙니다.
 * 재는 픽셀마다 뒤로 열일곱 자리를 짚으므로 그 화면에서는 열 자리로 줄입니다.
 *
 * **모습은 달라지지 않습니다.** 격자와 거리를 화면의 자로 재므로, 짚는 수를 줄이면
 * 재가 성기어질 뿐입니다.
 */
function coarsePointer(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches
}

/** 처리하는 방법. 셰이더의 `uKind` 에 그대로 들어갑니다. */
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
  /** 조각으로 부서져 흩어져 오릅니다. */
  ash: 5,
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
/** 방법. 0 잦아듦 · 1 조각 · 2 밀림 · 3 탐 · 4 옆으로 · 5 재 */
uniform float uKind;
/** 남는 색. 다 지워진 자리가 이 색입니다. */
uniform vec3 uInk;
/** 미는 방향. 1 이면 다가오고(또는 오른쪽) -1 이면 물러납니다(또는 왼쪽). */
uniform float uPush;
/** 가로세로 비. 조각이 찌그러지지 않게 씁니다. */
uniform float uAspect;
/**
 * 값을 아껴야 하는 기계인가.
 *
 * **재는 픽셀마다 뒤로 여러 자리를 짚습니다.** 그 수를 핸드폰에서 그대로 두면 화면이
 * 200만 픽셀이 넘고 GPU 는 데스크탑의 것이 아닙니다 — 짚는 수를 줄입니다. 격자와 거리는
 * **화면의 자로 재므로** 줄여도 모습이 달라지지 않고 성기어집니다.
 */
uniform float uLite;

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

// ------------------------------------------------------------------------- 재
//
// **부서지는 것과 삭는 것은 다릅니다.** 문턱 하나로 지우면 자리마다 구멍이 나고 넓어지는
// 것이고, 그것은 화면이 삭는 모습입니다. 재가 되는 것은 **조각이 떨어져 나와 바람에 실려
// 연기처럼 흩어지는 것**이고, 그래서 성한 몸통과 날아가는 재가 한 화면에 같이 있어야
// 합니다.
//
// ## 흐름을 거슬러 오릅니다
//
// 조각을 하나씩 그릴 수는 없으므로 거꾸로 찾습니다. **바람이 휘므로 뒤로 곧게 짚어서는
// 찾지 못합니다** — 곧게 짚으면 휜 길로 온 재가 그 선에 없고, 옆으로도 벌려 짚으면 찾을
// 자리가 넓이가 되어 짚는 수로는 덮이지 않습니다. 실제로 그렇게 해서 재가 띄엄띄엄해졌습니다.
//
// 그래서 **바람의 길을 실제로 거슬러 오릅니다.** 이 픽셀에서 바람을 거꾸로 한 걸음씩 물러
// 나면 그 길이 곧 재가 온 길이고, 그 길 위의 자리만 보면 됩니다 — 넓이가 아니라 선입니다.
//
// 길 위의 한 자리에서 묻는 것은 하나입니다. **거기서 떠난 재가 지금 여기 있으려면 얼마나
// 빨라야 하는가.** 그 빠르기가 재가 가질 수 있는 범위 안이면 여기 있는 것입니다. 빠르기는
// 길을 따라 어디에 있는지만 바꾸므로 **길은 재마다 같습니다** — 그래서 걸음 하나로 빠른
// 재와 느린 재를 함께 찾습니다.
//
// ## 조각과 연기
//
// |무엇|무엇을 그리는가|
// |--|--|
// |조각|막 떨어져 나온 것. 떠나온 자리의 색을 그대로 가지고 갑니다|
// |연기|떠난 지 오래된 것. 재의 색 하나에 짙은 정도뿐입니다|
//
// **걸음은 하나로 씁니다.** 둘이 같은 길 위에 있으므로 한 번 오르며 둘을 함께 셉니다.
// 걸음의 간격은 고르지 않습니다 — **가까운 데를 촘촘히** 짚습니다. 조각은 가까이 있고
// 연기는 멀리 있습니다.

/** 가장 느린 재와 가장 빠른 재. 걸음 하나에서 이 사이의 빠르기를 함께 찾습니다. */
const float ASH_PACE_LOW = 0.45;
const float ASH_PACE_SPAN = 1.05;
/** 올려 주는 바람. 맴돌이가 이것에 더해집니다. */
#define ASH_LIFT normalize(vec2(uPush * 0.30, -1.0))

/** 조각의 격자. 가로로 몇 칸인가. */
const float ASH_CELLS = 240.0;
/** 연기의 알갱이. 조각보다 잘아야 연기로 보입니다. */
const float ASH_GRIT = 520.0;

/**
 * 부서지는 앞이 화면을 다 지나는 때.
 *
 * **여기에 대부분을 씁니다.** 앞이 일찍 끝나면 부서지는 것을 보기 전에 볼 것이 없어지고,
 * 남은 시간에는 재만 날립니다 — 판이 부서지는 것이 이 전환에서 보아야 하는 것입니다.
 *
 * **끝까지 새 연기가 나오게 하는 것도 이 값입니다.** 앞이 일찍 끝나면 그 뒤로는 새로
 * 떠나는 재가 없고, 남은 재는 이미 옅어져 있어 뒤쪽이 빈 검은 화면이 됩니다.
 */
const float ASH_FRONT = 0.78;
/** 조각이 조각으로 보이는 동안. 이 뒤는 연기입니다. */
const float ASH_CHUNK = 0.06;
/**
 * 연기가 흩어져 없어지기까지.
 *
 * **셋의 합이 1을 넘습니다.** 앞이 느리면서 재가 멀리까지 가려면 그래야 합니다 — 합을
 * 1에 맞추면 앞에 쓴 만큼 재의 목숨이 짧아지고, 재는 앞을 따라다니는 띠가 됩니다.
 *
 * 마지막에 남는 재는 아래의 **마지막 6%** 가 남는 색으로 채웁니다. 그 6%는 곡선의 끝이라
 * 실제로는 지우는 시간의 1/7 이고, 재가 그동안 잦아들어 사라지는 것으로 보입니다.
 */
const float ASH_FADE = 0.55;
/**
 * 바람이 실어 가는 빠르기. 화면 높이 / 지워짐 1
 *
 * **아래에서 떠난 재가 화면 위까지 갈 만큼입니다.** 이보다 느리면 재가 부서진 자리
 * 가까이에 머물고, 그것은 실려 간 것이 아니라 그 자리에서 삭은 것입니다.
 */
const float ASH_WIND = 2.10;
/**
 * 얼마나 가면 느려지는가. 아무리 오래되어도 이 거리를 넘지 않습니다.
 *
 * **화면 안에 머물 만큼입니다.** 이보다 크면 재가 위로 빠져나가 지우는 시간의 뒤쪽이
 * 아무것도 없는 검은 화면이 됩니다 — 재가 사라지는 것과 화면 밖으로 나가는 것은 다릅니다.
 */
const float ASH_SLOW = 0.22;
/** 맴돌이가 올려 주는 바람에 비해 얼마나 센가. 1에 가까우면 옆으로도 아래로도 갑니다. */
const float ASH_SWIRL = 0.62;

/**
 * 조각 하나의 성질 넷.
 *
 * **해시 한 번에서 갈라 씁니다.** 성질마다 따로 부르면 걸음마다 네 번이 됩니다.
 */
vec4 ashTraits(vec2 cell) {
  float r = hash(cell + 0.5);
  return vec4(r, fract(r * 57.31), fract(r * 191.73), fract(r * 13.71));
}

/**
 * 그 자리가 부서지기 시작하는 때.
 *
 * **앞이 선으로 보이면 안 됩니다.** 자리를 한 축으로만 재면 앞이 곧은 사선이 되고, 그것은
 * 부서지는 것이 아니라 쓸어 내는 것입니다. 그래서 축에 굴곡 다섯을 섞습니다 — 넓은 것이
 * 앞을 크게 휘게 하고 좁은 것이 그 가장자리를 톱니로 만듭니다.
 *
 * **칸마다의 값을 섞지 않습니다.** 칸 하나에 한 값이면 그 칸의 네모가 앞의 모양이 되고,
 * 실제로 화면에 네모가 보였습니다. 칸 사이를 이어 주는 결은 픽셀마다 해시 열두 번이라
 * 여기서는 쓸 수 없습니다 — 굴곡은 사인이므로 값이 훨씬 쌉니다.
 */
float ashFront(vec2 uv) {
  float sweep = uPush > 0.0 ? uv.x : 1.0 - uv.x;
  // **아래가 먼저 부서집니다.** 재가 바람에 올라가므로, 아래에서 시작해야 아직 성한
  // 위쪽을 재가 지나갑니다.
  float axis = (1.0 - uv.y) * 0.76 + sweep * 0.24;
  float wave = sin(uv.x * 2.3 - uv.y * 3.7 + 1.7) * 0.095
             + sin(uv.x * 5.0 + uv.y * 3.0) * 0.062
             + sin(uv.x * 8.7 + uv.y * 11.3 + 4.1) * 0.030;
  return axis + wave;
}

/**
 * 떠난 지 얼마나 되었는가. 음수면 아직 성합니다.
 *
 * **앞은 밖에서 한 번만 잽니다.** 조각과 연기가 같은 자리를 저마다 물으므로 걸음마다 두 번이
 * 되고, 그 안에 사인이 셋씩 들어 있습니다 — 걸음 열이면 사인 60개가 같은 값을 두 번 셉니다.
 * 재마다 조금 이르거나 늦는 것만 여기서 더합니다.
 */
float ashSince(float front, float speck, float amount) {
  return amount - clamp(front + (speck - 0.5) * 0.06, 0.0, 1.0) * ASH_FRONT;
}

/** 재마다의 빠르기. 가벼운 것이 멀리 갑니다. */
float ashPace(float roll) {
  return ASH_PACE_LOW + roll * ASH_PACE_SPAN;
}

/**
 * 떠난 뒤 얼마나 갔는가. 빠르기 1을 기준으로 한 길의 길이입니다.
 *
 * **처음에 빠르고 뒤로 느려집니다.** 곧게 늘어나면 재가 화면 밖으로 다 나가 버리고 지우는
 * 시간의 뒤쪽이 아무것도 없는 검은 화면이 됩니다 — 실제로 그랬습니다. 재는 멀리 갈수록
 * 흩어지며 느려지고, 그 자리에서 옅어지며 없어집니다.
 */
float ashRun(float since) {
  return ASH_WIND * since / (1.0 + since / ASH_SLOW);
}

/**
 * 바람.
 *
 * **벡터로 적지 않고 흐름 함수 하나로 적습니다.** 그 함수의 기울기를 90도 돌리면 발산이
 * 없는 흐름이 되고, 발산이 없다는 것은 재가 한 자리에 모이거나 한 자리에서 솟지 않는다는
 * 뜻입니다 — 사선 하나로 미는 바람에 맴돌이가 없는 이유가 그것입니다. 흐름 함수가 사인
 * 둘이므로 기울기를 손으로 적을 수 있고, 값을 네 번 재지 않아도 됩니다.
 *
 * **때에 따라 바뀝니다.** 위상이 지나간 시간만큼 밀리므로 같은 자리의 바람이 계속 돌고,
 * 재가 그 흐름에 실려 휩니다.
 *
 * 올려 주는 바람과 맴돌이를 더한 것이 값입니다 — **올리는 것만 있으면 사선입니다.**
 */
vec2 ashWind(vec2 at, float when) {
  float ax = 3.10, by = 2.30, w1 = 0.62;
  float cx = 6.70, dy = 4.10, w2 = 0.24;
  float u = at.x * ax;
  float v = at.y * by - when * 0.9;
  float t = at.x * cx - at.y * dy + 2.2 + when * 1.6;
  float su = sin(u), cu = cos(u);
  float sv = sin(v), cv = cos(v);
  // 흐름 함수 su * cv * w1 + sin(t) * w2 의 기울기.
  float fx = ax * cu * cv * w1;
  float fy = -by * su * sv * w1;
  // **잔 맴돌이는 값을 아낄 때 뺍니다.** 큰 맴돌이가 흐름을 정하고 이것은 그 결입니다 —
  // 빠지면 흐름이 덜 잘아지지만 사선으로 돌아가지는 않습니다.
  if (uLite < 0.5) {
    float ct = cos(t);
    fx += cx * ct * w2;
    fy -= dy * ct * w2;
  }
  return ASH_LIFT + vec2(fy, -fx) * ASH_SWIRL;
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
  } else if (uKind < 4.5) {
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
  } else {
    // 재. **조각으로 부서져 바람에 실려 연기처럼 흩어집니다.**
    //
    // 이 픽셀에 놓인 것은 셋입니다 — **아직 성한 몸통** · **막 떨어져 나온 조각** ·
    // **떠난 지 오래된 연기**. 셋이 한 화면에 겹쳐 있고, 그래서 아래에서 떠난 재가 아직
    // 성한 위쪽 위를 지납니다.

    // 화면의 자. uv 는 가로세로가 다른 자이므로 방향을 그대로 재면 비스듬해집니다.
    vec2 here = vec2(uv.x * uAspect, uv.y);
    vec2 grid = vec2(ASH_CELLS, ASH_CELLS / uAspect);
    vec2 grit = vec2(ASH_GRIT, ASH_GRIT / uAspect);
    // 칸 하나의 크기. 조각이 이 자리에 닿았는지를 이것으로 봅니다.
    float chipSize = uAspect / ASH_CELLS;

    // 아직 성한 몸통. **가장자리가 뭉개지지 않습니다** — 부서지는 것은 조각으로 갈리는
    // 것이고, 흐려지는 것은 초점이 나간 것입니다.
    vec4 mine = ashTraits(floor(uv * grid));
    float body = 1.0 - smoothstep(0.0, 0.02, ashSince(ashFront(uv), mine.x, amount));

    vec3 sum = plain(grab(uv)) * body;
    float total = body;
    float cover = body;
    // 막 떨어져 나온 조각. 잠깐 밝습니다.
    float fresh = 0.0;
    // 연기의 짙은 정도.
    float smoke = 0.0;

    // 바람의 길을 거슬러 오릅니다. **걸음 사이가 고르지 않습니다** — 가까운 데를 촘촘히.
    int steps = uLite > 0.5 ? 6 : 10;
    float back = ASH_CHUNK + ASH_FADE;
    vec2 walk = here;
    float went = 0.0;
    for (int j = 0; j < steps; j++) {
      // 걸음의 간격. **거듭제곱을 쓰지 않습니다** — 곱 둘로 같은 굽이가 나옵니다.
      float x = (float(j) + 1.0) / float(steps);
      float t = back * x * x * (0.55 + 0.45 * x);
      float arc = ashRun(t);
      vec2 at = vec2(walk.x / uAspect, walk.y);
      walk -= normalize(ashWind(at, amount - t)) * (arc - went);
      went = arc;
      vec2 from = vec2(walk.x / uAspect, walk.y);
      // **화면 밖으로 나가면 더 거슬러 갈 것이 없습니다.** 거기에는 부서질 것이
      // 없었으므로, 그 뒤의 걸음도 볼 것이 없습니다.
      if (from.x < 0.0 || from.x > 1.0 || from.y < 0.0 || from.y > 1.0) break;

      // **이 자리의 앞은 여기서 한 번 잽니다.** 조각과 연기가 이 값을 함께 씁니다.
      float front = ashFront(from);

      // ---- 연기. 알갱이 하나하나가 아니라 얼마나 짙은가입니다.
      vec2 gcell = floor(from * grit);
      float speck = hash(gcell + 11.0);
      float aired = ashSince(front, speck, amount);
      if (aired > 0.0) {
        // **거기서 떠난 재가 지금 여기 있으려면 이만큼 빨라야 합니다.**
        float need = arc / max(ashRun(aired), 0.0001);
        float fits = 1.0 - smoothstep(0.0, ASH_PACE_SPAN * 0.40,
                                      abs(need - ashPace(speck)));
        // 떠난 뒤 짙어지고 흩어지며 옅어집니다.
        // **오래 옅게 남습니다.** 곧게 잦아들면 뒤쪽이 한꺼번에 비고, 연기는 옅어진 채로
        // 한참 남아 있는 것입니다.
        float spent = clamp((aired - ASH_CHUNK * 1.2) / ASH_FADE, 0.0, 1.0);
        float fade = smoothstep(0.0, ASH_CHUNK * 0.8, aired) * pow(1.0 - spent, 0.75);
        // 알갱이. **고르게 깔리면 안개입니다** — 짙은 자리와 옅은 자리가 결을 이룹니다.
        float bit = smoothstep(0.34, 0.92, hash(gcell * 1.7 + 3.0));
        float wisp = 0.30 + 0.70 * (0.5 + 0.5 * sin(from.x * 6.3 + from.y * 4.1 + aired * 3.0));
        smoke += fits * fade * bit * wisp;
      }

      // ---- 조각. **떠난 지 얼마 안 된 것만입니다.** 그 뒤는 연기가 그립니다.
      vec2 cell = floor(from * grid);
      vec4 traits = ashTraits(cell);
      float since = ashSince(front, traits.x, amount);
      float holds = ASH_CHUNK * (0.60 + traits.w * 0.80);
      if (since <= 0.0 || since >= holds) continue;

      // **조각은 칸 안의 제자리에서 떠납니다.** 짚은 자리에서 떠나면 조각이 픽셀을
      // 따라다니고, 그러면 칸 하나가 통째로 덮여 격자가 눈에 보입니다.
      vec2 home = (cell + vec2(fract(traits.x * 97.0), fract(traits.y * 61.0))) / grid;
      float aged = since / holds;
      // **조각은 칸보다 잡니다.** 칸을 채우는 크기면 덮인 자리가 칸의 모양이 되고, 그것은
      // 화면을 칸으로 나눈 것이지 부서진 것이 아닙니다. 가면서 더 잘아집니다.
      float grain = chipSize * (0.95 - aged * 0.45) * (0.70 + (1.0 - traits.y) * 0.60);
      // 빠르기가 이만큼 어긋나면 조각 하나만큼 어긋납니다.
      float tol = grain / max(ashRun(since), 0.0001);
      float need = arc / max(ashRun(since), 0.0001);
      // **가운데는 꽉 찹니다.** 가운데에서부터 옅어지면 조각이 저마다 반투명해지고, 그러면
      // 부서진 조각이 아니라 얼룩이 낀 것으로 보입니다.
      float match = 1.0 - smoothstep(tol * 0.45, tol, abs(need - ashPace(traits.y)));
      float weight = match * (1.0 - smoothstep(0.55, 1.0, aged));
      if (weight <= 0.0) continue;

      // **조각 하나는 한 색입니다.** 떠나온 자리의 색을 그대로 가지고 갑니다.
      sum += plain(grab(home)) * weight;
      total += weight;
      cover = max(cover, weight);
      fresh += weight * (1.0 - smoothstep(0.0, 0.35, aged));
    }

    color = total > 0.0001 ? sum / total : uInk;

    // 이 자리에 놓인 것 가운데 조각의 몫. **성한 몸통 위에 떠 있는 조각도 재입니다** — 제
    // 자리가 부서졌는가로 가르면 아직 성한 곳 위를 지나는 재가 판의 색 그대로입니다.
    float chips = clamp((total - body) / max(total, 0.0001), 0.0, 1.0);
    // 재는 빛깔이 빠지고 조금 따뜻해집니다. **어두운 데서 온 조각도 재의 밝기를 가집니다** —
    // 떠나온 자리의 색 그대로면 어두운 판에서 나온 재는 배경에 묻혀 보이지 않습니다.
    float grey = dot(color, vec3(0.30, 0.59, 0.11));
    vec3 tone = (vec3(0.20, 0.17, 0.14) + vec3(grey) * 0.72) * vec3(1.06, 0.99, 0.88);
    color = mix(color, tone, chips * 0.75);
    // 막 떨어져 나온 조각이 잠깐 밝습니다.
    color += vec3(0.95, 0.84, 0.68) * clamp(fresh, 0.0, 1.0) * 0.22;

    // 연기를 그 위에 얹습니다. **덮인 정도에도 셉니다** — 재가 아직 날고 있는데 그 자리가
    // 남는 색이면 재는 사라진 것입니다.
    float veil = clamp(smoke * 0.34, 0.0, 1.0);
    color = mix(color, vec3(0.41, 0.36, 0.30), veil * 0.88);
    cover = max(cover, veil);
    gone = 1.0 - clamp(cover, 0.0, 1.0);
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
      //
      // **모바일에서는 1배입니다.** 재는 픽셀마다 바람의 길을 거슬러 오르므로 값이 픽셀
      // 수에 그대로 붙고, 배율 2는 픽셀이 네 배입니다 — 짚는 수를 줄이는 것보다 이쪽이
      // 훨씬 큽니다. 늘려 그리므로 아직 성한 판이 그동안 조금 무릅니다만, **부서지는 중인
      // 판의 1.6초**이고 그 값은 프레임을 놓치는 것과 견줄 것이 아닙니다.
      resolution: coarsePointer() ? 1 : 'inherit',
      resources: {
        crossUniforms: {
          uAmount: { value: 0, type: 'f32' },
          uKind: { value: 0, type: 'f32' },
          uInk: { value: new Float32Array([0.02, 0.03, 0.05]), type: 'vec3<f32>' },
          uPush: { value: 1, type: 'f32' },
          uAspect: { value: 1.6, type: 'f32' },
          uLite: { value: coarsePointer() ? 1 : 0, type: 'f32' },
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

  /**
   * 값을 아끼는 쪽으로 돌릴 것인가.
   *
   * **처음 값은 기계가 정합니다.** 이 설정자는 핸드폰이 아닌 곳에서 그 모습을 보려고 두는
   * 것이고, 그러지 않으면 핸드폰에서만 보이는 모습을 눈으로 볼 길이 없습니다.
   */
  set lite(value: boolean) {
    this.uniforms.uLite = value ? 1 : 0
    this.resolution = value ? 1 : 'inherit'
  }
}
