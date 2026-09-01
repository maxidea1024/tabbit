// 왼쪽 패널.
//
// **눈이 여기부터 갑니다.** 블라인드가 무엇을 요구하는지, 지금 점수가 얼마인지, 칩과 배수가
// 얼마인지가 한 덩어리로 붙어 있어야 판단이 됩니다.

import { Container, Graphics, Text } from 'pixi.js'
import { tf } from '../core/strings'

import { NUMERALS } from '../ui/font'
import { box, BOTTOM, inset, putText, splitY } from '../ui/layout'
import { mix, plate, slotStyle } from './skin'
import { COLOR } from './theme'

/** 값 하나가 들어가는 칸. */
/** 이름이 앉는 띠의 높이. 숫자는 그 아래의 남은 자리를 씁니다. */
const CAPTION_H = 22
/**
 * 가장자리에 붙는 숫자가 벽에서 떨어지는 만큼.
 *
 * **넓습니다.** 칩과 배수가 맞닿는 자리에 곱셈표 딱지가 앉으므로, 좁게 두면 숫자가 그
 * 딱지에 닿습니다 — 그 딱지의 절반과 사이의 숨이 이 값입니다.
 */
const VALUE_PAD = 28
/**
 * 이름이 없는 칸의 여백. 칩과 배수가 그렇습니다.
 *
 * **곱셈표가 상자 밖의 빈 자리에 서므로** 숫자가 상자 끝까지 가도 그것에 닿지 않습니다 —
 * 좁을수록 세 개가 한 식으로 읽힙니다.
 */
const BARE_PAD = 12

export class Slot extends Container {
  private readonly plate = new Graphics()
  private readonly caption_ = new Text({
    text: '', style: { fontSize: 10, fill: COLOR.inkDim, fontWeight: '700' },
  })
  private readonly value = new Text({
    text: '0',
    style: {
      fontSize: 23, fill: COLOR.ink, fontWeight: '800', fontFamily: NUMERALS,
      stroke: { color: 0x0a0f18, width: 3 },
    },
  })

  /**
   * 이 칸이 얼마나 타고 있는가. 0..1.
   *
   * **바탕이 비칩니다.** 불은 칸 뒤에 있고 칸은 불투명이라, 그대로 두면 아무것도 보이지
   * 않습니다 — 위로 옮기면 점수 칸을 덮고, 아래로 옮기면 바닥에서 새어 나오는 것으로
   * 보입니다. 타는 것은 이 칸이므로 이 칸이 비쳐야 합니다.
   */
  private burn = 0
  private shown = 0
  private wanted = 0
  private numeric = true
  /** 값이 바뀌었을 때의 튐. **툭 바뀌는 숫자는 아무 느낌도 주지 않습니다.** */
  private pop = 0
  private lastText = ''
  /** 이미 조용한 모습으로 돌려놓았는가. 매 프레임 다시 그리지 않기 위한 것입니다. */
  private settledLook = true

  /**
   * 값이 놓이는 가로 자리. 0 이면 왼쪽, 0.5 면 가운데, 1 이면 오른쪽입니다.
   *
   * **칩과 배수는 가운데가 아닙니다.** 둘 사이에 곱셈표가 있으므로 칩은 오른쪽으로,
   * 배수는 왼쪽으로 붙어야 세 개가 한 식으로 읽힙니다 — 가운데로 두면 자릿수가 늘어날
   * 때마다 곱셈표와의 사이가 벌어졌다 좁아집니다.
   */
  private readonly pull: number

  /**
   * 자기 판을 그리지 않는가.
   *
   * **칩과 배수는 한 덩어리입니다.** 둘이 각자 테두리를 두르면 그 사이에 곱셈표가 어디에도
   * 속하지 않은 채로 걸치고, 불은 테두리 밖으로 새어 나옵니다 — 바탕은 화면이 통째로
   * 그리고, 이 칸은 이름과 숫자만 얹습니다.
   */
  private readonly bare: boolean

