// 타이틀.
//
// **게임은 타이틀에서 시작합니다.** 열자마자 판이 깔려 있으면 무엇을 하는 화면인지 읽을
// 자리가 없고, 시드를 확인하거나 게임 방법을 먼저 볼 자리도 없습니다.
//
// **판이 아니라 화면입니다.** 상자 안에 담으면 게임 위에 뜬 대화창으로 보이고, 그러면 뒤에
// 이미 무언가가 돌고 있다는 뜻이 됩니다.
//
// **덩어리가 셋입니다.** 아래에 216픽셀짜리 바를 두고 그 안에 두 줄 격자를 짜던 것을
// 걷었습니다 — 계정과 단추 넷과 설정 둘과 아이콘 둘이 그 안에서 서로 폭을 나눠 서느라,
// 단추 하나가 늘 때마다 나머지가 전부 옮겨 갔습니다.
//
// |자리|무엇|왜|
// |--|--|--|
// |위 왼쪽|계정|「지금 누구로 하고 있는가」. 게임의 내용이 아니므로 단추들과 섞이지 않습니다|
// |위 오른쪽|도움말 · 옵션|판 바깥의 일. 아이콘이므로 글이 있는 것들과 갈립니다|
// |가운데|이름과 「시작」|눌러야 하는 것 하나가 가장 크고, 그 아래에 그 밖의 것들이 한 줄로 놓입니다|
//
// **단추가 셋입니다.** 무엇으로 시작하는가(덱 · 스테이크) · 챌린지 · 랭크가 저마다 단추
// 하나씩을 차지하고 있었는데, 셋 다 판을 여는 일이므로 「시작」이 여는 판 안으로
// 들어갔습니다.
//
// **가운데의 넷은 세로로 쌓입니다.** 「시작」 아래에 둘을 나란히 두었더니 그 둘이 서로의
// 옆에서 한 덩어리가 되었고, 그러면 「시작」과 그것들이 다른 갈래라는 것이 자리로 읽히지
// 않습니다. 「시작」과 그 아래 사이만 줄 사이보다 넓습니다.
//
// **이름 위의 도안을 걷었습니다.** 네 잎을 원 넷으로 그려 얹어 두었는데, 원 넷은 어느
// 배율에서도 잎으로 읽히지 않고 이름과 겹쳐 덩어리 하나가 되었습니다 — 이름 자체가
// 표식이므로 그 위에 표식을 하나 더 둘 자리가 없습니다. 이름은 [`wordmark.ts`](wordmark.ts)
// 이고 로그인 화면이 같은 것을 씁니다.
//
// **배경이 이 화면의 절반입니다.** 판이 없고 이름과 단추 몇 개가 전부이므로, 어둡게 낮춘
// 무늬 하나를 깔면 그 어두움이 곧 화면입니다 — 판 밖의 배경은 따로 있습니다
// ([`shader/front.ts`](../shader/front.ts)).

import { Container, Graphics, Text } from 'pixi.js'
import { t } from '../core/strings'

import { UI, SIZE, TEXT, WEIGHT } from '../render/theme'
import type { ToolSpot } from './layout'
import { Tooltip } from './tooltip'
import { Button, IconButton } from './widgets'
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

/** 화면 가장자리에서 띄우는 거리. 위 왼쪽·위 오른쪽·아래 왼쪽이 같습니다. */
const EDGE = 28

/** 계정 자리. **`hub.ts` 의 카드와 같은 크기입니다** — 그 자리에 그 카드가 놓입니다. */
const ACCOUNT_W = 200
const ACCOUNT_H = 72
const SIGNOUT_H = 30

/** 위 오른쪽의 아이콘 둘. */
const ICON = 56
const ICON_GAP = 10

/** 가운데. 이름과 그 아래의 단추들입니다. */
/**
 * 로고와 단추의 자리. **2026-09-10에 오른쪽 세로줄로 옮겼습니다.**
 *
 * 배경이 그림 한 장이 되었으므로 로고와 단추가 그림의 어디에 놓이는지가 그림의 구성을
 * 정합니다 — 주인공이 왼쪽에 서고 오른쪽이 열려 있으므로 로고는 왼쪽 위, 단추는 오른쪽에
 * 세로로 내려옵니다. 규격은 `notes/hypephoria-rebrand-plan.md` 에 있습니다.
 */
