// 타이틀.
//
// **게임은 타이틀에서 시작합니다.** 열자마자 판이 깔려 있으면 무엇을 하는 화면인지 읽을
// 자리가 없고, 시드를 확인하거나 게임 방법을 먼저 볼 자리도 없습니다.
//
// **판이 아니라 화면입니다.** 큰 이름 하나가 가운데 위에 있고 단추가 아래에 한 줄로
// 놓입니다 — 상자 안에 담으면 게임 위에 뜬 대화창으로 보이고, 그러면 뒤에 이미 무언가가
// 돌고 있다는 뜻이 됩니다.
//
// **아래가 바 하나입니다.** 계정과 단추와 아이콘을 화면 여기저기에 놓으면 세 덩어리가
// 서로 다른 높이에 흩어져 「늘어놓은 것」으로 보입니다 — 하나의 판 위에 얹고 밑변을
// 맞추면 그 셋이 한 줄의 세 자리가 됩니다.
//
// 바 안의 자리는 셋입니다.
//
// |자리|무엇|왜|
// |--|--|--|
// |왼쪽|계정|「지금 누구로 하고 있는가」. 게임의 내용이 아니므로 단추들과 섞이지 않습니다|
// |가운데|시작과 그 설정|눌러야 하는 것. 위가 무엇으로 시작하는가이고 아래가 어디로 가는가입니다|
// |오른쪽|도움말 · 옵션|판 바깥의 일. 아이콘이므로 글이 있는 것들과 갈립니다|
//
// 규칙은 모릅니다. 시작을 누르면 화면이 알아서 판을 폅니다.

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

/** 아래 바. **화면의 밑변에 붙습니다.** */
const DOCK_H = 216
const DOCK_Y = SIZE.height - DOCK_H
const DOCK_PAD = 26

/** 바 안의 두 줄. 위가 설정, 아래가 단추입니다. */
const UPPER_H = 34
const ROW_H = 62
const ROW_GAP = 10
const ROW_Y = DOCK_Y + DOCK_PAD + UPPER_H + ROW_GAP
const UPPER_Y = DOCK_Y + DOCK_PAD

/** 단추의 폭. **시작이 가장 큽니다.** */
const START_W = 196
const OTHER_W = 132
const BTN_GAP = 10

/**
 * 계정 자리.
 *
 * **두 줄을 통째로 차지합니다.** 윗변이 설정 줄과 같고 밑변이 단추 줄과 같아야, 왼쪽
 * 덩어리가 가운데 덩어리와 한 격자 위에 있는 것으로 보입니다.
 */
const ACCOUNT_W = 200
const ACCOUNT_H = UPPER_H + ROW_GAP + ROW_H
const SIGNOUT_H = 26
const CARD_H = ACCOUNT_H - SIGNOUT_H - 8

export interface TitleHooks {
  onStart: () => void
  onGuide: () => void
  onOptions: () => void
  onJokers: () => void
  onChallenges: () => void
  onLeaderboard: () => void
  /** 랭크 런. **로그인 상태에서만 보입니다.** */
  onRanked: () => void
  /** 덱 · 스테이크 · 풀을 고르는 판. 단추에 지금 고른 것이 적힙니다. */
  onSetup: () => void
  /** 계정 칩. 로그인이면 프로필, 싱글플레이면 로그인 씬입니다. */
  onAccount: () => void
  /** 로그아웃. **로그인 상태에서만 보입니다.** */
  onSignOut: () => void
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
   * **좌표를 도구에 적어 두지 않기 위한 것입니다.** 이 화면의 단추는 아래 바의 폭을 나눠
   * 서므로 단추가 하나 늘거나 바가 다시 짜이면 자리가 전부 옮겨 갑니다 — 도구가 그 셈을
   * 베껴 적고 있었고, 바가 새로 짜인 뒤로 어떤 도구는 아무것도 없는 곳을 눌렀습니다.
   */
  private readonly toolNodes = new Map<string, ToolSpot>()

  /** 그 단추들의 자리. 화면 좌표로 바꾸는 것은 이 화면을 띄운 쪽이 합니다. */
  get toolSpots(): [string, ToolSpot][] {
    return [...this.toolNodes]
  }

  /** 글을 다시 읽어야 하는 것들. 말이 바뀌면 갈아 끼웁니다. */
  private readonly buttons: { key: string; button: Button }[] = []

