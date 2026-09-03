// 리더보드를 게임에 잇는 자리.
//
// **`game.ts` 가 이것 하나만 압니다.** 로그인·판·제출·순위가 전부 여기 있으므로, 화면 쪽에
// 더해지는 것이 단추 하나와 카드 하나와 끝난 판의 한 줄입니다.
//
// **로그아웃 상태에서는 아무것도 하지 않습니다.** 게임은 혼자 하는 것이고 계정은 권유가
// 아닙니다 — 타이틀 구석이 비어 있는 것이 그 상태입니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import type { Action } from '../core/run'
import type { RunState } from '../core/state'
import { seal, type MetricsAcc } from '../core/metrics'
import { t, tf } from '../core/strings'
import * as account from '../net/session'
import * as board from '../net/leaderboard'
import type { Me } from '../net/session'
import type { Submission } from '../net/leaderboard'
import { COLOR } from '../render/theme'
import { HandlePanel, ProfilePanel } from './account'
import { LeaderboardPanel } from './leaderboard'
import type { Modals } from './modal'
import type { Toasts } from './toast'

/** 런이 끝난 뒤 화면에 얹는 한 줄. */
export interface EndLine {
  text: string
  tone: number
  /** 순위가 오른 폭. 0 이면 그대로이고, `undefined` 면 처음 오른 것입니다. */
  moved?: number
  /** 굴러 내려갈 숫자. 없으면 글만 적습니다. */
  from?: number
  to?: number
  /** 등급이 바뀌었으면 그 이름. */
  tier?: string
}

/** 랭크 런의 설정. **서버가 준 시드로만 시작합니다.** */
export interface RankedRun {
  seed: string
  deck: string
  stake: string
  pool: string
  challenge: string
}

export class LeaderboardHub {
  /** 타이틀 오른쪽 위의 카드. **로그인이면 상시로 보입니다.** */
  readonly card: MyCard

  private profile?: Me
  private ranked?: RankedRun
  private panel?: LeaderboardPanel

  /** 판을 여는 것이 게임의 다른 판과 같은 길을 지나야 합니다. */
  constructor(private readonly data: Data,
              private readonly modals: Modals,
              private readonly toasts: Toasts) {
    this.card = new MyCard(data)
    this.card.onPress = () => this.openProfile()
  }

  // -------------------------------------------------------------------------
  // 부팅
  // -------------------------------------------------------------------------

  /**
   * 게임이 뜰 때 한 번.
   *
   * **되돌아온 주소를 먼저 봅니다.** 제공자에서 돌아온 것이면 그 code 를 세션으로 바꾸는
   * 것이 다른 무엇보다 앞입니다.
   */
  async boot(): Promise<void> {
    const arrived = await account.claimFromUrl()
    // 돌아온 길이면 로그인한 것입니다 — 「정하지 않음」이 아니게 됩니다.
    if (!account.loggedIn()) {
      this.card.show(undefined)
      return
    }

    await this.refresh()

    // **이름이 없으면 그 자리에서 받습니다.** 이름 없는 계정은 순위표에 놓을 자리가
    // 없습니다.
    //
    // **로그인하고 돌아온 길이어도 리더보드를 열지 않습니다.** 로그인은 리더보드를 위한
    // 것이 아니라 계정을 위한 것이고, 다음 화면은 타이틀입니다.
    if (this.profile && this.profile.handle === '') this.askHandle(true)
    void arrived

    // 지난번에 보내지 못한 것이 있으면 지금 보냅니다.
    const sent = await board.flushPending()
    if (sent > 0) await this.refresh()
  }

  /** 계정 상태가 바뀌면 부릅니다. 화면이 이것으로 칩을 갱신합니다. */
  onAccountChanged?: () => void

  /** 서버에서 내 것을 다시 읽습니다. */
  async refresh(): Promise<void> {
    if (!account.loggedIn()) {
      this.profile = undefined
      this.card.show(undefined)
      return
    }
    try {
      this.profile = await account.me()
    } catch {
      // 알림은 `NetStatus` 가 띄웁니다. 카드는 지금 아는 것을 그대로 둡니다.
      return
    }
    this.card.show(this.profile)
    this.onAccountChanged?.()
  }

  get signedIn(): boolean {
    return account.loggedIn()
  }

  /** 지금 이름. 없으면 빈 문자열입니다. */
  get handle(): string {
    return this.profile?.handle ?? ''
  }

