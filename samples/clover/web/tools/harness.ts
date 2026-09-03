// 화면을 실제로 눌러 판을 두는 도구들.
//
// **좌표가 `render/game.ts` 의 상수와 같아야 합니다.** 그래서 한 자리에 모읍니다 — 도구마다
// 따로 적어 두면 배치를 고칠 때 한쪽만 고쳐지고, 그 도구는 엉뚱한 곳을 눌러 놓고 아무 말도
// 하지 않습니다.

import type { Page } from 'playwright'

/** 화면이 스스로 알리는 것들. `game.ts` 의 `publishPeek` 이 씁니다. */
export interface Peek {
  /** 지금 어느 씬인가. 로딩 · 타이틀 · 판 셋뿐입니다. */
  scene: string
  /** 화면에 살아 있는 카드·조커 뷰의 수. 판을 접었으면 0 입니다. */
  views: number
  seed: string
  /** 무엇으로 시작한 판인가. `Deck.deck_id` 와 `StakeKind` 의 이름입니다. */
  deck: string
  stake: string
  /** 지금 칩이 날고 있는가. */
  /** 설명 쪽지가 떠 있는가. */
  tip: boolean
  /** 지금 골라 둔 카드의 수. */
  picked: number
  /** 상점 판이 지금 서 있는가. */
  shopUp: boolean
  /** 연출의 시계. 초입니다. */
  clock: number
  phase: string
  ante: number
  blind: number
  money: number
  score: number
  target: number
  jokers: number
  discards: number
  hands: number
  packOpen: boolean
  packs: number
  played: number
  coins: boolean
  cleared: boolean
  consumables: number
  /** 상점 칸마다 무엇이 서 있는가. `ShopItemKind` 의 값입니다 — 조커가 1, 소모품이 2~5. */
  shopKinds?: number[]
  /** 소모품이 올 자리를 잡아 준 횟수와, 잡을 것이 없어 그냥 돌아온 횟수. */
  flyAsked?: number
  flyMissed?: number
  /** 최근에 난 소리들. 새것이 뒤입니다. */
  sounds?: string[]
  /** 들고 있는 태그와, 딱지에 실제로 그린 칩 수. */
  tags?: string[]
  tagChips?: number
  /** 칩들이 실제로 그려진 자리. 피벗을 뺀 왼쪽 위 모서리입니다. */
  tagAt?: { x: number; y: number; scale: number }[]
  /** 카드 뷰가 어느 통에 몇 장 있는가. */
  bins?: { hand: number; played: number; fades: number; deals: number; shown: number }
  busy: boolean
  /** 맨 위 판이 화면에서 차지한 사각형. 다 떠오른 판만 값이 있습니다. */
  modalBox?: { x: number; y: number; width: number; height: number }
  hand: { rank: number, suit: number }[]
  hurry(times: number): void
  grantJoker?(count: number): void
  grantConsumable?(count: number): void
  grantMoney?(amount: number): void
  /** 조커 첫 장이 지금 그려진 자리. 흔들림을 재는 도구가 씁니다. */
  jokerX?(): number | undefined
  /** 소모품 마지막 칸이 지금 그려진 자리. 사서 오는 길을 재는 데 씁니다. */
  itemX?(): number | undefined
  handOrder: number[]
  jokerOrder: number[]
  /** 눌러야 하는 것들의 자리. `game.ts` 가 그린 그대로 알립니다. */
  spots: Record<string, { x: number; y: number } | undefined>
  /** 연출이 다음에 낼 박자. 소리가 비는 자리를 찾을 때 씁니다. */
  coming: string
  /** 정산 판이 떠 있는가. */
  payout: boolean
}

/**
 * 연출의 속도를 바꿉니다.
 *
 * **판을 끝까지 두는 구간에만 씁니다.** 연출을 찍는 구간에서 올리면 스크린샷이 다른 순간을
 * 잡습니다 — 사람이 보라고 넣은 뜸을 지우는 것이므로, 그 뜸을 찍는 자리에서는 지우면 안
 * 됩니다.
 */