const COLUMN_W = 260
/**
 * 단추 줄의 왼쪽 변.
 *
 * **화면 오른쪽 변에서 108픽셀 들여놓습니다.** 56이었을 때 단추가 변에 붙어 보였습니다 —
 * 그림의 오른쪽에 어두운 근경이 있으므로 그 안쪽에 놓여야 판 위에 얹힌 것으로 보입니다.
 */
const COLUMN_X = SIZE.width - COLUMN_W - 108
const COLUMN_TOP = 232
const COLUMN_GAP = 14
/**
 * 단추 한 칸의 높이.
 *
 * **넷이 다 같습니다.** 시작만 크고 밝던 것을 걷었습니다 — 타이틀의 단추는 등급이 같고,
 * 무엇을 먼저 누를지는 자리가 정합니다(맨 위가 시작). 크기·서체·색을 달리하면 화면에
 * 등급이 넷 생깁니다.
 */
const ROW_H = 56
const LOGO_SIZE = 76
const LOGO_Y = 96
const TAGLINE_Y = 334

const START_W = COLUMN_W
const START_H = ROW_H
const START_Y = COLUMN_TOP

/**
 * 「시작」을 둘러싼 빛.
 *
 * **누를 것 하나를 가리킵니다.** 색과 크기로 이미 갈라 두었지만 멈춰 있는 화면에서는 넷이
 * 다 같은 무게로 보이고, 이 화면에서 다음에 할 일은 언제나 하나입니다.
 *
 * **한 번 그리고 알파와 배율만 움직입니다.** 매 프레임 다시 그리면 그때마다 다시
 * 삼각화됩니다.
 */
const GLOW_RINGS = 14

/**
 * 「시작」 아래의 단추들. **세로로 쌓입니다.**
 *
 * 나란히 두었더니 두 단추가 서로의 옆에 서서 한 덩어리가 되었고, 그러면 「시작」과 그 둘이
 * 서로 다른 갈래의 것이라는 것이 자리로 읽히지 않습니다 — 하나씩 아래로 쌓으면 눈이 위에서
 * 아래로 한 번만 지납니다.
 */
const SECOND_W = COLUMN_W
const SECOND_H = ROW_H

/**
 * 「시작」과 그 아래 사이의 틈.
 *
 * **줄 사이보다 넓습니다.** 같으면 넷이 한 줄로 이어진 목록이 되고, 그 목록에서는 「시작」이
 * 그저 첫째 칸입니다 — 눌러야 하는 것 하나가 따로 놓여 있어야 그것이 먼저 읽힙니다.
 */
const SECOND_Y = START_Y + START_H + COLUMN_GAP

/** 나가기. **맨 아래이고 낮습니다** — 여기서 누를 일이 가장 드뭅니다. */
const QUIT_W = SECOND_W
const QUIT_H = ROW_H
const QUIT_Y = SECOND_Y + (SECOND_H + COLUMN_GAP) * 2

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

