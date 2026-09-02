// 카드 한 벌의 겉모습.
//
// **덱과 다른 축입니다.** 덱은 런의 시작 조건이고 이것은 그 52장이 어떻게 보이는가입니다 —
// 판이 도는 중에 갈아입어도 규칙은 하나도 달라지지 않습니다. 그래서 리플레이에도 해시에도
// 들어가지 않고 옵션에만 남습니다.
//
// **한 판에 하나입니다.** 카드를 그리는 자리가 넷이므로(손패 · 덱 보기 · 팩 · 상점) 넷이
// 저마다 「어느 세트인가」를 알아야 하면 그 넷 중 하나만 놓쳐도 그 자리의 카드가 다른 벌이
// 됩니다. 뒷면을 `card-back.ts` 한 곳에 둔 것과 같은 이유입니다.

import type { Data } from '../core/data'
import { SuitKind } from '../generated/enums/suit-kind'
import { COLOR } from './theme'

export interface SetLook {
  setId: string
  /**
   * 그림이 있는 폴더. **비면 무늬와 랭크를 그립니다.**
   *
   * `public/art/<폴더>/<무늬>_<랭크>.png` 이고, 그림이 곧 얼굴입니다 — 모서리의 글자까지
   * 그림 안에 있습니다.
   */
  artDir?: string
  /**
   * 그림에 모서리의 랭크와 무늬가 들어 있는가.
   *
   * **정본 한 벌만 그렇습니다.** 밖에서 온 52장이라 모서리까지 그려져 있고, 우리가 굽는
   * 세트는 그림 카드 12컷뿐이라 모서리를 화면이 그 위에 그립니다 — 그래서 세트 하나가
   * 52컷이 아니라 12컷입니다.
   */
  artHasIndex: boolean
  /** 종이의 색. **그림이 없는 랭크가 이 색을 씁니다** — 그림의 바탕과 같아야 한 벌로 보입니다. */
  paper: number
  /** 무늬마다의 색. 그림이 없는 자리에서 보입니다. */
  ink: Record<number, number>
  /** 그림이 어디서 왔는가. 고르는 자리에 적힙니다. */
  credit?: string
}

/**
 * 그림도 표도 없을 때의 한 벌.
 *
 * **표를 읽기 전에도 카드가 그려집니다** — 부팅과 미리보기 도구가 그렇습니다.
 */
const FALLBACK: SetLook = {
  setId: '',
  artHasIndex: false,
  paper: COLOR.cardFace,
  ink: {
    [SuitKind.Spade]: COLOR.black,
    [SuitKind.Heart]: COLOR.red,
    [SuitKind.Club]: COLOR.black,
    [SuitKind.Diamond]: COLOR.red,
  },
}

let inPlay: SetLook = FALLBACK

/** 이 판이 쓸 한 벌. 고른 그 자리에서 부릅니다. */
export function setCardSet(look: SetLook): void {
  inPlay = look
}

/** 지금 그려야 할 한 벌. */
export function cardSet(): SetLook {
  return inPlay
}

/** 이 무늬의 색. */
export function suitInk(suit: SuitKind): number {
  return inPlay.ink[suit] ?? FALLBACK.ink[suit] ?? COLOR.black
}

/** 지금 세트의 그림 폴더. 없으면 그립니다. */
export function cardArtDir(): string | undefined {
  return inPlay.artDir
}

/** 지금 세트의 종이색. */
export function cardPaper(): number {
  return inPlay.paper
}

/** 그림이 있는 자리에서도 모서리를 그려야 하는가. */
export function drawsIndex(): boolean {
  return !inPlay.artHasIndex
}

/**
 * `CardSet` 표의 한 줄이 한 벌이 됩니다.
 *
 * **없는 세트를 넘겨받으면 첫 줄로 갑니다.** 손으로 고친 저장소나 예전 판의 값이 그러하고,
 * 겉모습 하나 때문에 카드가 그려지지 않을 이유는 없습니다.
 */
export function setLookOf(data: Data, setId: string): SetLook {
  const row = data.tables.cardSet.findBySetId(setId)
    ?? [...data.tables.cardSet.records].sort((one, two) => one.sortOrder - two.sortOrder)[0]
  if (!row) return FALLBACK

  const ink: Record<number, number> = { ...FALLBACK.ink }
  for (const suit of [SuitKind.Spade, SuitKind.Heart, SuitKind.Club, SuitKind.Diamond]) {
    const found = data.tables.cardSetSuit.findBySetIdAndSuit(row.setId, suit)
    if (found) ink[suit] = hex(found.ink)
  }

  return {
    setId: row.setId,
    artDir: row.artDir === '' ? undefined : row.artDir,
    artHasIndex: row.artHasIndex,
    paper: hex(row.paper),
    ink,
    credit: row.credit === '' ? undefined : row.credit,
  }
}

/** 고를 수 있는 세트들. 순서는 표가 정합니다. */
export function setsOf(data: Data): { setId: string; name: string; credit?: string }[] {
  return [...data.tables.cardSet.records]
    .sort((one, two) => one.sortOrder - two.sortOrder)
    .map(row => ({
      setId: row.setId,
      name: row.name,
      credit: row.credit === '' ? undefined : row.credit,
    }))
}

function hex(value: string): number {
  const parsed = Number.parseInt(value.replace('#', ''), 16)
  return Number.isNaN(parsed) ? COLOR.black : parsed
}