/**
 * 도구가 여는 판이 곧바로 타이틀이게 합니다.
 *
 * **게임의 첫 화면은 로그인 씬입니다.** 계정을 만들지 말지를 한 번 묻고, 정한 다음부터는
 * 묻지 않습니다 — 도구는 그 물음의 대상이 아니므로 「정해 둔 것」으로 시작합니다.
 *
 * 페이지를 만든 직후에 겁니다.
 *
 *     const page = await browser.newPage(...)
 *     await skipLogin(page)
 *     await page.goto(...)
 *
 * **로그인 화면 자체를 보는 도구는 부르지 않습니다.** `check-leaderboard` 와
 * `check-relabel` 이 그렇습니다.
 */
export async function skipLogin(page: Page): Promise<void> {
  await page.addInitScript(
    'try { localStorage.setItem("clover.account.mode", "single") } catch {}')
}

export async function hurry(page: Page, times: number): Promise<void> {
  await page.evaluate(fast => {
    ;(window as unknown as { __clover: { hurry(times: number): void } }).__clover.hurry(fast)
  }, times)
}

/**
 * 조커를 그냥 놓습니다. **개발 서버에서만 됩니다.**
 *
 * 자리를 바꾸는 것이 되는지 보려면 조커가 둘 있어야 하는데, 그것을 사려고 판을 열 판 두는
 * 동안 도구가 확인하려던 것과 상관없는 곳에서 멈춥니다.
 */
export async function grantJoker(page: Page, count: number): Promise<void> {
  await page.evaluate(many => {
    const hook = (window as unknown as {
      __clover: { grantJoker?(count: number): void }
    }).__clover
    hook.grantJoker?.(many)
  }, count)
}

/** 돈을 그냥 놓습니다. **개발 서버에서만 됩니다.** */
export async function grantMoney(page: Page, amount: number): Promise<void> {
  await page.evaluate(many => {
    const hook = (window as unknown as {
      __clover: { grantMoney?(amount: number): void }
    }).__clover
    hook.grantMoney?.(many)
  }, amount)
}

/** 소모품을 그냥 놓습니다. **개발 서버에서만 됩니다.** */
export async function grantConsumable(page: Page, count: number): Promise<void> {
  await page.evaluate(many => {
    const hook = (window as unknown as {
      __clover: { grantConsumable?(count: number): void }
    }).__clover
    hook.grantConsumable?.(many)
  }, count)
}

export async function peek(page: Page): Promise<Peek> {
  // **화면이 다시 뜨는 순간이 있습니다** — 개발 서버가 파일을 고쳐 다시 읽히면 그 한 순간
  // 손잡이가 없습니다. 없는 것을 그대로 돌려주면 도구가 그 자리에서 멈춥니다.
  for (let wait = 0; wait < 25; wait++) {
    const seen = await page.evaluate(
      () => (window as unknown as { __clover?: Peek }).__clover)
    if (seen) return seen
    await page.waitForTimeout(200)
  }
  throw new Error('화면이 상태를 알리지 않습니다')
}

/** 연출이 끝날 때까지 기다립니다. */
export async function settle(page: Page): Promise<void> {
  for (let wait = 0; wait < 60; wait++) {
    if (!(await peek(page)).busy) return
    await page.waitForTimeout(200)
  }
}

/**
 * 살 수 있는 팩이 있으면 뜯습니다.
 *
 * **딱지를 누르는 것은 고르는 것까지입니다.** 뜯는 것은 그 밑에 서는 「산다」가 하므로
 * 두 번 누릅니다 — 한 번만 누르고 열렸는지 묻고 있어서, 이 도구는 팩을 한 번도 뜯지
 * 못한 채 「돈이 모자랍니다」를 적고 있었습니다.
 */
export async function buyAffordablePack(page: Page): Promise<void> {
  for (let slot = 0; slot < 2; slot++) {
    const packs = (await peek(page)).packs
    if (packs <= slot) return
    const spot = await packSlot(page, slot, packs)
    await page.mouse.click(spot.x, spot.y)
    await page.waitForTimeout(350)
    const buy = await packBuySpot(page, slot, packs)
    await page.mouse.click(buy.x, buy.y)
    await page.waitForTimeout(700)
    if ((await peek(page)).packOpen) return
  }
}

/**
 * 고른 팩 밑의 「산다」 단추.
 *
 * `syncHeldBar` 과 같은 계산입니다 — 딱지의 가운데이고, 값이 있던 그 줄입니다.
 */
