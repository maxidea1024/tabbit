// 콜렉션 — 물건 전부의 도감.
//
// **판 하나에 갈래 9개입니다.** 조커만 볼 수 있는 판이 따로 서 있었고, 그러면 소모품과
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

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import { seen, type CollectionGroup, type CollectionProgress } from '../core/collection'
import { describe, handDisplay } from '../core/describe'
import { poolsOf, type PoolChoice } from '../core/pool'
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
import { COLOR, SIZE, UI } from '../render/theme'
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

/** 칸 하나가 실제로 차지하는 높이. 카드와 그 아래 이름 두 줄입니다. */
const LINE_H = SIZE.jokerHeight + 30

/** 동그란 얼굴로 그리는 묶음들. 카드와 원점이 다릅니다. */
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

/** 칸 하나. **얼굴은 그릴 때 만듭니다** — 500장을 미리 만들면 판을 여는 데 그만큼 걸립니다. */
interface Cell {
  group: CollectionGroup
  id: string
  name: string
  kind: string
  lines: string[]
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

  private readonly foundLabel = new Text({
    text: '', style: { fontSize: 15, fill: COLOR.ink, fontWeight: '800' },
  })
  private readonly hint = new Text({
    text: '', style: { fontSize: 13, fill: COLOR.inkDim },
  })

  private readonly tabButtons: { key: TabKey; button: Button; label: string }[] = []
  private readonly rangeButtons: { choice: PoolChoice; button: Button; key: string }[] = []
  private readonly sortButtons: { key: SortKey; button: Button; label: string }[] = []
  private order?: Button
  private frame?: Container

  private views: Container[] = []
  private tab: TabKey = 'joker'
  /** 지금 지어 둔 줄의 범위. 굴려서 이 밖으로 나가면 다시 짓습니다. */
  private built = { from: -1, to: -1 }
  private sort: SortKey = 'order'
  private ascending = true
  /** 조커 탭에서 무엇까지 보는가. **옵션을 바꾸지 않습니다** — 보는 범위일 뿐입니다. */
  private range: PoolChoice
  /** 다음에 세울 때 다시 지어야 하는가. 그림이 들어오면 켜집니다. */
  private dirty = false

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
  get census(): { tab: string; cells: number; found: number; offset: number } {
    const cells = this.cells()
    return {
      tab: this.tab, cells: cells.length, found: this.metCount(cells),
      // 얼마나 굴려 내려왔는가. **도구가 굴림이 되는지를 이 수로 봅니다** — 화면의 칸을
      // 세는 것으로는 바퀴가 도는지 손가락이 끄는지가 갈리지 않습니다.
      offset: Math.round(this.scroll.content.y),
    }
  }

  constructor(private readonly data: Data,
              private progress: CollectionProgress,
              range: PoolChoice,
              private readonly onClose: () => void) {
    this.range = range
    this.build()
    this.rebuild()

    // **그림은 늦게 들어옵니다.** 그림 하나마다 다시 세우면 한 쪽을 여는 데 카드가 수백
    // 장이므로, 표시만 남기고 다음 프레임에 한 번 세웁니다.
    onArtReady(() => { this.dirty = true })
  }

  /** 발견이 늘었습니다. **떠 있으면 그 자리에서 다시 세웁니다.** */
  setProgress(progress: CollectionProgress): void {
    this.progress = progress
    if (this.view.parent) this.rebuild()
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
      const button = new Button(t(one.label), tabW, TAB_H, UI.btn,
                                () => this.choose(one.key), 15)
      button.position.set(tabsX + index * (tabW + tabGap), TAB_Y)
      this.tabButtons.push({ key: one.key, button, label: one.label })
      this.toolNodes.set(`tab:${one.key}`, { node: button, cx: tabW / 2, cy: TAB_H / 2 })
      this.body.addChild(button)
    }

