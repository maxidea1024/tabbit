// 무력해지는 것.
//
// **없어지는 것도 갈리는 것도 아닙니다.** 타는 것(`dissolve.ts`)은 없애는 일이고 뒤집혀
// 돌아오는 것(`CardView.turnInto`)은 다른 것이 되는 일인데, 보스가 거는 것은 **있던 그대로
// 죽는** 일입니다 — 그 셋이 같은 몸짓이면 무엇이 일어난 것인지 화면에서 갈리지 않습니다.
//
// **얼룩이 종이에 스미듯 번집니다.** 가운데에서 바깥으로 번지고, 번진 자리는 색이 빠지고
// 어두워집니다. 스미는 자리만 보스의 색으로 조금 짙습니다.
//
// **바깥에서 안으로가 아니라 안에서 바깥으로입니다.** 바깥에서 조여 오면 무엇이 밖에서
// 덮은 것으로 보이고, 이것은 그 카드 자체가 죽는 일입니다.
//
// 얼개는 **문턱 하나로 번지는 소멸 셰이더**입니다 — 널리 쓰이는 것이고, 다른 것은 문턱을
// 넘은 자리를 지우는 대신 색을 빼는 것뿐입니다.
//
// 앞서 두 가지가 잘못되어 있었습니다. **경계를 거리 하나로 정해서** 완전한 타원이 퍼졌고
// 그것이 물방울로 보였습니다 — 지금은 노이즈가 그 경계를 흩뜨립니다. 그리고 **앞머리에
// 밝은 띠를 둘러서** 카드 위에서 그 띠가 가장 먼저 보였습니다 — 무력해지는 것이 아니라
// 무엇이 빛나는 것으로 읽혔습니다. 지금은 젖은 자리처럼 짙어질 뿐입니다. 읽히지 않던
// 금(ridge 노이즈)은 걷었습니다.

import { Filter, GlProgram } from 'pixi.js'

import { noiseResources } from './noise'

const VERTEX = `#version 300 es
in vec2 aPosition;
out vec2 vTextureCoord;
out vec2 vLimit;

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
  vLimit = uOutputFrame.zw * uInputSize.zw;
  vTextureCoord = aPosition * vLimit;
}
`

