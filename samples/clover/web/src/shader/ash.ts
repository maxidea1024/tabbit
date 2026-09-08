// 화면이 재가 되어 바람에 날려 가는 것.
//
// **덩어리로 깨지는 것이 아닙니다.** 판이 조각으로 갈려 날아가면 그것은 깨진 것이고, 재가
// 되는 것은 **표면이 모래알로 삭아 빠지고 그 알갱이가 바람에 실려 흩어지는 것**입니다.
// 그래서 이 연출에서 화면의 대부분은 **비어 갑니다** — 빈 자리에 잔 알갱이와 몇 안 되는
// 조각이 성기게 떠 있는 것이 알맹이이고, 무언가가 화면을 덮고 있으면 그것은 재가 아닙니다.
//
//     성한 판 → 빛깔이 빠지고 금이 벌어짐 → 표면이 모래알로 뚫려 레이스가 됨
//       → 잔 조각과 알갱이가 떠남 → 바람에 실려 흩어짐 → 빈 화면에 알갱이 몇
//
// **노이즈는 그림으로 읽습니다.** 모래알·큰 얼룩·금·바람·부드러운 결 다섯이고, 어디서
// 왔는지는 `public/noise/readme.md` 에 있습니다. 셰이더 안에 해시가 하나도 없습니다.
//
// **모래알 그림은 칸마다 한 값으로도 읽습니다.** 픽셀마다 독립인 그림이므로 텍셀 하나가 곧
// 알갱이 하나이고, 좌표를 텍셀의 가운데에 맞춰 짚으면 그 칸 안에서 한 값이 나옵니다 —
// 알갱이 하나의 성질이 그렇게 옵니다. 격자가 보이지 않는 것은 **칸이 3~18픽셀**이기
// 때문입니다.
//
// **둘입니다.** 데스크탑의 것과 핸드폰의 것은 같은 시간표와 같은 모습을 겨누지만 다른
// 방법으로 그립니다 — 핸드폰의 것은 데스크탑의 것에서 값만 줄인 것이 아닙니다. 아래
// 「핸드폰」.
//
// 규격은 `doc/ui/ash.md` 입니다.

import { Filter, GlProgram } from 'pixi.js'

import { coarsePointer } from './device'
import { grainTexels, noiseResources } from './noise'

/**
 * 파라미터.
 *
 * 거리는 **화면의 높이를 1로 한 자**이고, 시간은 **지워지는 정도 0..1** 입니다 — 빠르기는
 * 「지워짐 1 동안 가는 높이」입니다.
 */
export interface AshParams {
  /** 바람이 가는 쪽. 화면의 자이고 y 는 아래가 + 입니다. 크기는 보지 않습니다 */
  windDir: [number, number]
  /** 바람의 세기. 맴돌이에 대한 비입니다 — 둘 다 키우면 아무것도 바뀌지 않습니다 */
  windStrength: number
  /** 맴돌이의 세기. 0 이면 곧게 갑니다 */
  turbulence: number
  /** 큰 얼룩의 배율. 화면 높이에 그림이 몇 번 들어가는가 */
  noiseScale: number
  /** 바람의 결과 연기가 흘러가는 빠르기 */
  noiseSpeed: number
  /** 삭아 뚫리는 동안. 지워짐의 몫이고, 화면에서는 앞의 띠의 너비입니다 */
  edgeWidth: number
  /** 조각이 얼마나 짙게 남는가. 0..1 */
  fragmentAmount: number
  /** 조각 하나의 크기. 화면 높이의 몫 */
  fragmentSize: number
  /** 조각의 빠르기 */
  fragmentSpeed: number
  /** 조각이 조각으로 있는 동안. 이 뒤는 없습니다 */
  fragmentLife: number
  /** 알갱이가 얼마나 짙은가. 0..1 */
  ashAmount: number
  /** 알갱이 하나의 크기. 화면 높이의 몫 */
  ashSize: number
  /** 알갱이의 빠르기. 가장 빠른 것이 이것의 두 배, 가장 느린 것이 4분의 1입니다 */
  ashSpeed: number
  /** 알갱이가 사라지기까지. **조각보다 오래 삽니다** */
  ashLife: number
  /** 연기가 얼마나 많은가. 0..1 */
  smokeAmount: number
  /** 연기가 얼마나 짙은가. 0..1 */
  smokeStrength: number
}

/**
 * 처음 값.
 *
 * **위로 갑니다.** 진 판의 재가 올라가는 것이고, `toward` 가 가로의 방향을 뒤집습니다.
 *
 * **알갱이가 조각보다 잘고 많고 오래 삽니다.** 조각은 18픽셀에 지워짐의 0.13 을 살고,
 * 알갱이는 3.6픽셀에 0.42 를 삽니다 — 그러지 않으면 앞을 따라다니는 띠 하나가 되고 뒤쪽이
 * 빈 검은 화면이 됩니다.
 */
export const ASH_DEFAULTS: AshParams = {
  windDir: [0.30, -1],
  windStrength: 1.0,
  turbulence: 0.6,
  noiseScale: 1.1,
  noiseSpeed: 0.5,
  edgeWidth: 0.075,
  fragmentAmount: 1.0,
  fragmentSize: 0.017,
  fragmentSpeed: 1.15,
  fragmentLife: 0.13,
  ashAmount: 1.0,
  ashSize: 0.006,
  ashSpeed: 2.0,
  ashLife: 0.55,
  smokeAmount: 0.5,
  smokeStrength: 0.4,
}

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

