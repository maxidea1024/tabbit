// 화면이 스스로 들고 있는 것들의 꼴.
//
// **코어의 타입이 아닙니다.** 판의 상태는 `core/state` 에 있고, 여기 있는 것은 그것을
// 그리는 동안 화면이 따로 들고 있어야 하는 것들입니다 — 지금 뒤집히는 중인 카드,
// 굴러 올라가는 숫자 하나, 펼쳐 둔 팩의 한 장.

import { Container, Graphics } from 'pixi.js'
import { ArriveFilter } from '../shader/arrive'
import { CardView } from '../render/card-view'
import { Motion, Spring } from '../render/motion'
import type { ShopItem } from '../core/shop'

/**
 * 판 위로 나와 바뀌는 것을 보이는 중인 카드들.
 *
 * **한 몸짓에 여럿을 담습니다.** 타로 하나가 다섯 장을 바꾸면 다섯 장이 함께 나와 차례로
 * 뒤집히고 함께 돌아갑니다 — 다섯 번 서고 걷히는 것이 아닙니다.
 */
export interface CardShow {
  cards: {
    view: CardView
    uid: number
    kind: 'modify' | 'destroy' | 'add'
    /** 손패에서 빌려 온 것인가. 끝나면 손패가 도로 가져갑니다. */
    borrowed: boolean
  }[]
  /** 카드를 돌려보내는 시각. */
  until: number
  /** 남은 뷰를 지우는 시각. */
  clear: number
  closed: boolean
}

/** 떠오르는 글자 하나. **글과 그 뒤의 번쩍임이 한 덩어리입니다.** */
export interface Riser {
  node: Container
  life: number
  /** 이 글이 떠오르는 거리. 위에 남은 자리만큼입니다. */
  lift: number
  homeX: number
  homeY: number
  drift: number
  rumble: number
}

/**
 * 머리띠에 달린 태그 칩 하나.
 *
 * **칩은 `refresh` 에서 한 번 만들고, 번쩍임과 발동은 이것을 매 프레임 만집니다.** 전에는
 * 번쩍이는 1초 동안 매 프레임 칩 전부를 버리고 다시 만들었고, 칩마다 필터를 새로 걸었습니다.
 */
export interface TagCell {
  cell: Container
  tagId: string
  /** 쓴 태그인가. 발동이 끝나면 이 밝기로 돌아갑니다. */
  used: boolean
  size: number
  /**
   * 발동하는 동안의 흰 번쩍임. 끝나면 지웁니다.
   *
   * **새로 선 칩과 같은 기법입니다**(`shine`). `ArriveFilter` 는 카드 한 장의 크기에 맞춰
   * 여백을 잡아 두어 26픽셀짜리 칩에서는 그림이 왼쪽 위로 밀리는데, 그 까닭으로 새 칩에는
   * 걸지 않으면서 발동하는 칩에는 걸고 있었습니다.
   */
  lit?: Graphics
  /** 새로 들어온 칩의 흰 번쩍임. 끝나면 지웁니다. */
  shine?: Graphics
}

/**
 * 블라인드 고르기 판의 카드 하나.
 *
 * **들어오는 동안 매 프레임 바뀌는 것은 자리와 알파뿐입니다.** 전에는 그 1초 동안 매 프레임
 * 카드 셋을 글 25개와 함께 버리고 다시 만들었습니다.
 */
export interface BlindGroup {
  group: Container
  index: number
  x: number
  bottom: number
  width: number
  height: number
  now: boolean
  done: boolean
  /** 건너뛰기 단추의 가운데. 카드 위쪽 기준입니다. 도구가 누르는 자리를 매 프레임 맞춥니다. */
  skipY?: number
  pickY?: number
}

/**
 * 펼친 팩의 카드 하나.
 *
 * **띠를 따로 들고 있습니다.** 자리가 없다는 표시는 팩이 열려 있는 동안에도 바뀌므로 —
 * 소모품을 쓰면 자리가 생깁니다 — 매 프레임 켜고 끄려면 그 조각을 찾을 수 있어야 합니다.
 */
/**
 * 「런 정보」 의 갈래.
 *
 * **한 판을 도는 동안 궁금해지는 것이 넷입니다** — 어느 족보가 몇 점인지, 이 안테의
 * 블라인드가 무엇인지, 지금 난이도가 무엇을 바꾸는지, 그리고 **지금 이 판에서 다음 한 수를
 * 무엇으로 두어야 하는지**입니다.
 *
 * 넷째가 인사이트이고, 규격은 `doc/insight.md` 입니다. 앞의 셋은 표를 읽어 적는 것이고
 * 그것 하나만 지금의 상태를 셉니다.
 */
export type RunInfoTab = 'hands' | 'blinds' | 'stakes' | 'insight'

export interface PackFace {
  node: Container
  card: Container
}

/**
 * 겉면만 시각을 받는 것 하나.
 *
 * 판(에디션)의 셰이더는 판 전체의 시계를 받아 씁니다 — 넣어 주는 자리가 없으면 `uTime` 이
 * 0 에 굳어 무늬가 흐르지 않습니다.
 */
export interface LookTick {
  at(time: number): void
  /** 지금 보고 있는 시각과 기울기. **도구가 조회합니다.** */
  seen(): { time: number; tilt: number } | undefined
}

/** 펼친 팩의 카드 한 장. 얼굴과, 그 한 장이 자기만 아는 것들입니다. */
export interface PackView {
  face: PackFace
  /** 이 카드의 겉면. 판이 걸린 것만 있습니다. */
  look?: LookTick
  motion: Motion
  /**
   * 고른 한 장이 올라온 높이.
   *
   * **용수철입니다.** 고른 것인지로 자리를 바로 정하면 놓는 순간 카드가 툭 내려앉습니다 —
   * 줄에서 고른 조커와 상점의 칸이 같은 몸짓이고 같은 용수철입니다.
   */
  lift: Spring
  index: number
  item: ShopItem
  /**
   * 갸웃거리는 물결의 자리.
   *
   * **낱장이 저마다 다른 자리에서 시작합니다.** 같은 자리에서 시작하면 다섯 장이 한
   * 몸으로 기울어지고, 그것은 살아 있는 것이 아니라 판이 통째로 흔들리는 것입니다.
   */
  sway: number
  /**
   * 나오면서 한 번 반짝이는 것. 아직 나오지 않았으면 `-1`, 나온 뒤 0 에서 1 로 갑니다.
   *
   * **끝을 세는 것이지 남은 것을 세는 것이 아닙니다.** 세기는 이 값의 사인이므로 0 에서
   * 올라 1 에서 내려오고, 그래서 켜지는 것도 꺼지는 것도 부드럽습니다.
   */
  glow: number
  /** 반짝이는 동안에만 붙습니다. 다 반짝이면 떼어 냅니다 — 필터 하나가 곧 텍스처 하나입니다. */
  arrive?: ArriveFilter
}