const FRAGMENT = `#version 300 es
precision highp float;

in vec2 vTextureCoord;
in vec2 vLimit;
out vec4 finalColor;

uniform sampler2D uTexture;
/** 0 이면 그대로, 1 이면 가장자리까지 다 번졌습니다. */
uniform float uSpread;
/** 얼마나 세게. 0 이면 꺼집니다. */
uniform float uAmount;
/** 번지는 앞머리의 색. 보스의 붉은색입니다. */
uniform vec3 uEdge;

/** 큰 얼룩. 번지는 앞머리가 동그라미로 보이지 않게 하는 것입니다. */
uniform sampler2D uLarge;
/** 모래알. 금의 잔 결입니다. */
uniform sampler2D uGrain;

/**
 * 번지는 얼룩.
 *
 * **경계를 노이즈가 정합니다.** 거리 하나로만 번지면 그 경계가 완전한 타원이라, 카드
 * 위에서 물방울이 퍼지는 것으로 보입니다 — 얼룩이 종이에 스미는 것은 어디가 먼저 스미는지가
 * 결마다 다른 일입니다.
 */
float stain(vec2 uv) {
  float a = texture(uLarge, uv * 1.7 + 0.13).r;
  float b = texture(uGrain, uv * 3.3 - 0.27).r;
  return a * 0.68 + b * 0.32;
}

void main(void) {
  vec4 src = texture(uTexture, vTextureCoord);
  if (uAmount <= 0.001 || src.a < 0.004) {
    finalColor = src;
    return;
  }

  vec2 uv = vTextureCoord / max(vLimit, vec2(0.0001));
  // 가운데에서의 거리. 세로가 긴 카드이므로 가로를 늘려 원이 아니라 카드 모양으로 번집니다.
  vec2 off = (uv - 0.5) * vec2(1.35, 1.0);
  float away = clamp(length(off) * 1.42, 0.0, 1.0);

  // **문턱 하나로 번집니다.** 널리 쓰이는 소멸 셰이더의 얼개이고, 다른 것은 문턱을 넘은
  // 자리를 지우는 대신 색을 빼는 것뿐입니다 — 무력해지는 것은 없어지는 일이 아닙니다.
  //
  // 거리가 차례를 정하고 노이즈가 경계를 흩뜨립니다. 노이즈의 몫이 너무 크면 얼룩 셋이
  // 따로 생기고, 너무 작으면 다시 타원이 됩니다.
  float field = away * 0.66 + (1.0 - stain(uv)) * 0.34;
  float front = uSpread * 1.18 - 0.09;
  // **경계가 넓습니다.** 좁으면 오려 붙인 자국이 되고, 넓어야 스민 것으로 보입니다.
  float dead = smoothstep(front + 0.16, front - 0.06, field);

  vec3 color = src.rgb / src.a;

  // 죽은 자리는 색이 빠지고 어두워집니다. **회색으로 가는 것이지 검게 칠하는 것이
  // 아닙니다** — 검게 칠하면 카드가 지워진 것으로 보입니다.
  float grey = dot(color, vec3(0.299, 0.587, 0.114));
  vec3 gone = mix(color, vec3(grey), 0.86) * 0.52;
  // **종이의 결이 남습니다.** 고른 회색은 칠한 것으로 보이고, 이 카드는 종이입니다.
  gone *= 0.92 + texture(uGrain, uv * 4.1).r * 0.16;

  color = mix(color, gone, dead * uAmount);

  // **스미는 자리만 아주 조금 짙습니다.**
  //
  // 밝은 띠를 두르던 것을 걷었습니다 — 그 띠가 카드 위에서 가장 먼저 보여서, 무력해지는
  // 것이 아니라 무엇이 빛나는 것으로 읽혔습니다. 걷고 나서 보스의 색을 그 자리에 0.42로
  // 섞어 보았고, 이번에는 붉은 얼룩이 종이에 번진 것이 되었습니다 — 알릴 것은 색이
  // 아니라 「여기까지 스몄다」이므로, 젖은 종이처럼 한 톤 어두워지는 것으로 족합니다.
  float wet = (1.0 - smoothstep(0.0, 0.20, abs(field - front))) * dead;
  color = mix(color, mix(color, uEdge, 0.12) * 0.88, wet * uAmount * 0.55);

  finalColor = vec4(color * src.a, src.a);
}
`

/**
 * 무력해지는 순간의 필터.
 *
 * **걸릴 때 만들고 다 번지면 뗍니다.** 죽어 있는 모습 자체는 카드의 얼굴이 이미 들고
 * 있으므로(`card-face.ts` 의 `debuffed`), 이 필터는 그 사이의 한 몸짓입니다.
 */
export class BlightFilter extends Filter {
  constructor(edge: [number, number, number] = [1.0, 0.28, 0.32]) {
    super({
      glProgram: GlProgram.from({ vertex: VERTEX, fragment: FRAGMENT }),
      padding: 2,
      resolution: 'inherit',
      resources: {
        blightUniforms: {
          uSpread: { value: 0, type: 'f32' },
          uAmount: { value: 0, type: 'f32' },
          uEdge: { value: new Float32Array(edge), type: 'vec3<f32>' },
        },
        ...noiseResources({ uLarge: 'large', uGrain: 'grain' }),
      },
    })
  }

  private get uniforms(): Record<string, number | Float32Array> {
    return this.resources.blightUniforms.uniforms as Record<string, number | Float32Array>
  }

  /** 어디까지 번졌는가. 0 에서 1 로 한 번 갑니다. */
  set spread(value: number) {
    this.uniforms.uSpread = Math.max(0, Math.min(1, value))
  }

  /** 얼마나 세게. */
  set amount(value: number) {
    this.uniforms.uAmount = Math.max(0, value)
  }
}