/** 고른 팩 밑의 「산다」. 상점 칸과 같은 셈입니다. */
export async function packBuySpot(page: Page, slot: number, count = 2):
    Promise<{ x: number; y: number }> {
  const tileW = 104
  const gap = 26
  const span = count * tileW + (count - 1) * gap
  const left = POPUP_X - SHOP_W / 2 + (SHOP_W - span) / 2
  return at(page, left + slot * (tileW + gap) + tileW / 2, SHOP_PACKS + 62 + 82)
}

/** 상점의 팩 칸. `drawPackRow` 와 같은 계산입니다. */
export async function packSlot(page: Page, slot: number, count = 2): Promise<{ x: number; y: number }> {
  const tileW = 104
  const gap = 26
  const span = count * tileW + (count - 1) * gap
  const left = POPUP_X - SHOP_W / 2 + (SHOP_W - span) / 2
  return at(page, left + slot * (tileW + gap) + tileW / 2, SHOP_PACKS + 62)
}

/**
 * 상점의 칸을 살 수 있으면 삽니다.
 *
 * 자리는 `game.ts` 의 `syncShop` 과 같은 계산입니다 — 판이 가운데에 서고 물건이 그 안에서
 * 가운데로 모입니다.
 */
export async function buyFirstAffordable(page: Page): Promise<void> {
  for (let slot = 0; slot < 4; slot++) {
    const spot = await shopSlot(page, slot)
    await page.mouse.click(spot.x, spot.y)
    await page.waitForTimeout(350)
    // **딱지를 누르는 것은 고르는 것까지입니다.** 사는 것은 그 밑의 단추입니다 —
    // `buyAffordablePack` 과 같은 이유로 낡아 있었습니다.
    const buy = await shopBuySpot(page, slot)
    await page.mouse.click(buy.x, buy.y)
    await page.waitForTimeout(500)
    if ((await peek(page)).jokers > 0) return
  }
}

/**
 * 고른 상점 칸 밑의 사기 단추.
 *
 * `syncHeldBar` 과 같은 계산입니다 — 값이 있던 줄이고, 딱지 가운데에서 82px 아래입니다.
 */
export async function shopBuySpot(page: Page, slot: number, count = 2):
    Promise<{ x: number; y: number }> {
  const tileW = 158
  const gap = 14
  const span = count * tileW + (count - 1) * gap
  const left = POPUP_X - SHOP_W / 2 + (SHOP_W - span) / 2
  return at(page, left + slot * (tileW + gap) + tileW / 2,
            SHOP_Y + SHOP_ITEMS + 86 + 82)
}

/** 상점의 물건 칸 하나의 가운데. */
export async function shopSlot(page: Page, slot: number, count = 2): Promise<{ x: number; y: number }> {
  const tileW = 158
  const gap = 14
  const span = count * tileW + (count - 1) * gap
  const left = POPUP_X - SHOP_W / 2 + (SHOP_W - span) / 2
  return at(page, left + slot * (tileW + gap) + tileW / 2, SHOP_Y + SHOP_ITEMS + 86)
}

/** `game.ts` 의 `syncShop` 과 같은 값들. */
export const SHOP_W = 660
export const SHOP_ITEMS = 78
export const SHOP_Y = 200
/** 상점의 팩 줄이 시작하는 자리. 칸 셋이 다 있을 때입니다. */
export const SHOP_PACKS = SHOP_Y + SHOP_ITEMS + 160 + 12 + 22

/**
 * 상점 판의 높이.
 *
 * **칸 셋이 다 있을 때입니다** — 다 산 칸은 없어지므로 그만큼 판이 낮아집니다.
 */
export const SHOP_H = 586

/** 화면 좌표를 캔버스 위의 자리로. 기준 해상도는 1280 × 720 입니다. */
/**
 * 기준 좌표를 캔버스 위의 자리로.
 *
 * **판은 창을 꽉 채우지 않습니다** — 기준 비율에 맞춰 가운데에 놓이고 남는 자리는 배경이
 * 덮습니다. 그래서 캔버스의 비율만으로 환산하면 어긋납니다. `game.ts` 의 `layout` 과 같은
 * 계산을 여기서도 합니다.
 */
