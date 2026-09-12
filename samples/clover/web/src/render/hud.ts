// 왼쪽 패널.
//
// **눈이 여기부터 갑니다.** 블라인드가 무엇을 요구하는지, 지금 점수가 얼마인지, 칩과 배수가
// 얼마인지가 한 덩어리로 붙어 있어야 판단이 됩니다.

import { Container, Graphics, Text, TextStyle } from 'pixi.js'
import { t, tf } from '../core/strings'

import { NUMERALS, outline, outlined, outlineOf, outlineWidth } from '../ui/font'
import { type Anchor, type Box, box, BOTTOM, inset, pointOf, putText, splitY }
  from '../ui/layout'
import { richLeading, richStyle, richBlock, rowsOf, type RichStyle } from '../ui/rich'
import { mix, plate, slotStyle, wellTint } from './skin'
import { PAINT } from './ink'
import { piece } from '../ui/chrome'
import { Spring } from './motion'
import { UI, TEXT, WEIGHT, STEP } from './theme'

/** 값 하나가 들어가는 칸. */
/** 이름이 앉는 띠의 높이. 숫자는 그 아래의 남은 자리를 씁니다. */
const CAPTION_H = 22

/** 딱지의 머리 판 높이. 이름(24)이 앉는 띠입니다. */
const HEAD_H = 48
/** 딱지의 높이. 머리 판과 요구 점수 자리입니다 — 자라지 않습니다. */
const BADGE_H = 212
/**
 * 한 줄 칸의 좌우 여백. 이름은 왼쪽 끝, 값은 오른쪽 끝에서 이만큼 들어옵니다.
 */
const ROW_PAD = 12

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
/**
 * 숫자가 물러났다 돌아오는 데 걸리는 시간.
 *
 * **±N 글이 앉아 있는 동안에 떠오르기 시작하는 동안까지입니다.**
 *
 * 그 글은 `DELTA_LIFE`(0.8초) 의 앞 0.4 를 값의 자리에 앉아 있다가 떠오릅니다. 앉아 있는
 * 동안(320밀리초)에 딱 맞춰 두었더니, 돌아오는 것이 그 글이 아직 제자리에 있는 동안에
 * 끝나 마지막 짧은 동안 둘이 겹쳤습니다 — 떠오르는 것은 이차 곡선이라 앞부분이 느리고,
 * 320밀리초 시점의 그 글은 1픽셀도 올라가 있지 않습니다.
 *
 * 620밀리초면 돌아오기 시작할 때 그 글이 5픽셀 올라가 있고 다 돌아왔을 때 13픽셀에
 * 옅어지는 중입니다.
 */
const MUTE_MS = 620

/**
 * 오른 것과 내린 것의 색.
 *
 * **바탕색이 그 방향을 나타냅니다.** 칸의 숫자는 언제나 지금 값이고 ±N 글은 0.8초 뒤에
 * 사라지므로, 그 글을 놓치면 무엇이 늘었고 무엇이 줄었는지가 남지 않습니다 — 판때기가
 * 초록으로 밝았는가 붉게 밝았는가는 눈 구석으로도 읽힙니다.
 */
const UP_INK = UI.good
const DOWN_INK = UI.bad

/** 글자 하나가 튀었다 앉는 데 걸리는 시간. */
const WAVE_MS = 300
/** 옆 글자가 뒤따르기까지의 사이. **글자 수가 늘면 그만큼 물결이 깁니다.** */
const WAVE_STAGGER_MS = 34
/** 튀어오르는 높이와 부푸는 정도. */
const WAVE_LIFT = 12
const WAVE_SWELL = 0.35
/**
 * 글자별로 세우는 수에서 글자끼리 겹치는 만큼. 글자 크기의 몫입니다.
 *
 * **두른 테의 굵기입니다.** 재는 값에 테가 양쪽으로 들어 있어 그대로 붙이면 사이가 그만큼
 * 벌어지고, 테는 획 밖으로 나간 것이므로 이만큼 겹쳐도 획끼리는 닿지 않습니다. 라틴·숫자의
 * 테가 크기의 0.075이므로 그 값입니다.
 */
const TUCK_RATIO = 0.075
/**
 * 값이 바뀐 뒤 바탕이 밝은 동안.
 *
 * **튐과 따로입니다.** 튐의 세기는 얼마나 크게 바뀌었는가를 따르므로, 그것으로 바탕을
 * 밝히면 조금 바뀐 값은 색이 거의 들지 않습니다 — 바뀌었다는 것은 크기와 무관하게
 * 같은 세기로 보여야 합니다. 글자 물결이 도는 동안과 대략 같은 길이입니다.
 */
const FLARE_MS = 420
/**
 * 값이 더해진 것이 잦아드는 데 걸리는 시간.
 *
 * **`FLARE_MS` 와 같은 값이고 하는 일이 다릅니다.** 그것은 바탕의 번쩍임이고 이것은 파형의
 * 세기입니다 — 하나로 묶으면 둘 중 하나를 고칠 수 없게 됩니다.
 */
const SURGE_MS = 420
/**
 * 박자에 얹힌 크기가 잦아드는 데 걸리는 시간.
 *
 * **박자의 간격보다 깁니다.** 짧으면 박자와 박자 사이에서 숫자가 한 번씩 제 크기로
 * 돌아와, 커지는 것이 이어지지 않고 매번 끊깁니다.
 */
const EMPHASIS_MS = 220

/**
 * 글자 폭. **글자 하나와 크기 하나마다 한 번만 잽니다.**
 *
 * 재는 것은 글자를 캔버스에 구워 그 넓이를 읽는 일입니다 — 수가 굴러가는 동안 매 단계
 * 다시 재면 그 값이 초당 60번입니다.
 */
const GLYPH_W = new Map<string, number>()

/**
 * 천 단위를 끊는 것.
 *
 * **한 번 만들어 둡니다.** `Number.prototype.toLocaleString` 은 부를 때마다 Intl 을
 * 거치는데, 점수가 굴러가는 동안 칸마다 초당 60번입니다.
 *
 * **말과 무관하게 `en-US` 입니다.** 숫자의 자리 표기가 말을 따라가면 같은 판의 점수가
 * 사람마다 다르게 적히고, 이 게임의 수는 자릿수를 세는 수입니다.
 */
const COMMAS = new Intl.NumberFormat('en-US')

/**
 * 수 하나를 글자별로 세우는 통.
 *
 * **글자마다 따로 움직여야 할 때만 씁니다.** `Text` 하나는 통째로만 움직이므로, 왼쪽
 * 글자부터 차례로 튀어오르는 물결을 만들 수 없습니다.
 *
 * **글자는 만들고 버리지 않습니다.** 수가 굴러가는 동안 자릿수가 늘었다 줄었다 하므로,
 * 그때마다 만들면 만드는 값이 보여 주는 값보다 커집니다 — 남는 것은 숨겨 둡니다.
 */
class Digits extends Container {
  private readonly glyphs: Text[] = []
  private shown = ''
  /**
   * 넘지 않아야 하는 너비. 0 이면 재지 않습니다.
   *
   * 칸이 이것을 넣습니다 — 통은 자기가 어느 칸에 앉아 있는지 모릅니다.
   */
  private room = 0
  /**
   * 칸에 들어가려고 줄인 배율. 1 이면 그대로입니다.
   *
   * **여기서 `scale` 을 만지지 않습니다.** 값이 바뀔 때의 튐도 같은 `scale` 을 쓰므로, 둘이
   * 저마다 적으면 나중에 적은 것이 앞의 것을 지웁니다 — 칸이 둘을 곱해 넣습니다.
   */
  fitScale = 1

  set fit(width: number) {
    if (width === this.room) return
    this.room = width
    this.relay()
  }
  /**
   * 물결이 돈 지 지난 시간. 음수면 돌고 있지 않습니다.
   *
   * **끝난 뒤에는 음수로 되돌립니다.** 그러지 않으면 다 앉은 글자들의 자리를 매 프레임
   * 다시 셉니다.
   */
  private life = -1
  private fromLeft = true