/** 파라미터의 유니폼. 셰이더 둘과 파티클이 같은 것을 받습니다. */
export const ASH_PARAMS_GLSL = `
/** 0 이면 그대로이고 1 이면 아무것도 남지 않습니다. */
uniform float uAmount;
/** 가로세로 비. 화면의 자는 높이가 1 이고 가로가 이것입니다. */
uniform float uAspect;
uniform vec2 uWindDir;
uniform float uWindStrength;
uniform float uTurbulence;
uniform float uNoiseScale;
uniform float uNoiseSpeed;
uniform float uEdgeWidth;
uniform float uFragmentAmount;
uniform float uFragmentSize;
uniform float uFragmentSpeed;
uniform float uFragmentLife;
uniform float uAshAmount;
uniform float uAshSize;
uniform float uAshSpeed;
uniform float uAshLife;
uniform float uSmokeAmount;
uniform float uSmokeStrength;
/** 대비가 큰 큰 얼룩. 부서지는 앞이 선으로 보이지 않게 하는 것입니다. */
uniform sampler2D uLarge;
/** 모래알. 알갱이·조각·구멍이 다 이것입니다. */
uniform sampler2D uGrain;
/** 모래알 그림이 몇 개의 텍셀인가. **칸마다 한 값으로 읽는 자리가 씁니다.** */
uniform float uGrainTexels;
`

/**
 * 셋이 나눠 쓰는 마당 — 어디가 언제 재가 되고, 떠난 것이 얼마나 갔는가.
 *
 * **파티클도 같은 식을 씁니다.** 그래야 파티클이 앞이 지나는 바로 그 자리에서 떠납니다.
 *
 * |상수|무엇|
 * |--|--|
 * |`START` · `END`|앞이 출발하고 다 지나는 때. 그 뒤는 남은 재가 흩어지는 동안입니다|
 * |`SLOW`|멀리 갈수록 느려지는 척도. **재가 사라지는 것과 화면 밖으로 나가는 것은 다릅니다**|
 */
export const ASH_FIELD_GLSL = `
const float START = 0.12;
const float END = 0.80;
const float SLOW = 0.26;
const vec3 LUMA = vec3(0.30, 0.59, 0.11);

/**
 * 그 자리가 재가 되는 때. 지워짐의 값이고, 지나면 그 자리에 판이 없습니다.
 *
 * **바람이 오는 쪽이 먼저입니다.** 그래야 떠난 재가 아직 성한 쪽 위를 지납니다.
 *
 * **앞이 선이 아닙니다.** 축만으로 재면 사선 하나가 쓸고 지나가는 것이 되므로 큰 얼룩을
 * 축과 거의 같은 무게로 섞습니다 — 그러면 앞이 얼룩덜룩하게 번지고, 어떤 자리는 일찍 뚫리고
 * 어떤 자리는 늦게까지 남습니다.
 */
float front(vec2 p) {
  vec2 w = normalize(uWindDir);
  float span = abs(w.x) * uAspect + abs(w.y);
  float axis = 0.5 + dot(p - vec2(uAspect * 0.5, 0.5), w) / span;
  float blob = texture(uLarge, p * uNoiseScale + 0.13).r;
  return START + (END - START) * clamp(axis * 0.56 + blob * 0.50 - 0.03, 0.0, 1.0);
}

/**
 * 떠난 뒤 얼마나 갔는가. 빠르기 1 을 기준으로 한 거리입니다.
 *
 * **처음에 빠르고 뒤로 느려집니다.** 곧게 늘어나면 재가 화면 밖으로 다 나가 버리고 지우는
 * 시간의 뒤쪽이 아무것도 없는 검은 화면이 됩니다.
 */
float run(float s) {
  return s / (1.0 + s / SLOW);
}

/** 그 칸의 가운데. 알갱이가 떠난 자리입니다. */
vec2 cellHome(vec2 p, float size) {
  return (floor(p / size) + 0.5) * size;
}

/**
 * 그 칸의 성질. **칸 안에서 한 값입니다.**
 *
 * 모래알 그림은 픽셀마다 독립이므로 텍셀 하나가 곧 알갱이 하나입니다. 좌표를 텍셀의
 * 가운데에 맞춰 짚으면 그 칸의 모든 픽셀이 같은 텍셀을 읽고, 「nudge」 를 텍셀 몇 개만큼 밀면
 * 같은 칸의 다른 성질이 나옵니다 — 목숨과 빠르기가 그렇게 옵니다.
 *
 * **0층을 짚습니다.** 칸의 경계에서 좌표가 뛰므로 기울기가 커지고, 밉맵을 그대로 두면 GPU 가
 * 흐린 층을 골라 칸 안이 한 값이 아니게 됩니다.
 */
float speckAt(vec2 p, float size, vec2 nudge) {
  vec2 cell = floor(p / size) + 0.5 + nudge;
  return textureLod(uGrain, cell / uGrainTexels, 0.0).r;
}

/**
 * 재의 색. 빛깔이 빠지고 조금 따뜻해집니다.
 *
 * **떠나온 자리의 색 그대로는 아닙니다.** 어두운 판에서 나온 재가 배경에 묻혀 보이지 않고,
 * 그러면 날아가는 것이 있다는 것 자체가 화면에 없습니다. 밝기를 바닥으로 깔고 섞습니다.
 */
vec3 ashen(vec3 color, float aged) {
  float grey = dot(color, LUMA);
  vec3 ash = (vec3(0.46, 0.44, 0.41) + grey * 0.44) * vec3(1.03, 1.0, 0.95);
  // **재의 색이 대부분입니다.** 떠나온 자리의 색을 절반 넘게 남기면 어두운 판에서 나온
  // 것이 거의 검고, 검은 것이 남는 색 위를 지나면 날아가는 것이 아니라 얼룩이 번지는
  // 것으로 보입니다 — 실제로 그랬습니다. 남는 것은 「어디서 왔다」의 힌트뿐입니다.
  return mix(color, ash, 0.65 + 0.30 * aged);
}

/** 미리 곱한 알파로 겹칩니다. **앞의 것이 뒤의 것을 가립니다.** */
vec4 over(vec4 top, vec4 under) {
  return top + under * (1.0 - top.a);
}
`