  constructor(caption: string, private readonly boxWidth: number,
              private readonly boxHeight: number, private readonly ink: number,
              valueSize = 23, pull = 0.5, bare = false) {
    super()
    this.pull = pull
    this.bare = bare
    this.value.style.fontSize = valueSize
    this.addChild(this.plate, this.caption_, this.value)
    this.caption_.text = caption
    // 이름은 위 가운데, 숫자는 그 아래의 남은 자리에. **기울기는 `pull` 이 정합니다** —
    // 칩은 오른쪽으로, 배수는 왼쪽으로 붙습니다.
    //
    // **이름이 없는 칸도 있습니다.** 칩과 배수가 그렇습니다 — 그 둘은 색과 자리로 이미
    // 갈리므로 이름이 자리만 잡아먹고, 그러면 숫자가 칸 아래로 밀려납니다.
    const inner = box(0, 0, boxWidth, boxHeight)
    const named = caption !== ''
    this.caption_.visible = named
    const [head, rest] = splitY(inner, [CAPTION_H, boxHeight - CAPTION_H])
    const body = named ? rest : inner
    if (named) putText(this.caption_, head, BOTTOM, { y: -2 })
    // **이름이 없으면 여백이 좁아도 됩니다.** 곱셈표가 상자 밖의 빈 자리에 서므로 숫자가
    // 그것에 닿지 않습니다.
    putText(this.value, inset(body, 0, named ? VALUE_PAD : BARE_PAD), { x: pull, y: 0.5 })
    this.baseY = this.value.y
    this.value.style.fill = ink
    this.draw()
  }

  /**
   * 숫자가 쉬는 자리.
   *
   * **한 번 세고 그것을 지킵니다.** 떨리는 동안에도 이 자리를 기준으로 흔들리므로, 두 칸의
   * 숫자가 같은 높이에서 흔들립니다 — 칸마다 다시 세면 그 둘의 기준선이 어긋납니다.
   */
  private baseY = 0
  private get valueX(): number {
    // **이름이 있는 칸과 없는 칸의 여백이 다릅니다.** 28은 이름이 붙은 칸의 여백인데
    // 여기서 한 값만 쓰고 있었고, 그래서 칩과 배수의 숫자가 곱셈표 쪽에서 28픽셀 물러나
    // 있었습니다 — 한 자리일 때는 칸 가운데에 있는 것으로 보이고, 자릿수가 늘면 그만큼
    // **반대쪽으로 넘칩니다.** 붙어야 할 쪽이 곱셈표 쪽이므로 여백은 좁아야 합니다.
    const pad = this.bare ? BARE_PAD : VALUE_PAD
    return this.boxWidth * this.pull + (this.pull <= 0 ? pad : this.pull >= 1 ? -pad : 0)
  }

  private draw(glow = 0): void {
    if (this.bare) return
    const style = slotStyle(this.ink)
    this.plate.clear()
    plate(this.plate, this.boxWidth, this.boxHeight, {
      ...style,
      top: glow > 0 ? mix(style.top, this.ink, glow * 0.35) : style.top,
      weight: 1.5 + glow * 2 + this.burn * 1.5,
    })
    this.plate.alpha = 1 - this.burn * 0.82
  }

  /** 칸의 이름. **말이 바뀌면 갈아 끼웁니다** — 만들 때 한 번 읽고 마는 글입니다. */
  set caption(value: string) {
    this.caption_.text = value
  }

  /** 이 칸이 타오르는 세기. 바탕이 그만큼 비칩니다. */
  set heat(value: number) {
    const next = Math.max(0, Math.min(1, value))
    if (Math.abs(next - this.burn) < 0.01) return
    this.burn = next
    this.draw()
    this.settledLook = false
  }

  /** 숫자가 아닌 값. 바뀌면 한 번 튑니다. */
  set text(value: string) {
    this.numeric = false
    if (this.value.text !== value) {
      if (this.lastText !== '') this.pop = 1
      this.lastText = value
    }
    this.value.text = value
  }

  reset(value: number): void {
    this.numeric = true
    this.shown = value
    this.wanted = value
    this.redraw()
  }

  set target(value: number) {
    this.numeric = true
    if (value !== this.wanted) this.pop = Math.min(1, Math.abs(value - this.shown) / 400 + 0.35)
    this.wanted = value
  }

  get settled(): boolean { return this.shown === this.wanted }

  /**
   * 굴러가는 정도. 0 이면 다 왔고 1 이면 아직 멉니다.
   *
   * **소리가 이것을 따라 오릅니다.** 숫자가 굴러가는 동안 무음이면 그 1초가 비고, 그
   * 1초가 이 게임에서 가장 중요한 순간입니다.
   */
  get rolling(): number {
    if (!this.numeric || this.shown === this.wanted) return 0
    return Math.min(1, Math.abs(this.wanted - this.shown) / 400)
  }

