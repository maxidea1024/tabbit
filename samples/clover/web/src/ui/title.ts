// 타이틀.
//
// **게임은 타이틀에서 시작합니다.** 열자마자 판이 깔려 있으면 무엇을 하는 화면인지 읽을
// 자리가 없고, 시드를 확인하거나 게임 방법을 먼저 볼 자리도 없습니다.
//
// **전면 화면입니다.** 상자 안에 담으면 게임 위에 뜬 대화창으로 보이고, 그러면 뒤에 이미
// 무언가가 돌고 있다는 뜻이 됩니다. 배경 그림 위에 막 하나를 깔고 내용이 화면의 변까지
// 갑니다 — 디자인 언어의 「전면 화면」이 정본입니다.
//
// **큰 판 셋입니다.** 시작 · 콜렉션 · 리더보드가 저마다 판 하나를 가지고, 판 아래에
// 나아가는 단추 하나가 놓입니다. 시작만 금색입니다 — 넷이 다 회색이면 무엇을 누를지가
// 화면에 없습니다. 그 밖의 것(계정 · 도움말 · 옵션 · 나가기)은 오른쪽 위의 곁단추 줄입니다.
//
// **아래 변에는 누를 것을 두지 않습니다.** 가운데에 저작권 한 줄이고, 아래 변에서 한 줄
// 위입니다.
//
// 이름은 [`wordmark.ts`](wordmark.ts) 이고 로그인 화면이 같은 것을 씁니다.

import { Container, Graphics, Text } from 'pixi.js'
import { t } from '../core/strings'

import { plateTint, wellTint } from '../render/skin'
import { UI, SIZE, TEXT, WEIGHT } from '../render/theme'
import { glowEdge, piece } from './chrome'
import type { ToolSpot } from './layout'
import { Button } from './widgets'
import { sceneArt } from './scene-art'
import { Wordmark } from './wordmark'

/**
 * 무작위 시드 하나.
 *
 * **한 자리에 둡니다.** 처음 열 때 · 타이틀의 「무작위」 · 게임이 끝난 뒤 다시 시작할 때
 * 셋이 같은 모양이어야 시드만 보고 이 게임의 것인지 알 수 있습니다.
 */
export function randomSeed(): string {
  return `CLOVER-${Math.floor(Math.random() * 1e6).toString().padStart(6, '0')}`
}

/** 화면 가장자리에서 띄우는 거리. 전면 화면의 여백입니다. */
const EDGE = 64

/** 오른쪽 위의 곁단추 줄. 높이 계단의 `sm` 입니다. */
const TOP_Y = 40
const TOP_W = 180
const TOP_H = 36
const TOP_GAP = 20

/** 이름. 왼쪽 위입니다. */
const LOGO_SIZE = 72
const LOGO_Y = 100
const TAGLINE_Y = 196

/** 큰 판 셋. */
const CARD_W = 352
const CARD_H = 430
const CARD_Y = 254
const CARD_GAP = 32
/** 판 위쪽의 그림 자리. */
const ART_H = 236
/** 판 아래의 나아가는 단추. 높이 계단의 `lg` 입니다. */
const GO_H = 60

/** 저작권 줄. 아래 변에서 한 줄 위입니다. */
const OWNER = 'Tabbit'
const YEAR = 2026

/** 계정 카드의 크기. **`hub.ts` 의 카드와 같습니다** — 그 자리에 그 카드가 놓입니다. */
const ACCOUNT_W = 200
const ACCOUNT_H = 72

export interface TitleHooks {
  /** 판을 여는 자리. 새 런 · 이어하기 · 챌린지가 그 안에 있습니다. */
  onStart: () => void
  onGuide: () => void
  onOptions: () => void
  onCollection: () => void
  onLeaderboard: () => void
  /** 계정 자리. 로그인이면 프로필, 아니면 로그인 화면입니다. */
  onAccount: () => void
  /** 로그아웃. **로그인 상태에서만 보입니다.** */
  onSignOut: () => void
  /** 게임을 나갑니다. **묻는 것은 부르는 쪽이 합니다.** */
  onQuit: () => void
}

/** 큰 판 하나의 재료. */
interface Card {
  key: string
  tone: number
  primary: boolean
  press: () => void
}

export class Title extends Container {
  private readonly logo = new Wordmark(LOGO_SIZE, 4)
  /**
   * 한 줄 소개.
   *
   * **그림 위에 놓이는 글에는 늘 그림자가 집니다.** 테두리는 두르지 않습니다 — 픽셀
   * 서체의 격자가 무너집니다.
   */
  private readonly tagline = new Text({
    text: t('ui.title.tagline'),
    style: {
      fontSize: TEXT.base, fill: UI.ink, fontWeight: WEIGHT.heavy, letterSpacing: 2,
      dropShadow: { color: UI.outline, alpha: 0.85, blur: 0, distance: 2, angle: Math.PI / 2 },
    },
  })
  private time = 0