/**
 * 셰이더 둘이 나눠 쓰는 머리. 마당에 그림 읽기와 표면의 처리와 마무리를 더한 것입니다.
 */
const HEAD = `#version 300 es
precision highp float;

in vec2 vTextureCoord;
out vec4 finalColor;

uniform sampler2D uTexture;
/** 이 그림이 놓인 자리. 이 밖을 읽으면 옆의 그림이 딸려 옵니다. */
uniform vec4 uInputClamp;
/** 남는 색. 다 지워진 자리가 이 색입니다. */
uniform vec3 uInk;
${ASH_PARAMS_GLSL}
${ASH_FIELD_GLSL}

vec4 grab(vec2 uv) {
  return texture(uTexture, clamp(uv, uInputClamp.xy, uInputClamp.zw));
}

/** 곱해 둔 알파를 풉니다. */
vec3 plain(vec4 src) {
  return src.a > 0.003 ? src.rgb / src.a : uInk;
}

vec2 toUv(vec2 here) { return vec2(here.x / uAspect, here.y); }
vec2 toHere(vec2 uv) { return vec2(uv.x * uAspect, uv.y); }

/** 화면 안인가. 밖에서 온 것은 부서질 것이 없었습니다. */
bool inside(vec2 uv) {
  return uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0;
}

/**
 * 재가 되기 직전의 표면.
 *
 * **깨끗한 경계가 없습니다.** 성한 그림에서 빈 자리로 곧바로 가면 그것은 지워진 것이고,
 * 재가 되는 것은 그 사이에 **삭는 동안**이 있는 것입니다. 「k」 가 0 에서 1 로 가는 동안 넷이
 * 함께 일어납니다 — 빛깔이 빠지고, 어두워지고, 금이 벌어지고, 모래알이 빠져 뚫립니다.
 *
 * **뚫리는 것이 알맹이입니다.** 앞의 띠에서 판이 레이스처럼 되어야 「삭는다」로 읽히고,
 * 색만 바뀌면 그것은 어두워지는 것입니다.
 */
vec3 crumble(vec3 color, float k, float crack, float grain, inout float body) {
  float grey = dot(color, LUMA);
  color = mix(color, vec3(grey), 0.45 * k) * (1.0 - 0.22 * k);
  // **금이 먼저 벌어집니다.** 선이 어두워지고 깊은 금은 열려 뚫립니다.
  //
  // **곱을 둘로 둡니다.** 곧게 걸면 아직 멀쩡한 판에도 금이 보이고, 그러면 그것은 삭는
  // 중인 판이 아니라 금이 그려진 판입니다 — 카드가 실제로 그렇게 보였습니다.
  float line = 1.0 - crack;
  color *= 1.0 - line * 0.80 * k * k;
  body *= 1.0 - smoothstep(0.92 - 0.40 * k, 1.00 - 0.40 * k, line);
  // **모래알이 빠집니다.** 문턱이 내려오며 뚫린 자리가 넓어집니다.
  body *= 1.0 - smoothstep(1.00 - 0.56 * k, 1.08 - 0.56 * k, grain);
  return color;
}

/**
 * 갓 재가 되었지만 아직 떠나지 않은 것.
 *
 * **이것이 없으면 앞의 바로 뒤가 검은 구멍이 됩니다.** 표면이 뚫린 그 순간에 재는 아직 그
 * 자리에 있고, 바람이 실어 가는 데는 시간이 걸립니다 — 그 사이를 비워 두면 화면이 삭는 것이
 * 아니라 구멍이 뚫려 뒤가 보이는 것이 되고, 뒤에는 아무것도 없으므로 검은 구멍이 됩니다.
 * 실제로 그렇게 보였습니다.
 *
 * **찾지 않습니다.** 아직 떠나지 않은 것이므로 이 자리에 있고, 그림 한 번으로 끝납니다 —
 * 바람 쪽으로 조금 밀어 읽으므로 흘러가기는 합니다.
 */
vec4 justAsh(vec3 color, float s, vec2 drift, sampler2D grain) {
  // **앞에 붙은 얇은 띠입니다.** 넓게 두면 앞의 기울기가 완만한 자리에서 띠가 화면의
  // 몇분의 일이 되고, 그것은 재가 아니라 갈색 구름입니다 — 실제로 그렇게 보였습니다.
  float fade = 1.0 - smoothstep(0.0, uEdgeWidth * 0.9, s);
  if (fade <= 0.004) return vec4(0.0);
  float g = texture(grain, drift * 1.6 + 0.7).r;
  float alpha = pow(fade, 1.4) * smoothstep(0.44, 0.74, g);
  if (alpha <= 0.004) return vec4(0.0);
  return vec4(ashen(color, 0.35) * alpha, alpha);
}

/**
 * 마무리. **덮이지 않은 자리는 남는 색입니다.**
 *
 * 알갱이가 성기므로 이 한 줄이 이 연출의 대부분을 정합니다 — 화면은 비어 가고, 그 빈 자리가
 * 남는 색입니다.
 */
vec4 seal(vec4 acc, float a, vec4 src) {
  float cover = clamp(acc.a, 0.0, 1.0);
  vec3 color = cover > 0.004 ? acc.rgb / cover : uInk;
  // **지워졌다는 것은 한 픽셀도 남지 않았다는 뜻입니다.** 마지막 6%는 남는 색으로 채웁니다.
  float gone = max(1.0 - cover, smoothstep(0.94, 1.0, a));
  color = mix(color, uInk, gone);
  float alpha = max(src.a, gone);
  return vec4(color * alpha, alpha);
}
`