  /**
   * @param style 글자 전부가 나눠 쓰는 모습. **하나입니다** — 크기나 색을 고치면 글자
   *   전부가 함께 바뀌어야 하고, 글자마다 사본을 두면 그중 하나만 남습니다.
   * @param pull 0 이면 왼쪽 끝에서 오른쪽으로 자라고, 1 이면 오른쪽 끝에서 왼쪽으로
   *   자랍니다. `Text` 의 `anchor.x` 와 같은 뜻입니다.
   */
  constructor(readonly style: TextStyle, private readonly pull: number) {
    super()
  }

  get text(): string { return this.shown }

  /** 글자 하나의 모습. **채움만 흰색이고 나머지는 나눠 쓰는 모습 그대로입니다.** */
  private glyphStyle(): TextStyle {
    const own = this.style.clone()
    own.fill = PAINT.sheen
    return own
  }

  set text(value: string) {
    if (value === this.shown) return
    this.shown = value
    this.relay()
  }

  /**
   * 물결을 한 번 보냅니다.
   *
   * **바깥쪽 끝에서 시작해 곱셈표 쪽으로 갑니다.** 어느 쪽이 바깥인지는 `pull` 이 이미
   * 알고 있습니다 — 오른쪽 끝에 붙은 수(칩)의 바깥은 왼쪽입니다.
   */
  wave(): void {
    this.life = 0
    this.fromLeft = this.pull >= 0.5
  }

  /** 물결을 한 단계 진행합니다. 돌고 있지 않으면 아무것도 하지 않습니다. */
  advance(deltaMs: number): void {
    if (this.life < 0) return
    this.life += deltaMs

    const count = this.shown.length
    const span = WAVE_MS + Math.max(0, count - 1) * WAVE_STAGGER_MS
    const done = this.life >= span
    for (let i = 0; i < count; i++) {
      const glyph = this.glyphs[i]
      if (!glyph) continue
      if (done) {
        glyph.y = 0
        glyph.scale.set(1)
        continue
      }
      const order = this.fromLeft ? i : count - 1 - i
      const at = (this.life - order * WAVE_STAGGER_MS) / WAVE_MS
      if (at <= 0 || at >= 1) {
        glyph.y = 0
        glyph.scale.set(1)
        continue
      }
      // 올라갔다 내려옵니다. 한가운데가 가장 높습니다. **그 글자에 흰빛이 지나갑니다** —
      // 제 색에서 흰색으로 갔다가 제 색으로 돌아옵니다. 글자의 채움은 흰색이고 색은
      // `tint` 로 얹으므로 글자마다 다른 색을 줄 수 있습니다.
      const bounce = Math.sin(at * Math.PI)
      glyph.y = -WAVE_LIFT * bounce
      glyph.scale.set(1 + WAVE_SWELL * bounce)
      glyph.tint = mix(this.ink, PAINT.sheen, bounce)
    }
    if (done) {
      this.life = -1
      for (const glyph of this.glyphs) glyph.tint = this.ink
    }
  }

  /**
   * 글자의 색.
   *
   * **채움은 흰색이고 색은 `tint` 입니다.** 물결이 지나가는 동안 글자마다 다른 색이어야
   * 하는데, 채움은 글자 전부가 나눠 쓰는 모습 하나에 있습니다.
   */
  private ink: number = PAINT.sheen

  /** 글자들의 색을 바꿉니다. 물결이 돌고 있지 않으면 곧바로 듭니다. */
  recolor(ink: number): void {
    this.ink = ink
    if (this.life < 0) for (const glyph of this.glyphs) glyph.tint = ink
  }

  /**
   * 글자들을 다시 세웁니다.
   *
   * **바뀐 글자에만 새 글을 넣습니다.** 넣는 순간 그 글자가 캔버스에 다시 구워지므로,
   * 「1,234」 가 「1,235」 가 될 때 다섯 번 굽는 것과 한 번 굽는 것의 차이입니다.
   */
  private relay(): void {
    const size = this.style.fontSize as number
    let total = 0
    const widths: number[] = []
    for (let i = 0; i < this.shown.length; i++) {
      const ch = this.shown[i]
      let glyph = this.glyphs[i]
      if (!glyph) {
        glyph = new Text({ text: ch, style: this.glyphStyle() })
        glyph.anchor.set(0.5, 0.5)
        glyph.tint = this.ink
        this.glyphs.push(glyph)
        this.addChild(glyph)
      } else if (glyph.style.fontSize !== this.style.fontSize) {
        glyph.style.fontSize = this.style.fontSize
      }
      glyph.visible = true
      if (glyph.text !== ch) glyph.text = ch
      const key = `${ch}|${size}`
      let width = GLYPH_W.get(key)
      if (width === undefined) {
        width = glyph.width
        GLYPH_W.set(key, width)
      }
      widths.push(width)
      total += width
    }
    for (let i = this.shown.length; i < this.glyphs.length; i++) {
      this.glyphs[i].visible = false
    }

    // **잰 너비끼리 붙이면 사이가 넓습니다.**
    //
    // 재는 값은 그 글자를 캔버스에 구운 그림의 너비이고, 그 그림에는 **두른 테가 양쪽으로
    // 들어 있습니다** — 34픽셀 숫자의 테가 2.55픽셀이므로 글자마다 5픽셀이 붙어 있고, 그
    // 둘을 나란히 두면 사이가 5픽셀 벌어집니다. 글 하나로 그리는 칸에는 이 일이 없습니다 —
    // 글꼴이 정한 자리에 붙고 테는 그 위에 덧그려집니다.
    //
    // 테의 굵기만큼 겹쳐 놓습니다. 테는 글자의 획 밖으로 나간 것이므로 그만큼 겹쳐도
    // 획끼리는 닿지 않습니다.
    const tuck = size * TUCK_RATIO
    const span = Math.max(0, total - tuck * Math.max(0, widths.length - 1))

    // **칸을 넘으면 통째로 줄입니다.** 넘은 만큼만 잘려 보이면 그것은 자릿수가 사라진
    // 것이고, 몇 자리인지가 이 게임에서 가장 중요한 수입니다.
    this.fitScale = this.room > 0 && span > this.room ? this.room / span : 1

    // **자라는 방향이 `pull` 입니다.** 오른쪽 끝에 붙는 수는 통째로 왼쪽으로 물러섭니다.
    let at = -span * this.pull
    for (let i = 0; i < widths.length; i++) {
      this.glyphs[i].x = at + widths[i] / 2
      at += widths[i] - tuck
    }
  }
}

export class Slot extends Container {
  private readonly plate = new Graphics()
  /** 구워 둔 칸. 없으면 `plate` 가 그립니다. */
  private skin?: Container
  private readonly caption_ = new Text({
    text: '', style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
  })
  /**
   * 값이 앉는 것.
   *
   * **글자별로 움직여야 하는 칸만 통입니다.** 나머지는 `Text` 하나이고, 그 편이 굽는
   * 것도 재는 것도 한 번입니다 — 통은 글자 수만큼입니다.
   */
  private readonly value: Text | Digits
  /** 값의 모습. **통이든 글 하나든 이것 하나를 씁니다.** */
  private readonly valueStyle: TextStyle

  private shown = 0
  private wanted = 0
  private numeric = true
  /** 값이 바뀌었을 때의 튐. **툭 바뀌는 숫자는 아무 느낌도 주지 않습니다.** */
  private pop = 0
  private lastText = ''
  /** 이미 조용한 모습으로 돌려놓았는가. 매 프레임 다시 그리지 않기 위한 것입니다. */
  private settledLook = true
  /** 마지막으로 그린 판때기의 모습. 같으면 다시 그리지 않습니다. */
  /** 마지막으로 그린 판때기의 빛과 색. **모습이 같으면 다시 그리지 않습니다.** */
  private plateStep = -1
  private plateInk = -1

