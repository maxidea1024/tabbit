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

import { PokerHandKind } from './generated/enums/poker-hand-kind'
import { ShopItemKind } from './generated/enums/shop-item-kind'
import type { Data } from './core/data'
import { evaluate } from './core/hand'
import { loadFromDisk } from './core/load-node'
import { snapshotHash } from './core/hash'
import { apply, newRun, type Action } from './core/run'
import type { CardInstance, RunState } from './core/state'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const DATA = path.resolve(HERE, '..', 'public', 'data')

export interface Replay {
  seed: string
  deck: string
  stake: string
  actions: Action[]
  /** 액션마다의 상태 해시. 갈라진 지점을 이분해서 찾는 데 씁니다. */
  hashes?: string[]
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
}

/** 리플레이 하나를 돌립니다. */
export function play(replay: Replay, dataPath = DATA): RunReport {
  const data = loadFromDisk(dataPath)
  const start = newRun(data, replay.seed, replay.deck, replay.stake)
  let state: RunState = start.state

  const hashes = [snapshotHash(state)]
  const customs: string[] = []

  for (const action of replay.actions) {
    const step = apply(data, state, action)
    state = step.state
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
  }
}

/**
 * 무작위로 두는 러너.
 *
 * **리플레이는 손으로 만들지 않습니다.** 이것에 시드를 먹여 완주하는 것을 골라내고, 그것을
 * 파일로 굽습니다. 그러면 리플레이가 코어를 따라 늘어납니다.
 */
export function autoplay(seed: string, deck: string, stake: string, limit: number,
                        dataPath = DATA): { replay: Replay; report: RunReport } {
  const data = loadFromDisk(dataPath)
  const start = newRun(data, seed, deck, stake)
  let state = start.state

  const actions: Action[] = []
  const hashes = [snapshotHash(state)]

  for (let step = 0; step < limit; step++) {
    const action = decide(data, state)
    if (!action) break

    actions.push(action)
    state = apply(data, state, action).state
    hashes.push(snapshotHash(state))
    if (state.phase === 'won' || state.phase === 'lost') break
  }

  const replay: Replay = { seed, deck, stake, actions, hashes }
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
function shopMove(_data: Data, state: RunState): Action {
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

  return { t: 'leave_shop' }
}

/** 낼 수 있는 조합 중 가장 점수가 높은 것. 남은 핸드가 있으면 낮은 카드를 버립니다. */
function roundMove(data: Data, state: RunState): Action | undefined {
  if (state.hand.length === 0) return undefined
  if (state.handsLeft <= 0) return undefined

  const held = state.hand
    .map(uid => state.deck.find(card => card.uid === uid))
    .filter((card): card is CardInstance => card !== undefined)

  const best = bestPlay(data, state, held)
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

/** 패에서 고를 수 있는 조합을 전부 보고 가장 좋은 것을 돌려줍니다. */
function bestPlay(data: Data, state: RunState, held: CardInstance[]) {
  let best: { cards: CardInstance[]; value: number } | undefined

  for (const subset of subsets(held, Math.min(5, data.run.maxPlayedCards))) {
    const { hand } = evaluate(subset, state.rules)
    const row = data.tables.pokerHand.findByHand(hand)
    if (!row) continue

    const level = state.handLevels[PokerHandKind[hand]] ?? 1
    const chips = row.baseChips + row.chipsPerLevel * (level - 1)
    const mult = row.baseMult + row.multPerLevel * (level - 1)
    const value = chips * mult

    if (!best || value > best.value) best = { cards: subset, value }
  }

  return best
}

/** 1장에서 `max` 장까지의 조합. 패가 8장이므로 218가지입니다. */
function* subsets(cards: CardInstance[], max: number): Generator<CardInstance[]> {
  const total = 1 << cards.length
  for (let mask = 1; mask < total; mask++) {
    const chosen: CardInstance[] = []
    for (let i = 0; i < cards.length; i++) {
      if (mask & (1 << i)) chosen.push(cards[i])
    }
    if (chosen.length <= max) yield chosen
  }
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

  const { replay, report } = autoplay(seed, deck, stake, limit)
  if (out) fs.writeFileSync(out, JSON.stringify(replay, null, 2), 'utf8')
  console.log(`${seed}  ${report.phase}  안테 ${report.ante}  액션 ${report.actions}  ${report.finalHash}`)
  return 0
}

if (process.argv[1] && import.meta.url.endsWith(path.basename(process.argv[1]))) {
  process.exitCode = main(process.argv.slice(2))
}
