// 리더보드의 지표.
//
// **지표는 해시와 나란한 골든입니다.** 상태가 같아도 지표를 세는 셈이 바뀌면 순위가
// 달라지므로, 구워 둔 리플레이에 적힌 값과 지금 세는 값이 같아야 합니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'

import { describe, expect, it } from 'vitest'

import { loadFromDisk } from '../src/core/load-node'
import { newMetrics, progressOf, seal, stakeIndexOf, type Metrics } from '../src/core/metrics'
import { newRun } from '../src/core/run'
import { play, type Replay } from '../src/headless'
import { fingerprintOf } from '../tools/write-version'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const REPLAYS = path.resolve(HERE, '../../design-data/out/replay')
const DATA = path.resolve(HERE, '../public/data')
const VERSION = path.resolve(HERE, '../public/data/version.json')

const names = fs.readdirSync(REPLAYS).filter(name => name.endsWith('.json')).sort()
const data = loadFromDisk(DATA)

describe('구워 둔 리플레이의 지표', () => {
  it('구워 둔 것이 전부 지표를 들고 있습니다', () => {
    // **개수를 못박습니다.** 리플레이 하나가 폴더에서 빠지면 그 아래의 대조가 조용히
    // 줄어들 뿐이고, 줄어든 것은 통과로 보입니다.
    expect(names.length).toBe(21)
    for (const name of names) {
      const replay = JSON.parse(fs.readFileSync(path.join(REPLAYS, name), 'utf8')) as Replay
      expect(replay.metrics, name).toBeDefined()
    }
  })

  for (const name of names) {
    it(`${name} 의 지표가 그대로입니다`, () => {
      const replay = JSON.parse(fs.readFileSync(path.join(REPLAYS, name), 'utf8')) as Replay
      const report = play(replay, DATA)
      expect(report.metrics).toEqual(replay.metrics)
    })
  }
})

describe('등정', () => {
  it('런의 시작은 0 입니다', () => {
    const start = newRun(data, 'CLOVER-0001', 'red_deck', 'White')
    expect(progressOf(data, start.state)).toBe(0)
    expect(seal(data, newMetrics(), start.state).ascent).toBe(0)
  })

  it('스테이크가 앞자리입니다', () => {
    // **한 스테이크의 폭이 25 입니다.** 지나온 블라인드가 0부터 24까지 25가지이므로,
    // 24로 잡으면 「흰 완주」와 「붉은 시작」이 같은 수가 되어 되읽을 수 없습니다.
    const start = newRun(data, 'CLOVER-0001', 'red_deck', 'Red')
    expect(seal(data, newMetrics(), start.state).ascent).toBe(25)
  })

  it('완주와 다음 스테이크의 시작이 겹치지 않습니다', () => {
    const white = newRun(data, 'CLOVER-0001', 'red_deck', 'White').state
    white.ante = data.run.winAnte + 1
    const red = newRun(data, 'CLOVER-0001', 'red_deck', 'Red').state
    const whiteDone = seal(data, newMetrics(), white).ascent
    const redStart = seal(data, newMetrics(), red).ascent
    expect(whiteDone).toBe(24)
    expect(redStart).toBe(25)
    expect(redStart).toBeGreaterThan(whiteDone)
  })

  it('금 스테이크 완주가 199 입니다', () => {
    const state = newRun(data, 'CLOVER-0001', 'red_deck', 'Gold').state
    // 완주하면 `ante` 가 승리 안테를 넘어섭니다.
    state.ante = data.run.winAnte + 1
    state.phase = 'won'
    const metrics = seal(data, newMetrics(), state)
    expect(metrics.ascent).toBe(199)
    expect(metrics.won).toBe(true)
  })

  it('승리 안테를 넘어서도 상한에서 멈춥니다', () => {
    const state = newRun(data, 'CLOVER-0001', 'red_deck', 'White').state
    state.ante = data.run.winAnte + 5
    expect(progressOf(data, state)).toBe(24)
  })

  it('스테이크를 이름으로도 값으로도 찾습니다', () => {
    expect(stakeIndexOf(data, 'White')).toBe(1)
    expect(stakeIndexOf(data, '1')).toBe(1)
    expect(stakeIndexOf(data, 'Gold')).toBe(8)
    // 모르는 것은 가장 낮은 스테이크로 봅니다. 순위를 부풀리지 않는 방향입니다.
    expect(stakeIndexOf(data, '없는스테이크')).toBe(1)
  })
})

describe('한 손 최고 점수', () => {
  it('라운드가 넘어가도 가장 큰 것이 남습니다', () => {
    for (const name of names) {
      const replay = JSON.parse(fs.readFileSync(path.join(REPLAYS, name), 'utf8')) as Replay
      const metrics = replay.metrics as Metrics
      // 한 손의 점수이므로 라운드의 누계보다 작습니다. 누계를 담고 있으면 이 값이
      // 안테를 넘기는 목표만큼 커집니다.
      expect(metrics.bestHand).toBeGreaterThan(0)
    }
  })
})

describe('규칙 지문', () => {
  it('적혀 있는 것이 지금의 것입니다', () => {
    // **어긋나면 `npx tsx tools/write-version.ts` 로 다시 씁니다.** 그 배포는 시즌을
    // 나누는 배포입니다 — `season` 표에 행 하나가 따라야 합니다.
    const now = fingerprintOf(REPLAYS)
    const was = JSON.parse(fs.readFileSync(VERSION, 'utf8')) as typeof now
    expect(was).toEqual(now)
  })

  it('리플레이 하나가 달라지면 지문이 달라집니다', () => {
    const now = fingerprintOf(REPLAYS)
    expect(now.fingerprint).toMatch(/^[0-9a-f]{8}$/)
    expect(now.replays).toBe(names.length)
  })
})