  /**
   * 잠긴 단추에 올렸을 때 뜨는 쪽지.
   *
   * **판 안의 쪽지와 같은 것입니다.** 타이틀에만 따로 만들면 모습이 두 가지가 되고, 잠긴
   * 것을 알리는 자리가 화면마다 달라집니다.
   */
  private readonly tip = new Tooltip()
  private challenges?: Button
  private rankedButton?: Button
  private signOutButton?: Button
  private setupButton?: Button
  /** 로그인 상태. 랭크 단추의 쪽지를 띄울지가 이것으로 갈립니다. */
  private signedIn = false

  /**
   * 로그인했을 때 그 자리에 놓이는 것.
   *
   * **카드가 칩을 대신합니다.** 이름을 두 곳에 적으면 같은 것을 두 번 보게 되고, 카드에는
   * 순위까지 있으므로 칩이 남을 이유가 없습니다 — `game.ts` 가 여기에 카드를 넣습니다.
   */
  readonly accountSlot = new Container()

  /** 로그인하지 않았을 때의 칩. 상태에 따라 글이 바뀝니다. */
  private readonly chip = new Container()
  private chipLabel = ''
  private chipName = t('ui.account.guest')

  constructor(hooks: TitleHooks) {
    super()

    // **덮는 층이 없습니다.** 글이 읽히게 하려고 반투명 사각형을 얹으면 그 겹의 변이
    // 그대로 가로선으로 보입니다 — 어둡게 할 것은 배경이므로 배경을 어둡게 합니다.
    //
    // `game.ts` 의 `syncMood` 가 타이틀에서 그렇게 넘깁니다.

    this.drawLeaf()
    this.leaf.position.set(SIZE.width / 2, 196)

    this.logo.anchor.set(0.5, 0)
    this.logo.position.set(SIZE.width / 2, 240)

    this.tagline.anchor.set(0.5, 0)
    this.tagline.position.set(SIZE.width / 2, 384)

    this.note.anchor.set(0.5, 0)
    this.note.position.set(SIZE.width / 2, 420)

    // 바. **하나의 판 위에 전부 얹힙니다.**
    const dock = new Graphics()
    dock.rect(0, DOCK_Y, SIZE.width, DOCK_H).fill({ color: UI.panel, alpha: 0.6 })
    dock.rect(0, DOCK_Y, SIZE.width, 1.5).fill(UI.rule)

    // **시작이 가장 큽니다.** 넷이 한 줄에 있어도 눌러야 하는 것 하나는 커야 합니다 —
    // 크기가 같으면 넷 중에서 고르는 일이 되고, 손가락으로는 잘못 누르는 일까지 생깁니다.
    const total = START_W + OTHER_W * 3 + BTN_GAP * 3
    const left = Math.round((SIZE.width - total) / 2)
    let x = left

    const start = new Button(t('ui.button.start'), START_W, ROW_H, UI.yellow,
                             hooks.onStart, 26)
    start.position.set(x, ROW_Y)
    this.buttons.push({ key: 'ui.button.start', button: start })
    this.toolNodes.set('start', { node: start, cx: START_W / 2, cy: ROW_H / 2 })
    x += START_W + BTN_GAP

    // **밝은 단추는 「시작」 하나입니다.** 넷이 저마다의 색이면 어느 것을 먼저 누를지가
    // 색으로 정해지지 않고, 그러면 색은 장식입니다.
    const pool = new Button(t('ui.button.jokers'), OTHER_W, ROW_H, UI.slate,
                            hooks.onJokers, 17)
    pool.position.set(x, ROW_Y)
    this.buttons.push({ key: 'ui.button.jokers', button: pool })
    this.toolNodes.set('jokers', { node: pool, cx: OTHER_W / 2, cy: ROW_H / 2 })
    x += OTHER_W + BTN_GAP

    // **열리기 전에는 비활성입니다.** 눌러서 잠긴 것을 알게 하는 것보다, 눌리지 않는 것이
    // 보이고 올렸을 때 무엇으로 열리는지 적히는 쪽이 그 자리에서 끝납니다.
    const dare = new Button(t('ui.button.challenges'), OTHER_W, ROW_H, UI.slate,
                            hooks.onChallenges, 17)
    dare.position.set(x, ROW_Y)
    this.buttons.push({ key: 'ui.button.challenges', button: dare })
    this.toolNodes.set('challenges', { node: dare, cx: OTHER_W / 2, cy: ROW_H / 2 })
    this.challenges = dare
    dare.enabled = false
    x += OTHER_W + BTN_GAP

    const board = new Button(t('ui.button.leaderboard'), OTHER_W, ROW_H, UI.slate,
                             hooks.onLeaderboard, 17)
    board.position.set(x, ROW_Y)
    this.buttons.push({ key: 'ui.button.leaderboard', button: board })
    this.toolNodes.set('leaderboard', { node: board, cx: OTHER_W / 2, cy: ROW_H / 2 })

    dare.on('pointerover', () => {
      if (this.locked) {
        this.tip.show(t('ui.button.challenges'), '', 0, [t('ui.challenge.lockedAll')],
                      dare.x + OTHER_W / 2, ROW_Y + ROW_H, SIZE)
      }
    })
    dare.on('pointerout', () => this.tip.hide())

    // **무엇으로 시작하는가는 시작 바로 위입니다.** 판을 한 번 열어 봐야 아는 것이 아니라
    // 단추에 적혀 있어야 하고, 적혀 있으려면 시작에 붙어 있어야 합니다.
    // **윗줄이 아랫줄과 같은 격자를 씁니다.** 시작 하나의 폭에만 맞추면 윗줄의 가운데가
    // 아랫줄의 가운데와 어긋나고, 그것이 「가로가 안 맞는」 모습입니다 — 설정이 앞의 두
    // 칸을, 랭크가 뒤의 두 칸을 차지하므로 두 줄의 좌우 끝이 같습니다.
    // **랭크는 늘 그 자리에 있습니다.** 로그인 상태에 따라 나타났다 사라지면 윗줄의
    // 폭이 바뀌고, 그러면 같은 화면이 두 가지 모습이 됩니다 — 잠긴 채로 보이는 것이
    // 챌린지와 같은 규칙이고, 무엇으로 열리는지는 올렸을 때 적힙니다.
    const setupW = START_W + BTN_GAP + OTHER_W * 2 + BTN_GAP
    const rankedW = OTHER_W

    const setup = new Button('', setupW, UPPER_H, UI.slate, hooks.onSetup, 14)
    setup.position.set(left, UPPER_Y)
    this.toolNodes.set('setup', { node: setup, cx: setupW / 2, cy: UPPER_H / 2 })
    this.setupButton = setup

    // **랭크는 그 옆입니다.** 시작과 같은 일이고 다만 오르는 판이므로, 아래 줄에 다섯째로
    // 두면 조커 풀과 같은 갈래로 보입니다.
    // **시작보다 조용합니다.** 같은 초록으로 크게 두면 눌러야 하는 것이 둘로 보입니다.
    const ranked = new Button(t('ui.lb.ranked'), rankedW, UPPER_H, UI.slate,
                              hooks.onRanked, 13)
    ranked.position.set(left + setupW + BTN_GAP, UPPER_Y)
    this.toolNodes.set('ranked', { node: ranked, cx: rankedW / 2, cy: UPPER_H / 2 })
    ranked.enabled = false
    this.rankedButton = ranked
    this.buttons.push({ key: 'ui.lb.ranked', button: ranked })

    ranked.on('pointerover', () => {
      if (!this.signedIn) {
        this.tip.show(t('ui.lb.ranked'), '', 0, [t('ui.account.needLink')],
                      ranked.x + rankedW / 2, UPPER_Y + UPPER_H, SIZE)
      }
    })
    ranked.on('pointerout', () => this.tip.hide())

    // 계정. **바의 왼쪽 끝이고 두 줄과 위아래 변이 같습니다.**
    this.accountSlot.position.set(DOCK_PAD, UPPER_Y)
    this.accountSlot.visible = false
    this.chip.position.set(DOCK_PAD, UPPER_Y)
    this.chip.eventMode = 'static'
    this.chip.cursor = 'pointer'
    this.chip.on('pointertap', () => hooks.onAccount())

    const signOut = new Button(t('ui.button.logout'), ACCOUNT_W, SIGNOUT_H, UI.slate,
                               hooks.onSignOut, 12)
    signOut.position.set(DOCK_PAD, UPPER_Y + CARD_H + 8)
    signOut.visible = false
    this.signOutButton = signOut
    this.buttons.push({ key: 'ui.button.logout', button: signOut })

    // 게임 방법과 옵션. **바의 오른쪽 끝이고 아래 줄에 가운데를 맞춥니다.**
    // **단추 줄과 같은 높이입니다.** 크기가 다르면 밑변이 어긋나고, 그 한 줄이 화면
    // 전체를 흐트러뜨립니다.
    const icon = ROW_H
    const iconY = ROW_Y
    const guide = new IconButton(icon, 'circle-question-mark', hooks.onGuide)
    guide.position.set(SIZE.width - DOCK_PAD - icon * 2 - BTN_GAP, iconY)
    this.toolNodes.set('guide', { node: guide, cx: icon / 2, cy: icon / 2 })
    const option = new IconButton(icon, 'settings', hooks.onOptions)
    option.position.set(SIZE.width - DOCK_PAD - icon, iconY)
    this.toolNodes.set('options', { node: option, cx: icon / 2, cy: icon / 2 })

    // 판 번호. **로그인 화면과 같은 구석입니다.**
    const version = new Text({
      text: `v${__APP_VERSION__}`,
      style: { fontSize: 11, fill: 0x4f5c6d, fontWeight: '700' },
    })
    version.anchor.set(0, 1)
    version.position.set(DOCK_PAD, SIZE.height - 10)

    this.addChild(this.leaf, this.logo, this.tagline, this.note, dock, version,
                  start, pool, dare, board, setup, ranked,
                  this.chip, this.accountSlot, signOut, guide, option, this.tip)

    this.drawChip()
    this.relabel()

    // 뒤를 눌러도 아무 일도 없습니다. **시작은 눌러서 시작하는 것입니다.**
    this.eventMode = 'static'
    this.on('pointertap', () => undefined)
  }

