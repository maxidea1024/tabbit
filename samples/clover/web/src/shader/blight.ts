// 무력해지는 것.
//
// **없어지는 것도 갈리는 것도 아닙니다.** 타는 것(`dissolve.ts`)은 없애는 일이고 줄기가
// 지나가는 것(`imprint.ts`)은 다른 것이 되는 일인데, 보스가 거는 것은 **있던 그대로 죽는**
// 일입니다 — 그 셋이 같은 몸짓이면 무엇이 일어난 것인지 화면에서 갈리지 않습니다.
//
// 금이 가운데에서 바깥으로 번집니다. 번진 자리는 색이 빠지고 어두워지고, 번지는 앞머리만
// 보스의 색으로 잠깐 탑니다.
//
// **바깥에서 안으로가 아니라 안에서 바깥으로입니다.** 바깥에서 조여 오면 무엇이 밖에서
// 덮은 것으로 보이고, 이것은 그 카드 자체가 죽는 일입니다.

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
 * 금 한 줄.
 *
 * **마루를 세웁니다.** 노이즈를 그대로 쓰면 얼룩이고, 0.5 에서의 거리를 뒤집으면 그 값이
 * 가장 높은 자리가 가는 선으로 남습니다 — 그것이 금입니다.
 */
float ridge(vec2 p) {
  float n = texture(uLarge, p).r;
  return pow(1.0 - abs(n * 2.0 - 1.0), 7.0);
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
  float away = length(off) * 1.42;

  // 번지는 앞머리. **울퉁불퉁해야 번진 것으로 보입니다.**
  float wobble = (texture(uLarge, uv * 0.7 + 0.21).r - 0.5) * 0.30;
  float front = uSpread * 1.25 - 0.12 + wobble;
  float dead = smoothstep(front + 0.06, front - 0.06, away);
  // 앞머리 바로 뒤의 띠. 여기만 색이 남습니다.
  float rim = (1.0 - smoothstep(0.0, 0.14, abs(away - front))) * dead;

  vec3 color = src.rgb / src.a;

  // 죽은 자리는 색이 빠지고 어두워집니다. **회색으로 가는 것이지 검게 칠하는 것이
  // 아닙니다** — 검게 칠하면 카드가 지워진 것으로 보입니다.
  float grey = dot(color, vec3(0.299, 0.587, 0.114));
  vec3 gone = mix(color, vec3(grey), 0.86) * 0.52;

  // 금. 죽은 자리 안에만 그어지고, 잔 결이 겹칩니다.
  float crack = ridge(uv * vec2(2.1, 1.5) + 0.13);
  crack = clamp(crack * (0.7 + texture(uGrain, uv * 3.0).r * 0.6), 0.0, 1.0);
  gone = mix(gone, gone * 0.35, crack * dead * 0.9);

  color = mix(color, gone, dead * uAmount);
  // 앞머리가 탑니다. **번지는 그 순간에만 있습니다.**
  color += uEdge * rim * uAmount * 1.15;

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