export class Title extends Container {
  private readonly logo = new Wordmark(LOGO_SIZE, 4)
  /** 한 줄 소개. **금색이 아니라 따뜻한 흰색입니다** — 금색은 값과 나아감의 색입니다. */
  private readonly tagline = new Text({
    text: t('ui.title.tagline'),
    // **그림 위에 놓이는 글입니다.** 얇고 옅게 두었더니 배경의 붓질에 묻혔습니다 — 굵게
    // 하고 테두리를 둘러야 읽힙니다. 자간은 슬로건이므로 넓게 둡니다.
    style: {
      // **그림 위의 글입니다.** 이 그림의 왼쪽은 밝기가 2에서 246까지이므로 어느 한 색으로도
      // 읽히지 않습니다 — 뒤에 사각형을 깔아 보았고 그림 위에 회색 판이 놓인 것이 되었습니다.
      // **글 자체에 두꺼운 테두리와 떨어지는 그림자를 둡니다.** 판이 생기지 않습니다.
      fontSize: TEXT.big,
      stroke: { color: UI.outline, width: 5, join: 'round' as const },
      fill: UI.ink, fontWeight: WEIGHT.heavy, letterSpacing: 4,
      dropShadow: {
        color: UI.outline, alpha: 0.85, blur: 6, distance: 3, angle: Math.PI / 2,
      },
    },
  })
  /**
   * 한 줄 소개의 양옆에 서는 선.
   *
   * **글의 폭에서 자리를 냅니다.** 말마다 길이가 다르므로 수로 적어 두면 어느 말에서는
   * 글자에 닿고 어느 말에서는 멀리 떨어집니다 — `relabel` 이 다시 그립니다.
   */
  private readonly rule = new Graphics()
  /** 「시작」을 둘러싼 빛. 알파와 배율만 움직입니다. */
  private readonly startGlow = new Graphics()
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
   * 아이콘 단추들.
   *
   * **겉면을 갈아 끼우면 이것들도 다시 그려야 합니다.** `restyle` 이 글이 있는 단추만
   * 돌고 있어서, 도움말과 옵션은 앞 겉면의 색으로 남아 있었습니다 — 두 아이콘만 다른
   * 겉면인 화면이 그것입니다.
   */
  private readonly icons: IconButton[] = []

  /**
   * 아이콘 둘의 쪽지.
   *
   * **판때기를 걷었으므로 이름이 남지 않았습니다.** 그림 하나만 놓여 있으면 처음 보는 사람은
   * 물음표와 톱니가 무엇을 여는지를 눌러 봐야 압니다 — 가리키면 그 자리에 뜹니다.
   */
  private readonly tooltip = new Tooltip()

  /**
   * 로그인했을 때 그 자리에 놓이는 것.
   *
   * **카드가 단추를 대신합니다.** 이름을 두 곳에 적으면 같은 것을 두 번 보게 되고, 카드에는
   * 순위까지 있습니다 — `game.ts` 가 여기에 카드를 넣습니다.
   */
  readonly accountSlot = new Container()