export async function at(page: Page, x: number, y: number): Promise<{ x: number; y: number }> {
  const box = await (await page.$('#stage'))?.boundingBox()
  if (!box) return { x, y }
  const scale = Math.min(box.width / STAGE_W, box.height / STAGE_H)
  const originX = box.x + Math.round((box.width - STAGE_W * scale) / 2)
  const originY = box.y + Math.round((box.height - STAGE_H * scale) / 2)
  return { x: originX + x * scale, y: originY + y * scale }
}

// 화면의 자리들. `render/game.ts` 의 상수와 같아야 합니다.
export const STAGE_W = 1280
export const STAGE_H = 800
export const BOARD_X = (16 + 264 + 20 + STAGE_W) / 2
/** 판 위에 뜨는 것들의 가운데. `game.ts` 의 `POPUP_X` 와 같습니다. */
export const POPUP_X = STAGE_W / 2
export const HAND_Y = 608
export const CARD_SPACING = 100
/**
 * 판 아래 버튼 줄. **`game.ts` 와 같아야 합니다.**
 *
 * 손가락에 맞게 키운 뒤로 줄이 위로 올라왔습니다 — 자리를 여기 한곳에 두고 셈도 같이 둡니다.
 */
export const BUTTON_Y = 728
const PLAY_W = 148
const PLAY_H = 56
const CLEAR_W = 76
const BUTTON_GAP = 8
const ROW_LEFT = BOARD_X - (PLAY_W * 2 + CLEAR_W + BUTTON_GAP * 2) / 2
/** 낸다·취소·버린다의 가운데. */
export const PLAY_BUTTON = { x: ROW_LEFT + PLAY_W / 2, y: BUTTON_Y + PLAY_H / 2 }
export const CLEAR_BUTTON = {
  x: ROW_LEFT + PLAY_W + BUTTON_GAP + CLEAR_W / 2, y: BUTTON_Y + PLAY_H / 2,
}
export const DISCARD_BUTTON = {
  x: ROW_LEFT + PLAY_W + CLEAR_W + BUTTON_GAP * 2 + PLAY_W / 2, y: BUTTON_Y + PLAY_H / 2,
}

/**
 * 왼쪽 아래 버튼 둘의 가운데. 왼쪽이 족보 목록, 오른쪽이 메뉴입니다.
 *
 * **`game.ts` 와 같아야 합니다.** 판의 밑단에 붙어 있어서, 그 자리를 고치면 여기도 따라
 * 옵니다.
 */
export const PANEL_BUTTON_Y = 726 + 17
export const HAND_LIST_BUTTON = { x: 16 - 2 + 59, y: PANEL_BUTTON_Y }
export const MENU_BUTTON = { x: 16 + 134 + 59, y: PANEL_BUTTON_Y }

/**
 * 타이틀의 자리들.
 *
 * **`ui/title.ts` 와 같아야 합니다.** 도구마다 따로 적어 두면 배치를 고칠 때 한쪽만
 * 고쳐지고, 그 도구는 엉뚱한 곳을 눌러 놓고 아무 말도 하지 않습니다.
 *
 * 아래 바 하나에 전부 들어 있습니다 — 바가 216 이고 안쪽 여백이 26, 윗줄이 34, 틈이 10,
 * 아랫줄이 62 입니다.
 */
const DOCK_H = 216
const DOCK_PAD = 26
const TITLE_UPPER_H = 34
const TITLE_ROW_H = 62
const TITLE_GAP = 10
const TITLE_UPPER_Y = STAGE_H - DOCK_H + DOCK_PAD
const TITLE_ROW_Y = TITLE_UPPER_Y + TITLE_UPPER_H + TITLE_GAP

/** 아래 줄의 네 칸. 시작만 넓습니다. */
const TITLE_START_W = 196
const TITLE_OTHER_W = 132
const TITLE_LEFT = Math.round(
  (STAGE_W - (TITLE_START_W + TITLE_OTHER_W * 3 + TITLE_GAP * 3)) / 2)