    // 조커 탭의 보는 범위. **고르는 것이 아니라 보는 것입니다** — 다음 판의 풀은 판을
    // 여는 자리에서 고릅니다.
    const rw = 150
    for (const [index, choice] of (['base', 'all'] as PoolChoice[]).entries()) {
      const key = choice === 'all' ? 'ui.pool.all' : 'ui.pool.base'
      const button = new Button(t(key), rw, HEAD_H, UI.btn,
                                () => this.setRange(choice), 15)
      button.position.set(GRID_X + index * (rw + 10), HEAD_Y)
      this.rangeButtons.push({ choice, button, key })
      this.toolNodes.set(`range:${choice}`,
                         { node: button, cx: rw / 2, cy: HEAD_H / 2 })
      this.body.addChild(button)
    }

    // 줄 세우기. **조커 탭에만 섭니다** — 500종이면 눈으로 훑어서는 찾지 못합니다.
    const sw = 70
    for (const [index, one] of SORTS.entries()) {
      const button = new Button(t(one.label), sw, HEAD_H, UI.btn,
                                () => this.sortBy(one.key), 14)
      button.position.set(480 + index * (sw + 6), HEAD_Y)
      this.sortButtons.push({ key: one.key, button, label: one.label })
      this.body.addChild(button)
    }
    this.order = new Button('', 40, HEAD_H, UI.btn, () => this.flip(), 18)
    this.order.position.set(790, HEAD_Y)
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