  /**
   * 바탕이 오르내림을 따라 물드는가.
   *
   * **자원 칸만 그렇습니다.** 핸드 · 버리기 · 소지금 · 안티는 늘었는지 줄었는지가 곧
   * 좋은 소식인지 나쁜 소식인지입니다. 라운드 득점은 오르기만 하므로 그 색이 아무것도
   * 가르지 않고, 매 판 초록으로 밝으면 그것이 판의 기본 모습이 됩니다.
   */
  signed = false

  /**
   * 값이 줄어드는 동안은 들뜨지 않는가.
   *
   * **칩과 배수가 그렇습니다.** 그 둘은 한 판에서 쌓이기만 하고, 줄어드는 것은 판이 끝나
   * 0으로 되돌아가는 것뿐입니다 — 그것은 알릴 일이 아닌데도 바탕이 밝고 숫자가 떨어서,
   * 판이 끝날 때마다 무언가 일어난 것으로 보였습니다.
   */
  quietOnDrop = false

  /**
   * 값이 움직이는 동안 판때기가 밝아지는가.
   *
   * **자원 칸은 밝아지지 않습니다.** 핸드 · 버리기 · 금액 · 안티는 늘고 주는 것이 ±N 글로
   * 적히므로 그것으로 충분하고, 넷이 번갈아 밝으면 밝은 것이 무엇도 가리지 않습니다.
   *
   * `signed` 와 다릅니다 — 그것은 밝아지는 **색**을 오르내림으로 정하는 것이라, 그것만
   * 걷으면 색이 빠지고 밝기는 그대로 남습니다.
   */
  plateGlow = true

  /**
   * 지금 번쩍임에 쓸 색. **기본은 칸의 색입니다.**
   *
   * `signed` 인 칸에서는 오르내림이 이것을 갈아 끼웁니다.
   */
  private glowInk: number

  /**
   * 값이 놓이는 가로 자리. 0 이면 왼쪽, 0.5 면 가운데, 1 이면 오른쪽입니다.
   *
   * **칩과 배수는 가운데가 아닙니다.** 둘 사이에 곱셈표가 있으므로 칩은 오른쪽으로,
   * 배수는 왼쪽으로 붙어야 세 개가 한 식으로 읽힙니다 — 가운데로 두면 자릿수가 늘어날
   * 때마다 곱셈표와의 사이가 벌어졌다 좁아집니다.
   */
  private pull: number

  /**
   * 이름과 값이 한 줄에 있는가.
   *
   * **이름 있는 칸은 한 줄입니다.** 이름을 위에 얹고 숫자를 그 아래에 두면 칸의 절반이
   * 이름 자리가 되어 숫자가 작아지고, 칸마다 색 테를 둘러 무엇의 값인지를 알리던 것도
   * 그 배치의 산물입니다 — 이름은 왼쪽, 값은 오른쪽 한 줄이면 테는 하나로 족합니다.
   */
  private readonly row: boolean

  /**
   * 자기 판을 그리지 않는가.
   *
   * **칩과 배수는 한 덩어리입니다.** 둘이 각자 테두리를 두르면 그 사이에 곱셈표가 어디에도
   * 속하지 않은 채로 걸칩니다 — 바탕은 화면이 통째로 그리고, 이 칸은 이름과 숫자만 얹습니다.
   */
  private readonly bare: boolean

  constructor(caption: string, private readonly boxWidth: number,
              private readonly boxHeight: number, private readonly ink: number,
              valueSize = 24, pull = 0.5, bare = false, wave = false) {
    super()
    this.pull = pull
    this.bare = bare
    this.glowInk = ink
    // **칸마다 숫자 크기가 다릅니다.** 테두리의 굵기는 크기에서 나오는 값이므로 크기와
    // 함께 정합니다.
    this.valueStyle = new TextStyle({
      ...outlined(valueSize, UI.outline, true),
      fill: UI.ink, fontWeight: WEIGHT.bold, fontFamily: NUMERALS,
    })
    this.valueStyle.fontSize = valueSize
    this.valueStyle.stroke = outline(valueSize, UI.outline, true)
    this.value = wave
      ? new Digits(this.valueStyle, pull)
      : new Text({ text: '0', style: this.valueStyle })
    this.addChild(this.plate, this.caption_, this.value)
    // **칸은 구워 둔 그림입니다.** 판 안으로 눌린 자리 — 위 안쪽의 그늘과 아래의 밝은
    // 줄이 그 안에 있습니다. 그림이 없으면 지금까지의 길로 그립니다.
    if (!bare) {
      this.skin = piece('well', boxWidth, boxHeight, wellTint(UI.cell))
      if (this.skin !== undefined) this.addChildAt(this.skin, 0)
    }
    this.caption_.text = caption
    // 이름은 위 가운데, 숫자는 그 아래의 남은 자리에. **기울기는 `pull` 이 정합니다** —
    // 칩은 오른쪽으로, 배수는 왼쪽으로 붙습니다.
    //
    // **이름이 없는 칸도 있습니다.** 칩과 배수가 그렇습니다 — 그 둘은 색과 자리로 이미
    // 갈리므로 이름이 자리만 잡아먹고, 그러면 숫자가 칸 아래로 밀려납니다.
    const inner = box(0, 0, boxWidth, boxHeight)
    const named = caption !== ''
    this.caption_.visible = named
    this.row = named && !bare
    // **한 줄 칸의 숫자에는 테를 두르지 않습니다.** 칸의 어두운 바탕 위에 있으므로 테가
    // 할 일이 없고, 테를 두르면 12픽셀 이름 옆에서 숫자만 굵어 보입니다 — 테는 색 상자
    // 위에 앉는 칩과 배수에만 남습니다.
    this.valueStyle.stroke = outlineOf(
      this.row ? 0 : outlineWidth(valueSize, true), UI.outline)
    if (this.row) {
      // 이름은 왼쪽, 값은 오른쪽. **값은 오른쪽 끝에 붙으므로 `pull` 이 1 입니다** — ±N
      // 글이 같은 자리에 서려면 그 기준이 같아야 합니다.
      this.pull = 1
      const line = inset(inner, 0, ROW_PAD)
      putText(this.caption_, line, { x: 0, y: 0.5 })
      this.putValue(line, { x: 1, y: 0.5 })
    } else {
      const [head, rest] = splitY(inner, [CAPTION_H, boxHeight - CAPTION_H])
      const body = named ? rest : inner
      if (named) putText(this.caption_, head, BOTTOM, { y: -2 })
      // **이름이 없으면 여백이 좁아도 됩니다.** 곱셈표가 상자 밖의 빈 자리에 서므로 숫자가
      // 그것에 닿지 않습니다.
      const body2 = inset(body, 0, BARE_PAD)
      this.putValue(body2, { x: pull, y: 0.5 })
      // **칸을 넘으면 통째로 줄입니다.** 칩이 여섯 자리가 되면 34픽셀 숫자가 상자보다
      // 넓어지고, 지금까지는 그만큼 상자 밖으로 나가 있었습니다 — 넘은 자리가 잘려 보이면
      // 그것은 자릿수가 사라진 것이고, 몇 자리인지가 이 게임에서 가장 중요한 수입니다.
      if (this.value instanceof Digits) this.value.fit = body2.width
    }
    this.baseY = this.value.y
    this.valueStyle.fill = ink
    if (this.value instanceof Digits) this.value.recolor(ink)
    this.draw()
  }

  /**
   * 값을 사각형 안의 한 자리에 붙입니다.
   *
   * **`putText` 를 대신합니다.** 그것은 `Text` 의 `anchor` 를 만지는데, 글자별 통에는
   * 그런 것이 없습니다 — 통은 만들 때 받은 `pull` 로 스스로 자라는 방향을 압니다.
   */
  private putValue(area: Box, at: Anchor): void {
    if (this.value instanceof Text) this.value.anchor.set(at.x, at.y)
    const spot = pointOf(area, at)
    this.value.position.set(spot.x, spot.y)
  }