  /**
   * 검증 도구가 짚을 단추들.
   *
   * **좌표를 도구에 적어 두지 않기 위한 것입니다.** 배치를 고친 날부터 베껴 적은 값은
   * 빈자리를 가리키고, 도구는 아무것도 맞히지 못한 채로 통과합니다.
   */
  private readonly toolNodes = new Map<string, ToolSpot>()

  /** 그 단추들의 자리. 화면 좌표로 바꾸는 것은 이 화면을 띄운 쪽이 합니다. */
  get toolSpots(): [string, ToolSpot][] {
    return [...this.toolNodes]
  }

  /** 글을 다시 읽어야 하는 것들. 말이 바뀌면 갈아 끼웁니다. */
  private readonly buttons: { key: string; button: Button }[] = []

  private signOutButton?: Button
  private linkButton?: Button

  /**
   * 로그인했을 때 그 자리에 놓이는 것.
   *
   * **카드가 단추를 대신합니다.** 이름을 두 곳에 적으면 같은 것을 두 번 보게 되고, 카드에는
   * 순위까지 있습니다 — `game.ts` 가 여기에 카드를 넣습니다.
   */
  readonly accountSlot = new Container()

  constructor(hooks: TitleHooks) {
    super()

    // **배경 그림 위에 막을 깝니다.** 판 없이 놓인 글이 읽혀야 합니다 — 그림을 어둡게
    // 물들이는 것이 곧 막이고, 사각형을 얹으면 그 변이 가로선으로 보입니다.
    const art = sceneArt(0.42)
    if (art !== undefined) this.addChild(art)

    // 이름과 한 줄 소개. **왼쪽 위입니다.**
    this.addChild(this.logo, this.tagline)
    this.logo.position.set(EDGE + this.logo.width / 2, LOGO_Y)
    this.tagline.anchor.set(0, 0)
    this.tagline.position.set(EDGE, TAGLINE_Y)

    // 곁단추 줄. **오른쪽 위이고 오른쪽 변에 붙습니다.** 판 바깥의 일들이고, 나가기는
    // 되돌릴 수 없는 것이라 붉음입니다.
    const tops: { key: string; intent: 'neutral' | 'danger'; press: () => void; spot: string }[] = [
      { key: 'ui.account.link', intent: 'neutral', press: hooks.onAccount, spot: 'account' },
      { key: 'ui.button.guide', intent: 'neutral', press: hooks.onGuide, spot: 'guide' },
      { key: 'ui.button.options', intent: 'neutral', press: hooks.onOptions, spot: 'options' },
      { key: 'ui.button.quit', intent: 'danger', press: hooks.onQuit, spot: 'quit' },
    ]
    tops.forEach((one, index) => {
      const button = new Button(t(one.key), TOP_W, TOP_H, one.intent, one.press)
      const x = SIZE.width - EDGE - TOP_W - (tops.length - 1 - index) * (TOP_W + TOP_GAP)
      button.position.set(x, TOP_Y)
      this.buttons.push({ key: one.key, button })
      this.toolNodes.set(one.spot, { node: button, cx: TOP_W / 2, cy: TOP_H / 2 })
      this.addChild(button)
      if (one.spot === 'account') this.linkButton = button
    })

    // 계정 카드와 로그아웃. **로그인했을 때만 보입니다** — 카드가 「계정 연결」 자리를
    // 대신하고, 로그아웃은 그 아래에 붙습니다.
    this.accountSlot.position.set(EDGE, TOP_Y)
    this.accountSlot.visible = false
    const signOut = new Button(t('ui.button.logout'), ACCOUNT_W, TOP_H, 'neutral',
                               hooks.onSignOut)
    signOut.position.set(EDGE, TOP_Y + ACCOUNT_H + 12)
    signOut.visible = false
    this.signOutButton = signOut
    this.buttons.push({ key: 'ui.button.logout', button: signOut })
    this.toolNodes.set('signOut', { node: signOut, cx: ACCOUNT_W / 2, cy: TOP_H / 2 })
    this.addChild(this.accountSlot, signOut)

    // 큰 판 셋. **시작만 금색입니다.**
    const cards: Card[] = [
      { key: 'ui.button.start', tone: UI.red, primary: true, press: hooks.onStart },
      { key: 'ui.button.collection', tone: UI.legendary, primary: false, press: hooks.onCollection },
      { key: 'ui.button.leaderboard', tone: UI.money, primary: false, press: hooks.onLeaderboard },
    ]
    const left = (SIZE.width - (CARD_W * cards.length + CARD_GAP * (cards.length - 1))) / 2
    cards.forEach((card, index) => {
      const node = this.card(card)
      node.position.set(left + index * (CARD_W + CARD_GAP), CARD_Y)
      this.addChild(node)
    })

    // 저작권. **아래 변의 가운데, 한 줄 위입니다.** 판 번호가 여기 함께 적힙니다.
    const copyright = new Text({
      text: `© ${YEAR} ${OWNER} · v${__APP_VERSION__}`,
      style: { fontSize: TEXT.small, fill: UI.inkFaint, fontWeight: WEIGHT.normal, letterSpacing: 1 },
    })
    copyright.anchor.set(0.5, 1)
    copyright.position.set(SIZE.width / 2, SIZE.height - 24)
    this.addChild(copyright)

    // 뒤를 눌러도 아무 일도 없습니다. **시작은 눌러서 시작하는 것입니다.**
    this.eventMode = 'static'
    this.on('pointertap', () => undefined)
  }