  /**
   * 무엇으로 시작하는지를 단추에 적습니다.
   *
   * **말을 여기서 만들지 않습니다** — 덱과 스테이크의 표시 이름이므로 데이터에서 나오고,
   * 그것을 한 줄로 잇는 것은 `ui/setup.ts` 가 합니다.
   */
  setSetupLabel(label: string): void {
    if (this.setupButton) this.setupButton.text = label
  }

  /**
   * 계정 상태를 알립니다.
   *
   * **로그아웃 단추는 로그인했을 때만 있습니다.** 싱글플레이에서는 그 자리에 「계정 연결」이
   * 있고, 그것이 로그인 화면으로 되돌립니다.
   */
  setAccount(signedIn: boolean, name: string): void {
    this.chipName = name === '' ? t('ui.account.guest') : name
    this.chipLabel = t('ui.account.link')
    this.signedIn = signedIn
    this.chip.visible = !signedIn
    this.accountSlot.visible = signedIn
    if (this.rankedButton) this.rankedButton.enabled = signedIn
    if (this.signOutButton) this.signOutButton.visible = signedIn
    if (signedIn) this.tip.hide()
    this.drawChip()
  }

  private drawChip(): void {
    this.chip.removeChildren().forEach(child => child.destroy({ children: true }))

    const plate = new Graphics()
    plate.roundRect(0, 0, ACCOUNT_W, ACCOUNT_H, 10)
      .fill({ color: 0x151d2a, alpha: 0.9 })
      .stroke({ color: 0x2c3849, width: 1.5 })
    this.chip.addChild(plate)

    const name = new Text({
      text: this.chipName,
      style: { fontSize: 16, fill: COLOR.ink, fontWeight: '800' },
    })
    name.anchor.set(0.5, 0.5)
    name.position.set(ACCOUNT_W / 2, ACCOUNT_H / 2 - 11)

    const label = new Text({
      text: this.chipLabel === '' ? t('ui.account.link') : this.chipLabel,
      style: { fontSize: 12, fill: UI.bar, fontWeight: '700' },
    })
    label.anchor.set(0.5, 0.5)
    label.position.set(ACCOUNT_W / 2, ACCOUNT_H / 2 + 12)

    this.chip.addChild(name, label)
  }

  /** 챌린지가 잠겨 있는가. 쪽지를 띄울지가 이것으로 갈립니다. */
  private locked = true

  /** 챌린지가 열렸는지 알립니다. 저장을 읽는 쪽이 겁니다. */
  setChallengesOpen(open: boolean): void {
    this.locked = !open
    if (this.challenges) this.challenges.enabled = open
    if (open) this.tip.hide()
  }

  relabel(): void {
    this.tagline.text = t('ui.title.tagline')
    this.note.text = t('ui.title.note')
    for (const one of this.buttons) one.button.text = t(one.key)
    this.drawChip()
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
    this.leaf.rotation = Math.sin(this.time * 0.8) * 0.16
    this.leaf.scale.set(1 + Math.sin(this.time * 1.6) * 0.04)
  }
}
