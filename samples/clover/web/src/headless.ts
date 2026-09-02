// 화면 없이 도는 러너.
//
// **코어가 화면을 모르므로 이것이 가능합니다.** 리플레이를 먹여 상태 해시를 내고, 그 해시가
// 유니티 쪽과 같아야 합니다 — 그것이 이 샘플의 판정 기준입니다.
//
//     npm run headless -- --replay <파일>
//     npm run headless -- --seed CLOVER-0001 --random 200

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'

import { ShopItemKind } from './generated/enums/shop-item-kind'
import type { Data } from './core/data'
import { bestHand } from './core/suggest'
import { loadFromDisk } from './core/load-node'
import { snapshotHash } from './core/hash'
import { JokerPool } from './generated/enums/joker-pool'
import { apply, newRun, type Action } from './core/run'
import { newMetrics, observe, seal, type Metrics } from './core/metrics'
import type { CardInstance, RunState } from './core/state'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const DATA = path.resolve(HERE, '..', 'public', 'data')

export interface Replay {
  seed: string
  deck: string
  stake: string
  /**
   * 이 런의 챌린지. **없으면 챌린지가 아닙니다.**
   *
   * 옵셔널인 것은 구워 둔 리플레이를 그대로 두기 위해서입니다 — 챌린지는 런 설정이고
   * 해시에 들어가지 않으므로, 이 칸이 비면 예전 리플레이가 같은 해시를 다시 냅니다.
   */
  challenge?: string
  actions: Action[]
  /** 액션마다의 상태 해시. 갈라진 지점을 이분해서 찾는 데 씁니다. */
  hashes?: string[]
  /**
   * 이 런의 지표.
   *
   * **해시와 나란한 골든입니다.** 해시는 상태가 같은지를 보고 이것은 그 상태에서 뽑아낸
   * 값이 같은지를 봅니다 — 지표를 세는 셈만 바뀌면 해시는 그대로이고 순위만 달라집니다.
   */
  metrics?: Metrics
}

export interface RunReport {
  seed: string
  deck: string
  stake: string
  actions: number
  phase: string
  ante: number
  money: number
  finalHash: string
  hashes: string[]
  customs: string[]
  /**
   * 리더보드의 지표.
   *
   * **골든에 함께 적힙니다** — 지표를 세는 셈이 바뀌면 해시가 그대로여도 순위가 달라지므로,
   * 해시 옆에 값이 있어야 그것이 보입니다.
   */
  metrics: Metrics
}

/** 리플레이 하나를 돌립니다. */
export function play(replay: Replay, dataPath = DATA): RunReport {
  const data = loadFromDisk(dataPath)
  const start = newRun(data, replay.seed, replay.deck, replay.stake,
                       [JokerPool.Base], replay.challenge ?? '')
  let state: RunState = start.state

  const hashes = [snapshotHash(state)]
  const customs: string[] = []
  const acc = newMetrics()
  observe(acc, start.events)

  for (const action of replay.actions) {
    const step = apply(data, state, action)
    state = step.state
    observe(acc, step.events)
    hashes.push(snapshotHash(state))
    if (state.phase === 'won' || state.phase === 'lost') break
  }

  return {
    seed: replay.seed,
    deck: replay.deck,
    stake: replay.stake,
    actions: replay.actions.length,
    phase: state.phase,
    ante: state.ante,
    money: state.money,
    finalHash: hashes[hashes.length - 1],
    hashes,
    customs,
    metrics: seal(data, acc, state),
  }
}

/**
 * 무작위로 두는 러너.
 *
 * **리플레이는 손으로 만들지 않습니다.** 이것에 시드를 먹여 완주하는 것을 골라내고, 그것을
 * 파일로 굽습니다. 그러면 리플레이가 코어를 따라 늘어납니다.
 */
export function autoplay(seed: string, deck: string, stake: string, limit: number,
                        dataPath = DATA,
                        pools: JokerPool[] = [JokerPool.Base],
                        challenge = ''):
                        { replay: Replay; report: RunReport } {
  const data = loadFromDisk(dataPath)
  const start = newRun(data, seed, deck, stake, pools, challenge)
  let state = start.state

  const actions: Action[] = []
  const hashes = [snapshotHash(state)]
  const acc = newMetrics()
  observe(acc, start.events)

  for (let step = 0; step < limit; step++) {
    const action = decide(data, state)
    if (!action) break

    actions.push(action)
    const next = apply(data, state, action)
    state = next.state
    observe(acc, next.events)
    hashes.push(snapshotHash(state))
    if (state.phase === 'won' || state.phase === 'lost') break
  }

  // **챌린지가 아니면 칸을 두지 않습니다.** 빈 문자열을 적어 두면 구워 둔 리플레이의
  // 파일이 전부 달라집니다.
  const replay: Replay = {
    seed, deck, stake, actions, hashes, metrics: seal(data, acc, state),
    ...(challenge === '' ? {} : { challenge }),
  }
  return {
    replay,
    report: {
      seed, deck, stake,
      actions: actions.length,
      phase: state.phase,
      ante: state.ante,
      money: state.money,
      finalHash: hashes[hashes.length - 1],
      hashes,
      customs: [],
      metrics: seal(data, acc, state),
    },
  }
}

