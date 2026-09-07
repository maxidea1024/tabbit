// 화면을 실제로 눌러 판을 두는 도구들.
//
// **좌표가 `render/game.ts` 의 상수와 같아야 합니다.** 그래서 한 자리에 모읍니다 — 도구마다
// 따로 적어 두면 배치를 고칠 때 한쪽만 고쳐지고, 그 도구는 엉뚱한 곳을 눌러 놓고 아무 말도
// 하지 않습니다.

import type { Page } from 'playwright'

/** 사각형 하나. */
export interface Rect {
  x: number
  y: number
  width: number
  height: number
}

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
  /** 지금 상태의 해시. 이어서 한 판이 그만두던 판과 같은지를 이것으로 봅니다. */
  hash?: string
  /** 지금 칩이 날고 있는가. */
  /** 설명 쪽지가 떠 있는가. */
  tip: boolean
  /** 지금 골라 둔 카드의 수. */
  picked: number
  /** 상점 판이 지금 서 있는가. */
  shopUp: boolean
  /** 상점 판이 서 있는 높이. 0 이 다 선 자리이고, 클수록 화면 아래입니다. */
  shopY?: number
  /** 상점이 자리를 비켜 내려가 있어야 하는가. 팩을 뜯었거나 자리를 비우는 중입니다. */
  shopParked?: boolean
  /** 자리를 비우는 화면(줄에서 내놓을 것을 고르는 것)이 들었는가 · 든 정도. */
  focus?: boolean
  focusEnter?: number
  /** 펼쳐 놓은 팩의 카드 수. 상점이 물러난 뒤에야 0 이 아닙니다. */
  packCards?: number
  /** 덱이 팩의 카드를 받으려고 나와 있는가. */
  deckPeek?: boolean
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
  /** 팩 칸마다 어느 팩인가. */
  packIds?: string[]
  /** 뽑을 패와 덱의 크기. 라운드 사이에는 같습니다. */
  drawLeft?: number
  deckSize?: number
  /** 칸 수 글이 강조되어 있는가. */
  countPulse?: boolean
  /** 산 뒤 그 자리에 남아 있는 딱지의 수. */
  leaving?: number
  /** 덱 층의 가로 어긋남. 0 이면 제자리이고 300 이면 물러난 것입니다. */
  deckX?: number
  played: number
  coins: boolean
  cleared: boolean
  consumables: number
  /**
   * 도감이 지금 무엇을 몇 개 세우고 있는가. **판이 떠 있을 때만 값이 있습니다.**
   *
   * 화면의 칸을 세면 한 쪽에 40개까지이므로, 갈래의 수는 이 값으로만 확인됩니다.
   */
  collection?: { tab: string; cells: number; found: number; offset: number }
  /** 상점 칸마다 무엇이 서 있는가. `ShopItemKind` 의 값입니다 — 조커가 1, 소모품이 2~5. */
  shopKinds?: number[]
  /** 상점의 줄마다 몸통이 시작하는 `y`. 판이 서 있지 않으면 비어 있습니다. */
  shopRows?: { items?: number; packs?: number; voucher?: number }
  /** 상점 칸마다 `[칸, x, 쉬는 x, 가운데 x, 가운데 y]`. 도구가 칸을 짚는 값입니다. */
  shopAt?: number[][]
  /** 팩 칸마다 `[칸, 가운데 x, 가운데 y]`. */
  packAt?: number[][]
  /** 소모품이 올 자리를 잡아 준 횟수와, 잡을 것이 없어 그냥 돌아온 횟수. */
  flyAsked?: number
  flyMissed?: number
  /** 최근에 난 소리들. 새것이 뒤입니다. */
  sounds?: string[]
  /**
   * 화면과 화면 사이. **씬이 갈리는 동안에만 걸음이 `off` 가 아닙니다.**
   *
   * 규격은 `doc/ui/transition.md` 이고, 도는 동안에는 누를 자리가 알려지지 않습니다.
   */
  transition?: { id: string; stage: string; cover: number; shots: number }
  /**
   * 인사이트 판에 지금 서 있는 줄들의 열쇠.
   *
   * **판이 떠 있고 그 갈래일 때만 값이 있습니다.** 줄 수만 알리면 문장이 열쇠 그대로
   * 적혀 있어도 같은 답이 나오므로, 열쇠를 알려 시트와 견줄 수 있게 합니다.
   */
  insight?: { keys: string[] }
  /**
   * 카드 앞면을 몇 장 굽고 몇 번 다시 썼는가.
   *
   * **다시 쓰는 쪽만 늘어야 맞습니다.** 앞면은 무늬 · 랭크 · 종이색 · 디버프가 같으면 같은
   * 그림이므로, 카드를 고르고 무르는 동안 구운 장수가 함께 늘면 굽기가 낭비만 됩니다.
   */
  faceBakes?: { baked: number; reused: number; held: number
                dropped: number; bytes: number }
  /**
   * 글에 두른 테두리의 굵기.
   *
   * **말마다 다릅니다.** 굵기는 그 말의 획 사이 틈에서 나오는 값이고, 한 번 만들고 글만
   * 갈아 끼우는 것들은 말이 바뀔 때 다시 정해집니다 — 그 길을 지났는지는 눈으로 보이지
   * 않으므로 값으로 봅니다.
   */
  inkWidth?: { hand: number; headline: number; button: number }
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
  /** 조커와 소모품의 자리. `game.ts` 의 `JOKER_TRAY`·`CONSUMABLE_TRAY` 그대로입니다. */
  trays?: { joker: Rect; item: Rect }
  /**
   * 줄에 선 카드들이 차지한 사각형.
   *
   * **자리를 넘어가지 않는지는 이 둘을 견주어야만 확인됩니다** — 눈으로는 몇 개까지
   * 담기는지 세어 볼 수 없고, 넘어간 한 장은 옆 줄이나 화면 밖에 섭니다.
   */
  trayCards?: { joker: Rect[]; item: Rect[] }
  /** 고른 것 아래에 선 단추 줄이 차지한 사각형. 고른 것이 없으면 없습니다. */
  heldBox?: Rect
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
  /**
   * 아무것도 없는 곳을 누른 횟수.
   *
   * **도구가 자기 좌표를 검사하는 자리입니다.** 눌린 것이 없으면 화면은 아무 말도 하지
   * 않으므로, 좌표가 낡은 도구는 빈자리를 눌러 놓고 그다음 줄로 넘어가 통과합니다.
   */
  blankTaps?: number
  /** 판이 하나라도 떠 있는가. */
  modalUp?: boolean
  /** 연출이 다음에 낼 박자. 소리가 비는 자리를 찾을 때 씁니다. */
  coming: string
  /** 정산 판이 떠 있는가. */
  payout: boolean
  /** 끝난 판이 떠 있는가. **카드가 다 걷힌 뒤에 섭니다.** */
  gameOver?: boolean
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
 * **게임의 첫 화면은 로그인 씬입니다.** 실행할 때마다 그렇고, 계정 없이 하기로 한 것은 그
 * 실행에만 적용됩니다 — 도구는 그 물음의 대상이 아니므로 표시 하나로 건너뜁니다.
 *
 * 페이지를 만든 직후에 겁니다.
 *
 *     const page = await browser.newPage(...)
 *     await skipLogin(page)
 *     await page.goto(...)
 *
 * **저장소가 아니라 표시 하나입니다.** 「계정 없이 하겠다」를 저장소에 적어 두던 것을
 * 걷었으므로 — 그것이 곧 실행할 때마다 묻지 않던 원인입니다 — 심을 자리가 없습니다.
 * 주소에 `?guest=1` 을 붙이는 것과 같은 자리이고, 도구는 주소를 저마다 지으므로 표시 쪽이
 * 한 줄로 끝납니다.
 *
 * **로그인 화면 자체를 보는 도구는 부르지 않습니다.** `check-leaderboard` 와
 * `check-relabel` 이 그렇습니다.
 */