/**
 * 데스크탑.
 *
 * ## 조각과 알갱이를 다르게 찾습니다
 *
 * 둘 다 「이 픽셀에 무엇이 와 있는가」를 거꾸로 묻는 것이지만, 크기와 가는 거리가 스무 배
 * 차이여서 같은 방법으로는 둘 다 찾지 못합니다.
 *
 * |무엇|어떻게|왜|
 * |--|--|--|
 * |조각|바람 방향으로 **자리 여섯을 짚습니다**|18픽셀이고 100픽셀쯤 갑니다. 짚는 간격이 조각보다 좁으면 놓치지 않습니다|
 * |알갱이|**나이를 정하고 그 나이만큼 거슬러 갑니다**|3.6픽셀이고 500픽셀까지 갑니다 — 길 위를 짚어 찾으려면 걸음이 150개여야 합니다|
 *
 * **알갱이를 길 위에서 찾으면 국수가 됩니다.** 걸음을 줄이면 「그만큼 갔는가」를 재는 띠가
 * 넓어지고, 띠가 알갱이보다 넓으면 알갱이 하나가 그 띠만큼 늘어난 선이 됩니다 — 실제로
 * 그렇게 보였습니다. 나이를 먼저 정하면 그 나이의 알갱이 전부가 겹 하나로 나오고, 그 나이의
 * 알갱이는 앞이 그때 지나간 자리에만 있으므로 겹마다 성깁니다.
 *
 * ## 조각이 늘어나지 않게
 *
 * **조각은 이 픽셀의 바람 방향으로 곧게 짚습니다.** 굽은 길을 걸음마다 거슬러 오르며 찾으면
 * 이웃한 픽셀이 조금씩 다른 길로 같은 조각을 찾아, 조각이 길을 따라 늘어난 리본이 됩니다.
 * 바람의 방향은 자리마다 매끄럽게 다르므로 곧게 짚어도 조각의 길은 화면 전체로 보면 굽어
 * 있고, 조각 하나는 굳은 채로 갑니다.
 *
 * **떠나는 때는 칸마다 한 값입니다.** 앞을 칸의 가운데에서 재므로 한 칸이 통째로 같은 때에
 * 떠나고, 그러지 않으면 칸의 위와 아래가 다른 때에 떠나 조각이 늘어납니다.
 *
 * ## 값
 *
 * 조각 여섯 자리에 그림을 많아야 여섯 번, 알갱이 여덟 겹에 겹마다 여덟 번입니다. **아직
 * 그만큼 오래된 것이 없으면 거기서 멈춥니다** — 지워짐이 작을 때는 겹 둘로 끝납니다.
 */