/**
 * 다음에 무엇을 둘 것인가.
 *
 * **잘 두려고 하지 않습니다.** 코어가 끝까지 도는지를 보는 것이 목적이므로, 좋은 수가 아니라
 * 규칙을 어기지 않는 수 하나를 고릅니다 — 다만 아무 카드나 내면 안테 1을 넘지 못하므로,
 * 낼 수 있는 조합 중 점수가 가장 높은 것을 고르는 정도는 합니다.
 */
function decide(data: Data, state: RunState): Action | undefined {
  switch (state.phase) {
    case 'blind-select':
      return { t: 'select_blind' }

    case 'shop':
      return shopMove(data, state)

    case 'round':
      return roundMove(data, state)

    default:
      return undefined
  }
}

/** 살 수 있는 것을 사고, 더 살 것이 없으면 나갑니다. */
function shopMove(data: Data, state: RunState): Action {
  // **팩이 열려 있으면 그것부터 끝냅니다** — 다 고르기 전에는 상점을 나가지 못합니다.
  const open = state.pack
  if (open) {
    const index = open.options.findIndex((item, at) => !open.taken[at] && hasRoom(state, item))
    return index >= 0 ? { t: 'pick_pack', index } : { t: 'skip_pack' }
  }

  // 행성 카드가 있으면 먼저 씁니다 — 족보 레벨이 안테를 넘기는 가장 싼 길입니다.
  const planet = state.consumables.findIndex(item => item.kind === 2)
  if (planet >= 0) return { t: 'use_consumable', index: planet }

  for (let slot = 0; slot < state.shop.cards.length; slot++) {
    const item = state.shop.cards[slot]
    if (item.cost > state.money - 1) continue
    if (item.kind === ShopItemKind.Joker && state.jokers.length >= state.rules.jokerSlots) continue
    if (item.kind !== ShopItemKind.Joker
        && state.consumables.length >= state.rules.consumableSlots) continue
    return { t: 'buy', slot }
  }

  // 팩은 카드 칸을 다 본 뒤에 봅니다. 값이 싸고 들어 있는 것이 여럿입니다.
  for (let slot = 0; slot < state.shop.packs.length; slot++) {
    const row = data.tables.boosterPack.findByPackId(state.shop.packs[slot])
    if (!row || row.cost > state.money - 1) continue
    return { t: 'buy_pack', slot }
  }

  return { t: 'leave_shop' }
}

/** 이 물건을 받을 자리가 있는가. */
function hasRoom(state: RunState, item: { kind: ShopItemKind }): boolean {
  if (item.kind === ShopItemKind.Joker) return state.jokers.length < state.rules.jokerSlots
  if (item.kind === ShopItemKind.PlayingCard) return true
  return state.consumables.length < state.rules.consumableSlots
}

/** 낼 수 있는 조합 중 가장 점수가 높은 것. 남은 핸드가 있으면 낮은 카드를 버립니다. */
function roundMove(data: Data, state: RunState): Action | undefined {
  if (state.hand.length === 0) return undefined
  if (state.handsLeft <= 0) return undefined

  const held = state.hand
    .map(uid => state.deck.find(card => card.uid === uid))
    .filter((card): card is CardInstance => card !== undefined)

  const best = bestHand(data, state, held)
  if (!best) return undefined

  // 아직 여유가 있고 지금 패가 시원찮으면 한 번 버립니다.
  const remaining = state.target - state.score
  const needed = state.handsLeft > 1 ? remaining / state.handsLeft : remaining
  if (state.discardsLeft > 0 && best.value < needed && held.length > 5) {
    const keep = new Set(best.cards.map(card => card.uid))
    const toss = held.filter(card => !keep.has(card.uid)).slice(0, 5).map(card => card.uid)
    if (toss.length > 0) return { t: 'discard', cards: toss }
  }

  return { t: 'play', cards: best.cards.map(card => card.uid) }
}

function main(argv: string[]): number {
  const arg = (name: string) => {
    const at = argv.indexOf(name)
    return at >= 0 && at + 1 < argv.length ? argv[at + 1] : undefined
  }

  const replayPath = arg('--replay')
  const out = arg('--out')

  if (replayPath) {
    const replay = JSON.parse(fs.readFileSync(replayPath, 'utf8')) as Replay
    const report = play(replay)
    if (out) fs.writeFileSync(out, JSON.stringify(report, null, 2), 'utf8')
    console.log(`${path.basename(replayPath)}  ${report.phase}  안테 ${report.ante}  ${report.finalHash}`)
    return 0
  }

  const seed = arg('--seed') ?? 'CLOVER-0001'
  const deck = arg('--deck') ?? 'red_deck'
  const stake = arg('--stake') ?? 'White'
  const limit = Number(arg('--random') ?? 200)

  // `--expansion` 없으면 기본 150종만 돕니다 — 리플레이를 굽는 것이 이 경로이므로
  // 기본값이 바뀌면 굽힌 리플레이가 한번에 어긋납니다.
  const pools = argv.includes('--expansion')
    ? [JokerPool.Base, JokerPool.Greenhouse]
    : [JokerPool.Base]

  const { replay, report } = autoplay(seed, deck, stake, limit, DATA, pools)
  if (out) fs.writeFileSync(out, JSON.stringify(replay, null, 2), 'utf8')
  console.log(`${seed}  ${report.phase}  안테 ${report.ante}  액션 ${report.actions}  ${report.finalHash}`)
  return 0
}

if (process.argv[1] && import.meta.url.endsWith(path.basename(process.argv[1]))) {
  process.exitCode = main(process.argv.slice(2))
}