export async function skipLogin(page: Page): Promise<void> {
  await page.addInitScript('window.__cloverGuest = true')
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

/**
 * 화면이 알린 자리 하나. 아직 없으면 잠깐 기다립니다.
 *
 * **좌표를 도구에 적어 두지 않기 위한 것입니다.** 적어 두면 배치를 고친 날에 그 도구는
 * 빈자리를 눌러 놓고 아무 말도 하지 않습니다 — 그리는 쪽이 그린 자리를 그대로 알립니다.
 */
export async function spot(page: Page, name: string, tries = 20):
    Promise<{ x: number; y: number }> {
  for (let wait = 0; wait < tries; wait++) {
    const found = (await peek(page)).spots?.[name]
    if (found) return at(page, found.x, found.y)
    await pass(page, 100)
  }
  throw new Error(`화면이 ${name} 의 자리를 알리지 않습니다`)
}

/**
 * 화면이 알린 자리를 누릅니다. **맞혔는지 확인합니다.**
 *
 * 화면이 알린 자리는 늘 무언가가 있는 자리이므로, 눌러서 아무것도 맞히지 못했다면 그것은
 * 이 도구의 결함입니다 — 판이 아직 서지 않았거나, 눌린 것이 다른 판에 덮여 있습니다.
 * 그것을 여기서 말하지 않으면 도구는 그다음 줄로 넘어가 통과합니다.
 */
export async function clickSpot(page: Page, name: string): Promise<void> {
  const where = await spot(page, name)
  const before = (await peek(page)).blankTaps ?? 0
  await page.mouse.click(where.x, where.y)
  await pass(page, 100)
  const after = (await peek(page)).blankTaps ?? 0
  if (after > before) {
    throw new Error(`${name} 을 눌렀는데 아무것도 맞히지 못했습니다`
      + ` — (${Math.round(where.x)}, ${Math.round(where.y)})`)
  }
}

/**
 * 이 동안 아무것도 없는 곳을 누르지 않았는가.
 *
 * 좌표를 스스로 셈하는 자리를 감싸는 데 씁니다 — 눌러 보고 아무 일도 없었으면 그 좌표가
 * 낡은 것입니다.
 */
export async function mustHit(page: Page, what: string,
                              press: () => Promise<void>): Promise<void> {
  const before = (await peek(page)).blankTaps ?? 0
  await press()
  await pass(page, 100)
  const after = (await peek(page)).blankTaps ?? 0
  if (after > before) throw new Error(`${what} 을 눌렀는데 아무것도 맞히지 못했습니다`)
}

/**
 * 타이틀의 단추 하나를 누릅니다.
 *
 * **좌표를 여기 적어 두지 않습니다.** 화면이 그린 자리를 그대로 알리고 도구는 그것을
 * 조회합니다 — 베껴 적은 값은 배치를 고친 날부터 빈자리를 가리키고, `check-gaps` 와
 * `shoot-last` 가 그렇게 아무것도 없는 곳을 눌러 놓고 통과했습니다.
 *
 * 이름은 `start` · `collection` · `leaderboard` · `guide` · `options` · `signOut` 입니다.
 *
 * **`start` 는 판을 여는 자리를 엽니다.** 곧바로 판이 시작되지 않습니다 — 새 런 ·
 * 이어하기 · 챌린지가 그 안의 탭 셋이고, 판을 여는 것은 `startNewRun` 이 합니다.
 */
export async function pressTitle(page: Page, name = 'start'): Promise<void> {
  await clickSpot(page, `title:${name}`)
}

/**
 * 판을 여는 자리의 단추 하나를 누릅니다.
 *
 * 이름은 탭이 `tab:new` · `tab:resume` · `tab:challenge` 이고, 단추가 `startNew` ·
 * `startRanked` · `startChallenge` · `resume` · `discard` 입니다. 덱과 스테이크의 칸은
 * `deck:<번호>` · `stake:<번호>` 입니다.
 */
export async function pressRunPanel(page: Page, name: string): Promise<void> {
  await clickSpot(page, `run:${name}`)
}

/**
 * 타이틀에서 새 판을 엽니다.
 *
 * **두 걸음입니다.** 시작이 여는 것은 판을 고르는 자리이고, 거기서 「이 덱으로 시작」을
 * 눌러야 판이 열립니다 — 도구가 이 두 걸음을 저마다 적으면 탭이 하나 늘 때마다 전부
 * 고쳐야 합니다.
 */
export async function startNewRun(page: Page): Promise<void> {
  await pressTitle(page, 'start')
  await pass(page, 500)
  await pressRunPanel(page, 'tab:new')
  await pass(page, 200)
  await pressRunPanel(page, 'startNew')
  // **묻고 나서 시작합니다.** 저장된 판이 있으면 그것이 사라지므로, 새 판을 여는 것은
  // 되돌릴 수 없는 일입니다.
  await pass(page, 400)
  await confirmYes(page)
  // 판에 들어서는 것은 화면이 덮인 프레임입니다. 그 사이가 끝나기를 기다립니다.
  await crossed(page)
}

/** 물어보는 판의 「예」를 누릅니다. 떠 있지 않으면 아무것도 하지 않습니다. */
export async function confirmYes(page: Page): Promise<void> {
  if ((await peek(page)).spots?.['confirm:yes'] === undefined) return
  await clickSpot(page, 'confirm:yes')
}

/**
 * 처음 여는 사람에게 펼쳐지는 게임 방법을 닫습니다.
 *
 * **화면 왼쪽 위를 누르던 것을 걷었습니다.** 도구 7개가 `(20, 20)` 을 눌러 판 밖을
 * 맞히는 것으로 닫고 있었는데, 판 밖은 이제 잘라 낸 자리라 아무것도 맞지 않습니다 —
 * 창의 비율이 기준과 다른 도구에서 그 누름이 조용히 아무 일도 하지 않았습니다.
 *
 * 떠 있지 않으면 아무것도 하지 않습니다.
 */
export async function closeGuide(page: Page): Promise<void> {
  // **씬이 갈리는 동안에는 아직 열리지 않았습니다.** 판에 들어서는 것은 화면이 덮인
  // 프레임에 일어나므로, 그 전에 물으면 「떠 있는 판이 없다」가 나오고 게임 방법은 그
  // 뒤에 열려 그대로 남습니다.
  await crossed(page)
  if ((await peek(page)).modalUp !== true) return
  await page.keyboard.press('Escape')
  await pass(page, 400)
}

/**
 * 타이틀에서 판을 열고 블라인드를 고릅니다.
 *
 * **도구 15개가 이 다섯 줄을 저마다 베껴 적고 있었습니다.** 시작을 누르고, 처음 여는
 * 사람에게 펼쳐지는 게임 방법을 닫고, 블라인드를 고르는 순서입니다.
 */
export async function openRun(page: Page): Promise<void> {
  await startNewRun(page)
  await pass(page, 900)
  // 게임 방법이 펼쳐져 있으면 닫습니다. **떠 있지 않으면 누를 것이 없습니다.**
  if ((await peek(page)).modalUp === true) await page.keyboard.press('Escape')
  await pass(page, 400)
  await clickPrimary(page)
  await settle(page)
}

/**
 * 이 라운드를 곧바로 이깁니다. **개발 서버에서만 됩니다.**
 *
 * 점수를 요구치로 올려 놓고 한 장을 내므로 라운드가 그 자리에서 끝납니다. 연출이 다 돌
 * 때까지 기다립니다.
 */
export async function clearBlind(page: Page): Promise<void> {
  await page.evaluate(() => {
    const hook = (window as unknown as { __clover: { clearBlind?(): void } }).__clover
    hook.clearBlind?.()
  })
  await settle(page)
}

/**
 * 정산 판의 「받는다」 를 누르고 상점이 설 때까지 기다립니다.
 *
 * **단추의 자리는 화면이 알립니다.** 정산 판의 높이는 줄 수를 따르는데, 도구가 줄 둘을
 * 전제로 셈하고 있어서 이자 줄이 붙으면 빈자리를 눌렀습니다. 줄이 하나씩 서므로 단추가
 * 설 때까지 조금 기다립니다.
 */
export async function takePayout(page: Page): Promise<void> {
  for (let wait = 0; wait < 60; wait++) {
    const spot = (await peek(page)).spots?.take
    if (spot) {
      // **판이 다 들어온 뒤에 누릅니다.** 자리가 알려지는 것은 판이 열리는 그 프레임이고,
      // 판은 그 뒤 잠깐 움직이며 들어오므로 그 자리를 곧바로 누르면 빗나갑니다 — 카드가
      // 덱으로 돌아간 뒤에 정산이 서게 되면서 도구의 첫 조회가 그 프레임에 닿았습니다.
      await pass(page, 400)
      const here = await at(page, spot.x, spot.y)
      await page.mouse.click(here.x, here.y)
      break
    }
    await pass(page, 200)
  }
  for (let wait = 0; wait < 40; wait++) {
    if ((await peek(page)).shopUp) return
    await pass(page, 100)
  }
}

/**
 * 상점 판이 다 서기를 기다립니다.
 *
 * **`shopUp` 은 올라오기 시작한 것입니다.** 판은 화면 아래에서 밀려 올라오고 `shopY` 가
 * 그 남은 거리이므로, 그것이 0 이 되어야 다 선 것입니다 — 올라오는 중에 찍으면 판이 화면
 * 밖으로 반쯤 걸친 그림이 남습니다.
 */
export async function shopStanding(page: Page): Promise<void> {
  for (let wait = 0; wait < 80; wait++) {
    const now = await peek(page)
    if (now.shopUp && (now.shopY ?? 1) < 1) return
    await pass(page, 100)
  }
}

/**
 * 상점을 만질 수 있는 상태로 만듭니다.
 *
 * **코어가 `shop` 인 것과 상점 판이 서 있는 것은 다릅니다.** 블라인드를 넘긴 그 자리에서
 * 국면은 `shop` 이 되지만 화면은 정산 판을 세우고 기다리므로, 받지 않으면 상점 판이
 * 올라오지 않습니다 — 그 사이에 칸을 짚는 도구는 「상점에 0번 칸이 없습니다」 로 끝납니다.
 *
 * **국면만 보고 칸을 짚는 자리가 둘 있었습니다.** 그래서 여기 하나로 둡니다.
 */
export async function shopFront(page: Page): Promise<void> {
  const now = await peek(page)
  if (now.cleared || now.payout) await takePayout(page)
  await shopStanding(page)
}

/**
 * 라운드를 이기고 정산을 받아 상점까지 갑니다.
 *
 * **자동 진행으로 이기지 않습니다.** 다섯 장을 고르는 봇은 안테 1 의 스몰 블라인드도 자주
 * 지고, 지면 상점에 닿지 못한 채 도구가 「사지 못했습니다」 로 끝납니다 — 상점을 보려는
 * 도구에 라운드의 승패는 확인하려는 것이 아닙니다.
 */
export async function winRound(page: Page): Promise<void> {
  await clearBlind(page)
  await pass(page, 1400)
  await takePayout(page)
  // 판이 아래에서 올라와 줄을 채우는 동안입니다.
  await pass(page, 1400)
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
  await crossed(page)
  for (let wait = 0; wait < 60; wait++) {
    if (!(await peek(page)).busy) return
    await pass(page, 200)
  }
}

/**
 * 씬이 다 갈리기를 기다립니다.
 *
 * **`settle` 과 다릅니다.** 그쪽은 연출이 끝난 것이고, 이것은 화면과 화면 사이가 끝난
 * 것입니다 — 도는 동안에는 누를 자리가 알려지지 않으므로, 그림을 찍는 도구는 이것을
 * 지나고 나서 찍습니다.
 */
export async function crossed(page: Page): Promise<void> {
  for (let wait = 0; wait < 60; wait++) {
    const stage = (await peek(page)).transition?.stage
    if (stage === undefined || stage === 'off') return
    await pass(page, 100)
  }
}

/**
 * 판이 다 걷히기를 기다립니다.
 *
 * **`settle` 은 연출이 끝난 것이고, 낸 카드는 그 뒤에도 잠깐 남습니다** — 읽을 시간을 주고
 * 한 장씩 밖으로 나가며, 나갈 때마다 소리가 납니다. 다음 패가 깔린 뒤를 재려면 낸 카드와
 * 나가는 카드와 들어오는 카드가 모두 0이어야 합니다.
 */
export async function swept(page: Page): Promise<void> {
  for (let wait = 0; wait < 60; wait++) {
    const bins = (await peek(page)).bins
    if (bins && bins.played === 0 && bins.fades === 0 && bins.deals === 0) return
    await pass(page, 200)
  }
}

/**
 * 시간을 보냅니다.
 *
 * **판을 `?tick=manual` 로 열었으면 그만큼 틱을 돌리고, 아니면 실제로 기다립니다.** 틱으로
 * 돌리면 기계의 부하와 무관하게 같은 수의 프레임이 지나므로 결과가 같습니다 — 실제로
 * 기다리는 도구는 느린 기계에서 모자라고 빠른 기계에서 남습니다. 60Hz 로 셉니다.
 *
 * 화면이 상태를 알리기 전에는 실제로 기다립니다 — 그때는 아직 틱을 돌릴 판이 없습니다.
 */
export async function pass(page: Page, ms: number): Promise<void> {
  for (let wait = 0; ; wait++) {
    const state = await page.evaluate(async wanted => {
      const hook = (window as unknown as {
        __clover?: { advance?(ms: number): Promise<void> }
      }).__clover
      if (!hook) return 'booting'
      if (!hook.advance) return 'realtime'
      await hook.advance(wanted)
      return 'stepped'
    }, ms)
    if (state === 'stepped') return
    // **수동 틱이 아닌 판이면 실제로 기다립니다.** 전에는 손잡이가 생기기를 기다리며 자기를
    // 다시 불렀고, `?tick=manual` 없이 연 판에서는 그 손잡이가 영영 없어서 호출 스택이
    // 바닥날 때까지 돌았습니다 — `check-setup` 이 10분을 그렇게 서 있었습니다.
    if (state === 'realtime' || wait >= 50) {
      await page.waitForTimeout(ms)
      return
    }
    // 아직 화면이 서기 전입니다. 잠깐 실제로 기다리고 다시 봅니다.
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
    await pass(page, 350)
    const buy = await packBuySpot(page)
    await page.mouse.click(buy.x, buy.y)
    await pass(page, 700)
    if ((await peek(page)).packOpen) return
  }
}

/**
 * 고른 팩 밑의 「산다」 단추.
 *
 * `syncHeldBar` 과 같은 계산입니다 — 딱지의 가운데이고, 값이 있던 그 줄입니다.
 */
/** 고른 팩 밑의 「산다」. 상점 칸과 같은 셈입니다. */
export async function packBuySpot(page: Page): Promise<{ x: number; y: number }> {
  return heldButton(page)
}

/**
 * 상점의 팩 칸 하나의 가운데. **화면이 알립니다.**
 *
 * 칸의 너비와 사이를 도구가 베껴 적고 있었고, 칸이 바뀔 때마다 빈자리를 눌렀습니다.
 */
export async function packSlot(page: Page, slot: number, count = 2): Promise<{ x: number; y: number }> {
  void count
  for (let wait = 0; wait < 10; wait++) {
    const spots = (await peek(page)).packAt ?? []
    const one = spots.find(entry => entry[0] === slot)
    if (one) return at(page, one[1], one[2])
    await pass(page, 100)
  }
  throw new Error(`상점에 ${slot}번 팩 칸이 없습니다`)
}

/**
 * 상점의 칸을 살 수 있으면 삽니다.
 *
 * 자리는 `game.ts` 의 `syncShop` 과 같은 계산입니다 — 판이 가운데에 서고 물건이 그 안에서
 * 가운데로 모입니다.
 */
export async function buyFirstAffordable(page: Page): Promise<void> {
  // **몇 번 칸이 서 있는지는 화면이 알립니다.** 넷을 전제로 돌면 상품 줄이 비었을 때 —
  // 다 팔렸거나 팩만 놓인 상점입니다 — 없는 칸을 짚고 「상점에 0번 칸이 없습니다」 로
  // 끝납니다. 살 것이 없는 것은 이 도구가 알릴 일이 아닙니다.
  const slots = ((await peek(page)).shopAt ?? []).map(entry => entry[0])

  for (const slot of slots) {
    // **산 칸은 없어지고 남은 칸은 제자리입니다.** 그래서 처음 읽어 둔 번호가 아직 서
    // 있는지를 칸마다 다시 봅니다.
    const standing = ((await peek(page)).shopAt ?? []).some(entry => entry[0] === slot)
    if (!standing) continue

    const spot = await shopSlot(page, slot)
    await page.mouse.click(spot.x, spot.y)
    await pass(page, 350)
    // **딱지를 누르는 것은 고르는 것까지입니다.** 사는 것은 그 밑의 단추입니다 —
    // `buyAffordablePack` 과 같은 이유로 낡아 있었습니다.
    const buy = await shopBuySpot(page)
    await page.mouse.click(buy.x, buy.y)
    await pass(page, 500)
    if ((await peek(page)).jokers > 0) return
  }
}

/**
 * 고른 상점 칸 밑의 사기 단추.
 *
 * `syncHeldBar` 과 같은 계산입니다 — 값이 있던 줄이고, 딱지 가운데에서 82px 아래입니다.
 */
export async function shopBuySpot(page: Page): Promise<{ x: number; y: number }> {
  return heldButton(page)
}

/**
 * 고른 것 밑에 선 첫 단추. 상점 칸의 「산다」, 팩 카드의 「집는다」 가 이것입니다.
 *
 * **자리는 화면이 알립니다.** 딱지 가운데에서 몇 픽셀 아래인지를 도구가 베껴 적고 있었고,
 * 단추 줄이 위로 올라간 뒤로 24픽셀 아래의 빈자리를 누르며 「사지 못했습니다」 로 끝났습니다.
 */
export async function heldButton(page: Page): Promise<{ x: number; y: number }> {
  for (let wait = 0; wait < 10; wait++) {
    const held = (await peek(page)).spots?.held
    if (held) return at(page, held.x, held.y)
    await pass(page, 100)
  }
  throw new Error('고른 것 밑에 단추가 서지 않았습니다')
}

/** 상점의 물건 칸 하나의 가운데. **화면이 알립니다.** */
export async function shopSlot(page: Page, slot: number, count = 2): Promise<{ x: number; y: number }> {
  void count
  for (let wait = 0; wait < 10; wait++) {
    const spots = (await peek(page)).shopAt ?? []
    const one = spots.find(entry => entry[0] === slot)
    if (one) return at(page, one[3], one[4])
    await pass(page, 100)
  }
  throw new Error(`상점에 ${slot}번 칸이 없습니다`)
}

/** `game.ts` 의 `syncShop` 과 같은 값. 판의 너비입니다. */
export const SHOP_W = 660

/**
 * 상점의 한 줄이 시작하는 `y`. 화면이 알립니다.
 *
 * **상수가 아닙니다.** 판이 바닥에 맞춰 서므로 윗변은 줄 수에 따라 움직이고, 다 산 줄은
 * 없어져 나머지가 내려옵니다 — 윗변 200 을 전제로 셌던 값은 첫 구매 뒤부터 빈자리를
 * 눌렀습니다. 그 줄이 없으면 오류입니다.
 */
export async function shopRow(page: Page, key: 'items' | 'packs' | 'voucher'): Promise<number> {
  const top = (await peek(page)).shopRows?.[key]
  if (top === undefined) throw new Error(`상점에 ${key} 줄이 없습니다`)
  return top
}

/**
 * 상점 판의 바닥. `game.ts` 의 `SHOP_BOTTOM` 과 같습니다.
 *
 * **바닥이 고정이고 윗변이 움직입니다** — 다 산 줄은 없어지고 그만큼 윗변이 내려오므로,
 * 밑단의 단추는 줄 수와 무관하게 이 자리입니다. 화면 높이 800 에서 14 위입니다.
 */
export const SHOP_BOTTOM = 800 - 14

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

/**
 * 한 자리에서 다른 자리로 끕니다.
 *
 * **한 번에 옮기지 않습니다** — 눌렀다 뗀 것과 끈 것은 손가락이 움직였는지로 갈리므로,
 * 곧바로 옮기면 화면이 그것을 누른 것으로 봅니다. 끝나면 커서를 치웁니다. 놓은 것 위에
 * 남아 있으면 그것만 들린 채로 남습니다.
 */
export async function dragBy(page: Page, from: { x: number; y: number },
                             to: { x: number; y: number }): Promise<void> {
  await page.mouse.move(from.x, from.y)
  await page.mouse.down()
  for (let step = 1; step <= 12; step++) {
    await page.mouse.move(from.x + (to.x - from.x) * step / 12,
      from.y + (to.y - from.y) * step / 12 - 14)
    await pass(page, 30)
  }
  await page.mouse.up()
  await pass(page, 300)
  await page.mouse.move(40, 40)
  await pass(page, 800)
}

/**
 * 손패 한 장이 지금 그려진 자리.
 *
 * **개수마다 간격이 달라집니다.** 카드는 정해진 넓이 안에서 가운데로 모이므로 몇 번째
 * 칸의 좌표를 상수로 셈할 수 없습니다 — `game.ts` 가 재는 것과 같은 셈입니다.
 */
export async function handSpot(page: Page, index: number, held: number):
    Promise<{ x: number; y: number }> {
  const spacing = Math.min(CARD_SPACING, 720 / Math.max(1, held))
  const startX = BOARD_X - ((held - 1) * spacing) / 2
  return at(page, startX + index * spacing, HAND_Y)
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
 * 조커 한 장이 지금 그려진 자리. **화면이 알린 것을 그대로 씁니다.**
 *
 * **자리는 개수마다 달라집니다.** 조커는 정해진 넓이 안에서 가운데로 모이므로 몇 번째
 * 칸의 좌표를 셈할 수 없고, 셈하려 든 도구는 배치를 고친 날부터 빈자리를 눌러 놓고
 * 통과합니다 — `372 + index * 100` 이 그렇게 적혀 있었습니다.
 */
export async function jokerSpot(page: Page, index: number):
    Promise<{ x: number; y: number }> {
  return spot(page, `joker:${index}`)
}

/** 소모품 한 장이 지금 그려진 자리. 조커와 같은 규칙입니다. */
export async function itemSpot(page: Page, index: number):
    Promise<{ x: number; y: number }> {
  return spot(page, `item:${index}`)
}

/**
 * 조커 자리 안의, 카드가 없는 곳.
 *
 * **카드가 가운데로 모이므로 자리의 왼쪽 끝이 빕니다.** 자리를 눌러도 아무 일이 없는지를
 * 보는 도구가 씁니다.
 */
export async function trayGap(page: Page, which: 'joker' | 'item'):
    Promise<{ x: number; y: number }> {
  const tray = (await peek(page)).trays?.[which]
  if (!tray) throw new Error(`화면이 ${which} 자리를 알리지 않습니다`)
  return at(page, tray.x + 6, tray.y + tray.height / 2)
}
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
 * 타이틀의 자리들은 **여기 없습니다.**
 *
 * 화면이 `spots` 로 알리고 도구는 `pressTitle` 로 그것을 조회합니다 — 아래 바의 폭을
 * 나누던 셈을 여기 베껴 적어 두었었고, 바를 걷어 낸 날에 그 값들은 전부 빈 곳을
 * 가리켰습니다.
 */

/**
 * 가운데 큰 버튼. 블라인드 선택과 상점이 씁니다.
 *
 * **상점에서는 자리가 다릅니다** — 바우처 딱지와 겹치지 않게 아래로 내려가 있습니다.
 */
export async function clickPrimary(page: Page): Promise<void> {
  if ((await peek(page)).phase === 'shop') {
    // 상점의 밑단. **자리는 화면이 알립니다** — 판이 바닥에 맞춰 서고 그 높이는 남은 줄
    // 수를 따르므로, 여기서 다시 셈하면 하나 살 때마다 어긋납니다.
    //
    // **판이 서기를 기다립니다.** 국면이 상점이 되는 것과 상점 판이 서는 것은 다른
    // 순간입니다 — 낸 카드가 걷히고 정산을 받은 뒤에 판이 아래에서 올라옵니다.
    for (let wait = 0; wait < 40; wait++) {
      if ((await peek(page)).shopUp) break
      await pass(page, 200)
    }
    await clickSpot(page, 'nextBlind')
    return
  }
  // 블라인드 선택은 **화면이 알린 자리를 누릅니다.** 판의 밑단이 글의 길이에 따라 자라므로
  // 여기서 다시 계산하면 말을 바꾼 날에 어긋납니다.
  const pick = (await peek(page)).spots?.pick
  if (!pick) throw new Error('블라인드 판의 버튼 자리를 화면이 알리지 않았습니다')
  const spot = await at(page, pick.x, pick.y)
  await page.mouse.move(spot.x, spot.y)
  await pass(page, 80)
  await page.mouse.down()
  await pass(page, 50)
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
  await pass(page, 350)
}

/** 고르기만 합니다. 고른 카드의 셰이더를 찍으려면 낸다를 누르기 전에 멈춰야 합니다. */
export async function pickCards(page: Page, picks: number[]): Promise<void> {
  // **다 깔린 뒤에 누릅니다.** 깔리는 중인 카드는 오는 길에 있으므로 셈한 자리에 없고,
  // 그것을 누르면 카드 사이의 빈 곳을 누르는 것이 됩니다.
  await swept(page)
  const held = (await peek(page)).hand.length
  // **맞혔는지 봅니다.** 패는 부챗살로 펴지고 그 셈이 여기 적혀 있으므로, 손패의 배치를
  // 고친 날에 이 도구들은 카드 사이의 빈 곳을 눌러 놓고 「고른 것 0장」 으로 갑니다.
  await mustHit(page, '손패', () => clickCards(page, picks, held))
}

export async function pressPlay(page: Page): Promise<void> {
  await mustHit(page, '낸다', async () => {
    const play = await at(page, PLAY_BUTTON.x, PLAY_BUTTON.y)
    await page.mouse.click(play.x, play.y)
  })
}

/** 부채꼴로 편 패에서 몇 장을 누릅니다. */
export async function clickCards(page: Page, picks: number[], held: number): Promise<void> {
  const spacing = Math.min(CARD_SPACING, 720 / Math.max(1, held))
  const startX = BOARD_X - ((held - 1) * spacing) / 2

  for (const i of picks) {
    const offset = i - (held - 1) / 2
    const spot = await at(page, startX + i * spacing, HAND_Y + offset * offset * 1.1)
    await page.mouse.click(spot.x, spot.y)
    await pass(page, 80)
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