  /**
   * 로그아웃합니다.
   *
   * **이 기계만입니다.** 다른 기계의 세션은 그대로이고, 그것이 여러 기계에서 동시에
   * 로그인하는 것의 반쪽입니다.
   */
  async signOut(): Promise<void> {
    await account.logout()
    this.profile = undefined
    this.card.show(undefined)
    this.onAccountChanged?.()
  }

  // -------------------------------------------------------------------------
  // 판
  // -------------------------------------------------------------------------

  /**
   * 순위표를 엽니다.
   *
   * **계정이 없어도 엽니다.** 오르는 데 계정이 필요한 것이지 보는 데 필요한 것이
   * 아닙니다 — 무엇을 위해 계정을 만드는지는 그 표를 봐야 알고, 판 안의 「내 자리」 줄이
   * 거기서 계정을 연결하는 자리입니다.
   */
  openLeaderboard(): void {
    if (account.loggedIn() && this.profile && this.profile.handle === '') {
      this.askHandle(true)
      return
    }

    const panel = new LeaderboardPanel(this.data, () => {
      this.modals.close(panel)
      this.panel = undefined
    })
    panel.onProfile = handle => this.openProfile(handle)
    // 「내 자리」 줄에서 계정을 연결하겠다고 하면 로그인 화면으로 갑니다.
    panel.onNeedAccount = () => {
      this.modals.close(panel)
      this.panel = undefined
      this.onNeedLogin?.()
    }
    this.panel = panel
    this.modals.open(panel)
  }

  /** 프로필 판에서 로그아웃을 눌렀습니다. **묻는 것은 화면이 합니다.** */
  onSignOut?: () => void

  /**
   * 로그인 화면으로 보내 달라고 합니다.
   *
   * **판이 아니라 씬이므로 여기서 열지 못합니다.** 씬을 바꾸는 것은 화면의 몫이고, 허브는
   * 그것을 부탁만 합니다.
   */
  onNeedLogin?: () => void

  private openLogin(): void {
    this.onNeedLogin?.()
  }

  openProfile(handle?: string): void {
    if (!account.loggedIn()) {
      this.openLogin()
      return
    }
    const panel = new ProfilePanel(this.data, handle, () => this.modals.close(panel))
    panel.onRename = () => {
      this.modals.close(panel)
      this.askHandle(false)
    }
    panel.onSignOut = () => this.onSignOut?.()
    panel.onSignedOut = () => {
      this.profile = undefined
      this.card.show(undefined)
      this.onAccountChanged?.()
    }
    this.modals.open(panel)
  }

  private askHandle(first: boolean): void {
    const panel = new HandlePanel(first, () => this.modals.close(panel))
    panel.onDone = handle => {
      if (this.profile) this.profile.handle = handle
      this.card.show(this.profile)
      void this.refresh()
    }
    this.modals.open(panel)
    this.typing = panel
  }

  /** 글쇠를 받는 판. 켜져 있으면 `advance` 가 깜빡임을 돌립니다. */
  private typing?: HandlePanel

  // -------------------------------------------------------------------------
  // 랭크 런
  // -------------------------------------------------------------------------

  /**
   * 랭크 런의 시드를 받습니다.
   *
   * **못 받으면 `undefined` 입니다.** 그때는 그냥 시드로 시작할지 부르는 쪽이 정합니다 —
   * 서버가 없다고 게임을 못 하게 하지 않습니다.
   */
  async requestRanked(options: { deck?: string; stake?: string; pool?: string
                                 challenge?: string }): Promise<string | undefined> {
    if (!account.loggedIn()) return undefined
    try {
      const issued = await board.rankedSeed(options)
      this.ranked = {
        seed: issued.seed,
        deck: issued.deck,
        stake: issued.stake,
        pool: issued.pool,
        challenge: issued.challenge,
      }
      return issued.seed
    } catch {
      this.toasts.push(t('ui.lb.fail.title'), t('ui.lb.ranked.cannot'), COLOR.bad, 3)
      return undefined
    }
  }

  /** 이 시드가 지금의 랭크 런인가. 시드 칸에 손을 대면 어긋납니다. */
  isRanked(seed: string): boolean {
    return this.ranked !== undefined && this.ranked.seed === seed
  }

  clearRanked(): void {
    this.ranked = undefined
  }