const DESKTOP = `${HEAD}
/** 금. 가는 어두운 선입니다. */
uniform sampler2D uCrack;
/** 바람. 이 그림의 기울기를 90도 돌려 씁니다. */
uniform sampler2D uFlow;
/** 부드러운 결. 연기입니다. */
uniform sampler2D uSoft;

const int FLAKES = 6;
const int LAYERS = 10;

/**
 * 바람.
 *
 * 곧은 바람에 흐름 그림의 기울기를 90도 돌린 것을 더합니다. **기울기를 돌린 흐름은 발산이
 * 없습니다** — 재가 한 자리에 모이거나 한 자리에서 솟지 않고 맴돌이가 생깁니다. 그림을
 * 바람 쪽으로 밀어 읽으므로 결이 시간과 함께 흘러갑니다.
 */
vec2 windAt(vec2 p, float t) {
  vec2 w = normalize(uWindDir);
  vec2 q = p * uNoiseScale * 0.5 - w * t * uNoiseSpeed * 0.55 + 0.2;
  float e = 0.012;
  float c = texture(uFlow, q).r;
  float dx = (texture(uFlow, q + vec2(e, 0.0)).r - c) / e;
  float dy = (texture(uFlow, q + vec2(0.0, e)).r - c) / e;
  return w * uWindStrength + vec2(dy, -dx) * 0.35 * uTurbulence;
}

/**
 * 이 픽셀을 덮는 조각. 미리 곱한 알파입니다.
 *
 * 「home」 이 조각이 떠난 칸의 가운데입니다. **칸의 일부만 조각이 됩니다** — 전부 날리면
 * 떠난 자리가 조각으로 가득 차고, 그것은 삭은 것이 아니라 옮겨진 화면입니다.
 */
vec4 flakeAt(vec2 here, vec2 home, vec2 dir, float a) {
  vec2 uv = toUv(home);
  if (!inside(uv)) return vec4(0.0);
  float roll = speckAt(home, uFragmentSize, vec2(0.0));
  if (roll < 0.56) return vec4(0.0);
  float pace = speckAt(home, uFragmentSize, vec2(37.0, 11.0));
  // **칸의 가운데에 두지 않습니다.** 그러면 조각이 칸의 격자 위에 서서 13픽셀 간격의 점의
  // 줄로 보입니다 — 실제로 그렇게 보였습니다. 성질은 칸에서 읽고 자리만 흔듭니다.
  vec2 seat = home + (vec2(roll, pace) - 0.5) * uFragmentSize * 0.75;
  float s = a - front(seat);
  float hold = uFragmentLife * (0.55 + 0.95 * pace);
  if (s <= 0.0 || s >= hold) return vec4(0.0);
  // 지금 그 조각이 있는 자리. 이 픽셀이 거기서 얼마나 떨어져 있는가를 봅니다.
  vec2 now = seat + dir * (run(s) * uFragmentSpeed * (0.70 + 0.65 * pace));
  vec2 local = (here - now) / uFragmentSize;
  if (dot(local, local) > 0.36) return vec4(0.0);

  float aged = s / hold;
  // **돕니다.** 도는 것이 없으면 조각이 미끄러지는 얼룩으로 보입니다.
  float turn = (pace - 0.5) * 8.0 * s;
  float cs = cos(turn), sn = sin(turn);
  vec2 spun = vec2(local.x * cs - local.y * sn, local.x * sn + local.y * cs);
  // 조각의 가장자리. **마스크로 깎지 않고 반지름을 흔듭니다.**
  //
  // 모래알 그림을 문턱으로 깎아 모양을 만들면 조각이 3픽셀짜리 네모 뭉치가 됩니다 — 픽셀마다
  // 독립인 그림을 늘려 읽은 것이 그대로 보이는 것이고, 실제로 그렇게 보였습니다. 반지름에
  // 얹으면 그 흔들림이 가장자리에서만 뜻을 가지므로 안쪽은 꽉 차고 가장자리만 고르지
  // 않습니다. **조각과 함께 돕니다** — 자리로 읽으면 조각이 지나가는 창이 됩니다.
  float ragged = (texture(uGrain, seat * 7.3 + spun * 0.030).r - 0.5) * 0.26;
  float edge = length(spun) + ragged;
  // 가면서 잘아집니다.
  float alive = 1.0 - smoothstep(0.30 - aged * 0.16, 0.44 - aged * 0.16, edge);
  float alpha = alive * (1.0 - smoothstep(0.72, 1.0, aged)) * uFragmentAmount;
  if (alpha <= 0.004) return vec4(0.0);
  // **조각은 떠나온 자리의 그림을 가지고 갑니다.** 가면서 재의 색이 됩니다.
  vec3 color = ashen(plain(grab(toUv(seat))), aged);
  // 막 떨어져 나온 것이 잠깐 밝습니다.
  color += vec3(0.10, 0.09, 0.07) * (1.0 - smoothstep(0.0, 0.30, aged));
  return vec4(color * alpha, alpha);
}

/**
 * 이 픽셀을 덮는 알갱이. 미리 곱한 알파입니다.
 *
 * 「origin」 이 이 겹이 거슬러 가 닿은 자리이고 「went」 가 그만큼의 거리입니다. 거기서 떠난
 * 알갱이가 지금 여기 있으려면 그 거리를 갔어야 하고, **알갱이마다 빠르기가 여덟 배
 * 차이**이므로 그 조건에 맞는 것만 여기 있습니다 — 어떤 것은 날아가고 어떤 것은 떠 있습니다.
 */
vec4 dustAt(vec2 origin, float went, float band, float a) {
  vec2 uv = toUv(origin);
  if (!inside(uv)) return vec4(0.0);
  float roll = speckAt(origin, uAshSize, vec2(0.0));
  if (roll < 0.50) return vec4(0.0);
  float pace = speckAt(origin, uAshSize, vec2(53.0, 29.0));
  float s = a - front(cellHome(origin, uAshSize));
  if (s <= 0.0) return vec4(0.0);
  float life = uAshLife * (0.45 + 1.10 * pace);
  if (s >= life) return vec4(0.0);
  float mine = run(s) * uAshSpeed * (0.25 + 1.75 * pace);
  float fits = 1.0 - smoothstep(0.0, band, abs(mine - went));
  if (fits <= 0.004) return vec4(0.0);
  float aged = s / life;
  // **칸을 채우지 않습니다.** 채우면 5픽셀 네모가 깔린 모자이크가 되고, 알갱이는 그 칸
  // 안의 점입니다 — 점 사이가 비어야 알갱이 하나하나가 보입니다.
  //
  // **점의 자리를 칸 안에서 흔듭니다.** 칸의 가운데에 두면 점들이 격자에 맞춰 서고, 그러면
  // 알갱이가 아니라 눈금으로 보입니다. 이미 읽은 두 값으로 밀므로 값이 들지 않습니다.
  vec2 spot = fract(origin / uAshSize) - 0.5 - (vec2(pace, roll) - 0.5) * 0.80;
  float wide = 0.30 + roll * 0.22;
  float blob = 1.0 - smoothstep(wide * 0.35, wide, length(spot));
  float alpha = fits * blob * (1.0 - aged) * (1.0 - aged) * 1.30 * uAshAmount;
  if (alpha <= 0.004) return vec4(0.0);
  return vec4(vec3(0.74, 0.72, 0.67) * alpha, alpha);
}

void main(void) {
  float a = clamp(uAmount, 0.0, 1.0);
  vec2 uv = vTextureCoord;
  vec4 src = grab(uv);
  if (a <= 0.001) {
    finalColor = src;
    return;
  }
  vec2 here = toHere(uv);
  vec2 straight = normalize(uWindDir);

  // ---- 표면. 아직 성한 판과 삭는 중인 판.
  float s0 = a - front(here);
  float body = s0 < 0.0 ? 1.0 : 0.0;
  vec3 skin = plain(src);
  if (body > 0.0) {
    float k = clamp(1.0 + s0 / uEdgeWidth, 0.0, 1.0);
    // **구멍은 픽셀 크기입니다.** 칸마다 한 값으로 읽으면 구멍이 칸의 네모가 되고, 그것은
    // 삭는 것이 아니라 모자이크입니다 — 텍셀 하나가 화면의 한 픽셀쯤 되게 읽습니다.
    skin = crumble(skin, k,
                   texture(uCrack, here * 3.1 + 0.4).r,
                   texture(uGrain, here * 1.6 + 0.7).r,
                   body);
  }
  vec4 acc = vec4(skin * body, body);

  // ---- 갓 된 재. **아직 떠나지 않은 것입니다.**
  if (s0 > 0.0) {
    acc = over(justAsh(plain(src), s0, here - normalize(uWindDir) * run(s0) * 0.35, uGrain), acc);
  }

  // ---- 연기. **앞의 바로 뒤에만 아주 옅게.** 재를 받치는 것이지 볼거리가 아닙니다 —
  // 짙게 깔면 화면을 덮는 얼룩이 되고, 그러면 비어 가는 것이 보이지 않습니다.
  float soft = texture(uSoft, (here - straight * a * uNoiseSpeed * 0.5) * uNoiseScale * 1.7 + 0.5).r;
  float behind = smoothstep(0.0, 0.04, s0) * (1.0 - smoothstep(0.06, 0.34, s0));
  float veil = smoothstep(0.36, 0.46, soft) * behind * uSmokeAmount * uSmokeStrength * 0.34;
  acc = over(vec4(vec3(0.33, 0.32, 0.30) * veil, veil), acc);

  // ---- 조각. 이 픽셀의 바람 방향으로 자리 여섯을 짚습니다.
  vec2 dir = normalize(windAt(here, a));
  float reach = run(uFragmentLife * 1.5) * uFragmentSpeed * 1.35;
  float able = run(max(a - START, 0.0)) * uFragmentSpeed * 1.35 + uFragmentSize;
  vec2 last = vec2(-999.0);
  for (int j = 0; j < FLAKES; j++) {
    float arc = reach * (float(j) + 0.5) / float(FLAKES);
    if (arc > able) break;
    // **같은 칸을 두 번 겹치지 않습니다.** 짚는 간격이 칸보다 좁으면 같은 조각이 두 번
    // 얹혀 가장자리가 진해집니다.
    vec2 home = cellHome(here - dir * arc, uFragmentSize);
    if (distance(home, last) < uFragmentSize * 0.5) continue;
    last = home;
    acc = over(flakeAt(here, home, dir, a), acc);
  }

  // ---- 알갱이. 나이가 다른 겹 여덟이고, 겹마다 그 나이만큼 바람을 거슬러 갑니다.
  float wentBefore = 0.0;
  for (int j = 0; j < LAYERS; j++) {
    float t = uAshLife * pow((float(j) + 1.0) / float(LAYERS), 2.0);
    // **그만큼 오래된 알갱이가 아직 없습니다.** 뒤의 겹도 볼 것이 없습니다.
    if (t > a - START) break;
    float went = run(t) * uAshSpeed;
    // 굽은 길을 두 걸음으로 거슬러 갑니다. **가운데의 바람으로 재면 길이 곧게 됩니다.**
    vec2 origin = here;
    origin -= normalize(windAt(origin, a - t * 0.25)) * (went * 0.5);
    origin -= normalize(windAt(origin, a - t * 0.75)) * (went * 0.5);
    // 띠는 옆 겹까지의 간격이되 **알갱이의 크기로 묶습니다.** 겹의 간격은 멀어질수록
    // 벌어지는데 띠가 알갱이보다 훨씬 넓으면 알갱이 하나가 그 띠만큼 늘어난 선이 됩니다 —
    // 묶으면 먼 겹이 성기어질 뿐이고, 멀리 간 알갱이는 성긴 것이 맞습니다.
    float band = clamp((went - wentBefore) * 0.6, uAshSize * 0.8, uAshSize * 4.0);
    wentBefore = went;
    acc = over(dustAt(origin, went, band, a), acc);
  }

  finalColor = seal(acc, a, src);
}
`

