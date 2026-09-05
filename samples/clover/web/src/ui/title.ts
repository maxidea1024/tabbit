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
// |가운데|이름과 「시작」|눌러야 하는 것 하나가 가장 크고, 그 아래에 그 밖의 것들이 한 줄로 섭니다|
//
// **단추가 셋입니다.** 무엇으로 시작하는가(덱 · 스테이크) · 챌린지 · 랭크가 저마다 단추
// 하나씩을 차지하고 있었는데, 셋 다 판을 여는 일이므로 「시작」이 여는 판 안으로
// 들어갔습니다.
//
// **가운데의 넷은 세로로 쌓입니다.** 「시작」 아래에 둘을 나란히 두었더니 그 둘이 서로의
// 옆에서 한 덩어리가 되었고, 그러면 「시작」과 그것들이 다른 갈래라는 것이 자리로 읽히지
// 않습니다. 「시작」과 그 아래 사이만 줄 사이보다 넓습니다.

import { Container, Graphics, Text } from 'pixi.js'
import { t } from '../core/strings'

import { COLOR, SIZE, UI } from '../render/theme'
import { outlineOf } from './font'
import type { ToolSpot } from './layout'
import { Tooltip } from './tooltip'
import { Button, IconButton } from './widgets'

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
const LOGO_Y = 214
const TAGLINE_Y = 358
const NOTE_Y = 394

const START_W = 300
const START_H = 72
const START_Y = 470

/**
 * 「시작」 아래의 단추들. **세로로 쌓입니다.**
 *
 * 나란히 두었더니 두 단추가 서로의 옆에 서서 한 덩어리가 되었고, 그러면 「시작」과 그 둘이
 * 서로 다른 갈래의 것이라는 것이 자리로 읽히지 않습니다 — 하나씩 아래로 쌓으면 눈이 위에서
 * 아래로 한 번만 지납니다.
 */
const SECOND_W = 240
const SECOND_H = 48
const SECOND_GAP = 10

/**
 * 「시작」과 그 아래 사이의 틈.
 *
 * **줄 사이보다 넓습니다.** 같으면 넷이 한 줄로 이어진 목록이 되고, 그 목록에서는 「시작」이
 * 그저 첫째 칸입니다 — 눌러야 하는 것 하나가 따로 서 있어야 그것이 먼저 읽힙니다.
 */
const SECOND_GULF = 34
const SECOND_Y = START_Y + START_H + SECOND_GULF