  constructor(hooks: TitleHooks) {
    super()

    // **덮는 층이 없습니다.** 글이 읽히게 하려고 반투명 사각형을 얹으면 그 겹의 변이
    // 그대로 가로선으로 보입니다 — 어둡게 할 것은 배경이므로 배경을 어둡게 합니다.
    //
    // `game.ts` 의 `syncMood` 가 타이틀에서 그렇게 넘깁니다.

    // **왼쪽 영역의 가운데입니다.** `Wordmark` 의 기준점이 가운데이므로 왼쪽 변에 맞추면
    // 글의 절반이 화면 밖으로 나갑니다. 단추 줄이 시작되는 자리까지가 그 영역입니다.
    this.logo.position.set(Math.round(COLUMN_X / 2), LOGO_Y)

    this.tagline.anchor.set(0.5, 0)
    this.tagline.position.set(Math.round(COLUMN_X / 2), TAGLINE_Y)
    this.drawRule()


    // **시작 하나가 가장 큽니다.** 눌러야 하는 것이 하나이면 그것 하나만 크고 밝습니다 —
    // 나머지는 그 아래에서 같은 크기로 놓입니다.
    // **넷이 같은 갈래입니다.** 나가기만 색을 달리 합니다 — 되돌릴 수 없는 것이므로
    // 그것 하나만 눈에 다르게 보이면 됩니다.
    // **시작만 금색입니다.** 넷이 다 회색이면 무엇을 누를지가 화면에 없습니다 — 크기와
    // 서체는 같게 두고 색으로만 가릅니다. 나가기는 붉은빛입니다.
    const start = new Button(t('ui.button.start'), START_W, START_H, 'primary',
                             hooks.onStart)
    start.position.set(COLUMN_X, START_Y)
    this.buttons.push({ key: 'ui.button.start', button: start })
    this.toolNodes.set('start', { node: start, cx: START_W / 2, cy: START_H / 2 })
    this.drawStartGlow()

    // 그 아래로 쌓입니다. **판을 여는 일이 아닌 것들입니다.**
    const secondX = COLUMN_X

    const pool = new Button(t('ui.button.collection'), SECOND_W, SECOND_H, 'neutral',
                            hooks.onCollection)
    pool.position.set(secondX, SECOND_Y)
    this.buttons.push({ key: 'ui.button.collection', button: pool })
    this.toolNodes.set('collection', { node: pool, cx: SECOND_W / 2, cy: SECOND_H / 2 })

    const board = new Button(t('ui.button.leaderboard'), SECOND_W, SECOND_H, 'neutral',
                             hooks.onLeaderboard)
    board.position.set(secondX, SECOND_Y + SECOND_H + COLUMN_GAP)
    this.buttons.push({ key: 'ui.button.leaderboard', button: board })
    this.toolNodes.set('leaderboard', { node: board, cx: SECOND_W / 2, cy: SECOND_H / 2 })

    // 나가기. **가장 아래이고 낮습니다** — 위의 둘과 같은 높이로 두면 게임을 끝내는 것이
    // 도감을 여는 것과 같은 무게가 됩니다.
    const quit = new Button(t('ui.button.quit'), QUIT_W, QUIT_H, 'danger',
                            hooks.onQuit)
    quit.position.set(COLUMN_X, QUIT_Y)
    this.buttons.push({ key: 'ui.button.quit', button: quit })
    this.toolNodes.set('quit', { node: quit, cx: QUIT_W / 2, cy: QUIT_H / 2 })

    // 계정. **위 왼쪽 구석입니다.**
    //
    // **싱글에서는 단추 하나입니다.** 「싱글플레이」라고 적힌 이름 줄과 「계정 연결」이
    // 함께 놓여 있었는데, 계정이 없다는 것은 계정 자리가 비어 있는 것으로 이미 읽힙니다.
    this.accountSlot.position.set(EDGE, EDGE)
    this.accountSlot.visible = false

    const link = new Button(t('ui.account.link'), ACCOUNT_W, ACCOUNT_H - 20, 'quiet',
                            hooks.onAccount)
    link.position.set(EDGE, EDGE)
    this.linkButton = link
    this.buttons.push({ key: 'ui.account.link', button: link })

    const signOut = new Button(t('ui.button.logout'), ACCOUNT_W, SIGNOUT_H, 'neutral',
                               hooks.onSignOut)
    signOut.position.set(EDGE, EDGE + ACCOUNT_H + 8)
    signOut.visible = false
    this.signOutButton = signOut
    this.buttons.push({ key: 'ui.button.logout', button: signOut })
    this.toolNodes.set('signOut', { node: signOut, cx: ACCOUNT_W / 2, cy: SIGNOUT_H / 2 })

    // 게임 방법과 옵션. **위 오른쪽 구석이고 계정과 윗변이 같습니다.**
    const guide = new IconButton(ICON, 'circle-question-mark', hooks.onGuide)
    guide.position.set(SIZE.width - EDGE - ICON * 2 - ICON_GAP, EDGE)
    this.toolNodes.set('guide', { node: guide, cx: ICON / 2, cy: ICON / 2 })
    this.tipOn(guide, 'ui.button.guide')
    const option = new IconButton(ICON, 'settings', hooks.onOptions)
    option.position.set(SIZE.width - EDGE - ICON, EDGE)
    this.toolNodes.set('options', { node: option, cx: ICON / 2, cy: ICON / 2 })
    this.tipOn(option, 'ui.button.options')
    this.icons.push(guide, option)

    // 판 번호. **로그인 화면과 같은 구석입니다.**
    const version = new Text({
      text: `v${__APP_VERSION__}`,
      style: { fontSize: TEXT.mini, fill: UI.inkFaint, fontWeight: WEIGHT.normal },
    })
    version.anchor.set(0, 1)
    version.position.set(EDGE, SIZE.height - 14)

    // **배경 그림이 맨 아래입니다.** 없으면 지금까지의 배경으로 갑니다 — 판 밖의 셰이더는
    // 판이 도는 동안 쓰는 것이므로 건드리지 않고, 이 그림이 그 위를 덮습니다.
    const art = sceneArt()
    if (art !== undefined) this.addChild(art)

    // **쪽지는 맨 위입니다.** 아이콘 아래에 떠야 하므로 마지막에 얹습니다.
    this.addChild(this.tooltip)

    this.addChild(this.logo, this.tagline, this.rule, version,
                  this.startGlow, start, pool, board, quit,
                  link, this.accountSlot, signOut, guide, option)

    // 뒤를 눌러도 아무 일도 없습니다. **시작은 눌러서 시작하는 것입니다.**
    this.eventMode = 'static'
    this.on('pointertap', () => undefined)
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
    this.drawRule()
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
    for (const one of this.icons) one.restyle()
  }