/**
 * 핸드폰.
 *
 * **데스크탑의 것에서 값만 줄인 것이 아닙니다.** 그림이 셋이고, 바람이 사인 둘이고, 짚는
 * 수가 고정이고, 되풀이 안에서 그림을 읽는 횟수가 절반입니다 — 같은 시간표와 같은 모습을
 * 겨누되 방법이 다릅니다.
 *
 * |데스크탑|핸드폰|
 * |--|--|
 * |흐름 그림의 기울기(그림 셋 읽기)|사인 둘의 흐름 함수. **기울기를 손으로 적을 수 있습니다**|
 * |그림 다섯|셋. 금은 모래알을 굵게 읽어 대신하고, 연기는 알갱이의 옅은 몫으로 대신합니다|
 * |조각 여섯 자리 · 알갱이 여덟 겹|조각 셋 · 알갱이 넷|
 * |굽는 배율은 화면의 것|**1배.** 값이 픽셀 수에 그대로 붙고 배율 2는 픽셀이 네 배입니다|
 *
 * **모습이 크게 달라지지 않는 것은 알갱이가 이 연출의 대부분이기 때문입니다.** 알갱이는 겹의
 * 수를 줄이면 성기어지고, 성긴 재는 옅은 재로 보입니다 — 없는 재로 보이지 않습니다.
 */
