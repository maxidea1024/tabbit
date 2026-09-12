// 콜렉션 — 물건 전부의 도감.
//
// **판 하나에 갈래 9개입니다.** 조커만 볼 수 있는 판이 따로 떠 있었고, 그러면 소모품과
// 바우처와 태그와 보스는 판에서 만나기 전에는 볼 길이 없습니다 — 도감은 「이 게임에 무엇이
// 있는가」를 답하는 자리이므로 한 갈래만 담을 수 없습니다.
//
// **만나 본 것이 앞면입니다.** 아직 만나지 못한 것은 뒷면으로 서고 이름도 효과도 가려집니다.
// 무엇이 몇 개인지는 뒷면도 알리므로, 남은 것이 몇인지는 세지 않아도 보입니다.
//
// **얼굴은 판에서 쓰는 그 함수입니다**(`render/faces.ts`). 여기서만 쓰는 그림을 두면 한
// 곳이 남고, 한쪽만 고친 날부터 도감과 판이 어긋납니다.
//
// **잠그지 않습니다.** 발견은 표시일 뿐이고 아무것도 여닫지 않습니다 — 결제가 없는
// 로그라이트에서 해금은 순수한 지연이라는 `ui/setup.ts` 의 결정과 같습니다.

import { COLOR, PAINT } from '../render/ink'
import { Container, Graphics, Rectangle, Sprite, Text, type Renderer } from 'pixi.js'

import type { Data } from '../core/data'
import { seen, type CollectionGroup, type CollectionProgress } from '../core/collection'
import { describe, handDisplay } from '../core/describe'
import { nameOf, t, tf } from '../core/strings'
import { stakeSlug } from '../core/stake'
import { newCounters, type JokerInstance } from '../core/state'
import { BlindKind } from '../generated/enums/blind-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { StakeKind } from '../generated/enums/stake-kind'
import { onArtReady } from '../render/art'
import { backLookOf, drawCardBack } from '../render/card-back'
import {
  blindFace, itemFace, packFace, packName, tagFace, voucherFace,
} from '../render/faces'
import { JokerView } from '../render/joker-view'
import { UI, SIZE, TEXT, WEIGHT } from '../render/theme'
import { Spring } from '../render/motion'
import { attachTip, TipHold, TIP_GROW } from './tip'
import type { ToolSpot } from './layout'
import { panelFrame, type ModalPanel } from './modal'
import { ScrollView } from './scroll'
import { Tooltip } from './tooltip'
import { Button } from './widgets'

const WIDTH = 1092

/**
 * 격자.
 *
 * **갈래가 아홉이어도 격자는 하나입니다.** 갈래마다 칸의 크기를 달리하면 탭을 누를 때마다
 * 판 안의 것이 통째로 옮겨 가고, 그러면 다음 탭을 누르려던 손이 빈자리를 누릅니다.
 */
const COLUMNS = 10
const CELL_X = 104
/**
 * 줄 사이. **카드 124에 이름 두 줄이 더 들어갑니다** — 긴 이름은 두 줄로 접히므로 그
 * 높이를 잡아 두지 않으면 아랫줄의 카드 위에 얹힙니다.
 */
const CELL_Y = 152

/**
 * 굴려서 보는 자리의 폭.
 *
 * **칸 열 줄에 막대 자리를 더한 것입니다.** 판의 폭에서 여백만 뺐더니 마지막 열이 그
 * 막대에 걸려 잘렸습니다 — 격자의 폭이 먼저이고 판의 여백이 그 나머지입니다.
 */
const VIEW_W = COLUMNS * CELL_X + 16
const GRID_X = Math.round((WIDTH - VIEW_W) / 2)
const GRID_Y = 178

/**
 * 판의 높이. **격자가 정합니다.**
 *
 * 수로 못박아 두었더니 넷째 줄이 판의 밑변 아래로 넘쳐 이름이 잘리고 아랫단의 한 줄이
 * 카드 위에 겹쳤습니다 — 줄 수를 고치면 높이가 따라와야 합니다.
 */
/**
 * 굴려서 보는 자리의 높이.
 *
 * **줄 하나가 반쯤 걸치게 둡니다.** 딱 세 줄이 들어가면 그 아래에 더 있다는 것이 화면에
 * 없고, 막대는 굴려 본 뒤에야 눈에 듭니다 — 잘린 줄이 그것을 먼저 알립니다.
 */
const VIEW_H = 3 * CELL_Y + 60

const HEIGHT = GRID_Y + VIEW_H + 44

/**
 * 보이는 줄의 위아래로 더 짓는 줄 수.
 *
 * **굴리는 도중에 빈자리가 보이지 않게 하는 것입니다.** 딱 보이는 만큼만 지으면 굴리기
 * 시작한 그 프레임에 다음 줄이 아직 없습니다.
 */
const MARGIN_ROWS = 1

/** 칸 하나가 실제로 차지하는 높이. 카드와 그 아래 이름 두 줄입니다. **굽는 사각형이기도 합니다.** */
const LINE_H = SIZE.jokerHeight + 30

/**
 * 스켈레톤에서 그림으로 겹쳐 흐르는 시간. 초입니다.
 *
 * **갈아 끼우면 튑니다.** 굴리는 동안 줄마다 열 칸이 저마다 다른 순간에 닿으므로, 그냥
 * 바꾸면 격자가 자글자글 깜박이는 것으로 보입니다 — 겹쳐 흐르면 같은 칸이 채워지는 것으로
 * 읽힙니다. 0.5초를 넘기면 굴리는 손끝보다 느려 「아직 안 나왔다」가 됩니다.
 */
const FADE = 0.22

/**
 * 굽는 사각형이 칸보다 아래로 더 담는 만큼.
 *
 * **이름이 두 줄이면 글이 칸의 밑변에 닿습니다.** 줄 높이 13에 두 줄이면 글이 놓이는 자리의
 * 아래끝이 `LINE_H` 와 같은 자리이고, 글자의 내림('g' 의 꼬리나 독일어의 움라우트가 아니라
 * 그 반대쪽)이 그 선을 몇 픽셀 넘습니다 — 굽기 전에는 아무것도 자르지 않았으므로 그 몇
 * 픽셀이 그냥 보였습니다. **구우면 사각형이 곧 가위입니다.**
 *
 * 한국어 이름은 여덟 글자까지라 한 줄에 들어가고, 두 줄이 되는 것은 독일어입니다
 * (`Verschmierte Scheibe`) — 한 언어에서만 드러나는 자리입니다.
 */
const BAKE_PAD = 6

/**
 * 칸을 굽는 데 쓰는 것. **렌더러와 글씨의 배율입니다.**
 *
 * 배율은 값이 아니라 함수입니다 — 화면의 배율은 창의 크기를 따라 바뀌고, 판은 한 번
 * 만들어 세션 내내 씁니다.
 */
export interface CellOven {
  renderer: Renderer
  density: () => number
}

/** 지어 둔 칸 하나. 그림이 도착했을 때 그 칸만 다시 짓기 위해 자리와 내용을 함께 듭니다. */
interface Placed {
  cell: Cell
  node: Container
  x: number
  y: number
}

/**
 * 동그란 얼굴로 그리는 묶음들. 카드와 원점이 다릅니다.
 *
 * **만나 보았는지와 무관합니다.** 만나 본 것만 동그랗게 그리고 나머지를 카드 뒷면으로
 * 두었더니, 태그 탭에서 동그란 칩 사이에 긴 카드가 섞여 섰습니다 — 아직 못 만난 것도 그
 * 물건의 틀로 가려야 「같은 갈래의 아직 안 열린 칸」으로 읽힙니다.
 */