/** 「시작」 가운데의 세로 자리. 예전 도구들이 이 이름을 씁니다. */
export const TITLE_START_Y = TITLE_ROW_Y + TITLE_ROW_H / 2
/** 「시작」 가운데. **가로도 가운데가 아닙니다** — 왼쪽 첫 칸입니다. */
export const TITLE_START = {
  x: TITLE_LEFT + TITLE_START_W / 2,
  y: TITLE_START_Y,
}
export const TITLE_JOKERS = {
  x: TITLE_LEFT + TITLE_START_W + TITLE_GAP + TITLE_OTHER_W / 2,
  y: TITLE_START_Y,
}
export const TITLE_CHALLENGES = {
  x: TITLE_LEFT + TITLE_START_W + (TITLE_GAP + TITLE_OTHER_W) * 1 + TITLE_OTHER_W / 2,
  y: TITLE_START_Y,
}
export const TITLE_LEADERBOARD = {
  x: TITLE_LEFT + TITLE_START_W + (TITLE_GAP + TITLE_OTHER_W) * 2 + TITLE_OTHER_W / 2,
  y: TITLE_START_Y,
}
/** 무엇으로 시작하는가. 시작 위의 줄입니다. */
export const TITLE_SETUP = {
  x: TITLE_LEFT + 120,
  y: TITLE_UPPER_Y + TITLE_UPPER_H / 2,
}
/** 랭크. 그 줄의 오른쪽 끝이고 **로그인해야 눌립니다.** */
export const TITLE_RANKED = {
  x: TITLE_LEFT + TITLE_START_W + TITLE_GAP + TITLE_OTHER_W * 2 + TITLE_GAP
     + TITLE_OTHER_W / 2,
  y: TITLE_UPPER_Y + TITLE_UPPER_H / 2,
}

/** 타이틀 오른쪽의 아이콘 둘. 왼쪽이 게임 방법, 오른쪽이 옵션입니다. */
const TITLE_ICON = TITLE_ROW_H
export const TITLE_GUIDE = {
  x: STAGE_W - DOCK_PAD - TITLE_ICON * 2 - TITLE_GAP + TITLE_ICON / 2,
  y: TITLE_ROW_Y + TITLE_ICON / 2,
}
export const TITLE_OPTIONS = {
  x: STAGE_W - DOCK_PAD - TITLE_ICON / 2,
  y: TITLE_ROW_Y + TITLE_ICON / 2,
}

/**
 * 가운데 큰 버튼. 블라인드 선택과 상점이 씁니다.
 *
 * **상점에서는 자리가 다릅니다** — 바우처 딱지와 겹치지 않게 아래로 내려가 있습니다.
 */
export async function clickPrimary(page: Page): Promise<void> {
  if ((await peek(page)).phase === 'shop') {
    // 상점의 밑단. **판 하나로 정돈되면서 버튼도 그 안으로 들어왔습니다.**
    const spot = await at(page, POPUP_X + 83, SHOP_Y + SHOP_H - 56 / 2)
    await page.mouse.click(spot.x, spot.y)
    return
  }
  // 블라인드 선택은 **화면이 알린 자리를 누릅니다.** 판의 밑단이 글의 길이에 따라 자라므로
  // 여기서 다시 계산하면 말을 바꾼 날에 어긋납니다.
  const pick = (await peek(page)).spots?.pick
  if (!pick) throw new Error('블라인드 판의 버튼 자리를 화면이 알리지 않았습니다')
  const spot = await at(page, pick.x, pick.y)
  await page.mouse.move(spot.x, spot.y)
  await page.waitForTimeout(80)
  await page.mouse.down()
  await page.waitForTimeout(50)
  await page.mouse.up()
}

/**
 * 패에서 다섯 장을 골라 냅니다.
 *
 * **화면을 실제로 누르는 것이 요점입니다** — 코어를 직접 부르면 화면이 도는지를 확인하지
 * 못합니다. 카드는 108픽셀 간격으로 가운데 놓이고, 8장일 때 첫 장의 중심이 262 입니다.
 */
export async function playHand(page: Page, picks: number[] = [0, 1, 2, 3, 4]): Promise<void> {
  await pickCards(page, picks)
  await pressPlay(page)
}

/** 남은 카드 판을 열고 닫습니다. 왼쪽 패널 아래의 버튼입니다. */
export async function openDeckView(page: Page): Promise<void> {
  const spot = await at(page, HAND_LIST_BUTTON.x, HAND_LIST_BUTTON.y)
  await page.mouse.click(spot.x, spot.y)
  await page.waitForTimeout(350)
}

/** 고르기만 합니다. 고른 카드의 셰이더를 찍으려면 낸다를 누르기 전에 멈춰야 합니다. */
export async function pickCards(page: Page, picks: number[]): Promise<void> {
  const held = (await peek(page)).hand.length
  await clickCards(page, picks, held)
}