const MOBILE = `${HEAD}
const int FLAKES = 3;
const int LAYERS = 4;

/**
 * 바람. **흐름 함수를 사인 둘로 적습니다.**
 *
 * 기울기를 손으로 적을 수 있으므로 그림을 세 번 읽지 않아도 됩니다. 기울기를 90도 돌린
 * 흐름은 발산이 없고, 위상이 시간만큼 밀리므로 같은 자리의 바람이 계속 돕니다.
 */
vec2 windAt(vec2 p, float t) {
  vec2 w = normalize(uWindDir);
  float u = p.x * 3.1;
  float v = p.y * 2.3 - t * uNoiseSpeed * 1.6;
  vec2 curl = vec2(-2.3 * sin(u) * sin(v), -3.1 * cos(u) * cos(v));
  return w * uWindStrength + curl * 0.16 * uTurbulence;
}

/** 이 픽셀을 덮는 조각. 데스크탑의 것에서 도는 것과 밝아지는 것을 뺀 것입니다. */
vec4 flakeAt(vec2 here, vec2 home, vec2 dir, float a) {
  vec2 uv = toUv(home);
  if (!inside(uv)) return vec4(0.0);
  float roll = speckAt(home, uFragmentSize, vec2(0.0));
  if (roll < 0.56) return vec4(0.0);
  float pace = speckAt(home, uFragmentSize, vec2(37.0, 11.0));
  vec2 seat = home + (vec2(roll, pace) - 0.5) * uFragmentSize * 0.75;
  float s = a - front(seat);
  float hold = uFragmentLife * (0.55 + 0.95 * pace);
  if (s <= 0.0 || s >= hold) return vec4(0.0);
  vec2 now = seat + dir * (run(s) * uFragmentSpeed * (0.70 + 0.65 * pace));
  vec2 local = (here - now) / uFragmentSize;
  if (dot(local, local) > 0.36) return vec4(0.0);
  float aged = s / hold;
  // 가장자리를 흔듭니다. **도는 것이 없을 뿐 데스크탑의 것과 같습니다.**
  float ragged = (texture(uGrain, seat * 7.3 + local * 0.030).r - 0.5) * 0.26;
  float edge = length(local) + ragged;
  float alive = 1.0 - smoothstep(0.30 - aged * 0.16, 0.44 - aged * 0.16, edge);
  float alpha = alive * (1.0 - smoothstep(0.72, 1.0, aged)) * uFragmentAmount;
  if (alpha <= 0.004) return vec4(0.0);
  return vec4(ashen(plain(grab(toUv(seat))), aged) * alpha, alpha);
}

/** 이 픽셀을 덮는 알갱이. **옅은 몫이 연기를 대신합니다.** */
vec4 dustAt(vec2 origin, float went, float band, float a) {
  vec2 uv = toUv(origin);
  if (!inside(uv)) return vec4(0.0);
  float roll = speckAt(origin, uAshSize, vec2(0.0));
  float pace = speckAt(origin, uAshSize, vec2(53.0, 29.0));
  float s = a - front(cellHome(origin, uAshSize));
  if (s <= 0.0) return vec4(0.0);
  float life = uAshLife * (0.45 + 1.10 * pace);
  if (s >= life) return vec4(0.0);
  float mine = run(s) * uAshSpeed * (0.25 + 1.75 * pace);
  float fits = 1.0 - smoothstep(0.0, band, abs(mine - went));
  if (fits <= 0.004) return vec4(0.0);
  float aged = s / life;
  vec2 spot = fract(origin / uAshSize) - 0.5 - (vec2(pace, roll) - 0.5) * 0.80;
  // 알갱이 하나와, 그 둘레의 옅은 것. **그림 하나로 둘을 냅니다** — 연기가 따로 없습니다.
  float wide = 0.30 + roll * 0.22;
  float blob = 1.0 - smoothstep(wide * 0.35, wide, length(spot));
  float bit = blob + smoothstep(0.40, 0.58, roll) * 0.14 * uSmokeAmount;
  float alpha = fits * bit * (1.0 - aged) * (1.0 - aged) * 1.30 * uAshAmount;
  if (alpha <= 0.004) return vec4(0.0);
  return vec4(vec3(0.74, 0.72, 0.67) * alpha, alpha);
}

void main(void) {
  float a = clamp(uAmount, 0.0, 1.0);
  vec2 uv = vTextureCoord;
  vec4 src = grab(uv);
  if (a <= 0.001) {
    finalColor = src;
    return;
  }
  vec2 here = toHere(uv);

  // ---- 표면. **금 그림을 읽지 않습니다.** 큰 얼룩을 잘게 읽어 넓은 결로 대신합니다 —
  // 칸마다 한 값으로 읽었더니 24픽셀 네모가 통째로 뚫려 모자이크가 되었습니다.
  float s0 = a - front(here);
  float body = s0 < 0.0 ? 1.0 : 0.0;
  vec3 skin = plain(src);
  if (body > 0.0) {
    float k = clamp(1.0 + s0 / uEdgeWidth, 0.0, 1.0);
    skin = crumble(skin, k,
                   texture(uLarge, here * 2.6 + 0.4).r,
                   texture(uGrain, here * 1.6 + 0.7).r,
                   body);
  }
  vec4 acc = vec4(skin * body, body);

  // ---- 갓 된 재. **아직 떠나지 않은 것입니다.** 데스크탑의 것과 같은 함수입니다.
  if (s0 > 0.0) {
    acc = over(justAsh(plain(src), s0, here - normalize(uWindDir) * run(s0) * 0.35, uGrain), acc);
  }

  // ---- 조각. 자리 셋.
  vec2 dir = normalize(windAt(here, a));
  float reach = run(uFragmentLife * 1.5) * uFragmentSpeed * 1.35;
  float able = run(max(a - START, 0.0)) * uFragmentSpeed * 1.35 + uFragmentSize;
  for (int j = 0; j < FLAKES; j++) {
    float arc = reach * (float(j) + 0.5) / float(FLAKES);
    if (arc > able) break;
    acc = over(flakeAt(here, cellHome(here - dir * arc, uFragmentSize), dir, a), acc);
  }

  // ---- 알갱이. 겹 넷이고 겹마다 한 걸음으로 거슬러 갑니다.
  float wentBefore = 0.0;
  for (int j = 0; j < LAYERS; j++) {
    float t = uAshLife * pow((float(j) + 1.0) / float(LAYERS), 2.0);
    if (t > a - START) break;
    float went = run(t) * uAshSpeed;
    // **한 걸음입니다.** 굽이는 자리마다 다른 바람이 내고, 길 하나가 곧은 것은 보이지 않습니다.
    vec2 origin = here - normalize(windAt(here, a - t * 0.5)) * went;
    float band = clamp((went - wentBefore) * 0.6, uAshSize * 0.8, uAshSize * 4.0);
    wentBefore = went;
    acc = over(dustAt(origin, went, band, a), acc);
  }

  finalColor = seal(acc, a, src);
}
`