const ROUND_GROUPS: readonly CollectionGroup[] = ['tag', 'blind', 'boss']

/** 탭 줄과 그 아래 단추 줄. */
const TAB_Y = 60
const TAB_H = 40
const HEAD_Y = 110
const HEAD_H = 46

/** 동그란 얼굴의 지름. 카드와 같은 자리에 서므로 카드의 폭을 넘지 않습니다. */
const ROUND = 84

const RARITY_KEYS = ['', 'ui.rarity.common', 'ui.rarity.uncommon',
                     'ui.rarity.rare', 'ui.rarity.legendary']

/**
 * 얼굴에 이름을 적지 않습니다.
 *
 * **칸이 이름을 적기 때문입니다.** 얼굴의 띠와 칸의 아래가 같은 이름을 한 칸에 두 번
 * 적고 있었고, 둘 중에 접을 것은 띠입니다 — 칸의 글은 격자를 훑을 때 읽는 것이고 띠의
 * 글은 그림을 덮습니다.
 *
 * **이름이 곧 내용인 얼굴에는 넘기지 않습니다.** 바우처와 강화·인장·에디션과 스테이크의
 * 종이는 그림이 없어 적힌 글이 그 카드의 전부입니다.
 */
const NAMELESS = { nameless: true } as const

/**
 * 탭 하나.
 *
 * **탭은 아홉이고 저장의 묶음은 14개입니다.** 소모품 탭 하나가 타로와 행성과 유령을 함께
 * 세우고, 카드 탭 하나가 강화와 인장과 에디션을 함께 세웁니다 — 저장이 탭을 따라가면
 * 탭을 나누거나 합칠 때 저장이 못 쓰게 됩니다.
 */
type TabKey = 'joker' | 'consumable' | 'voucher' | 'card' | 'pack'
  | 'tag' | 'blind' | 'stake' | 'deck'

const TABS: { key: TabKey; label: string }[] = [
  { key: 'joker', label: 'ui.kind.joker' },
  { key: 'consumable', label: 'ui.kind.consumable' },
  { key: 'voucher', label: 'ui.kind.voucher' },
  { key: 'card', label: 'ui.kind.card' },
  { key: 'pack', label: 'ui.kind.pack' },
  { key: 'tag', label: 'ui.kind.tag' },
  { key: 'blind', label: 'ui.kind.blind' },
  { key: 'stake', label: 'ui.kind.stake' },
  { key: 'deck', label: 'ui.kind.deck' },
]

/** 칸 하나. **얼굴은 그릴 때 만듭니다** — 150장을 미리 만들면 판을 여는 데 그만큼 걸립니다. */
interface Cell {
  group: CollectionGroup
  id: string
  name: string
  kind: string
  /**
   * 설명 줄. **읽는 순간에 만듭니다.**
   *
   * 읽는 곳이 쪽지 하나뿐인데 미리 만들면 조커 탭 하나에 `describe()` 가 150번이고, 그
   * 가운데 사람이 보는 것은 가리킨 한 칸뿐입니다. `face` 와 같은 규약입니다.
   */
  lines: () => string[]
  rarity: number
  cost?: number
  /** 앞면일 때 그리는 것. */
  face: () => Container
}

/**
 * 무엇으로 줄을 세우는가. **조커 탭에만 있습니다** — 나머지는 표의 순서가 곧 뜻입니다.
 */
type SortKey = 'order' | 'rarity' | 'name' | 'cost'

const SORTS: { key: SortKey; label: string }[] = [
  { key: 'order', label: 'ui.pool.sortOrder' },
  { key: 'rarity', label: 'ui.pool.sortRarity' },
  { key: 'name', label: 'ui.pool.sortName' },
  { key: 'cost', label: 'ui.pool.sortCost' },
]