  // -------------------------------------------------------------------------
  // 런이 끝났을 때
  // -------------------------------------------------------------------------

  /**
   * 끝난 런을 올립니다.
   *
   * **랭크 런이 아니면 아무것도 하지 않습니다.** 끝난 판이 지금과 같아야 합니다.
   */
  async finishRun(state: RunState, actions: readonly Action[],
                  acc: MetricsAcc): Promise<EndLine | undefined> {
    const ranked = this.ranked
    if (!ranked || ranked.seed !== state.seed || !account.loggedIn()) return undefined
    this.ranked = undefined

    const before = new Map((this.profile?.ranks ?? []).map(one => [one.boardId, one.rank]))
    const wasTier = this.profile?.tier ?? ''

    const run: Submission = {
      seed: ranked.seed,
      deck: ranked.deck === '' ? state.deckId : ranked.deck,
      stake: ranked.stake,
      pool: ranked.pool,
      challenge: ranked.challenge,
      actions: actions.slice(),
      fingerprint: await board.fingerprint(),
      // **순위에 쓰이지 않습니다.** 서버가 센 것과 다르면 그 사실만 기록에 남습니다.
      claimed: seal(this.data, acc, state) as unknown as Record<string, number | boolean>,
    }

    let verdict
    try {
      verdict = await board.submitRun(run)
    } catch (error) {
      const kind = error instanceof account.ApiError ? error.kind : 'unknown'
      if (kind === 'offline') {
        board.keepPending(run)
        return { text: t('ui.lb.end.later'), tone: COLOR.inkDim }
      }
      return { text: tf('ui.lb.end.rejected', { why: t(account.failKey(error)) }),
               tone: COLOR.inkDim }
    }

    if (verdict.status === 'pending') {
      // **아직 세지 못한 것은 실패가 아닙니다.** 다음에 타이틀로 돌아가면 순위가 갱신되어
      // 있습니다 — 서버는 이미 받아 두었습니다.
      return { text: t('ui.lb.end.judging'), tone: COLOR.inkDim }
    }

    if (verdict.status === 'rejected') {
      // **붉지 않습니다. 벌이 아닙니다.**
      return { text: tf('ui.lb.end.rejected', { why: t(`ui.lb.fail.${verdict.reason}`) }),
               tone: COLOR.inkDim }
    }

    await this.refresh()
    return this.lineFor(before, wasTier)
  }

  /**
   * 무엇이 얼마나 올랐는가.
   *
   * **가장 많이 오른 하나만 고릅니다.** 보드가 여럿이므로 하나씩 다 연출하면 끝난 판이
   * 30초가 됩니다 — 나머지는 부르는 쪽이 작은 글로 적습니다.
   */
  private lineFor(before: Map<string, number>, wasTier: string): EndLine | undefined {
    const now = this.profile?.ranks ?? []
    if (now.length === 0) return undefined

    let best: { name: string; rank: number; from?: number; moved: number } | undefined
    for (const rank of now) {
      const was = before.get(rank.boardId)
      const moved = was === undefined ? Number.POSITIVE_INFINITY : was - rank.rank
      if (moved <= 0 && was !== undefined) continue
      if (!best || moved > best.moved) {
        best = { name: rank.name, rank: rank.rank, from: was, moved }
      }
    }

    const tier = this.profile?.tier ?? ''
    const tierChanged = tier !== wasTier && tier !== '' && tier !== 'None'

    if (!best) {
      const first = now[0]
      return {
        text: tf('ui.lb.end.same', { name: first.name, rank: first.rank }),
        tone: COLOR.inkDim,
        moved: 0,
        tier: tierChanged ? tier : undefined,
      }
    }

    if (best.from === undefined) {
      return {
        text: tf('ui.lb.end.first', { name: best.name, rank: best.rank }),
        tone: COLOR.money,
        tier: tierChanged ? tier : undefined,
      }
    }

    return {
      text: tf('ui.lb.end.up', { name: best.name, rank: best.rank, by: best.moved }),
      tone: COLOR.money,
      moved: best.moved,
      from: best.from,
      to: best.rank,
      tier: tierChanged ? tier : undefined,
    }
  }

  /** 나머지 보드의 자리. 끝난 판의 작은 글입니다. */
  otherRanks(limit = 3): string {
    const now = this.profile?.ranks ?? []
    return now.slice(0, limit)
      .map(one => `${one.name} #${one.rank}`)
      .join(' · ')
  }