  /**
   * 숫자가 쉬는 자리.
   *
   * **한 번 세고 그것을 지킵니다.** 떨리는 동안에도 이 자리를 기준으로 흔들리므로, 두 칸의
   * 숫자가 같은 높이에서 흔들립니다 — 칸마다 다시 세면 그 둘의 기준선이 어긋납니다.
   */
  private baseY = 0
  /**
   * 숫자가 물러나 있는 정도. 1 에서 0 으로 갑니다.
   *
   * **±N 글이 숫자와 같은 자리, 같은 크기로 뜹니다.** 그 둘이 함께 놓여 있으면 어느 것이
   * 지금 값인지 알 수 없으므로, 뜬 동안 칸의 숫자가 옅어졌다 돌아옵니다.
   */
  private muted = 0
  /**
   * 숫자가 쉬는 자리와 그 크기.
   *
   * **재는 쪽이 좌표를 베껴 적지 않게 하는 것이 목적입니다.** ±N 글이 뜨는 자리가 그것이고,
   * 예전에는 화면이 `칸 + 108, 칸 + 16` 을 손으로 적어 두었습니다 — 칸의 크기나 여백을
   * 고치면 그 글만 엉뚱한 자리에 남습니다.
   *
   * `pull` 은 숫자가 어느 쪽에 붙어 있는가입니다. 같은 자리에 겹쳐 세우려면 앉히는 쪽도
   * 같은 기준을 써야 합니다.
   */
  get valueSpot(): { x: number; y: number; size: number; pull: number } {
    return {
      x: this.valueX,
      y: this.baseY,
      size: this.valueStyle.fontSize as number,
      pull: this.pull,
    }
  }

  /**
   * 숫자의 한가운데.
   *
   * **동전이 여기로 꽂힙니다.** 칸의 가운데로 보내면 이름과 숫자 사이의 빈자리에 닿아
   * 숫자에서 한참 떨어져 보입니다 — 오르는 것은 숫자이므로 닿는 자리도 숫자입니다.
   * 숫자는 `pull` 쪽 끝에 붙어 있으므로 그 너비의 절반만큼 되돌아옵니다.
   */
  get valueMiddle(): { x: number; y: number } {
    const width = this.value.width / (this.value.scale.x || 1)
    return { x: this.valueX + width * (0.5 - this.pull), y: this.baseY }
  }

  /** 숫자를 잠깐 물러나게 합니다. ±N 글이 그 자리를 씁니다. */
  mute(): void {
    this.muted = 1
  }

  /**
   * 바탕을 오른 색 · 내린 색으로 한 번 밝힙니다.
   *
   * **글로 들어오는 값의 방향은 여기서만 압니다.** 핸드와 버리기는 「4」 같은 글을 받으므로
   * 칸이 스스로 늘었는지 줄었는지를 셀 수 없고, 그것을 세는 쪽이 ±N 을 띄우는 쪽입니다.
   */
  flash(up: boolean): void {
    if (!this.signed) return
    this.glowInk = up ? UP_INK : DOWN_INK
    this.pop = Math.max(this.pop, 1)
  }

  private get valueX(): number {
    if (this.row) return this.boxWidth - ROW_PAD
    // **이름이 있는 칸과 없는 칸의 여백이 다릅니다.** 28은 이름이 붙은 칸의 여백인데
    // 여기서 한 값만 쓰고 있었고, 그래서 칩과 배수의 숫자가 곱셈표 쪽에서 28픽셀 물러나
    // 있었습니다 — 한 자리일 때는 칸 가운데에 있는 것으로 보이고, 자릿수가 늘면 그만큼
    // **반대쪽으로 넘칩니다.** 붙어야 할 쪽이 곱셈표 쪽이므로 여백은 좁아야 합니다.
    const pad = this.bare ? BARE_PAD : VALUE_PAD
    return this.boxWidth * this.pull + (this.pull <= 0 ? pad : this.pull >= 1 ? -pad : 0)
  }

  private draw(glow = 0): void {
    if (this.bare) return
    if (!this.plateGlow) glow = 0
    // **모습이 같으면 다시 그리지 않습니다.** 숫자가 굴러가는 동안 매 단계 불리므로, 빛의
    // 세기를 16단계로 끊어 그 단계가 바뀔 때만 판때기를 다시 만듭니다 — 눈에는 같고,
    // 초당 240번이던 재삼각화가 몇 번으로 줍니다.
    const step = Math.round(glow * 16) / 16
    // **색도 열쇠입니다.** 세기만 보면 같은 세기로 오른 것과 내린 것이 같은 모습으로
    // 남습니다 — 줄어든 다음 곧바로 같은 만큼 늘어나면 붉은 채로 밝습니다.
    // **열쇠는 수 둘입니다.** 문자열로 만들면 칸마다 초당 60번 문자열 하나가 생기고,
    // 견주는 것은 어느 쪽이나 같습니다.
    if (step === this.plateStep && this.glowInk === this.plateInk) return
    this.plateStep = step
    this.plateInk = this.glowInk
    const style = slotStyle(this.ink)
    this.plate.clear()
    if (this.skin !== undefined) {
      // 빛나는 것은 바탕뿐입니다. 그림을 물들이는 색에 그 빛을 섞습니다.
      const top = step > 0
        ? mix(style.top, this.glowInk, step * (this.signed ? 0.42 : 0.22))
        : style.top
      ;(this.skin as { tint: number }).tint = wellTint(top)
      return
    }
    // **빛나는 것은 바탕뿐입니다.** 테를 굵히면 그 칸만 다른 문법으로 그려진 것이 되고,
    // 값이 굴러가는 동안 판 왼쪽에서 테 하나가 자랐다 줄어듭니다.
    plate(this.plate, this.boxWidth, this.boxHeight, {
      ...style,
      // **오르내림이 드러나는 칸은 더 물듭니다.** 0.22 는 칸의 색으로 한 번 밝히는 세기이고,
      // 그 세기의 초록과 붉음은 어두운 바탕에서 서로 구분되지 않습니다.
      top: step > 0
        ? mix(style.top, this.glowInk, step * (this.signed ? 0.42 : 0.22))
        : style.top,
    })
  }

  /**
   * 겉면이 바뀌었으니 판때기를 다시 그립니다.
   *
   * **모습이 같으면 그리지 않는 기억을 지웁니다.** 그 기억은 「빛의 세기」 만 보므로, 색이
   * 바뀐 것을 알지 못합니다.
   */
  restyle(): void {
    this.plateStep = -1
    this.plateInk = -1
    this.draw()
  }

  /** 칸의 이름. **말이 바뀌면 갈아 끼웁니다** — 만들 때 한 번 읽고 마는 글입니다. */
  set caption(value: string) {
    this.caption_.text = value
  }

  /** 숫자가 아닌 값. 바뀌면 한 번 튑니다. */
  set text(value: string) {
    this.numeric = false
    if (this.value.text !== value) {
      if (this.lastText !== '') {
        this.pop = 1
        this.ripple()
      }
      this.lastText = value
    }
    this.value.text = value
  }

  /**
   * 글자별 물결을 한 번 보냅니다. **글자별 통이 아닌 칸에서는 아무 일도 없습니다.**
   *
   * 값이 새 목표를 받는 그 순간에 한 번입니다 — 굴러가는 매 단계마다 보내면 물결이 아니라
   * 내내 떠는 것이 됩니다.
   */
  private ripple(): void {
    this.flare = 1
    if (this.value instanceof Digits) this.value.wave()
  }