  private setRange(choice: PoolChoice): void {
    if (this.range === choice) return
    this.range = choice
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
   * **세워 둔 것을 다시 씁니다.** 쪽을 넘길 때마다 500행을 거르고 정렬하면, 이름 정렬은
   * 비교마다 글 표를 읽습니다.
   */
  private cells(): Cell[] {
    const key = `${this.tab}|${this.range}|${this.sort}|${this.ascending}`
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
    const pools = poolsOf(this.range)
    const rows = this.data.tables.joker.records.filter(row => pools.includes(row.pool))
    const name = (id: string, fallback: string) => nameOf(this.data, 'joker', id, fallback)

    const sorted = [...rows]
    if (this.sort === 'rarity') {
      sorted.sort((a, b) => a.rarity - b.rarity || a.sortOrder - b.sortOrder)
    } else if (this.sort === 'cost') {
      sorted.sort((a, b) => a.cost - b.cost || a.sortOrder - b.sortOrder)
    } else if (this.sort === 'name') {
      sorted.sort((a, b) => name(a.jokerId, a.name).localeCompare(name(b.jokerId, b.name)))
    } else {
      sorted.sort((a, b) => a.sortOrder - b.sortOrder)
    }
    if (!this.ascending) sorted.reverse()

    return sorted.map(row => ({
      group: 'joker' as CollectionGroup,
      id: row.jokerId,
      name: name(row.jokerId, row.name),
      kind: t(RARITY_KEYS[row.rarity] ?? ''),
      lines: describe(this.data, this.data.jokerEffects.get(row.jokerId) ?? []),
      rarity: row.rarity,
      cost: row.cost,
      face: () => this.jokerView(row.jokerId, name(row.jokerId, row.name), row.rarity),
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
        lines: describe(this.data, this.data.tarotEffects.get(row.tarotId) ?? []),
        face: () => itemFace(this.data, { kind: ShopItemKind.Tarot, id: row.tarotId }),
      })
    }
    for (const row of this.data.tables.planet.records) {
      out.push({
        group: 'planet', id: row.planetId,
        name: nameOf(this.data, 'planet', row.planetId, row.name),
        kind: t('ui.kind.planet'), rarity: 0,
        // **행성은 효과 표가 없습니다.** 어느 족보를 올리는지가 그 행성의 전부입니다.
        lines: [handDisplay(this.data, row.hand)],
        face: () => itemFace(this.data, { kind: ShopItemKind.Planet, id: row.planetId }),
      })
    }
    for (const row of this.data.tables.spectral.records) {
      out.push({
        group: 'spectral', id: row.spectralId,
        name: nameOf(this.data, 'spectral', row.spectralId, row.name),
        kind: t('ui.kind.spectral'), rarity: 0,
        lines: describe(this.data, this.data.spectralEffects.get(row.spectralId) ?? []),
        face: () => itemFace(this.data, { kind: ShopItemKind.Spectral, id: row.spectralId }),
      })
    }
    return out
  }

  private voucherCells(): Cell[] {
    // **상위는 하위 다음에 섭니다.** 16쌍이므로 표의 순서가 곧 그 짝입니다.
    return [...this.data.tables.voucher.records]
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map(row => {
        const lines = describe(this.data, this.data.voucherEffects.get(row.voucherId) ?? [])
        return {
          group: 'voucher' as CollectionGroup,
          id: row.voucherId,
          name: nameOf(this.data, 'voucher', row.voucherId, row.name),
          kind: t('ui.kind.voucher'), rarity: 0, lines,
          cost: row.cost,
          face: () => voucherFace(this.data, row.voucherId,
                                  lines[0] ?? t('ui.note.rest_of_run')),
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
        lines: describe(this.data, this.data.enhancementEffects.get(
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
        lines: describe(this.data, this.data.sealEffects.get(String(row.seal)) ?? []),
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
        lines: editionLines(row),
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
    paper.roundRect(0, 0, w, h, 9).fill(0xefe6d3)
    paper.roundRect(1, 1, w - 2, h - 2, 8).stroke({ color: UI.ink, width: 2 })
    const label = new Text({
      text: name,
      style: {
        fontSize: 14, fill: 0x2a2420, fontWeight: '900', align: 'center',
        wordWrap: true, wordWrapWidth: w - 12, breakWords: true, lineHeight: 16,
      },
    })
    label.anchor.set(0.5, 0.5)
    label.position.set(w / 2, h / 2)
    const head = new Text({
      text: kind,
      style: { fontSize: 9, fill: 0x6b6255, fontWeight: '800' },
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
      lines: [tf('ui.pack.spread', { cards: row.cards, picks: row.picks })],
      face: () => packFace(row),
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
        lines: describe(this.data, this.data.tagEffects.get(row.tagId) ?? []),
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
        lines: [t('ui.note.no_rules')],
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
        lines: describe(this.data, this.data.bossEffects.get(row.bossId) ?? []),
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
      lines: [tf('ui.stake.note', {
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
        lines: describe(this.data, this.data.deckEffects.get(row.deckId) ?? []),
        face: () => {
          const node = new Container()
          drawCardBack(node, SIZE.jokerWidth, SIZE.jokerHeight, 9, backLookOf(row))
          return node
        },
      }))
  }

  /** 아직 만나지 못한 것. 덱의 뒷면 하나와 그 위의 물음표입니다. */
  private unseenFace(): Container {
    const node = new Container()
    const w = SIZE.jokerWidth
    const h = SIZE.jokerHeight
    const back = new Container()
    drawCardBack(back, w, h, 9, { motif: 0 as never, ground: 0x1b2431, ink: 0x2f3d50 })
    const mark = new Text({
      text: '?',
      style: { fontSize: 34, fill: 0x51637c, fontWeight: '900' },
    })
    mark.anchor.set(0.5, 0.5)
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
   */
  private rebuild(): void {
    this.tooltip.hide()

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
    for (const one of this.rangeButtons) {
      one.button.text = t(one.key)
      one.button.visible = jokers
      const on = one.choice === this.range
      one.button.highlight = on
      one.button.alpha = on ? 1 : 0.55
    }
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
      .fill({ color: 0x000000, alpha: 0 })

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
   * **보이는 만큼만 짓습니다.** 확장까지 켠 조커 탭이 500칸이고, 그것을 한꺼번에 지으면
   * 탭을 누른 그 프레임에 카드 500장을 만들게 됩니다 — 화면에 서는 것은 30장 남짓입니다.
   */
  private draw(): void {
    const all = this.cells()
    const rows = Math.ceil(all.length / COLUMNS)
    const top = -this.scroll.content.y
    const from = Math.max(0, Math.floor(top / CELL_Y) - MARGIN_ROWS)
    const to = Math.min(rows - 1, Math.ceil((top + VIEW_H) / CELL_Y) + MARGIN_ROWS)
    if (from === this.built.from && to === this.built.to) return
    this.built = { from, to }
    this.clearCells()

    for (let row = from; row <= to; row++) {
      for (let column = 0; column < COLUMNS; column++) {
        const index = row * COLUMNS + column
        const cell = all[index]
        if (!cell) break
        this.grid.addChild(this.cellNode(cell, column * CELL_X, row * CELL_Y))
      }
    }
  }

  /** 지어 둔 칸을 치웁니다. */
  private clearCells(): void {
    for (const view of this.views) view.destroy({ children: true })
    this.views = []
    this.grid.removeChildren()
  }

  /** 칸 하나. 얼굴과 이름과 누르는 자리입니다. */
  private cellNode(cell: Cell, x: number, y: number): Container {
    const met = seen(this.progress, cell.group, cell.id)
    const node = new Container()
    const face = met ? cell.face() : this.unseenFace()
    // **동그란 얼굴은 가운데를 원점으로 그립니다.** 카드는 왼쪽 위이므로 자리가 갈립니다 —
    // 칸 안에서 가운데로 모으는 것은 여기 한 곳입니다.
    if (met && ROUND_GROUPS.includes(cell.group)) {
      face.position.set(CELL_X / 2, SIZE.jokerHeight / 2)
    } else {
      face.position.set((CELL_X - SIZE.jokerWidth) / 2, 0)
    }
    node.addChild(face)

    const label = new Text({
      text: met ? cell.name : '???',
      style: {
        fontSize: 11, fill: met ? COLOR.ink : COLOR.inkDim, fontWeight: '700',
        align: 'center', wordWrap: true, wordWrapWidth: CELL_X - 8,
        breakWords: true, lineHeight: 13,
      },
    })
    label.anchor.set(0.5, 0)
    label.position.set(CELL_X / 2, SIZE.jokerHeight + 4)
    node.addChild(label)

    node.position.set(x, y)
    node.eventMode = 'static'
    node.cursor = 'pointer'
    // 손가락에는 마우스 오버가 없으므로 누르는 것도 같은 일을 합니다. **굴린 것은 누른
    // 것이 아닙니다** — 격자가 판을 가득 채우므로, 보지 않으면 굴릴 때마다 쪽지가 뜹니다.
    node.on('pointerover', () => {
      if (this.scroll.holding) return
      this.hover(cell, met, x, y)
    })
    node.on('pointertap', () => {
      if (this.scroll.dragged) return
      this.hover(cell, met, x, y)
    })
    node.on('pointerout', () => this.tooltip.hide())
    this.views.push(node)
    return node
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
    this.tooltip.show(cell.name, cell.kind, cell.rarity, cell.lines, at,
                      { width: WIDTH, height: HEIGHT }, cell.cost)
  }

  relabel(): void {
    // 이름 정렬과 칸의 글이 말을 따르므로 세워 둔 것을 버립니다.
    this.cellsCache = undefined
    this.buildFrame()
    this.rebuild()
  }

  onClosed(): void {
    this.tooltip.hide()
  }

  advance(seconds: number): void {
    // **떠 있지 않으면 아무것도 하지 않습니다.** 판을 닫아도 칸은 남아 있고, 그것을 매
    // 프레임 돌면 한 번 열어 본 뒤로 세션 끝까지 그 값을 냅니다.
    if (!this.view.parent) return
    this.tooltip.advance(seconds)
    // **굴림통은 자기 시계를 갖지 않습니다.** 판이 프레임을 넘겨줍니다 — 자기 틱커를 걸면
    // 손 시계로 세운 도구에서 화면이 멈춰 있는데도 격자만 미끄러집니다.
    this.scroll.tick(seconds)
    // 굴려서 보이는 줄이 달라졌으면 그만큼 짓습니다. **달라지지 않았으면 수 둘을 견주고
    // 끝납니다.**
    this.draw()
    if (this.dirty) {
      this.dirty = false
      this.built = { from: -1, to: -1 }
      this.draw()
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