export class CollectionPanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }

  private readonly body = new Container()
  /**
   * 굴려서 보는 자리.
   *
   * **쪽 넘김이 아니라 굴림입니다.** 쪽을 넘기면 「그 조커가 몇 쪽이었더라」를 사람이
   * 기억해야 하고, 손가락에는 넘길 단추가 작습니다 — `ui/scroll.ts` 의 그 손끝이므로
   * 바퀴와 끌기와 관성이 옵션 판과 순위표에서와 같습니다.
   */
  private readonly scroll = new ScrollView(VIEW_W, VIEW_H)
  private readonly grid = new Container()
  /** 굴릴 수 있는 길이를 재게 하는 자리표. **칸은 보이는 만큼만 짓습니다.** */
  private readonly spacer = new Graphics()
  private readonly tooltip = new Tooltip()
  /**
   * 꾸욱 누르기. **판 위와 같은 것입니다**(`ui/tip.ts`).
   *
   * 누르면 곧바로 뜨게 해 두었더니 손가락으로는 한 번 스치기만 해도 떴고, 굴리려고 짚은
   * 손가락에도 떴습니다 — 같은 게임 안에서 쪽지가 두 가지로 돌았습니다.
   */
  private readonly hold = new TipHold()
  /**
   * 지금 커서가 올라가 있는 칸과 그 크기.
   *
   * **조커 딱지와 같은 몸짓입니다.** 도감은 그 딱지들을 늘어놓은 자리인데 가리켜도 아무
   * 일이 없어서, 무엇을 가리키고 있는지가 쪽지의 자리로만 읽혔습니다.
   */
  private hoverCell?: Container
  private readonly hoverGrow = new Spring(1, 320, 26)

  private readonly foundLabel = new Text({
    text: '', style: { fontSize: TEXT.base, fill: UI.ink, fontWeight: WEIGHT.bold },
  })
  private readonly hint = new Text({
    text: '', style: { fontSize: TEXT.body, fill: UI.inkDim },
  })

  private readonly tabButtons: { key: TabKey; button: Button; label: string }[] = []
  private readonly sortButtons: { key: SortKey; button: Button; label: string }[] = []
  private order?: Button
  private frame?: Container

  /**
   * 지어 둔 칸. **줄마다 묶어 둡니다.**
   *
   * 굴려서 보이는 줄이 달라지면 **나간 줄만 치우고 들어온 줄만 짓습니다.** 범위가 바뀔
   * 때마다 60칸을 통째로 버리고 다시 지었더니, 줄 하나를 지날 때마다 그 프레임이 카드 60장을
   * 만드는 값으로 눌려 굴림이 덜덜거렸습니다 — 그 가운데 실제로 새로 보이는 것은 한 줄
   * 10칸입니다.
   *
   * 그림이 도착했을 때 **그 칸 하나만 다시 짓기 위한 것**이기도 합니다.
   */
  private readonly rowsBuilt = new Map<number, Placed[]>()
  private tab: TabKey = 'joker'
  /** 지금 지어 둔 줄의 범위. 굴려서 이 밖으로 나가면 나간 줄을 치우고 들어온 줄을 짓습니다. */
  private built = { from: -1, to: -1 }
  /**
   * 닫혀 있는 동안 다시 세울 일이 있었는가. **다음에 뜰 때 세웁니다.**
   *
   * 닫힌 판을 그 자리에서 세우는 것은 아무도 보지 않는 칸을 굽는 일이고, 그렇게 구운 칸은
   * 그림이 닿아도 다시 구워지지 않습니다(`onArtReady` 가 떠 있는 판만 받습니다). 처음
   * 만들 때와 발견이 늘 때와 말이 바뀔 때가 다 이 길로 옵니다.
   */
  private stale = false
  private sort: SortKey = 'order'
  private ascending = true
  /** 다음에 세울 때 다시 지어야 하는가. 그림이 들어오면 켜집니다. */
  /** 그림이 도착한 칸의 식별자. **한 프레임에 모아서 처리합니다.** */
  private readonly artWaiting = new Set<string>()
  /** 지금까지 지은 칸의 수. 검증 도구가 읽습니다. */
  private builtCount = 0
  /**
   * 스켈레톤에서 그림으로 겹쳐 흐르는 중인 칸.
   *
   * **칸을 갈아 끼우지 않고 위에 얹습니다.** 칸의 통은 그대로 두므로 쪽지를 띄우는 손끝도
   * 겹치는 차례도 흐르는 동안 그대로입니다 — 통째로 갈면 가리키고 있던 칸이 그 순간
   * 손끝을 놓칩니다.
   */
  private readonly fading: { node: Container; from: Sprite; to: Sprite; at: number }[] = []

  /**
   * 검증 도구가 짚을 자리.
   *
   * **좌표를 도구에 적어 두지 않기 위한 것입니다.** 탭 아홉의 자리를 셈해 적으면 폭을 고친
   * 날부터 도구는 빈자리를 누르고 통과합니다.
   */
  private readonly toolNodes = new Map<string, ToolSpot>()

  /**
   * **보이는 것만 알립니다.** 조커 탭에만 서는 단추들이 다른 탭에서는 자리째 비어 있고,
   * 그때 자리를 알리면 도구는 없는 단추를 누르고 눌렀다고 넘어갑니다.
   */
  get toolSpots(): [string, ToolSpot][] {
    const out = [...this.toolNodes].filter(([, one]) => one.node.visible)
    // 막대. **굴릴 것이 있을 때만 알립니다** — 없으면 잡을 것도 없습니다.
    if (this.scroll.handle.visible) {
      out.push(['bar', {
        node: this.scroll.handle,
        cx: VIEW_W - 11,
        cy: this.scroll.handleTop + 14,
      }])
    }
    return out
  }

  /**
   * 지금 탭이 무엇을 몇 개 세우고 있는가.
   *
   * **도구가 표와 견주는 값입니다.** 화면에 보이는 칸을 세면 한 쪽에 40개까지이므로,
   * 갈래의 수를 확인하려면 쪽마다 넘겨 세어야 합니다 — 그것은 도구가 판의 쪽 나눔을
   * 알고 있어야 한다는 뜻입니다.
   */
  get census(): { tab: string; cells: number; found: number; offset: number; built: number;
                  fading: number } {
    const cells = this.cells()
    return {
      tab: this.tab, cells: cells.length, found: this.metCount(cells),
      fading: this.fading.length,
      // 판을 만든 뒤로 지은 칸의 수. **굴리는 동안 얼마나 다시 짓는지를 도구가 이 수로
      // 봅니다** — 화면의 칸을 세면 늘 60칸이고, 그 60칸이 몇 번째 60칸인지는 보이지 않습니다.
      built: this.builtCount,
      // 얼마나 굴려 내려왔는가. **도구가 굴림이 되는지를 이 수로 봅니다** — 화면의 칸을
      // 세는 것으로는 바퀴가 도는지 손가락이 끄는지가 갈리지 않습니다.
      offset: Math.round(this.scroll.content.y),
    }
  }

  constructor(private readonly data: Data,
              private progress: CollectionProgress,
              private readonly onClose: () => void,
              private readonly oven: CellOven) {
    this.build()
    this.rebuild()

    // **그림은 늦게 들어옵니다.**
    //
    // **어느 그림이 왔는지를 확인합니다.** 도착 하나에 격자를 통째로 다시 지으면, 한 줄을
    // 굴려 부탁한 그림 60장이 하나씩 들어오는 동안 60칸 짓기를 60번 하게 됩니다 — 판에
    // 놓여 있는 조커의 그림이 도착해도 그랬습니다. 지금 지어 둔 칸의 것만 받고, 받은 것도
    // 그 칸 하나만 다시 짓습니다.
    //
    // **모아서 다음 프레임에 처리합니다.** 한 프레임에 여럿이 도착하면 그만큼 짓는 것이
    // 아니라 한 번입니다.
    // **들어온 것과 놓은 것을 가리지 않습니다.** `art.ts` 가 둘 다 같은 길로 알리고,
    // 이쪽이 할 일도 둘 다 같습니다 — 그 칸을 다시 짓는 것입니다. 놓은 것을 흘리면 그
    // 칸이 두 틱 뒤에 버려진 그림을 가리킨 채로 남습니다.
    //
    // **놓인 것은 받지 않습니다.** 칸은 구워 둔 그림 한 장이므로 원본 그림을 들고 있지
    // 않고, 원본이 놓여도 구운 것은 그대로입니다. 놓인 것까지 받으면 놓인 칸이 그 자리에서
    // 다시 부탁하고, 그 부탁이 다른 칸의 그림을 놓게 하는 되먹임이 됩니다 — 한 화면의
    // 조커 60장이 상한을 넘는 날에는 그것이 멈추지 않습니다.
    onArtReady((key, gone) => {
      if (gone || !this.view.parent) return
      const id = key.slice(key.indexOf('/') + 1)
      if (this.findPlaced(id)) this.artWaiting.add(id)
    })
  }

  /** 발견이 늘었습니다. **떠 있으면 그 자리에서, 닫혀 있으면 다음에 뜰 때 다시 세웁니다.** */
  setProgress(progress: CollectionProgress): void {
    this.progress = progress
    this.rebuild()
  }

  /**
   * 구운 것을 전부 놓습니다. **GPU 의 자리가 회수되어 돌아온 뒤에 부릅니다.**
   *
   * 구운 그림은 원본이 GPU 에만 있어 컨텍스트가 다시 서면 빈 채로 돌아옵니다 — 카드의
   * 앞면과 뒷면이 그런 것과 같은 자리입니다. 다음 `advance` 가 보이는 줄을 다시 굽습니다.
   */
  forgetBakes(): void {
    this.clearCells()
    this.built = { from: -1, to: -1 }
  }

  /** 겹치는 중인 칸의 수. **검증 도구가 흐름이 실제로 도는지 이 수로 봅니다.** */
  get crossfading(): number {
    return this.fading.length
  }

  private buildFrame(): void {
    if (this.frame) {
      this.view.removeChild(this.frame)
      this.frame.destroy({ children: true })
    }
    this.frame = panelFrame(WIDTH, HEIGHT, t('ui.collection.title'), this.onClose,
                            undefined, false)
    this.view.addChildAt(this.frame, 0)
  }

  private build(): void {
    this.buildFrame()
    this.view.addChild(this.body)

    // 탭 아홉. **한 줄입니다** — 두 줄이 되면 어느 줄이 먼저인지가 읽히지 않습니다.
    const tabW = 108
    const tabGap = 8
    const tabsX = Math.round((WIDTH - (TABS.length * tabW + (TABS.length - 1) * tabGap)) / 2)
    for (const [index, one] of TABS.entries()) {
      const button = new Button(t(one.label), tabW, TAB_H, 'neutral',
                                () => this.choose(one.key), 15)
      button.position.set(tabsX + index * (tabW + tabGap), TAB_Y)
      this.tabButtons.push({ key: one.key, button, label: one.label })
      this.toolNodes.set(`tab:${one.key}`, { node: button, cx: tabW / 2, cy: TAB_H / 2 })
      this.body.addChild(button)
    }

    // 줄 세우기. **조커 탭에만 놓입니다** — 150종이면 눈으로 훑어서는 찾지 못합니다.
    // 왼쳴에 「기본 / 확장」 단추 둘이 있었고, 자작 350종을 걷으면서 함께 걷었습니다 —
    // 줄 세우기는 그 자리에서 왼쪽으로 옮겨 격자의 왼변에 맞춥니다.
    const sw = 70
    for (const [index, one] of SORTS.entries()) {
      const button = new Button(t(one.label), sw, HEAD_H, 'neutral',
                                () => this.sortBy(one.key), 14)
      button.position.set(GRID_X + index * (sw + 6), HEAD_Y)
      this.sortButtons.push({ key: one.key, button, label: one.label })
      this.toolNodes.set(`sort:${one.key}`, { node: button, cx: sw / 2, cy: HEAD_H / 2 })
      this.body.addChild(button)
    }
    this.order = new Button('', 40, HEAD_H, 'neutral', () => this.flip(), 18)
    this.order.position.set(GRID_X + SORTS.length * (sw + 6), HEAD_Y)
    this.toolNodes.set('order', { node: this.order, cx: 20, cy: HEAD_H / 2 })
    this.body.addChild(this.order)

    this.scroll.position.set(GRID_X, GRID_Y)
    this.scroll.content.addChild(this.spacer, this.grid)
    this.body.addChild(this.scroll)

    this.foundLabel.anchor.set(1, 0.5)
    this.foundLabel.position.set(WIDTH - 58, 23)
    this.body.addChild(this.foundLabel)

    this.hint.anchor.set(0.5, 0.5)
    this.hint.position.set(WIDTH / 2, HEIGHT - 26)
    this.body.addChild(this.hint)

    // **쪽지는 맨 위입니다.** 칸 위에 떠야 하므로 판의 마지막 자식입니다.
    this.view.addChild(this.tooltip)
  }

  private choose(tab: TabKey): void {
    if (this.tab === tab) return
    this.tab = tab
    this.cellsCache = undefined
    this.rebuild()
  }

  private sortBy(key: SortKey): void {
    if (this.sort === key) {
      this.flip()
      return
    }
    this.sort = key
    this.cellsCache = undefined
    this.rebuild()
  }

  private flip(): void {
    this.ascending = !this.ascending
    this.cellsCache = undefined
    this.rebuild()
  }

  /** 마지막으로 세운 칸들. 탭과 범위와 줄 세우기가 같으면 그대로 씁니다. */
  private cellsCache?: { key: string; cells: Cell[] }

  /**
   * 지금 탭의 칸들.
   *
   * **세워 둔 것을 다시 씁니다.** 쪽을 넘길 때마다 150행을 정렬하면, 이름 정렬은
   * 비교마다 글 표를 읽습니다.
   */
  private cells(): Cell[] {
    const key = `${this.tab}|${this.sort}|${this.ascending}`
    if (this.cellsCache?.key === key) return this.cellsCache.cells
    const cells = this.buildCells()
    this.cellsCache = { key, cells }
    return cells
  }

  private buildCells(): Cell[] {
    switch (this.tab) {
      case 'joker': return this.jokerCells()
      case 'consumable': return this.consumableCells()
      case 'voucher': return this.voucherCells()
      case 'card': return this.cardCells()
      case 'pack': return this.packCells()
      case 'tag': return this.tagCells()
      case 'blind': return this.blindCells()
      case 'stake': return this.stakeCells()
      default: return this.deckCells()
    }
  }

  private jokerCells(): Cell[] {
    const rows = this.data.tables.joker.records

    // **이름을 한 번만 뽑습니다.** 비교 안에서 부르면 150행 정렬에 비교가 1,000번 남짓이고
    // 이름 조회가 그 두 배입니다 — 뽑아 두면 150번입니다. 아래의 칸도 이 이름을 씁니다.
    const sorted = rows.map(row => ({
      row, name: nameOf(this.data, 'joker', row.jokerId, row.name),
    }))
    if (this.sort === 'rarity') {
      sorted.sort((a, b) => a.row.rarity - b.row.rarity || a.row.sortOrder - b.row.sortOrder)
    } else if (this.sort === 'cost') {
      sorted.sort((a, b) => a.row.cost - b.row.cost || a.row.sortOrder - b.row.sortOrder)
    } else if (this.sort === 'name') {
      sorted.sort((a, b) => a.name.localeCompare(b.name))
    } else {
      sorted.sort((a, b) => a.row.sortOrder - b.row.sortOrder)
    }
    if (!this.ascending) sorted.reverse()

    return sorted.map(({ row, name }) => ({
      group: 'joker' as CollectionGroup,
      id: row.jokerId,
      name,
      kind: t(RARITY_KEYS[row.rarity] ?? ''),
      lines: () => describe(this.data, this.data.jokerEffects.get(row.jokerId) ?? []),
      rarity: row.rarity,
      cost: row.cost,
      face: () => this.jokerView(row.jokerId, name, row.rarity),
    }))
  }

  private jokerView(jokerId: string, name: string, rarity: number): Container {
    const joker: JokerInstance = {
      uid: 0, jokerId,
      edition: 0 as JokerInstance['edition'],
      sticker: 0 as JokerInstance['sticker'],
      counters: newCounters(), age: 0, disabled: false,
    }
    const view = new JokerView(joker, { name, rarity, lines: [] })
    // **이름 띠를 접습니다.** 칸의 아래에 이름이 이미 적혀 있습니다.
    view.hideName()
    // **격자에는 피벗을 쓰지 않습니다.** 카드 뷰의 피벗이 가운데이므로 그대로 두면 칸의
    // 왼쪽 위에 반쯤 걸칩니다 — 칸이 자리를 정하고 얼굴은 왼쪽 위에서 그려집니다.
    view.pivot.set(0, 0)
    return view
  }

  private consumableCells(): Cell[] {
    const out: Cell[] = []
    for (const row of this.data.tables.tarot.records) {
      out.push({
        group: 'tarot', id: row.tarotId,
        name: nameOf(this.data, 'tarot', row.tarotId, row.name),
        kind: t('ui.kind.tarot'), rarity: 0,
        lines: () => describe(this.data, this.data.tarotEffects.get(row.tarotId) ?? []),
        face: () => itemFace(this.data, { kind: ShopItemKind.Tarot, id: row.tarotId },
                             NAMELESS),
      })
    }
    for (const row of this.data.tables.planet.records) {
      out.push({
        group: 'planet', id: row.planetId,
        name: nameOf(this.data, 'planet', row.planetId, row.name),
        kind: t('ui.kind.planet'), rarity: 0,
        // **행성은 효과 표가 없습니다.** 어느 족보를 올리는지가 그 행성의 전부입니다.
        lines: () => [handDisplay(this.data, row.hand)],
        face: () => itemFace(this.data, { kind: ShopItemKind.Planet, id: row.planetId },
                             NAMELESS),
      })
    }
    for (const row of this.data.tables.spectral.records) {
      out.push({
        group: 'spectral', id: row.spectralId,
        name: nameOf(this.data, 'spectral', row.spectralId, row.name),
        kind: t('ui.kind.spectral'), rarity: 0,
        lines: () => describe(this.data, this.data.spectralEffects.get(row.spectralId) ?? []),
        face: () => itemFace(this.data, { kind: ShopItemKind.Spectral, id: row.spectralId },
                             NAMELESS),
      })
    }
    return out
  }

  private voucherCells(): Cell[] {
    // **상위는 하위 다음에 놓입니다.** 16쌍이므로 표의 순서가 곧 그 짝입니다.
    return [...this.data.tables.voucher.records]
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map(row => {
        // **얼굴과 쪽지가 같은 줄을 씁니다.** 한 번 만들고 나눠 쓰되, 둘 다 부르지 않으면
        // 만들지 않습니다 — 얼굴은 보이는 칸만, 쪽지는 가리킨 칸만 부릅니다.
        let made: string[] | undefined
        const lines = (): string[] =>
          (made ??= describe(this.data, this.data.voucherEffects.get(row.voucherId) ?? []))
        return {
          group: 'voucher' as CollectionGroup,
          id: row.voucherId,
          name: nameOf(this.data, 'voucher', row.voucherId, row.name),
          kind: t('ui.kind.voucher'), rarity: 0, lines,
          cost: row.cost,
          face: () => voucherFace(this.data, row.voucherId,
                                  lines()[0] ?? t('ui.note.rest_of_run')),
        }
      })
  }

  /**
   * 카드에 붙는 것 셋.
   *
   * **「없음」은 칸이 되지 않습니다.** 강화와 인장의 표에는 아무것도 붙지 않은 줄이 하나씩
   * 있고 에디션에는 기본이 있습니다 — 그것은 붙일 수 있는 것이 아니라 붙지 않은 상태입니다.
   */
  private cardCells(): Cell[] {
    const out: Cell[] = []
    for (const row of this.data.tables.enhancement.records) {
      if (row.enhancement === EnhancementKind.None) continue
      const slug = EnhancementKind[row.enhancement].toLowerCase()
      out.push({
        group: 'enhancement', id: EnhancementKind[row.enhancement],
        name: nameOf(this.data, 'enhancement', slug, row.display),
        kind: t('ui.kind.enhancement'), rarity: 0,
        lines: () => describe(this.data, this.data.enhancementEffects.get(
          String(row.enhancement)) ?? []),
        face: () => this.markFace(nameOf(this.data, 'enhancement', slug, row.display),
                                  t('ui.kind.enhancement')),
      })
    }
    for (const row of this.data.tables.seal.records) {
      if (row.seal === SealKind.None) continue
      const slug = SealKind[row.seal].toLowerCase()
      out.push({
        group: 'seal', id: SealKind[row.seal],
        name: nameOf(this.data, 'seal', slug, row.display),
        kind: t('ui.kind.seal'), rarity: 0,
        lines: () => describe(this.data, this.data.sealEffects.get(String(row.seal)) ?? []),
        face: () => this.markFace(nameOf(this.data, 'seal', slug, row.display),
                                  t('ui.kind.seal')),
      })
    }
    for (const row of this.data.tables.edition.records) {
      if (row.edition === EditionKind.Base) continue
      const slug = EditionKind[row.edition].toLowerCase()
      out.push({
        group: 'edition', id: EditionKind[row.edition],
        name: nameOf(this.data, 'edition', slug, row.display),
        kind: t('ui.kind.edition'), rarity: 0,
        lines: () => editionLines(row),
        face: () => this.markFace(nameOf(this.data, 'edition', slug, row.display),
                                  t('ui.kind.edition')),
      })
    }
    return out
  }

  /**
   * 강화 · 인장 · 에디션의 얼굴.
   *
   * **카드 한 장에 붙는 것이므로 카드로 그립니다.** 이름과 갈래만 적힌 크림색 종이이고,
   * 붙은 모습은 판에서 그 카드가 보여 줍니다.
   */
  private markFace(name: string, kind: string): Container {
    const w = SIZE.jokerWidth
    const h = SIZE.jokerHeight
    const node = new Container()
    const paper = new Graphics()
    paper.roundRect(0, 0, w, h, 9).fill(COLOR.slip)
    paper.roundRect(1, 1, w - 2, h - 2, 8).stroke({ color: UI.outline, width: 2 })
    const label = new Text({
      text: name,
      style: {
        fontSize: TEXT.copy, fill: COLOR.slipInk, fontWeight: WEIGHT.heavy, align: 'center',
        wordWrap: true, wordWrapWidth: w - 12, breakWords: true, lineHeight: 16,
      },
    })
    label.anchor.set(0.5, 0.5)
    label.position.set(w / 2, h / 2)
    const head = new Text({
      text: kind,
      style: { fontSize: 9, fill: COLOR.slipDim, fontWeight: WEIGHT.bold },
    })
    head.anchor.set(0.5, 0)
    head.position.set(w / 2, 10)
    node.addChild(paper, head, label)
    return node
  }

  private packCells(): Cell[] {
    return this.data.tables.boosterPack.records.map(row => ({
      group: 'pack' as CollectionGroup,
      id: row.packId,
      name: packName(row.kind, row.size),
      kind: t('ui.kind.pack'), rarity: 0,
      cost: row.cost,
      lines: () => [tf('ui.pack.spread', { cards: row.cards, picks: row.picks })],
      face: () => packFace(row, NAMELESS),
    }))
  }

  private tagCells(): Cell[] {
    return [...this.data.tables.tag.records]
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map(row => ({
        group: 'tag' as CollectionGroup,
        id: row.tagId,
        name: nameOf(this.data, 'tag', row.tagId, row.name),
        kind: t('ui.kind.tag'), rarity: 0,
        lines: () => describe(this.data, this.data.tagEffects.get(row.tagId) ?? []),
        face: () => tagFace(row.tagId, ROUND),
      }))
  }

  private blindCells(): Cell[] {
    const out: Cell[] = []
    for (const row of this.data.tables.blind.records) {
      const slug = BlindKind[row.blind].toLowerCase()
      out.push({
        group: 'blind', id: BlindKind[row.blind],
        name: nameOf(this.data, 'blind', slug, row.name),
        kind: t('ui.kind.blind'), rarity: 0,
        lines: () => [t('ui.note.no_rules')],
        // **보스 칸은 인장을 그리지 않습니다** — 어느 보스인지는 그 아래의 28칸입니다.
        face: () => blindFace(row.blind, ROUND, ''),
      })
    }
    for (const row of [...this.data.tables.bossBlind.records]
      .sort((a, b) => a.sortOrder - b.sortOrder)) {
      out.push({
        group: 'boss', id: row.bossId,
        name: nameOf(this.data, 'boss', row.bossId, row.name),
        kind: t('ui.kind.boss'), rarity: 0,
        lines: () => describe(this.data, this.data.bossEffects.get(row.bossId) ?? []),
        face: () => blindFace(BlindKind.Boss, ROUND, row.bossId),
      })
    }
    return out
  }

  private stakeCells(): Cell[] {
    return this.data.tables.stake.records.map(row => ({
      group: 'stake' as CollectionGroup,
      id: StakeKind[row.stake],
      name: nameOf(this.data, 'stake', stakeSlug(row.stake), row.name),
      kind: t('ui.kind.stake'), rarity: 0,
      lines: () => [tf('ui.stake.note', {
        column: row.anteColumn, reward: row.smallBlindReward, discards: row.discardsDelta,
      })],
      face: () => this.markFace(nameOf(this.data, 'stake', stakeSlug(row.stake), row.name),
                                t('ui.kind.stake')),
    }))
  }

  private deckCells(): Cell[] {
    return [...this.data.tables.deck.records]
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map(row => ({
        group: 'deck' as CollectionGroup,
        id: row.deckId,
        name: nameOf(this.data, 'deck', row.deckId, row.name),
        kind: t('ui.kind.deck'), rarity: 0,
        // 해금 조건은 표시입니다 — 이 게임은 덱을 잠그지 않습니다.
        lines: () => describe(this.data, this.data.deckEffects.get(row.deckId) ?? []),
        face: () => {
          const node = new Container()
          drawCardBack(node, SIZE.jokerWidth, SIZE.jokerHeight, 9, backLookOf(row))
          return node
        },
      }))
  }

  /**
   * 아직 만나지 못한 것. **그 물건의 틀로 가립니다.**
   *
   * 카드인 것은 덱의 뒷면 하나와 그 위의 물음표이고, 동그란 것(태그 · 블라인드 · 보스)은
   * 같은 색의 원 하나와 물음표입니다 — 전부 카드 뒷면으로 두면 동그란 칩이 늘어선 탭에
   * 긴 카드가 섞여 서고, 그 칸만 다른 갈래의 물건처럼 보입니다.
   */
  private unseenFace(round: boolean): Container {
    const node = new Container()
    const mark = new Text({
      text: '?',
      style: { fontSize: TEXT.hero, fill: UI.inkFaint, fontWeight: WEIGHT.heavy },
    })
    mark.anchor.set(0.5, 0.5)

    if (round) {
      // **가운데가 원점입니다.** 동그란 얼굴의 규약이고, 칸 안에서 자리를 잡는 쪽이 그것을
      // 압니다.
      const disc = new Graphics()
      const r = ROUND / 2
      disc.circle(0, 0, r).fill({ color: COLOR.unseen })
      disc.circle(0, 0, r).stroke({ color: COLOR.unseenInk, width: 2 })
      // 안쪽 선 하나. 카드 뒷면의 안쪽 액자와 같은 자리입니다 — 없으면 그냥 원입니다.
      disc.circle(0, 0, r - 6).stroke({ color: COLOR.unseenInk, width: 1, alpha: 0.8 })
      node.addChild(disc, mark)
      node.alpha = 0.85
      return node
    }

    const w = SIZE.jokerWidth
    const h = SIZE.jokerHeight
    const back = new Container()
    drawCardBack(back, w, h, 9, { motif: 0 as never, ground: COLOR.unseen, ink: COLOR.unseenInk })
    mark.position.set(w / 2, h / 2)
    node.addChild(back, mark)
    node.alpha = 0.85
    return node
  }

  /**
   * 머리와 굴릴 길이를 지금 상태로 다시 세우고, 보이는 줄을 짓습니다.
   *
   * **탭을 바꾸면 맨 위로 돌아갑니다** — 앞 탭에서 굴려 둔 자리를 물려받으면 새 탭의
   * 한가운데가 열립니다.
   *
   * **판이 떠 있지 않으면 짓지 않고 표시만 남깁니다.** 이 판은 화면을 세울 때 한 번
   * 만들어 세션 내내 들고 있으므로, 생성자에서 격자를 지으면 **타이틀에서 조커 60칸을
   * 짓고 그림 60장을 읽습니다** — 판을 한 번도 열지 않아도 그렇고, 그 60장이 그림 상한
   * 96MB 를 거의 다 채웁니다. 게다가 그때 지은 칸은 그림이 닿기 전의 맨 판으로 구워지고
   * 판이 닫혀 있어 `onArtReady` 를 받지 못하므로, **나중에 판을 열면 굴릴 때까지 맨 판이
   * 그대로 있습니다.** 옵션 판이 미리 부탁하던 68건을 걷은 것과 같은 자리입니다.
   */
  private rebuild(): void {
    this.tooltip.hide()
    if (!this.view.parent) {
      this.stale = true
      this.clearCells()
      this.built = { from: -1, to: -1 }
      return
    }

    const all = this.cells()

    for (const one of this.tabButtons) {
      one.button.text = t(one.label)
      // **눌린 채로 두고 나머지를 흐리게 합니다.** 둘 중 하나만 하면 어두운 바탕에서 어느
      // 것이 고른 것인지가 눈에 들지 않습니다.
      const on = one.key === this.tab
      one.button.highlight = on
      one.button.alpha = on ? 1 : 0.55
    }

    // 조커 탭에만 서는 것들. **다른 탭에서는 자리째 비웁니다** — 눌리지 않는 단추가 서
    // 있으면 그 탭에서 무엇을 할 수 있는지가 흐려집니다.
    const jokers = this.tab === 'joker'
    for (const one of this.sortButtons) {
      one.button.text = t(one.label)
      one.button.visible = jokers
      const on = one.key === this.sort
      one.button.highlight = on
      one.button.alpha = on ? 1 : 0.55
    }
    if (this.order) {
      this.order.visible = jokers
      this.order.text = this.ascending ? '▲' : '▼'
    }

    // **굴릴 길이는 자리표가 알립니다.** 칸은 보이는 만큼만 지으므로, 지어 둔 것의 높이를
    // 재면 굴릴 수 있는 길이가 지금 보이는 세 줄이 됩니다.
    const rows = Math.ceil(all.length / COLUMNS)
    this.spacer.clear()
    this.spacer.rect(0, 0, VIEW_W, Math.max(VIEW_H, rows * CELL_Y - (CELL_Y - LINE_H)))
      .fill({ color: PAINT.hit, alpha: 0 })

    // **앞 탭의 칸을 먼저 치웁니다.** 굴릴 길이는 지어 둔 것을 재어 나오므로, 남겨 둔 채로
    // 재면 앞 탭이 길었던 만큼 막대가 서고 그 막대는 아무 데도 굴러가지 않습니다.
    this.clearCells()
    this.built = { from: -1, to: -1 }
    this.scroll.toTop()
    this.draw()
    // 지은 뒤에 다시 잽니다. 굴릴 길이가 이 탭의 것이 됩니다.
    this.scroll.refresh()

    this.foundLabel.text = tf('ui.collection.found', {
      at: this.metCount(all), of: all.length,
    })
    this.hint.text = t('ui.collection.hint')
  }

  /**
   * 지금 보이는 줄을 짓습니다.
   *
   * **보이는 만큼만 짓습니다.** 조커 탭이 150칸이고, 그것을 한꺼번에 지으면 탭을 누른 그
   * 프레임에 카드 150장을 만들게 됩니다 — 화면에 서는 것은 30장 남짓입니다.
   *
   * **바뀐 줄만 짓습니다.** 범위가 한 줄 내려가면 위의 한 줄을 치우고 아래의 한 줄을
   * 짓습니다 — 사이의 넉 줄은 그대로입니다.
   */
  private draw(): void {
    const all = this.cells()
    const rows = Math.ceil(all.length / COLUMNS)
    const top = -this.scroll.content.y
    const from = Math.max(0, Math.floor(top / CELL_Y) - MARGIN_ROWS)
    const to = Math.min(rows - 1, Math.ceil((top + VIEW_H) / CELL_Y) + MARGIN_ROWS)
    if (from === this.built.from && to === this.built.to) return
    this.built = { from, to }

    for (const row of [...this.rowsBuilt.keys()]) {
      if (row < from || row > to) this.dropRow(row)
    }
    for (let row = from; row <= to; row++) {
      if (!this.rowsBuilt.has(row)) this.buildRow(row, all)
    }
  }

  private buildRow(row: number, all: readonly Cell[]): void {
    const made: Placed[] = []
    for (let column = 0; column < COLUMNS; column++) {
      const cell = all[row * COLUMNS + column]
      if (!cell) break
      const x = column * CELL_X
      const y = row * CELL_Y
      const node = this.cellNode(cell, x, y)
      this.grid.addChild(node)
      made.push({ cell, node, x, y })
    }
    this.rowsBuilt.set(row, made)
  }

  private dropRow(row: number): void {
    const made = this.rowsBuilt.get(row)
    if (!made) return
    this.rowsBuilt.delete(row)
    for (const one of made) this.discard(one.node)
  }

  /**
   * 칸 하나를 버립니다. **구운 그림까지 놓습니다.**
   *
   * 구운 것은 Pixi 의 그림 수거 대상이 아니므로 만든 쪽이 놓아야 하고, `texture` 만 주면
   * 바탕이 남습니다 — 그 바탕이 곧 GPU 의 그림 한 장입니다.
   */
  private discard(node: Container): void {
    // **겹침 기록을 먼저 버립니다.** 남겨 두면 다음 프레임이 이미 버린 스프라이트의
    // 알파를 만집니다.
    this.forget(node)
    node.destroy({ children: true, texture: true, textureSource: true })
  }

  private findPlaced(id: string): Placed | undefined {
    for (const made of this.rowsBuilt.values()) {
      for (const one of made) if (one.cell.id === id) return one
    }
    return undefined
  }

  /**
   * 그림이 도착한 칸 하나를 다시 굽고 **겹쳐 흐르게 합니다.**
   *
   * 스켈레톤을 그린 그림에서 실제 그림으로 그 자리에서 갈아 끼우면 굴리는 동안 격자가
   * 자글자글 깜박입니다 — 줄마다 열 칸이 저마다 다른 순간에 닿기 때문입니다. 새로 구운
   * 것을 같은 통 위에 얹어 흐려 넣고, 다 흐른 뒤에 아래의 것을 놓습니다.
   */
  private repaint(id: string): void {
    for (const made of this.rowsBuilt.values()) {
      for (const one of made) {
        if (one.cell.id !== id) continue
        // **격자에 없으면 건너뜁니다.** 이 함수는 판의 `advance` 안이므로 여기서 던지면
        // 그 프레임의 나머지가 통째로 죽습니다.
        if (this.grid.children.indexOf(one.node) < 0) continue
        // 아직 흐르는 중이었으면 그것을 먼저 끝냅니다. 한 칸에 두 겹까지입니다.
        this.settle(one.node)
        const from = one.node.children[0]
        if (!(from instanceof Sprite)) continue
        const to = this.bake(this.cellFace(one.cell))
        to.alpha = 0
        one.node.addChild(to)
        this.fading.push({ node: one.node, from, to, at: 0 })
      }
    }
  }

  /** 그 칸의 겹침을 지금 끝냅니다. 아래의 것을 놓고 위의 것을 남깁니다. */
  private settle(node: Container): void {
    const at = this.fading.findIndex(one => one.node === node)
    if (at < 0) return
    const [one] = this.fading.splice(at, 1)
    one.to.alpha = 1
    one.from.destroy({ texture: true, textureSource: true })
  }

  /** 그 칸의 겹침을 버립니다. **칸을 통째로 버리는 자리에서 부릅니다.** */
  private forget(node: Container): void {
    const at = this.fading.findIndex(one => one.node === node)
    if (at >= 0) this.fading.splice(at, 1)
  }

  /** 지어 둔 칸을 전부 치웁니다. 탭이 바뀌는 자리입니다. */
  private clearCells(): void {
    for (const row of [...this.rowsBuilt.keys()]) this.dropRow(row)
    this.artWaiting.clear()
    this.grid.removeChildren()
  }

  /**
   * 칸 하나. 얼굴과 이름을 **그림 한 장으로 구워** 누르는 자리에 얹습니다.
   *
   * **굽는 이유는 매 프레임의 값입니다.** 조커 칸 하나가 `Graphics` 일곱에 글 둘에 스텐실
   * 마스크 하나이고, 한 화면이 60칸입니다 — 그것을 그대로 두면 굴리는 매 프레임에 마스크
   * 60개가 배치를 끊고 글 120장이 저마다 텍스처입니다. 구우면 60칸이 스프라이트 60장이고,
   * 굽는 값은 그 칸이 처음 보이는 한 번입니다.
   *
   * **원본 그림을 들고 있지 않게 되는 것이 덤입니다.** 상한이 넘쳐 원본이 놓여도 구운 칸은
   * 그대로이므로, 이 판은 놓인 그림을 다시 부탁하지 않습니다.
   */
  private cellNode(cell: Cell, x: number, y: number): Container {
    const node = new Container()
    node.addChild(this.bake(this.cellFace(cell)))

    // **가운데를 축으로 둡니다.** 왼쪽 위를 축으로 두면 가리켜 커지는 칸이 오른쪽 아래로
    // 밀려나면서 커집니다. 그리는 자리는 그대로입니다.
    node.pivot.set(CELL_X / 2, SIZE.jokerHeight / 2)
    node.position.set(x + CELL_X / 2, y + SIZE.jokerHeight / 2)
    node.eventMode = 'static'
    node.cursor = 'pointer'
    const met = seen(this.progress, cell.group, cell.id)
    // **판 위와 같은 체계입니다**(`attachTip`). 마우스는 올리면 뜨고, 손가락은 꾸욱 눌러야
    // 뜹니다 — 굴리려고 짚은 손가락에는 뜨지 않습니다.
    attachTip(node, this.hold,
      () => {
        if (this.scroll.holding) return
        this.hover(cell, met, x, y)
        this.raise(node)
      },
      () => {
        this.tooltip.hide()
        this.lower(node)
      })
    return node
  }

  /** 칸 하나의 내용. 얼굴과 이름입니다. **굽기 전의 모습이고 굽고 나면 버립니다.** */
  private cellFace(cell: Cell): Container {
    this.builtCount++
    const met = seen(this.progress, cell.group, cell.id)
    const raw = new Container()
    // **틀은 그 묶음의 것이고, 만나 보았는지는 그 안의 내용입니다.** 둘을 한 조건으로
    // 묶었더니 못 만난 태그가 긴 카드로 섰습니다.
    const round = ROUND_GROUPS.includes(cell.group)
    const face = met ? cell.face() : this.unseenFace(round)
    // **동그란 얼굴은 가운데를 원점으로 그립니다.** 카드는 왼쪽 위이므로 자리가 갈립니다 —
    // 칸 안에서 가운데로 모으는 것은 여기 한 곳입니다.
    if (round) {
      face.position.set(CELL_X / 2, SIZE.jokerHeight / 2)
    } else {
      face.position.set((CELL_X - SIZE.jokerWidth) / 2, 0)
    }
    raw.addChild(face)

    const label = new Text({
      text: met ? cell.name : '???',
      style: {
        fontSize: TEXT.mini, fill: met ? UI.ink : UI.inkDim, fontWeight: WEIGHT.normal,
        align: 'center', wordWrap: true, wordWrapWidth: CELL_X - 8,
        breakWords: true, lineHeight: 13,
      },
    })
    label.anchor.set(0.5, 0)
    label.position.set(CELL_X / 2, SIZE.jokerHeight + 4)
    raw.addChild(label)
    return raw
  }

  /**
   * 칸 하나를 그림 한 장으로 굽습니다. 그린 것은 버리고 스프라이트를 냅니다.
   *
   * **글은 굽는 배율로 맞춥니다.** 글은 렌더러의 배율로 구워지고 렌더 텍스처의 배율을
   * 따라가지 않으므로, 맞추지 않으면 배율 1로 구운 글자를 늘려 넣은 것이 됩니다 — 카드의
   * 앞면을 굽는 자리와 같은 배율(`textScale`)입니다.
   *
   * **경계는 직접 줍니다.** 카드의 마스크와 그림자가 있는 통의 경계를 세는 데 기대지
   * 않습니다. 칸의 폭과 이름 두 줄까지의 높이에 `BAKE_PAD` 를 더한 것입니다.
   */
  private bake(raw: Container): Sprite {
    const resolution = Math.min(3, Math.max(1, this.oven.density()))
    const sharpen = (node: Container): void => {
      if (node instanceof Text) node.resolution = resolution
      for (const child of node.children) sharpen(child as Container)
    }
    sharpen(raw)
    const texture = this.oven.renderer.generateTexture({
      target: raw,
      resolution,
      antialias: true,
      frame: new Rectangle(0, 0, CELL_X, LINE_H + BAKE_PAD),
    })
    // 원본 그림은 `Sprite.destroy` 의 기본값이 건드리지 않습니다 — 그것은 `art.ts` 의 것입니다.
    raw.destroy({ children: true })
    return new Sprite(texture)
  }

  /** 이 탭에서 몇 개를 만나 보았는가. **탭이 여러 묶음이면 그 묶음들을 함께 셉니다.** */
  private metCount(cells: readonly Cell[]): number {
    let count = 0
    for (const cell of cells) {
      if (seen(this.progress, cell.group, cell.id)) count++
    }
    return count
  }

  /**
   * 하나를 가리켰습니다. **뒷면은 이름도 효과도 알리지 않습니다.**
   *
   * **자리는 굴린 만큼을 더해 냅니다.** 격자 안에서의 자리를 그대로 넘기면 굴려 내린
   * 뒤에는 쪽지가 그 칸이 있던 자리에 뜹니다.
   */
  private hover(cell: Cell, met: boolean, x: number, y: number): void {
    const top = GRID_Y + y + this.scroll.content.y
    const at = { x: GRID_X + x + CELL_X / 2, top, bottom: top + SIZE.jokerHeight }
    if (!met) {
      this.tooltip.show('???', '', 0, [t('ui.collection.unseen')], at,
                        { width: WIDTH, height: HEIGHT })
      return
    }
    this.tooltip.show(cell.name, cell.kind, cell.rarity, cell.lines(), at,
                      { width: WIDTH, height: HEIGHT }, cell.cost)
  }

  /** 가리킨 칸이 커집니다. 조커 딱지와 같은 배율입니다. */
  private raise(node: Container): void {
    if (this.hoverCell === node) return
    this.lower(this.hoverCell)
    this.hoverCell = node
    // **맨 위로 옵니다.** 커지면 옆 칸과 겹치는데, 격자에 놓인 차례대로면 오른쪽 칸이 위에
    // 놓여 커진 칸의 오른쪽이 잘려 보입니다.
    node.parent?.setChildIndex(node, node.parent.children.length - 1)
  }

  private lower(node?: Container): void {
    if (!node || this.hoverCell !== node) return
    this.hoverCell = undefined
    if (!node.destroyed) node.scale.set(1)
    this.hoverGrow.snap(1)
  }

  relabel(): void {
    // 이름 정렬과 칸의 글이 말을 따르므로 세워 둔 것을 버립니다.
    this.cellsCache = undefined
    this.buildFrame()
    this.rebuild()
  }

  onClosed(): void {
    this.tooltip.hide()
    this.hold.reset()
    this.lower(this.hoverCell)
  }

  advance(seconds: number): void {
    // **떠 있지 않으면 아무것도 하지 않습니다.** 판을 닫아도 칸은 남아 있고, 그것을 매
    // 프레임 돌면 한 번 열어 본 뒤로 세션 끝까지 그 값을 냅니다.
    if (!this.view.parent) return
    // 닫혀 있는 동안 발견이 늘었으면 이제 세웁니다.
    if (this.stale) {
      this.stale = false
      this.rebuild()
    }
    this.tooltip.advance(seconds)
    this.hold.advance(seconds)
    // 가리킨 칸이 커지는 것. **격자를 다시 지으면 그 칸이 없어지므로 함께 놓습니다.**
    const over = this.hoverCell
    if (over && !over.destroyed && over.parent) {
      this.hoverGrow.target = TIP_GROW
      this.hoverGrow.advance(seconds)
      over.scale.set(this.hoverGrow.value)
    } else if (over) {
      this.hoverCell = undefined
      this.hoverGrow.snap(1)
    }
    // **굴림통은 자기 시계를 갖지 않습니다.** 판이 프레임을 넘겨줍니다 — 자기 틱커를 걸면
    // 손 시계로 세운 도구에서 화면이 멈춰 있는데도 격자만 미끄러집니다.
    this.scroll.tick(seconds)
    // 굴려서 보이는 줄이 달라졌으면 그만큼 짓습니다. **달라지지 않았으면 수 둘을 견주고
    // 끝납니다.**
    this.draw()
    // 그림이 도착한 칸만 다시 굽습니다. **격자는 그대로 둡니다.**
    if (this.artWaiting.size > 0) {
      for (const id of this.artWaiting) this.repaint(id)
      this.artWaiting.clear()
    }
    // 스켈레톤에서 그림으로 겹쳐 흐릅니다. **끝난 것은 아래의 것을 놓습니다.**
    for (let i = this.fading.length - 1; i >= 0; i--) {
      const one = this.fading[i]
      one.at += seconds / FADE
      if (one.at >= 1) {
        this.fading.splice(i, 1)
        one.to.alpha = 1
        one.from.destroy({ texture: true, textureSource: true })
        continue
      }
      // 양끝이 밋밋하지 않게 완만하게. 아래의 것은 그대로 두므로 겹치는 동안 빈 틈이 없습니다.
      one.to.alpha = one.at * one.at * (3 - 2 * one.at)
    }
  }
}

/**
 * 에디션이 무엇을 더하는가.
 *
 * **효과 표가 없습니다.** 값이 `Edition` 표의 칸에 그대로 있으므로 그 칸을 문장으로
 * 만듭니다 — 붙지 않는 칸은 적지 않습니다.
 */
function editionLines(row: { chips: number; multAdd: number; multMul: number;
                             jokerSlots: number }): string[] {
  const out: string[] = []
  if (row.chips !== 0) out.push(`+${row.chips} ${t('ui.slot.chips')}`)
  if (row.multAdd !== 0) out.push(`+${row.multAdd} ${t('ui.slot.mult')}`)
  if (row.multMul !== 10000) out.push(`×${(row.multMul / 10000).toFixed(1)} ${t('ui.slot.mult')}`)
  if (row.jokerSlots !== 0) out.push(`+${row.jokerSlots} ${t('ui.kind.joker')}`)
  return out
}
