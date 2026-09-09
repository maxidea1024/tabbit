// 통 하나의 사각형을 고정하는 것.
//
// **둘을 함께 둡니다.** `boundsArea` 는 그리는 것의 경계이고 `filterArea` 는 필터가 도는
// 자리입니다. 둘 다 그 통의 좌표계에서 재는 같은 사각형이므로 값이 하나이고, **하나만 두면
// 구운 사진에서 그 통이 빠집니다.**
//
// ## 하나만 두면 무엇이 일어나는가
//
// Pixi 8 은 `boundsArea` 가 있는 통의 필터 사각형에 **렌더 그룹의 변환을 두 번 곱합니다.**
//
// |어디|무엇을 하는가|
// |--|--|
// |`_getGlobalBoundsRecursive`|`boundsArea` 를 `worldTransform` 으로 옮깁니다 — 이 값에 이미 렌더 그룹의 변환이 들어 있습니다|
// |`getFastGlobalBounds`|그 결과에 `renderGroup.worldTransform` 을 한 번 더 곱합니다|
//
// `boundsArea` 가 없는 통은 아이들의 경계를 `groupTransform`(렌더 그룹 안쪽의 좌표)으로
// 모으므로 마지막의 한 번이 곧 유일한 한 번입니다. **`boundsArea` 가 있는 길만 두 번입니다.**
//
// 평소 프레임에서는 그 변환이 항등이라 두 번 곱해도 값이 같습니다. **어긋나는 것은 굽는
// 자리뿐입니다** — `renderer.render({ transform })` 로 화면을 구울 때 그 변환이 판을 창
// 가운데로 놓은 만큼의 이동(`crop`)이고, 필터 사각형이 그만큼 한 번 더 밀립니다.
//
// |밀린 사각형이|화면에 보이는 것|
// |--|--|
// |화면 안에 있으면|입력 그림에 내용이 들어가지 않아 **그 통이 통째로 빠집니다.** 뒤의 판 색이 그 자리에 보입니다|
// |화면 밖으로 나가면|Pixi 가 그 필터를 건너뛰고 통을 그대로 그립니다 — **셰이더의 무늬만 없어집니다**|
//
// 그래서 같은 원인이 「검은 카드」와 「무늬 없는 카드」 둘로 보이고, 카드마다 자리가 다르므로
// 한 화면에 둘이 섞입니다.
//
// **창의 비율이 기준 비율이면 재현되지 않습니다.** 판이 창을 꽉 채우면 `crop` 이 (0, 0)
// 이므로 두 번 곱한 값이 한 번 곱한 값과 같습니다 — 규격은 `doc/ui/transition.md` 의
// 「구울 그림의 필터 사각형」 입니다.

import { Rectangle, type Container } from 'pixi.js'

/**
 * 이 통의 경계와 필터 사각형을 그 크기로 고정합니다.
 *
 * **`boundsArea` 를 여기 밖에서 대입하지 않습니다.** 필터가 걸릴 통에 그것만 두면 위의
 * 두 번 곱하기를 지나가고, 그 결함은 기준 비율이 아닌 창에서만 드러나므로 눈으로 지나갑니다.
 * `tools/check-pin.ts` 가 이 규약을 확인합니다.
 */
export function pinBox(node: Container, width: number, height: number): void {
  // **사각형 하나를 둘이 나눠 씁니다.** Pixi 는 이 둘을 읽기만 하고, 값이 같아야 하는
  // 것이므로 따로 두면 한쪽만 고칠 길이 생깁니다.
  const box = new Rectangle(0, 0, width, height)
  node.boundsArea = box
  node.filterArea = box
}
