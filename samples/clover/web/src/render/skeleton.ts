// 스켈레톤 — 그림이 닿기 전의 자리.
//
// **아무것도 두지 않는 것과 갈라야 합니다.** 여기에는 식별자에서 뽑은 문양이 서 있었고
// (걷은 까닭은 `glyph.ts` 의 머리에 있습니다), 그것을 걷은 뒤 맨 판만 남기면 조커가
// 「그림이 없는 카드」로 읽힙니다 — 둘 다 오해이고, 갈래가 다른 오해입니다.
//
// **닿기 전이 짧지 않습니다.** 도감을 굴리면 새 줄마다 그림 열 장을 부탁하고 그 줄은 닿기
// 까지 비어 있습니다. 게다가 상한(96MB)이 조커 그림 전부(442MB)보다 작으므로 굴리는 동안
// 놓고 다시 읽는 것이 생깁니다 — 41줄을 굴려 요청 490건에 다시 읽은 것이 20건이었습니다.
// 그래서 이 자리는 한 번 지나가는 화면이 아니라 굴리는 내내 보이는 화면입니다.
//
// **모양이 셋입니다.** 그림의 틀이 셋이기 때문입니다 — 카드는 긴 사각형, 태그와 보스의
// 그림 파일은 정사각형이고 화면에서는 동그라미로 오려 씁니다. 대역이 그림과 다른 틀이면
// 닿는 순간 자리가 튀고, 튀는 것은 「바뀌었다」로 읽힙니다.
//
// **움직이지 않습니다.** 흐르는 빛을 지나가게 하는 것이 이런 자리의 흔한 방법인데, 도감의
// 칸은 한 번 구워 스프라이트로 놓으므로(`ui/collection.ts`) 그 빛이 구워진 채로 멈춥니다 —
// 멈춘 빛은 무늬로 읽히고, 그것이 걷어낸 문양과 같은 오해입니다. 대신 **한 단 밝은 안쪽
// 판**으로 「여기 그림이 들어온다」를 말합니다.

import { COLOR } from './ink'
import { Graphics } from 'pixi.js'

/**
 * 안쪽 판이 바깥에서 얼마나 들어오는가. 짧은 변에 대한 비율입니다.
 *
 * **비율입니다.** 픽셀로 두면 88짜리 카드와 84짜리 동그라미에서 테의 두께가 달라 보입니다.
 */
const INSET = 0.16

/** 긴 사각형 하나. 카드가 이 틀입니다. */
export function skeletonCard(g: Graphics, width: number, height: number,
                             radius: number): void {
  plate(g, width, height, radius)
}

/**
 * 정사각형 하나.
 *
 * **모서리가 카드보다 덜 둥급니다.** 그림 파일의 틀이고, 카드처럼 둥글게 두면 그 자리에
 * 카드가 들어오는 것으로 읽힙니다.
 */
export function skeletonSquare(g: Graphics, side: number): void {
  plate(g, side, side, side * 0.09)
}

/** 동그라미 하나. 태그의 칩과 보스의 인장이 이 틀입니다. **가운데가 원점입니다.** */
export function skeletonCircle(g: Graphics, diameter: number): void {
  const r = diameter / 2
  g.circle(0, 0, r).fill({ color: COLOR.unseen })
  g.circle(0, 0, r).stroke({ color: COLOR.unseenInk, width: 1.5 })
  g.circle(0, 0, r * (1 - INSET * 2)).fill({ color: COLOR.unseenInk, alpha: 0.55 })
}

/** 바탕 하나와 그 안의 한 단 밝은 판. 사각형 둘의 공통입니다. */
function plate(g: Graphics, width: number, height: number, radius: number): void {
  // 모서리는 0 입니다. 부르는 쪽의 인자 순서를 지키기 위해 매개변수만 남깁니다.
  void radius
  g.rect(0, 0, width, height).fill({ color: COLOR.unseen })
  const pad = Math.min(width, height) * INSET
  g.rect(pad, pad, width - pad * 2, height - pad * 2)
    .fill({ color: COLOR.unseenInk, alpha: 0.55 })
}