  /**
   * 그 아이콘의 이름을 쪽지로.
   *
   * **이름 하나뿐입니다.** 무엇을 여는 단추인지가 그 한 낱말이고, 그 아래에 설명을 더하면
   * 쪽지가 아이콘보다 커집니다.
   */
  private tipOn(node: IconButton, key: string): void {
    const show = (): void => {
      this.tooltip.show(t(key), '', 0, [], {
        x: node.x + ICON / 2, top: node.y, bottom: node.y + ICON,
      }, SIZE)
    }
    node.on('pointerover', show)
    node.on('pointerout', () => this.tooltip.hide())
  }

  /**
   * 한 줄 소개의 양옆에 서는 선.
   *
   * **바깥쪽이 더 옅습니다.** 같은 굵기의 선 하나면 글에 밑줄을 그은 것이 되고, 옅어지며
   * 끝나면 글이 그 사이에 놓인 것이 됩니다.
   */
  private drawRule(): void {
    const g = this.rule
    g.clear()
    const middle = Math.round(COLUMN_X / 2)
    const half = this.tagline.width / 2
    const y = Math.round(TAGLINE_Y + this.tagline.height / 2)
    for (const side of [-1, 1]) {
      const from = middle + side * (half + 20)
      g.moveTo(from, y).lineTo(from + side * 34, y)
        .stroke({ color: UI.yellow, width: 1.5, alpha: 0.55 })
      g.moveTo(from + side * 34, y).lineTo(from + side * 72, y)
        .stroke({ color: UI.yellow, width: 1.5, alpha: 0.18 })
    }
  }

  /** 「시작」을 둘러싼 빛. **테 셋이고 바깥일수록 옅습니다.** */
  private drawStartGlow(): void {
    const g = this.startGlow
    g.clear()
    // **테 하나가 아니라 여럿입니다.** 셋을 성기게 두었더니 테 셋이 그대로 보였습니다 —
    // 촘촘히 겹치고 바깥으로 갈수록 제곱으로 옅어지면 번짐 하나로 보입니다.
    for (let ring = 0; ring < GLOW_RINGS; ring++) {
      const part = ring / (GLOW_RINGS - 1)
      const out = 2 + part * 16
      const fade = (1 - part) * (1 - part)
      g.roundRect(-out, -out, START_W + out * 2, START_H + out * 2, 6 + out)
        .stroke({ color: UI.yellow, width: 2.5, alpha: 0.03 + fade * 0.085 })
    }
    g.pivot.set(START_W / 2, START_H / 2)
    g.position.set(SIZE.width / 2, START_Y + START_H / 2)
  }

  advance(seconds: number): void {
    if (!this.visible) return
    this.time += seconds
    this.tooltip.advance(seconds)
    this.logo.advance(seconds)
    // **숨 쉬듯 오르내립니다.** 값만 만지므로 다시 그리는 것이 없습니다.
    const breath = 0.5 + 0.5 * Math.sin(this.time * 1.9)
    this.startGlow.alpha = 0.45 + 0.55 * breath
    this.startGlow.scale.set(1 + breath * 0.012)
  }
}