  /** 값이 바뀐 뒤로 남은 밝기. 1 에서 0 으로 갑니다. */
  private flare = 0
  /**
   * 값이 **더해진** 뒤로 남은 것. 더해질 때마다 얹히고 잦아듭니다.
   *
   * **`flare` 와 따로입니다.** 그것은 값이 바뀌기만 하면 서므로 판이 끝나 0 으로 되돌아갈
   * 때도 0 이 아니고, 이 값은 그 대목에 쓰이지 않습니다 — 「더해질 때만」 이 필요한 쪽이
   * 파형이고, 바탕의 번쩍임은 지금대로 둡니다.
   *
   * **쌓입니다.** 조커가 연달아 더하면 그 수만큼 얹히고, 그것이 한 판에서 세기가 오르는
   * 모습입니다.
   */
  private surged = 0
  /**
   * 박자에 얹힌 크기. 1 을 넘는 몫만 들고 있고 0 이면 제 크기입니다.
   *
   * **얹기만 하고 내리지는 않습니다.** 부르는 쪽은 박자마다 부르므로 그 값이 오르내리는데,
   * 받는 대로 크기를 정하면 낮은 박자 하나에 숫자가 도로 줄었다가 다음 박자에 다시 커집니다.
   * 큰 것만 남기고 잦아드는 것은 시간이 맡습니다.
   */
  private lifted = 0

  reset(value: number): void {
    this.numeric = true
    this.shown = value
    this.wanted = value
    // 판을 새로 깔면 물러나 있던 것도 돌아오고, 파형도 조용해집니다.
    this.muted = 0
    this.surged = 0
    this.lifted = 0
    this.value.alpha = 1
    this.redraw()
  }

  set target(value: number) {
    this.numeric = true
    if (value !== this.wanted) {
      // **줄어드는 동안 조용한 칸은 여기서도 조용합니다.**
      //
      // `quietOnDrop` 이 `rolling` 만 막고 있었고, 번쩍임과 튐과 글자 물결은 `ripple()` 이
      // 세우므로 값이 바뀌기만 하면 떴습니다 — 그래서 판이 끝나 칩과 배수가 0 으로
      // 되돌아갈 때마다 두 상자가 파랑과 붉음으로 한 번 빛났습니다. 그것은 알릴 일이
      // 아니고, 그 대목에 필요한 것은 조용히 없어지는 것뿐입니다.
      const quiet = this.quietOnDrop && value < this.shown
      if (!quiet) {
        this.pop = Math.min(1, Math.abs(value - this.shown) / 400 + 0.35)
        this.ripple()
      }
      // **더해질 때만 얹습니다.** 판이 끝나 0 으로 되돌아가는 것은 알릴 일이 아닙니다.
      //
      // 얹는 크기는 「한 번 더해졌다」의 몫 0.28 에 상대적인 크기를 더한 것입니다. 절대값으로
      // 세면 칩이 다섯 자리인 대목에서만 세기가 오르고, 상대값만 쓰면 큰 수에 조금 더해진
      // 것이 아무것도 아니게 됩니다 — 어느 쪽도 「지금 하나 더해졌다」를 내지 못합니다.
      if (value > this.shown) {
        const relative = Math.abs(value - this.shown) / Math.max(1, Math.abs(value))
        this.surged = Math.min(1, this.surged + 0.28 + Math.min(0.42, relative * 0.9))
      }
      // **굴러가는 값은 방향을 스스로 압니다.** 소지금이 그렇습니다 — 지금 보이는 수와
      // 가려는 수를 견주면 되므로 부르는 쪽이 알려 줄 것이 없습니다.
      if (this.signed) this.glowInk = value > this.shown ? UP_INK : DOWN_INK
    }
    this.wanted = value
  }

  get settled(): boolean { return this.shown === this.wanted }

  /**
   * 지금 이 칸이 얼마나 들떠 있는가. 0 이면 조용합니다.
   *
   * **자기 판을 그리지 않는 칸이 있습니다.** 칩과 배수의 바탕은 화면이 통째로 그리므로,
   * 그 바탕을 밝히는 쪽이 이 값을 읽습니다 — 그러지 않으면 밝히는 쪽이 값이 바뀌었는지를
   * 따로 세게 되고, 그 셈이 칸의 셈과 어긋납니다.
   */
  get lit(): number {
    return Math.min(1, Math.max(this.flare, this.rolling))
  }

  /**
   * 값이 더해진 뒤로 남은 것. 0 이면 조용합니다.
   *
   * **`lit` 이 아닙니다.** 그것은 값이 바뀌기만 하면 서므로 판이 끝나 0 으로 되돌아가는
   * 대목에도 0 이 아닙니다.
   */
  get surge(): number { return this.surged }

  /**
   * 지금 화면에 있는 수. **굴러가는 동안은 그 중간값입니다.**
   *
   * 파형의 세기가 배당을 따라가므로 그것을 셈하는 쪽이 이 값을 읽습니다 — 상태의 값을 쓰면
   * 숫자가 아직 굴러가는 동안 파형만 먼저 최대가 됩니다.
   */
  get amount(): number { return this.shown }

  /**
   * 굴러가는 정도. 0 이면 다 왔고 1 이면 아직 멉니다.
   *
   * **소리가 이것을 따라 오릅니다.** 숫자가 굴러가는 동안 무음이면 그 1초가 비고, 그
   * 1초가 이 게임에서 가장 중요한 순간입니다.
   */
  get rolling(): number {
    if (!this.numeric || this.shown === this.wanted) return 0
    if (this.quietOnDrop && this.wanted < this.shown) return 0
    return Math.min(1, Math.abs(this.wanted - this.shown) / 400)
  }