  /**
   * 남은 거리의 일부씩 좁힙니다. 큰 수일수록 오래 굴러갑니다.
   *
   * **굴러가는 동안 숫자가 떱니다.** 값이 매끄럽게 올라가기만 하면 「바뀌었다」로 읽히고,
   * 흔들리면서 올라가면 「쌓이고 있다」로 읽힙니다. 흔드는 세기는 남은 거리에 따릅니다 —
   * 큰 수가 굴러갈 때 크게 떨고, 다 굴러가면 조용히 제자리에 섭니다.
   */
  advance(deltaMs: number): void {
    const rolling = this.numeric && this.shown !== this.wanted
    const heat = rolling
      ? Math.min(1, Math.abs(this.wanted - this.shown) / 240 + 0.4)
      : 0

    if (this.pop > 0) this.pop = Math.max(0, this.pop - deltaMs / 260)

    const ease = this.pop * this.pop
    const shake = Math.max(heat, ease)
    if (shake > 0.002) {
      // 튀는 것과 떠는 것을 같이 얹습니다.
      this.value.scale.set(1 + ease * 0.42 + heat * 0.14)
      this.value.x = this.valueX + (Math.random() - 0.5) * 7 * shake
      // **세로로는 조금만 흔듭니다.** 두 칸의 숫자가 나란히 서 있어서, 세로로 크게 흔들면
      // 그 둘의 기준선이 서로 어긋나 보입니다.
      this.value.y = this.baseY - ease * 4 + (Math.random() - 0.5) * 2.4 * shake
      this.value.rotation = (Math.random() - 0.5) * 0.13 * shake
      this.draw(Math.min(1, shake))
    } else if (this.settledLook !== true) {
      this.settledLook = true
      this.value.scale.set(1)
      this.value.position.set(this.valueX, this.baseY)
      this.value.rotation = 0
      this.draw()
    }
    if (shake > 0.002) this.settledLook = false

    if (!rolling) return
    const gap = this.wanted - this.shown
    const step = Math.max(1, Math.abs(gap) * (deltaMs / 130))
    this.shown = gap > 0
      ? Math.min(this.wanted, this.shown + step)
      : Math.max(this.wanted, this.shown - step)
    this.redraw()
  }

  /** 값이 클수록 크게, 그리고 테두리가 밝아집니다. */
  emphasize(scale: number): void {
    if (this.pop > 0) return
    this.value.scale.set(scale)
    this.draw(Math.max(0, Math.min(1, (scale - 1) * 2)))
  }

  private redraw(): void {
    const shown = Math.round(this.shown)
    this.value.text = shown >= 1_000_000
      ? shown.toExponential(2).replace('e+', 'e')
      : shown.toLocaleString('en-US')
  }
}

/**
 * 블라인드 하나의 딱지.
 *
 * **색이 어느 블라인드인지 말합니다** — 스몰은 파랑, 빅은 보라, 보스는 붉습니다. 이름을 읽지
 * 않아도 어디까지 왔는지가 보입니다.
 */
export class BlindBadge extends Container {
  private readonly plate = new Graphics()
  private readonly title = new Text({
    text: '', style: { fontSize: 17, fill: COLOR.ink, fontWeight: '800' },
  })
  private readonly need = new Text({
    text: '',
    style: {
      fontSize: 30, fill: COLOR.chips, fontWeight: '800', fontFamily: NUMERALS,
      stroke: { color: 0x0a0f18, width: 4 },
    },
  })
  private readonly note = new Text({
    text: '',
    style: {
      fontSize: 11, fill: COLOR.inkDim, lineHeight: 15,
      wordWrap: true, wordWrapWidth: 234, breakWords: true,
    },
  })
  private readonly reward = new Text({
    text: '', style: { fontSize: 13, fill: COLOR.money, fontWeight: '700' },
  })

  /**
   * 이름 옆에 붙는 표시.
   *
   * **보스에만 붙습니다.** 스물여덟이 이름 하나로만 갈리면 어느 것과 붙고 있는지가 판이
   * 도는 내내 이름 한 줄에만 남습니다. 무엇을 그릴지는 화면이 정하고 이 클래스는 자리만
   * 냅니다 — 그림이 어디서 오는지를 여기가 알 이유가 없습니다.
   */
  private seal?: Container

  constructor(private readonly boxWidth: number) {
    super()
    this.addChild(this.plate, this.title, this.need, this.reward, this.note)
  }

  /**
   * 이 딱지가 지금 얼마나 높은가. **들고 있는 태그가 늘면 자랍니다.**
   *
   * 아래에 무엇을 둘 자리를 세는 쪽이 알아야 합니다 — 화면이 같은 계산을 베껴 적으면
   * 여기를 고칠 때 그쪽만 남습니다.
   */
  boxHeight = 138