/** 파라미터를 유니폼으로. 셰이더 둘과 파티클이 같은 것을 받습니다. */
export function ashUniforms(p: AshParams): Record<string, { value: number | Float32Array; type: string }> {
  return {
    uAmount: { value: 0, type: 'f32' },
    uAspect: { value: 1.6, type: 'f32' },
    uWindDir: { value: new Float32Array(p.windDir), type: 'vec2<f32>' },
    uWindStrength: { value: p.windStrength, type: 'f32' },
    uTurbulence: { value: p.turbulence, type: 'f32' },
    uNoiseScale: { value: p.noiseScale, type: 'f32' },
    uNoiseSpeed: { value: p.noiseSpeed, type: 'f32' },
    uEdgeWidth: { value: p.edgeWidth, type: 'f32' },
    uFragmentAmount: { value: p.fragmentAmount, type: 'f32' },
    uFragmentSize: { value: p.fragmentSize, type: 'f32' },
    uFragmentSpeed: { value: p.fragmentSpeed, type: 'f32' },
    uFragmentLife: { value: p.fragmentLife, type: 'f32' },
    uAshAmount: { value: p.ashAmount, type: 'f32' },
    uAshSize: { value: p.ashSize, type: 'f32' },
    uAshSpeed: { value: p.ashSpeed, type: 'f32' },
    uAshLife: { value: p.ashLife, type: 'f32' },
    uSmokeAmount: { value: p.smokeAmount, type: 'f32' },
    uSmokeStrength: { value: p.smokeStrength, type: 'f32' },
    // **그림의 텍셀 수입니다.** 칸마다 한 값으로 읽는 자리가 이 수로 텍셀의 가운데를 찾으므로,
    // 틀리면 칸 안이 한 값이 아니게 되고 알갱이가 번집니다.
    uGrainTexels: { value: grainTexels(), type: 'f32' },
  }
}

/**
 * 파라미터 하나를 유니폼에. `windDir` 은 둘이라 따로입니다.
 *
 * **셰이더에 없는 파라미터는 받아도 아무 일도 하지 않습니다** — 핸드폰의 것에는
 * `smokeStrength` 가 없습니다.
 */
export function tuneUniforms(uniforms: Record<string, number | Float32Array>,
                             params: Partial<AshParams>): void {
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined) continue
    if (key === 'windDir') {
      (uniforms.uWindDir as Float32Array).set(value as [number, number])
      continue
    }
    uniforms[`u${key[0].toUpperCase()}${key.slice(1)}`] = value as number
  }
}

/**
 * 재의 필터 하나.
 *
 * **어느 것인지는 만들 때 정해집니다.** 셰이더가 둘이라 유니폼 하나로 오갈 수 없습니다 —
 * 바꾸려면 새로 만듭니다. `Transition` 이 그렇게 합니다.
 */
export class AshFilter extends Filter {
  readonly lite: boolean

  constructor(lite = coarsePointer(), params: Partial<AshParams> = {}) {
    const p = { ...ASH_DEFAULTS, ...params }
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: lite ? MOBILE : DESKTOP }),
      // **핸드폰에서는 1배입니다.** 값이 픽셀 수에 그대로 붙고 배율 2는 픽셀이 네 배입니다.
      // 늘려 그리므로 아직 성한 판이 그동안 조금 무릅니다만, 삭는 중인 판의 1.6초입니다.
      resolution: lite ? 1 : 'inherit',
      resources: {
        ashUniforms: {
          ...ashUniforms(p),
          uInk: { value: new Float32Array([0.02, 0.03, 0.05]), type: 'vec3<f32>' },
        },
        // 핸드폰은 석 장만 읽습니다. 나머지 둘은 그쪽 셰이더에 없습니다.
        ...noiseResources(lite
          ? { uLarge: 'large', uGrain: 'grain' }
          : { uLarge: 'large', uGrain: 'grain', uCrack: 'crack', uFlow: 'flow', uSoft: 'soft' }),
      },
    })
    this.lite = lite
    this.direction = p.windDir
  }

  private direction: [number, number]

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.ashUniforms.uniforms as Record<string, number | Float32Array>
  }

  /** 얼마나 지워졌는가. 0 에서 1 입니다. */
  set amount(value: number) {
    this.uniforms.uAmount = Math.max(0, Math.min(1, value))
  }

  get amount(): number {
    return this.uniforms.uAmount as number
  }

  /** 가로의 방향. 참이면 오른쪽으로, 거짓이면 왼쪽으로 기울어 갑니다. */
  set toward(value: boolean) {
    const dir = this.uniforms.uWindDir as Float32Array
    dir[0] = Math.abs(this.direction[0]) * (value ? 1 : -1)
    dir[1] = this.direction[1]
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

  /** 파라미터를 바꿉니다. 고르는 동안 쓰는 자리입니다. */
  tune(params: Partial<AshParams>): void {
    if (params.windDir) this.direction = params.windDir
    tuneUniforms(this.uniforms, params)
  }
}