  /**
   * 남은 거리의 일부씩 좁힙니다. 큰 수일수록 오래 굴러갑니다.
   *
   * **굴러가는 동안 숫자가 떱니다.** 값이 매끄럽게 올라가기만 하면 「바뀌었다」로 읽히고,
   * 흔들리면서 올라가면 「쌓이고 있다」로 읽힙니다. 흔드는 세기는 남은 거리에 따릅니다 —
   * 큰 수가 굴러갈 때 크게 떨고, 다 굴러가면 조용히 제자리에 멎습니다.
   */
  advance(deltaMs: number): void {
    if (this.value instanceof Digits) this.value.advance(deltaMs)
    const rolling = this.numeric && this.shown !== this.wanted
    // **줄어드는 동안 떨지 않는 칸이 있습니다.** 굴러가는 것은 그대로이고 들뜨는 것만
    // 없습니다 — 숫자는 0으로 내려가되 그것이 사건으로 보이지는 않습니다.
    const heat = rolling && !(this.quietOnDrop && this.wanted < this.shown)
      ? Math.min(1, Math.abs(this.wanted - this.shown) / 240 + 0.4)
      : 0

    if (this.pop > 0) this.pop = Math.max(0, this.pop - deltaMs / 260)
    if (this.flare > 0) this.flare = Math.max(0, this.flare - deltaMs / FLARE_MS)
    // **곱으로 잦아듭니다.** 얹히는 것이 쌓이는 값이므로, 처음에 빨리 빠지고 꼬리가 길어야
    // 연달아 더해지는 동안 끊기지 않고 이어집니다. 곱은 0 에 닿지 않으므로 문턱에서 끊습니다.
    if (this.surged > 0) {
      this.surged *= Math.exp(-deltaMs / SURGE_MS)
      if (this.surged < 0.004) this.surged = 0
    }
    // **얹힌 크기도 곱으로 잦아듭니다.** 박자가 연달아 오는 동안은 얹히는 것이 잦아드는
    // 것보다 빠르므로 숫자가 이어서 커지고, 박자가 끊기면 그 자리에서 제 크기로 내려옵니다.
    if (this.lifted > 0) {
      this.lifted *= Math.exp(-deltaMs / EMPHASIS_MS)
      if (this.lifted < 0.004) this.lifted = 0
    }

    // **곧바로 물러나고 천천히 돌아옵니다.** 옅어지는 데 시간을 쓰면 그 사이 두 수가 같은
    // 자리에 겹쳐 있고, 겹친 동안에는 어느 것도 읽히지 않습니다.
    if (this.muted > 0) {
      this.muted = Math.max(0, this.muted - deltaMs / MUTE_MS)
      // **떠 있는 동안은 아예 감추고, 그 글이 떠날 때 돌아옵니다.**
      //
      // 옅게 남겨 두면 그 수가 떠 있는 ±N 뒤에 그대로 보여 같은 자리에 수가 둘 있는 것으로
      // 읽힙니다. 서서히 돌아오게 두어도 같습니다 — ±N 은 앞의 0.4 를 제자리에 앉아 있다가
      // 떠오르므로, 그 사이에 조금이라도 보이면 둘이 겹칩니다. 그래서 거의 끝까지 감추고
      // 마지막 짧은 동안에 돌아옵니다.
      const back = 0.18
      this.value.alpha = this.muted > back ? 0 : 1 - this.muted / back
    }

    const ease = this.pop * this.pop
    const shake = Math.max(heat, ease)
    const lift = this.lifted
    // **바탕이 밝은 동안은 ±N 이 떠 있는 동안입니다.**
    //
    // 튐은 0.26초에 잦아드는데 그 글은 0.62초를 머뭅니다 — 튐에만 맞추면 글이 아직
    // 떠 있는데 색이 먼저 빠지고, 눈이 칸에 닿았을 때는 이미 아무 색도 없습니다.
    const glow = Math.min(1, Math.max(shake, lift * 2, this.signed ? this.muted : 0))
    if (shake > 0.002 || lift > 0) {
      // 튀는 것과 떠는 것과 얹힌 것을 같이 씁니다. **흔드는 것은 `shake` 뿐입니다** —
      // 얹힌 것만 남은 대목에서는 그 값이 0 이므로 숫자가 제자리에서 커졌다 줄어듭니다.
      // **칸에 들어가려 줄인 배율을 곱합니다.** 튐이 그것을 지우면 여섯 자리 수가 튀는
      // 동안에만 상자 밖으로 나갔다 돌아옵니다.
      this.value.scale.set(this.fitScale * (1 + ease * 0.42 + heat * 0.14 + lift))
      this.value.x = this.valueX + (Math.random() - 0.5) * 7 * shake
      // **세로로는 조금만 흔듭니다.** 두 칸의 숫자가 나란히 놓여 있어서, 세로로 크게 흔들면
      // 그 둘의 기준선이 서로 어긋나 보입니다.
      this.value.y = this.baseY - ease * 4 + (Math.random() - 0.5) * 2.4 * shake
      this.value.rotation = (Math.random() - 0.5) * 0.13 * shake
      this.settledLook = false
    } else if (this.settledLook !== true) {
      this.settledLook = true
      this.value.scale.set(this.fitScale)
      this.value.position.set(this.valueX, this.baseY)
      this.value.rotation = 0
    }
    // **모습이 같으면 돌아갑니다.** 열쇠를 보는 것이 이 함수의 첫 줄이므로 매번 불러도
    // 됩니다 — 조건마다 따로 부르면 그중 하나에서 색이 빠지지 않은 채로 남습니다.
    this.draw(glow)

    if (!rolling) return
    const gap = this.wanted - this.shown
    const step = Math.max(1, Math.abs(gap) * (deltaMs / 130))
    this.shown = gap > 0
      ? Math.min(this.wanted, this.shown + step)
      : Math.max(this.wanted, this.shown - step)
    this.redraw()
  }

  /**
   * 값이 클수록 크게, 그리고 바탕이 밝아집니다.
   *
   * **크기를 여기서 정하지 않고 얹기만 합니다.** 여기서 `scale` 을 그대로 앉히면 그 크기가
   * 다음에 부를 때까지 그대로 남아 있고, 박자가 끊긴 자리에서 커진 채로 멈췄다가 한 프레임에
   * 제 크기로 돌아옵니다 — 잦아드는 것은 `advance` 가 시간으로 합니다.
   */
  emphasize(scale: number): void {
    if (this.pop > 0) return
    // **줄어드는 값은 강조하지 않습니다.** 부르는 쪽은 박자마다 부르므로 방향을 모릅니다.
    if (this.quietOnDrop && this.wanted < this.shown) return
    this.lifted = Math.max(this.lifted, scale - 1)
  }

  /** 수 앞에 붙는 글. 소지금의 `$` 입니다 — 값이 굴러가는 동안에도 붙어 있습니다. */
  prefix = ''

  private redraw(): void {
    const shown = Math.round(this.shown)
    this.value.text = this.prefix + (shown >= 1_000_000
      ? shown.toExponential(2).replace('e+', 'e')
      : COMMAS.format(shown))
    // **자릿수가 바뀌면 조용한 모습을 다시 앉힙니다.** 칸에 들어가려 줄인 배율은 글자 수가
    // 정하는 값이고, 조용한 모습은 「이미 앉혔다」로 한 번만 적용됩니다 — 다시 적지 않으면
    // 여섯 자리가 된 수가 앞의 배율로 그려집니다.
    this.settledLook = false
  }

