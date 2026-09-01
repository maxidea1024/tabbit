// 로딩 씬으로 가는 창구.
//
// **씬은 `index.html` 안에 있습니다.** 여기서 읽는 것 중에 글꼴이 있어서, 글꼴이 있어야
// 그릴 수 있는 것으로는 그 화면을 그릴 수 없습니다 — 그래서 그 하나만 DOM 이고, 글과 다시
// 읽기 감시도 거기 있습니다. 이 파일은 「지금 몇째를 읽는 중」을 넘기는 것뿐입니다.

import type { Scene } from '../render/scene'

/** 읽는 차례. `index.html` 의 글과 같은 순서입니다. */
export type Step = 'data' | 'font' | 'art'

const ORDER: Step[] = ['data', 'font', 'art']

interface Bridge {
  at(index: number): void
  fail(text: string): void
  done(): void
}

/** 로딩 씬. 셋 중 첫째입니다. */
export class Boot {
  readonly scene: Scene = 'loading'

  private get bridge(): Bridge | undefined {
    return (window as unknown as { __boot?: Bridge }).__boot
  }

  /** 지금 무엇을 읽는 중인지 적습니다. */
  step(which: Step): void {
    this.bridge?.at(ORDER.indexOf(which))
  }

  /** 읽지 못했습니다. **빈 화면으로 두지 않습니다.** */
  fail(text: string): void {
    this.bridge?.fail(text)
  }

  /** 다 읽었습니다. 타이틀에 자리를 넘깁니다. */
  done(): void {
    this.bridge?.done()
  }
}