  /**
   * 큰 판 하나.
   *
   * 위에 그림 자리 · 아래에 이름과 나아가는 단추. **머리띠는 테두리의 빛입니다** — 고른
   * 것(시작)은 금색으로 번지고 나머지는 옅습니다.
   */
  private card(card: Card): Container {
    const node = new Container()
    const plate = piece('plate', CARD_W, CARD_H, plateTint(UI.panel))
    if (plate !== undefined) node.addChild(plate)
    else {
      const g = new Graphics()
      g.rect(0, 0, CARD_W, CARD_H).fill({ color: UI.panel, alpha: UI.panelAlpha })
      node.addChild(g)
    }

    // 그림 자리. **눌린 자리에 그 판의 색이 듭니다.** 그림은 뒤에 옵니다.
    const art = piece('tray', CARD_W - 2, ART_H, wellTint(card.tone))
    if (art !== undefined) {
      art.position.set(1, 1)
      art.alpha = 0.55
      node.addChild(art)
    }

    // 머리띠.
    const band = glowEdge(CARD_W, card.primary ? UI.yellow : UI.rule)
    if (band !== undefined) {
      band.position.set(0, 0)
      node.addChild(band)
    }

    // 이름.
    const name = new Text({
      text: t(card.key),
      style: { fontSize: TEXT.display, fill: UI.ink, fontWeight: WEIGHT.bold },
    })
    name.position.set(24, ART_H + 26)
    node.addChild(name)

    // 나아가는 단추. **판의 아랫변에 붙습니다.**
    const go = new Button(t(card.key), CARD_W - 48, GO_H, card.primary ? 'primary' : 'neutral',
                          card.press)
    go.position.set(24, CARD_H - 24 - GO_H)
    this.buttons.push({ key: card.key, button: go })
    const spot = card.key === 'ui.button.start' ? 'start'
      : card.key === 'ui.button.collection' ? 'collection' : 'leaderboard'
    this.toolNodes.set(spot, { node: go, cx: (CARD_W - 48) / 2, cy: GO_H / 2 })
    node.addChild(go)

    // **판 어디를 눌러도 그 단추입니다.** 큰 판이 곧 누르는 자리이고, 단추는 그 판의
    // 이름표입니다.
    node.eventMode = 'static'
    node.cursor = 'pointer'
    node.on('pointertap', event => {
      if (event.target === go || go.children.includes(event.target as never)) return
      card.press()
    })
    return node
  }

  /**
   * 계정 상태를 알립니다.
   *
   * **싱글에서는 「계정 연결」 하나입니다.** 로그인하면 그 자리에 카드가 서고 아래에
   * 「로그아웃」이 붙습니다.
   */
  setAccount(signedIn: boolean): void {
    if (this.linkButton) this.linkButton.visible = !signedIn
    this.accountSlot.visible = signedIn
    if (this.signOutButton) this.signOutButton.visible = signedIn
  }

  relabel(): void {
    this.tagline.text = t('ui.title.tagline')
    for (const one of this.buttons) one.button.text = t(one.key)
  }

  /**
   * 겉면을 갈아 끼운 뒤 다시 그립니다.
   *
   * **이 화면은 옵션을 여는 그 자리입니다.** 타이틀에서 겉면을 고르면 그 뒤에 있는 것이
   * 이 화면이고, 여기가 앞 겉면으로 남으면 고른 사람이 보는 것이 바뀌지 않습니다.
   */
  restyle(): void {
    for (const one of this.buttons) one.button.restyle()
  }

  advance(seconds: number): void {
    if (!this.visible) return
    this.time += seconds
    this.logo.advance(seconds)
  }
}