  /**
   * 칸에 들어가려고 줄인 배율. 1 이면 그대로입니다.
   *
   * **글 하나로 그리는 칸에는 없습니다.** 그쪽은 이름 옆의 한 줄이고 자릿수가 칸을 넘지
   * 않습니다 — 넘는 것은 칩과 배수뿐이고 그 둘이 글자별로 세우는 통입니다.
   */
  private get fitScale(): number {
    return this.value instanceof Digits ? this.value.fitScale : 1
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
  /**
   * 판 위의 글 전부. 태그 칩은 여기 있지 않습니다.
   *
   * **내용이 바뀌면 이 통이 살짝 위에서 내려와 앉습니다.** 왼쪽 판의 맨 위는 상황마다 다른
   * 것을 적는 자리이고, 글자가 제자리에서 바뀌면 바뀐 것을 알아채지 못합니다 — 한 번
   * 튕기며 앉는 것이 「여기가 바뀌었다」입니다.
   */
  private readonly body = new Container()
  private readonly bodyY = new Spring(0, 240, 14)
  private bodyFade = 1
  /** 지금 적힌 것의 요약. 같은 것을 다시 적으면 튕기지 않습니다. */
  private shownKey = ''
  /**
   * 상황을 적는 판의 글 둘. 요구 점수 대신 서는 것입니다.
   *
   * **통입니다.** 「요구 점수 1,200 · 격파 보상 $5」 에서 사람이 찾는 것은 두 수이고,
   * 그것이 문장과 같은 색이면 문장을 처음부터 읽어야 찾습니다 — 쪽지와 판이 이미 같은
   * 규칙으로 강조하므로 이 자리만 민글이면 여기가 다른 물건으로 보입니다.
   */
  private readonly lead = new Container()
  private readonly info = new Container()
  private readonly title = new Text({
    text: '', style: { fontSize: TEXT.base, fill: UI.ink, fontWeight: WEIGHT.bold },
  })
  private readonly need = new Text({
    text: '',
    // **그 화면의 주인공 하나입니다** — 계단의 맨 위 칸(72)입니다. 규칙 한 줄이 함께
    // 놓이는 판(보스)에서는 한 계단 내려갑니다(`fitNeed`) — 72로 두면 그 줄이 딱지
    // 밖으로 밀려 판의 변을 넘어갑니다.
    style: { fontSize: STEP[4], fill: UI.bad, fontWeight: WEIGHT.bold, fontFamily: NUMERALS },
  })
  /** 요구 점수라는 것을 적는 작은 글. */
  private readonly caption = new Text({
    text: '',
    style: { fontSize: TEXT.micro, fill: UI.inkDim, fontWeight: WEIGHT.normal, letterSpacing: 1 },
  })
  /** 보스의 규칙 한 줄. 수가 그 규칙의 요점이라 여기도 강조가 붙습니다. */
  private readonly note = new Container()
  /**
   * 격파 보상. **이름은 작고 값은 큽니다** — 사람이 찾는 것은 값이고, 이름과 값이 같은
   * 크기면 문장을 처음부터 읽어야 찾습니다. 글 표의 한 줄을 마지막 빈칸에서 가릅니다.
   */
  private readonly rewardLabel = new Text({
    text: '', style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
  })
  private readonly rewardValue = new Text({
    text: '',
    style: { fontSize: TEXT.base, fill: UI.money, fontWeight: WEIGHT.bold, fontFamily: NUMERALS },
  })

  /**
   * 이름 옆에 붙는 표시.
   *
   * **보스에만 붙습니다.** 스물여덟이 이름 하나로만 갈리면 어느 것과 붙고 있는지가 판이
   * 도는 내내 이름 한 줄에만 남습니다. 무엇을 그릴지는 화면이 정하고 이 클래스는 자리만
   * 냅니다 — 그림이 어디서 오는지를 여기가 알 이유가 없습니다.
   */
  private seal?: Container

  /** 굵은 한 줄 · 옅은 몇 줄 · 규칙 한 줄. 크기만 다르고 강조의 색은 같습니다. */
  private static leadRich(): RichStyle {
    const style = richStyle('body')
    return { ...style, base: { ...style.base, fontWeight: WEIGHT.bold } }
  }

  private static infoRich(): RichStyle {
    return richStyle('note')
  }

  /**
   * 통 하나를 다시 세우고 몇 줄을 썼는지 돌려줍니다.
   *
   * **줄 수는 세어서 받습니다.** 접힌 줄까지 세어야 그 아래의 글이 겹치지 않는데, 통의
   * 높이는 마지막 줄의 글자 높이까지라 그것으로 재면 줄마다 조금씩 어긋납니다.
   */
  private fill(into: Container, lines: readonly string[], style: RichStyle,
               lineHeight: number, top: number): number {
    into.removeChildren().forEach(child => child.destroy({ children: true }))
    if (lines.length === 0 || lines.every(one => one === '')) return 0
    const block = richBlock(lines, style, lineHeight, this.boxWidth - 10, 'center')
    into.addChild(block)
    into.position.set(5, top)
    return rowsOf(block)
  }

  constructor(private readonly boxWidth: number) {
    super()
    this.body.addChild(this.title, this.caption, this.need, this.rewardLabel, this.rewardValue,
      this.note, this.lead, this.info)
    this.addChild(this.plate, this.body)
  }

  /**
   * 내용이 바뀌었으면 글 통이 살짝 위에서 내려와 앉습니다.
   *
   * **탄력이 있습니다.** 감쇠를 낮춰 한 번 넘치고 돌아오게 두었고, 그동안 글도 함께
   * 짙어집니다 — 같은 내용을 다시 적는 것은 다시 그리는 것뿐이므로 움직이지 않습니다.
   */
  private settle(key: string): void {
    if (key === this.shownKey) return
    this.shownKey = key
    this.bodyY.snap(-18)
    this.bodyY.target = 0
    this.bodyFade = 0
    this.body.alpha = 0
    this.body.y = -18
  }

  /** 글 통의 움직임을 한 단계 진행합니다. 화면의 틱이 부릅니다. */
  advance(seconds: number): void {
    if (this.bodyFade >= 1 && this.bodyY.settled) return
    this.bodyY.advance(seconds)
    this.bodyFade = Math.min(1, this.bodyFade + seconds / 0.22)
    this.body.y = this.bodyY.value
    this.body.alpha = this.bodyFade
  }

  /**
   * 상황을 적습니다 — 상점 · 뜯은 팩 · 자리 비우기처럼 요구 점수가 뜻을 갖지 않는 때입니다.
   *
   * **틀은 같고 안의 글만 다릅니다.** 이름 띠는 그대로이고, 요구 점수와 격파 보상 자리에
   * 굵은 한 줄(`lead`)과 옅은 몇 줄(`info`)이 놓입니다 — 같은 딱지가 상황을 따라 다른 것을
   * 적는 것이어야 왼쪽 판이 여러 판으로 보이지 않습니다.
   */
  /**
   * 판때기. **금속 테를 두르지 않습니다.**
   *
   * 판때기의 금속 테 그림을 여기에도 걸어 보았고, 이 칸은 왼쪽 판 안에 놓이므로 테 안에
   * 테가 되었습니다 — 그리고 이 크기에서 네 귀의 볼트판이 과합니다. 바깥 판의 테를
   * 안쪽 칸에 그대로 쓸 수 없습니다.
   *
   * **안쪽 칸에는 따로 그린 테가 필요합니다.** 볼트가 없고 더 얇은 것입니다. 그것이
   * 생기기 전까지는 파인 줄과 얇은 테로 둡니다.
   */
  private dressPlate(height: number, mark = UI.mark): void {
    this.skin?.destroy()
    this.band?.destroy()
    this.skin = piece('well', this.boxWidth, height, wellTint(UI.cell))
    if (this.skin === undefined) {
      plate(this.plate, this.boxWidth, height, {
        top: UI.cell, bottom: UI.cell, border: UI.hairline, radius: 0, weight: 1,
      })
      return
    }
    const home = this.plate.parent ?? this
    home.addChildAt(this.skin, 0)
    // **머리 판.** 판의 폭을 다 쓰고, 색은 채움과 글자에 듭니다 — 스몰은 파랑, 빅은
    // 보라, 보스는 붉음. 밑줄의 번짐까지 그림 한 장에 있습니다.
    this.band = piece('head', this.boxWidth, HEAD_H, mix(mark, UI.cell, 0.62))
    if (this.band !== undefined) home.addChildAt(this.band, 1)
    this.title.style.fill = mix(mark, UI.ink, 0.35)
  }

  /** 구워 둔 몸통과 머리 판. 없으면 `plate` 가 그립니다. */
  private skin?: Container
  private band?: Container

  setInfo(name: string, lead: string, lines: string[], mark: number, seal?: Container,
          tags: Container[] = []): void {
    this.settle(`info|${name}|${lead}|${lines.join('|')}`)
    const height = BADGE_H
    this.boxHeight = height

    this.plate.clear()
    this.dressPlate(height, mark)

    this.seal?.destroy()
    this.seal = undefined
    if (seal) {
      this.seal = seal
      seal.position.set(20, HEAD_H / 2)
      this.body.addChild(seal)
    }

    this.title.text = name
    this.title.anchor.set(0.5, 0.5)
    this.title.scale.set(1)
    const room = this.boxWidth - 40 * 2
    if (this.title.width > room) this.title.scale.set(room / this.title.width)
    this.title.position.set(this.boxWidth / 2, HEAD_H / 2)

    // 요구 점수 쪽은 비웁니다. 자리는 그대로이고 글만 없습니다.
    this.caption.text = ''
    this.need.text = ''
    this.rewardLabel.text = ''
    this.rewardValue.text = ''
    this.fill(this.note, [], BlindBadge.infoRich(), richLeading('note'), 0)

    const rows = this.fill(this.lead, [lead], BlindBadge.leadRich(), richLeading('body'), HEAD_H + 24)
    // 굵은 줄 바로 아래입니다. 굵은 줄이 두 줄이면 그만큼 내려섭니다.
    this.fill(this.info, lines, BlindBadge.infoRich(), richLeading('note'),
              HEAD_H + 24 + rows * richLeading('body') + 10)

    this.setTags(tags)
  }

  /**
   * 이 딱지가 지금 얼마나 높은가. **들고 있는 태그가 늘면 자랍니다.**
   *
   * 아래에 무엇을 둘 자리를 세는 쪽이 알아야 합니다 — 화면이 같은 계산을 베껴 적으면
   * 여기를 고칠 때 그쪽만 남습니다.
   */
  boxHeight = BADGE_H

  set(name: string, target: number, reward: number, note: string,
      boss: boolean, big = false, seal?: Container, tags: Container[] = []): void {
    this.settle(`blind|${name}|${target}|${reward}|${note}`)
    this.fill(this.lead, [], BlindBadge.leadRich(), richLeading('body'), 0)
    this.fill(this.info, [], BlindBadge.infoRich(), richLeading('note'), 0)
    // **딱지는 자라지 않습니다.**
    //
    // 두 가지가 키우고 있었습니다 — 들고 있는 태그를 아래에 한 줄로 세운 것과, 보스의
    // 규칙 한 줄이 있을 때만 26픽셀을 더한 것입니다. 태그는 머리띠의 오른쪽 끝으로
    // 옮겼고(`setTags`), 규칙의 자리는 규칙이 없을 때도 비워 둡니다.
    //
    // **비워 두는 자리가 아까운 것보다 흔들리는 것이 나쁩니다.** 이 딱지는 왼쪽 판의 맨
    // 위이고, 높이가 바뀌면 그 아래가 전부 따라 움직입니다 — 블라인드를 넘길 때마다 판이
    // 한 번씩 출렁이던 것이 그것입니다.
    const height = BADGE_H
    this.boxHeight = height
    // **판을 물들이지 않습니다.** 셋이 저마다의 바탕색이면 판 셋이 서로 다른 물건이 되고,
    // 어느 블라인드인지는 이름과 문양이 이미 말합니다 — 색은 이름 앞의 문양 하나에만
    // 듭니다.
    const mark = boss ? UI.red : big ? UI.legendary : UI.bar

    this.plate.clear()
    this.dressPlate(height, mark)
    // **문양은 하나입니다.** 화면이 넘겨주는 딱지가 그 문양이므로 여기서 또 그리지 않습니다.
    // 넘겨주지 않는 판(상점)은 문양이 없고, 자리 표시도 두지 않습니다 — 머리 판에는 이름
    // 하나만 놓입니다.

    // 앞의 표시를 걷고 새것을 답니다. **그대로 두면 보스가 바뀌어도 앞의 것이 남습니다.**
    this.seal?.destroy()
    this.seal = undefined

    // **이름은 언제나 띠의 가운데입니다.** 인장이 붙으면 그만큼 오른쪽으로 비켜세웠는데,
    // 그러면 보스일 때만 이름이 다른 자리에 있습니다 — 띠에 얹히는 것들은 이름의 옆에
    // 서는 것이 아니라 띠의 양 끝에 서는 것이고, 이름은 그것과 무관하게 띠의 가운데입니다.
    // 고르기 판의 칸 셋도 같은 규칙입니다.
    this.title.text = name
    this.title.anchor.set(0.5, 0.5)
    this.title.scale.set(1)
    // **문양과 태그를 밀지 않습니다.** 긴 이름은 그 사이에 들어가는 만큼 줄입니다 — 말에
    // 따라 이름의 길이가 배로 달라집니다.
    const room = this.boxWidth - 40 * 2
    if (this.title.width > room) this.title.scale.set(room / this.title.width)
    this.title.position.set(this.boxWidth / 2, HEAD_H / 2)

    if (seal) {
      this.seal = seal
      seal.position.set(20, HEAD_H / 2)
      this.body.addChild(seal)
    }

    this.layoutValue(t('ui.label.target'), target.toLocaleString('en-US'), reward, note)

    this.setTags(tags)
  }

  /**
   * 이름 밑의 세 줄 — 무엇의 수인가 · 그 수 · 격파 보상. 규칙 한 줄이 있으면 그 밑입니다.
   *
   * **줄의 자리가 규칙 한 줄의 있고 없음으로 갈립니다.** 딱지의 높이는 어느 판에서나
   * 같으므로(`BADGE_H`), 넷째 줄이 들어오는 판에서는 위의 셋이 함께 올라가고 수가 한
   * 계단 내려갑니다 — 자리를 고정해 두었더니 보스의 규칙과 고르는 판의 안내가 딱지
   * 밖으로 밀려 판의 변을 넘었습니다.
   */
  private layoutValue(caption: string, value: string, reward: number, note: string): void {
    const dense = note !== ''
    this.caption.text = caption
    this.caption.anchor.set(0.5, 0)
    this.caption.position.set(this.boxWidth / 2, HEAD_H + 22)

    this.need.style.fontSize = dense ? STEP[3] : STEP[4]
    this.need.text = value
    this.need.anchor.set(0.5, 0)
    this.need.position.set(this.boxWidth / 2, HEAD_H + (dense ? 36 : 40))

    // **한 줄을 마지막 빈칸에서 가릅니다.** 앞은 이름이고 뒤는 값입니다 — 값만 크고 금색입니다.
    const rewardText = tf('ui.blind.reward', { n: reward })
    const cut = rewardText.lastIndexOf(' ')
    this.rewardLabel.text = cut > 0 ? rewardText.slice(0, cut).trim() : ''
    this.rewardValue.text = cut > 0 ? rewardText.slice(cut + 1) : rewardText
    const between = this.rewardLabel.text === '' ? 0 : 8
    const span = this.rewardLabel.width + between + this.rewardValue.width
    const start = (this.boxWidth - span) / 2
    const rewardY = HEAD_H + (dense ? 100 : 148)
    this.rewardLabel.anchor.set(0, 0.5)
    this.rewardLabel.position.set(start, rewardY)
    this.rewardValue.anchor.set(0, 0.5)
    this.rewardValue.position.set(start + this.rewardLabel.width + between, rewardY)

    // **딱지 안에 들어오는 만큼만 적습니다.** 넘치는 줄은 딱지 밖으로 나가 판의 변을
    // 넘어가므로, 들어갈 줄 수를 남은 높이에서 셉니다.
    const top = HEAD_H + 118
    const room = Math.max(0, BADGE_H - top - 8)
    const rows = Math.floor(room / richLeading('note'))
    this.fill(this.note, dense && rows > 0 ? [note] : [], BlindBadge.infoRich(),
              richLeading('note'), top)
  }

  /**
   * 다음에 붙을 판을 적습니다 — 상점입니다.
   *
   * **블라인드 딱지와 같은 세 줄입니다.** 상점에서만 문단으로 적어 두었더니 그 문단이
   * 딱지의 폭을 넘어 낱말 가운데에서 접혔고, 왼쪽 판이 판 안에서 두 가지 물건이 되었습니다.
   */
  setNext(name: string, caption: string, target: number, reward: number, mark: number,
          seal?: Container, tags: Container[] = []): void {
    this.settle(`next|${name}|${caption}|${target}|${reward}`)
    this.fill(this.lead, [], BlindBadge.leadRich(), richLeading('body'), 0)
    this.fill(this.info, [], BlindBadge.infoRich(), richLeading('note'), 0)
    this.boxHeight = BADGE_H

    this.plate.clear()
    this.dressPlate(BADGE_H, mark)

    this.seal?.destroy()
    this.seal = undefined
    if (seal) {
      this.seal = seal
      seal.position.set(20, HEAD_H / 2)
      this.body.addChild(seal)
    }

    this.title.text = name
    this.title.anchor.set(0.5, 0.5)
    this.title.scale.set(1)
    const room = this.boxWidth - 40 * 2
    if (this.title.width > room) this.title.scale.set(room / this.title.width)
    this.title.position.set(this.boxWidth / 2, HEAD_H / 2)

    this.layoutValue(caption, target.toLocaleString('en-US'), reward, '')
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

    // **이미 달려 있는 그 칩들이면 그대로 둡니다.** 딱지 전체를 다시 그리는 길이 태그를
    // 한 번 더 넘기는데, 같은 것을 걷고 다시 달면 만든 것을 그 자리에서 버리는 일입니다.
    if (tags.length === this.tags.length && tags.every((one, i) => one === this.tags[i])) return

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

  /**
   * 칩들이 실제로 그려진 자리. **피벗을 뺀 왼쪽 위 모서리입니다.**
   *
   * 재는 쪽이 보는 것은 화면에 보이는 자리이고, `position` 은 피벗만큼 어긋납니다.
   */
  get chipSpots(): { x: number; y: number; scale: number }[] {
    return this.tags.map(one => ({
      x: Math.round(one.x - one.pivot.x),
      y: Math.round(one.y - one.pivot.y),
      scale: Math.round(one.scale.x * 100) / 100,
    }))
  }
}