  // -------------------------------------------------------------------------

  advance(seconds: number): void {
    this.typing?.advance(seconds)
    this.card.advance(seconds)
  }

  relabel(): void {
    this.card.show(this.profile)
    this.panel?.relabel()
  }
}

// ---------------------------------------------------------------------------
// 내 카드
// ---------------------------------------------------------------------------

/** **타이틀의 계정 자리와 같은 크기입니다.** 그 자리에 놓이므로 거기서 정한 값입니다. */
const CARD_W = 200
const CARD_H = 72

/**
 * 제목 화면 오른쪽 위에 **항상** 있는 작은 판.
 *
 * 게임을 켤 때마다 자기 자리를 봅니다 — 리더보드 판을 열지 않아도 됩니다.
 * **로그아웃이면 아무것도 없습니다.** 「로그인하세요」를 두지 않습니다.
 */
export class MyCard extends Container {
  private readonly body = new Container()
  private glow = 0

  onPress?: () => void

  constructor(private readonly data: Data) {
    super()
    this.addChild(this.body)
    this.visible = false
    this.eventMode = 'static'
    this.cursor = 'pointer'
    this.on('pointertap', () => this.onPress?.())
  }

  show(profile: Me | undefined): void {
    this.body.removeChildren().forEach(child => child.destroy({ children: true }))
    this.visible = profile !== undefined
    if (!profile) return

    const plate = new Graphics()
    plate.roundRect(0, 0, CARD_W, CARD_H, 10)
      .fill({ color: 0x151d2a, alpha: 0.9 })
      .stroke({ color: 0x2c3849, width: 1.5 })
    this.body.addChild(plate)

    const tierRow = this.data.tables.tier.records.find(one =>
      one.name === profile.tier || String(one.tier) === profile.tier)
    const color = tierRow ? Number.parseInt(tierRow.color.slice(1), 16) : 0x6f7d90

    const hasTier = profile.tier !== '' && profile.tier !== 'None'

    // 1줄 — 배지와 이름.
    if (hasTier) {
      const badge = new Graphics()
      badge.moveTo(0, -5).lineTo(5, 0).lineTo(0, 5).lineTo(-5, 0).closePath().fill(color)
      badge.position.set(19, 20)
      this.body.addChild(badge)
    }

    const name = new Text({
      text: profile.handle,
      style: { fontSize: 15, fill: COLOR.ink, fontWeight: '800' },
    })
    name.anchor.set(0, 0.5)
    name.position.set(hasTier ? 32 : 14, 20)
    this.body.addChild(name)

    // 2줄 — 등급과 등정 순위. **등급의 근거인 보드이므로 이것이 첫 줄입니다.**
    const ascent = profile.ranks.find(one => one.metric === 'Ascent')
    const tierName = profile.tier === '' || profile.tier === 'None'
      ? t('ui.lb.card.noTier')
      : tierRow?.name ?? profile.tier

    const second = ascent
      ? `${tierName} · ${boardLabel2(ascent.name)} #${ascent.rank}`
      : t('ui.lb.noRecord')
    const line2 = new Text({
      text: second,
      style: { fontSize: 12, fill: color },
    })
    line2.anchor.set(0, 0.5)
    line2.position.set(14, 40)
    this.body.addChild(line2)

    // 3줄 — 그 밖에서 내 순위가 가장 높은 것 하나. **자랑할 것이 있으면 그것이 보입니다.**
    const other = profile.ranks
      .filter(one => one.metric !== 'Ascent')
      .sort((one, two) => one.rank - two.rank)[0]
    if (other) {
      const line3 = new Text({
        text: `${boardLabel2(other.name)} #${other.rank}`,
        style: { fontSize: 11, fill: 0x8a99ad },
      })
      line3.anchor.set(0, 0.5)
      line3.position.set(14, 58)
      this.body.addChild(line3)
    }
  }

  /** 눌렀을 때 잠깐 밝아집니다. */
  advance(seconds: number): void {
    if (!this.visible) return
    this.glow = Math.max(0, this.glow - seconds * 2)
    this.body.alpha = 0.92 + this.glow * 0.08
  }
}

/** 시트의 이름은 기획자가 읽는 것이라 길 수 있습니다. 카드에서는 줄입니다. */
function boardLabel2(name: string): string {
  return name.length > 10 ? `${name.slice(0, 9)}…` : name
}