/** 나가기. **맨 아래이고 낮습니다** — 여기서 누를 일이 가장 드뭅니다. */
const QUIT_W = SECOND_W
const QUIT_H = 40
const QUIT_Y = SECOND_Y + (SECOND_H + SECOND_GAP) * 2 + 8

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
  private readonly logo = new Text({
    text: 'clover',
    style: {
      fontSize: 128, fill: COLOR.good, fontWeight: '800',
      // **여기만 굵기를 손으로 정합니다.** 배수는 어느 글자가 올지 모르는 자리의 위쪽
      // 한계이고, 이 글은 `clover` 여섯 자로 고정이라 그 한계보다 굵어도 속이 막히지
      // 않습니다.
      stroke: outlineOf(12, 0x07130b),
      letterSpacing: 10,
    },
  })
  private readonly tagline = new Text({
    text: t('ui.title.tagline'),
    style: { fontSize: 20, fill: COLOR.ink, fontWeight: '700', letterSpacing: 4 },
  })
  private readonly note = new Text({
    text: t('ui.title.note'),
    style: { fontSize: 13, fill: COLOR.inkDim },
  })
  /** 클로버 잎. 이름 위에서 천천히 흔들립니다. */
  private readonly leaf = new Graphics()
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
   * **판때기를 걷었으므로 이름이 남지 않았습니다.** 그림 하나만 서 있으면 처음 보는 사람은
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

    this.drawLeaf()
    this.leaf.position.set(SIZE.width / 2, 172)

    this.logo.anchor.set(0.5, 0)
    this.logo.position.set(SIZE.width / 2, LOGO_Y)

    this.tagline.anchor.set(0.5, 0)
    this.tagline.position.set(SIZE.width / 2, TAGLINE_Y)

    this.note.anchor.set(0.5, 0)
    this.note.position.set(SIZE.width / 2, NOTE_Y)

    // **시작 하나가 가장 큽니다.** 눌러야 하는 것이 하나이면 그것 하나만 크고 밝습니다 —
    // 나머지는 그 아래에서 같은 크기로 섭니다.
    const start = new Button(t('ui.button.start'), START_W, START_H, UI.yellow,
                             hooks.onStart, 30)
    start.position.set(Math.round((SIZE.width - START_W) / 2), START_Y)
    this.buttons.push({ key: 'ui.button.start', button: start })
    this.toolNodes.set('start', { node: start, cx: START_W / 2, cy: START_H / 2 })

    // 그 아래로 쌓입니다. **판을 여는 일이 아닌 것들입니다.**
    const secondX = Math.round((SIZE.width - SECOND_W) / 2)

    const pool = new Button(t('ui.button.collection'), SECOND_W, SECOND_H, UI.btn,
                            hooks.onCollection, 17)
    pool.position.set(secondX, SECOND_Y)
    this.buttons.push({ key: 'ui.button.collection', button: pool })
    this.toolNodes.set('collection', { node: pool, cx: SECOND_W / 2, cy: SECOND_H / 2 })

    const board = new Button(t('ui.button.leaderboard'), SECOND_W, SECOND_H, UI.btn,
                             hooks.onLeaderboard, 17)
    board.position.set(secondX, SECOND_Y + SECOND_H + SECOND_GAP)
    this.buttons.push({ key: 'ui.button.leaderboard', button: board })
    this.toolNodes.set('leaderboard', { node: board, cx: SECOND_W / 2, cy: SECOND_H / 2 })

    // 나가기. **가장 아래이고 낮습니다** — 위의 둘과 같은 높이로 두면 게임을 끝내는 것이
    // 도감을 여는 것과 같은 무게가 됩니다.
    const quit = new Button(t('ui.button.quit'), QUIT_W, QUIT_H, UI.btn, hooks.onQuit, 14)
    quit.position.set(Math.round((SIZE.width - QUIT_W) / 2), QUIT_Y)
    this.buttons.push({ key: 'ui.button.quit', button: quit })
    this.toolNodes.set('quit', { node: quit, cx: QUIT_W / 2, cy: QUIT_H / 2 })

    // 계정. **위 왼쪽 구석입니다.**
    //
    // **싱글에서는 단추 하나입니다.** 「싱글플레이」라고 적힌 이름 줄과 「계정 연결」이
    // 함께 서 있었는데, 계정이 없다는 것은 계정 자리가 비어 있는 것으로 이미 읽힙니다.
    this.accountSlot.position.set(EDGE, EDGE)
    this.accountSlot.visible = false

    const link = new Button(t('ui.account.link'), ACCOUNT_W, ACCOUNT_H - 20, UI.cell,
                            hooks.onAccount, 15)
    link.position.set(EDGE, EDGE)
    this.linkButton = link
    this.buttons.push({ key: 'ui.account.link', button: link })

    const signOut = new Button(t('ui.button.logout'), ACCOUNT_W, SIGNOUT_H, UI.btn,
                               hooks.onSignOut, 13)
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
      style: { fontSize: 11, fill: 0x4f5c6d, fontWeight: '700' },
    })
    version.anchor.set(0, 1)
    version.position.set(EDGE, SIZE.height - 14)

    // **쪽지는 맨 위입니다.** 아이콘 아래에 떠야 하므로 마지막에 얹습니다.
    this.addChild(this.tooltip)

    this.addChild(this.leaf, this.logo, this.tagline, this.note, version,
                  start, pool, board, quit,
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
    this.note.text = t('ui.title.note')
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

  /** 네 잎. 원 넷을 돌려 붙인 모양입니다. */
  private drawLeaf(): void {
    const g = this.leaf
    g.clear()
    for (let i = 0; i < 4; i++) {
      const angle = (Math.PI / 2) * i + Math.PI / 4
      g.circle(Math.cos(angle) * 19, Math.sin(angle) * 19, 16)
        .fill({ color: COLOR.good, alpha: 0.92 })
    }
    g.rect(-2, 16, 4, 28).fill({ color: 0x2f8f52 })
  }

  advance(seconds: number): void {
    if (!this.visible) return
    this.time += seconds
    this.tooltip.advance(seconds)
    this.leaf.rotation = Math.sin(this.time * 0.8) * 0.16
    this.leaf.scale.set(1 + Math.sin(this.time * 1.6) * 0.04)
  }
}
