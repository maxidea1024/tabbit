// 타이틀.
//
// **게임은 타이틀에서 시작합니다.** 열자마자 판이 깔려 있으면 무엇을 하는 화면인지 읽을
// 자리가 없고, 시드를 확인하거나 게임 방법을 먼저 볼 자리도 없습니다.
//
// **판이 아니라 화면입니다.** 큰 이름 하나가 가운데 위에 서고 버튼이 아래에 줄지어 섭니다 —
// 상자 안에 담으면 게임 위에 뜬 대화창으로 보이고, 그러면 뒤에 이미 무언가가 돌고 있다는
// 뜻이 됩니다.
//
// 규칙은 모릅니다. 시작을 누르면 화면이 알아서 판을 폅니다.

import { Container, Graphics, Text } from 'pixi.js'
import { t } from '../core/strings'

import { COLOR, SIZE } from '../render/theme'
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

export class Title extends Container {
  private readonly logo = new Text({
    text: 'clover',
    style: {
      fontSize: 128, fill: COLOR.good, fontWeight: '800',
      stroke: { color: 0x07130b, width: 12 },
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
  /** 글을 다시 읽어야 하는 것들. 말이 바뀌면 이 셋을 갈아 끼웁니다. */
  private readonly buttons: { key: string; button: Button }[] = []

  /**
   * 잠긴 단추에 올렸을 때 뜨는 쪽지.
   *
   * **판 안의 쪽지와 같은 것입니다.** 타이틀에만 따로 만들면 모습이 두 가지가 되고, 잠긴
   * 것을 알리는 자리가 화면마다 달라집니다.
   */
  private readonly tip = new Tooltip()
  private challenges?: Button

  constructor(onStart: () => void, onGuide: () => void, onOptions: () => void,
              onJokers?: () => void, onChallenges?: () => void,
              onLeaderboard?: () => void, onRanked?: () => void,
              onSetup?: () => void) {
    super()

    // **덮는 층이 없습니다.** 글이 읽히게 하려고 반투명 사각형을 얹으면 그 겹의 변이
    // 그대로 가로선으로 보입니다 — 어둡게 할 것은 배경이므로 배경을 어둡게 합니다.
    //
    // `game.ts` 의 `syncMood` 가 타이틀에서 그렇게 넘깁니다.

    this.drawLeaf()
    this.leaf.position.set(SIZE.width / 2, 132)

    this.logo.anchor.set(0.5, 0)
    this.logo.position.set(SIZE.width / 2, 176)

    this.tagline.anchor.set(0.5, 0)
    this.tagline.position.set(SIZE.width / 2, 320)

    this.note.anchor.set(0.5, 0)
    this.note.position.set(SIZE.width / 2, 356)

    // **누를 것은 하나입니다.** 셋이 같은 크기로 나란히 서 있으면 무엇을 눌러야 하는지가
    // 셋 중에서 골라야 하는 일이 되고, 손가락으로는 잘못 누르는 일까지 생깁니다 — 시작
    // 하나만 판 가운데에 크게 두고, 나머지 둘은 구석의 아이콘으로 갑니다.
    const bw = 236
    const bh = 72
    const start = new Button(t('ui.button.start'), bw, bh, 0x2f8f52, onStart, 30)
    start.position.set(SIZE.width / 2 - bw / 2, 452)
    this.buttons.push({ key: 'ui.button.start', button: start })

    // 게임 방법과 옵션. **오른쪽 아래 구석입니다** — 판을 여는 것이 아니라 판 바깥의
    // 일이므로, 가운데의 줄에서 빠져 나와야 시작이 혼자 남습니다.
    const icon = 58
    const gap = 14
    const edge = 30
    const iconY = SIZE.height - edge - icon
    const guide = new IconButton(icon, 'circle-question-mark', onGuide)
    guide.position.set(SIZE.width - edge - icon * 2 - gap, iconY)
    const option = new IconButton(icon, 'settings', onOptions)
    option.position.set(SIZE.width - edge - icon, iconY)

    // 조커 풀. **시작 바로 아래입니다** — 판에 무엇이 들어가는지를 정하는 일이므로
    // 시작과 가장 가깝고, 구석의 아이쵘으로 보내면 눈에 들지 않습니다.
    const pool = new Button(t('ui.button.jokers'), bw, 52, 0x2f5f8f,
                            () => onJokers?.(), 20)
    pool.position.set(SIZE.width / 2 - bw / 2, 452 + bh + 14)
    this.buttons.push({ key: 'ui.button.jokers', button: pool })

    // 챌린지. **조커 풀 아래입니다** — 둘 다 판에 무엇이 들어가는지를 정하는 일이므로
    // 같은 줄에 있어야 하고, 시작만 위에 혼자 남습니다.
    //
    // **열리기 전에는 비활성입니다.** 눌러서 잠긴 것을 알게 하는 것보다, 눌리지 않는 것이
    // 보이고 올렸을 때 무엇으로 열리는지 적히는 쪽이 그 자리에서 끝납니다.
    const dare = new Button(t('ui.button.challenges'), bw, 52, 0x8f5f2f,
                            () => onChallenges?.(), 20)
    dare.position.set(SIZE.width / 2 - bw / 2, 452 + bh + 14 + 52 + 14)
    this.buttons.push({ key: 'ui.button.challenges', button: dare })
    this.challenges = dare
    dare.enabled = false

    // **쪽지는 단추 아래에 가운데로 섭니다.** `show` 가 `x` 를 가운데로 보고 `y` 아래에
    // 16픽셀을 띄우므로, 단추의 가운데와 아랫변을 넘깁니다 — 오른쪽에 두려고 `x` 에
    // 단추의 오른쪽 끝을 넘겼다가 쪽지가 단추를 덮었습니다.
    dare.on('pointerover', () => {
      if (this.locked) {
        this.tip.show(t('ui.button.challenges'), '', 0, [t('ui.challenge.lockedAll')],
                      dare.x + bw / 2, dare.y + 52, SIZE)
      }
    })
    dare.on('pointerout', () => this.tip.hide())

    // 리더보드. **구석의 아이콘이 아니라 줄에 있습니다** — 판 바깥의 설정이 아니라
    // 게임에 딸린 내용이고, 조커 풀과 챌린지가 같은 줄에 있습니다.
    const board = new Button(t('ui.button.leaderboard'), bw, 52, 0x5f2f8f,
                             () => onLeaderboard?.(), 20)
    board.position.set(SIZE.width / 2 - bw / 2, 452 + bh + 14 + (52 + 14) * 2)
    this.buttons.push({ key: 'ui.button.leaderboard', button: board })

    // 덱과 스테이크. **시작의 왼쪽입니다** — 아래 줄에 한 칸 더 두면 구석의 아이콘과
    // 겹치고, 무엇보다 이것은 「무엇으로 시작하는가」이므로 시작에 붙어 있어야 합니다.
    //
    // **단추에 고른 것이 적힙니다.** 「덱」이라고만 적혀 있으면 무엇으로 시작하는지 알려면
    // 판을 한 번 열어 봐야 하고, 그 판을 열지 않고 시작을 누르는 것이 보통입니다.
    const setup = new Button('', 200, 44, 0x2f5f8f, () => onSetup?.(), 16)
    setup.position.set(SIZE.width / 2 - bw / 2 - 16 - 200, 452 + (bh - 44) / 2)
    this.setupButton = setup

    // **랭크 런은 로그인한 사람에게만 보입니다.** 로그아웃 상태의 타이틀이 지금과 같아야
    // 합니다.
    const rankedStart = new Button(t('ui.lb.ranked'), 168, 44, 0x2f8f52,
                                   () => onRanked?.(), 16)
    rankedStart.position.set(SIZE.width / 2 + bw / 2 + 16, 452 + (bh - 44) / 2)
    rankedStart.visible = false
    this.rankedButton = rankedStart
    this.buttons.push({ key: 'ui.lb.ranked', button: rankedStart })

    this.addChild(this.leaf, this.logo, this.tagline, this.note, start, pool, dare,
                  board, setup, rankedStart, guide, option, this.tip)

    this.relabel()

    // 뒤를 눌러도 아무 일도 없습니다. **시작은 눌러서 시작하는 것입니다.**
    this.eventMode = 'static'
    this.on('pointertap', () => undefined)
  }

  private rankedButton?: Button
  private setupButton?: Button

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
   * 로그인 상태를 알립니다.
   *
   * **로그아웃이면 랭크 단추가 없습니다.** 눌러서 로그인 창이 뜨는 것보다, 없는 편이
   * 「게임은 혼자 하는 것」이라는 것과 어긋나지 않습니다.
   */
  setSignedIn(signedIn: boolean): void {
    if (this.rankedButton) this.rankedButton.visible = signedIn
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