export async function pressPlay(page: Page): Promise<void> {
  const play = await at(page, PLAY_BUTTON.x, PLAY_BUTTON.y)
  await page.mouse.click(play.x, play.y)
}

/** 부채꼴로 편 패에서 몇 장을 누릅니다. */
export async function clickCards(page: Page, picks: number[], held: number): Promise<void> {
  const spacing = Math.min(CARD_SPACING, 720 / Math.max(1, held))
  const startX = BOARD_X - ((held - 1) * spacing) / 2

  for (const i of picks) {
    const offset = i - (held - 1) / 2
    const spot = await at(page, startX + i * spacing, HAND_Y + offset * offset * 1.1)
    await page.mouse.click(spot.x, spot.y)
    await page.waitForTimeout(80)
  }
}

/** 고른 것을 버립니다. */
export async function discardHand(page: Page, picks: number[]): Promise<void> {
  const held = (await peek(page)).hand.length
  await clickCards(page, picks, held)
  const discard = await at(page, DISCARD_BUTTON.x, DISCARD_BUTTON.y)
  await page.mouse.click(discard.x, discard.y)
}

/** 쓸 만한 다섯 장에 들지 못한 카드들. 버릴 대상입니다. */
export function spare(hand: { rank: number }[], picks: number[]): number[] {
  const keep = new Set(picks)
  return hand.map((_, index) => index).filter(index => !keep.has(index)).slice(0, 5)
}

/**
 * 패에서 쓸 만한 다섯 장.
 *
 * 다섯 장 조합 56가지를 전부 보고 가장 높은 족보를 고릅니다. **잘 두려는 것이 아니라
 * 상점까지 가려는 것입니다** — 무작정 왼쪽 다섯 장을 내면 안테 1 에서 끝납니다.
 *
 * 족보의 값은 여기 손으로 적혀 있습니다. 도구이므로 그래도 되고, 게임의 값은 시트에
 * 있습니다.
 */
export function chooseFive(hand: { rank: number; suit: number }[]): number[] {
  let best: number[] = [0, 1, 2, 3, 4].filter(i => i < hand.length)
  let bestScore = -1

  const indices = hand.map((_, index) => index)
  for (const combo of fiveOf(indices)) {
    const value = rate(combo.map(index => hand[index]))
    if (value > bestScore) {
      bestScore = value
      best = combo
    }
  }
  return best.sort((a, b) => a - b)
}

export function* fiveOf(indices: number[]): Generator<number[]> {
  const want = Math.min(5, indices.length)
  const stack: number[][] = [[]]
  while (stack.length > 0) {
    const current = stack.pop() as number[]
    if (current.length === want) {
      yield current
      continue
    }
    const from = current.length === 0 ? 0 : current[current.length - 1] + 1
    for (let i = indices.length - 1; i >= from; i--) stack.push([...current, indices[i]])
  }
}

/** 족보의 대략적인 값. 순서만 맞으면 됩니다. */
export function rate(cards: { rank: number; suit: number }[]): number {
  const ranks = new Map<number, number>()
  const suits = new Map<number, number>()
  for (const card of cards) {
    ranks.set(card.rank, (ranks.get(card.rank) ?? 0) + 1)
    suits.set(card.suit, (suits.get(card.suit) ?? 0) + 1)
  }

  const counts = [...ranks.values()].sort((a, b) => b - a)
  const flush = [...suits.values()].some(count => count >= 5)
  const sorted = [...ranks.keys()].sort((a, b) => a - b)
  const straight = sorted.length >= 5
    && sorted[sorted.length - 1] - sorted[0] === sorted.length - 1

  const high = Math.max(...cards.map(card => card.rank)) / 100
  if (counts[0] >= 5) return 400 + high
  if (flush && counts[0] >= 3 && counts[1] >= 2) return 380 + high
  if (flush && straight) return 300 + high
  if (counts[0] >= 4) return 200 + high
  if (counts[0] >= 3 && counts[1] >= 2) return 160 + high
  if (flush) return 140 + high
  if (straight) return 120 + high
  if (counts[0] >= 3) return 90 + high
  if (counts[0] >= 2 && counts[1] >= 2) return 60 + high
  if (counts[0] >= 2) return 30 + high
  return high
}