  set(name: string, target: number, reward: number, note: string,
      boss: boolean, big = false, seal?: Container, tags: Container[] = []): void {
    // **딱지는 자라지 않습니다.**
    //
    // 두 가지가 키우고 있었습니다 — 들고 있는 태그를 아래에 한 줄로 세운 것과, 보스의
    // 규칙 한 줄이 있을 때만 26픽셀을 더한 것입니다. 태그는 머리띠의 오른쪽 끝으로
    // 옮겼고(`setTags`), 규칙의 자리는 규칙이 없을 때도 비워 둡니다.
    //
    // **비워 두는 자리가 아까운 것보다 흔들리는 것이 나쁩니다.** 이 딱지는 왼쪽 판의 맨
    // 위이고, 높이가 바뀌면 그 아래가 전부 따라 움직입니다 — 블라인드를 넘길 때마다 판이
    // 한 번씩 출렁이던 것이 그것입니다.
    const height = 164
    this.boxHeight = height
    const tint = boss ? 0x3d1622 : big ? 0x2a2140 : 0x1b2c44
    const edge = boss ? COLOR.bad : big ? 0xa279e0 : 0x5d92d6

    this.plate.clear()
    plate(this.plate, this.boxWidth, height, {
      top: mix(tint, 0xffffff, 0.14), bottom: mix(tint, 0x000000, 0.3),
      border: edge, radius: 12, weight: 2, drop: 5, gloss: 0.24,
    })
    // 이름이 앉는 띠.
    this.plate.roundRect(6, 6, this.boxWidth - 12, 32, 8)
      .fill({ color: edge, alpha: 0.28 })

    // 앞의 표시를 걷고 새것을 답니다. **그대로 두면 보스가 바뀌어도 앞의 것이 남습니다.**
    this.seal?.destroy()
    this.seal = undefined

    // **이름은 언제나 가운데입니다.** 인장이 붙으면 그만큼 오른쪽으로 비켜세웠는데,
    // 그러면 보스일 때만 이름이 다른 자리에 있습니다 — 띠에 얹히는 것들은 이름의 옆에
    // 서는 것이 아니라 띠의 양 끝에 서는 것이고, 이름은 그것과 무관하게 띠의 가운데입니다.
    this.title.text = name
    this.title.anchor.set(0.5, 0)
    this.title.position.set(this.boxWidth / 2, 12)

    if (seal) {
      this.seal = seal
      seal.position.set(24, 22)
      this.addChild(seal)
    }

    this.need.text = target.toLocaleString('en-US')
    this.need.anchor.set(0.5, 0)
    this.need.position.set(this.boxWidth / 2, 52)

    this.reward.text = tf('ui.blind.reward', { n: reward })
    this.reward.anchor.set(0.5, 0)
    this.reward.position.set(this.boxWidth / 2, 92)

    this.note.text = note
    this.note.anchor.set(0.5, 0)
    this.note.position.set(this.boxWidth / 2, 116)

    this.setTags(tags)
  }

  /**
   * 들고 있는 태그만 갈아 끼웁니다.
   *
   * **딱지 전체와 갈라 두었습니다.** 딱지는 연출이 도는 동안 건드리지 않습니다 — 득점이
   * 끝나기 전에 다음 블라인드의 이름이 뜨면 순서가 뒤집히기 때문입니다. 그런데 태그는 그
   * 연출 안에서 들어오므로, 함께 묶어 두면 **딱지가 언제나 한 번씩 뒤처집니다** — 첫
   * 스킵의 태그가 보이지 않고 다음 스킵에서야 그 앞의 것이 뜨던 것이 그것입니다.
   */
  setTags(tags: Container[]): void {
    const chips = 26

    // 앞의 태그를 걷고 새것을 답니다. **그대로 두면 쓰인 태그가 띠에 남습니다.**
    for (const one of this.tags) one.destroy()
    this.tags.length = 0

    // 머리띠의 오른쪽 끝에서 왼쪽으로 쌓습니다. 새로 받은 것이 바깥쪽입니다.
    const gap = 4
    let x = this.boxWidth - 10 - chips
    for (const one of tags) {
      // **피벗만큼 되돌립니다.** 발동할 때 가운데를 기준으로 부풀리려고 피벗을 옮기는데,
      // 자리를 그대로 두면 그 옮긴 만큼 왼쪽 위로 밀립니다 — 발동이 끝나 피벗이 돌아오면
      // 다시 제자리로 튀고, 그것이 「안착했다가 한 번 튄다」로 보입니다.
      one.position.set(x + one.pivot.x, 22 - chips / 2 + one.pivot.y)
      x -= chips + gap
      this.addChild(one)
      this.tags.push(one)
    }
  }

  /** 지금 달려 있는 태그 칩들. 다시 그릴 때 걷습니다. */
  private readonly tags: Container[] = []

  /** 지금 띠에 몇 개를 그려 두었는가. 재는 쪽이 상태와 견주는 값입니다. */
  get chipCount(): number {
    return this.tags.length
  }
}
