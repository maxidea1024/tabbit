// 안드로이드에서 몇 프레임이 나오는가.
//
// **짐작하지 않고 그 기계에서 잰 값을 씁니다.** 디버그 빌드의 WebView 는 원격 디버깅이
// 열려 있으므로, `adb forward` 한 포트의 페이지에 붙어 그 안의 시계를 읽습니다.
//
//     adb shell cat /proc/net/unix | grep -o "webview_devtools_remote_[0-9]*"
//     adb forward tcp:9333 localabstract:webview_devtools_remote_<pid>
//     npx tsx tools/check-android.ts
//
// **플레이라이트로 붙지 않습니다.** 안드로이드 WebView 의 브라우저 종점은 플레이라이트가
// 기대하는 대상 붙이기를 지원하지 않아 그대로 끊깁니다 — 페이지 종점에 곧바로 붙어
// `Runtime.evaluate` 둘을 보내는 것이 전부입니다.

const PORT = Number(process.env.CLOVER_CDP ?? 9333)
/** 첫 몇 프레임은 아직 자리를 잡는 중이라 버립니다. */
const WARMUP = 1500
const SPAN = 4000

interface Frame {
  id?: number
  result?: { result?: { value?: unknown } }
  error?: { message?: string }
}

async function main(): Promise<number> {
  const list = await (await fetch(`http://localhost:${PORT}/json/list`)).json() as
    { webSocketDebuggerUrl?: string; url?: string }[]
  const target = list.find(one => one.webSocketDebuggerUrl)
  if (!target?.webSocketDebuggerUrl) {
    console.log('붙을 페이지가 없습니다 — adb forward 를 확인하십시오')
    return 1
  }
  console.log('주소', target.url)

  const socket = new WebSocket(target.webSocketDebuggerUrl)
  await new Promise<void>((resolve, reject) => {
    socket.addEventListener('open', () => resolve())
    socket.addEventListener('error', () => reject(new Error('붙지 못했습니다')))
  })

  let next = 1
  const waiting = new Map<number, (frame: Frame) => void>()
  socket.addEventListener('message', event => {
    const frame = JSON.parse(String(event.data)) as Frame
    if (frame.id === undefined) return
    waiting.get(frame.id)?.(frame)
    waiting.delete(frame.id)
  })

  /** 페이지 안에서 한 줄을 재고 그 값을 받아 옵니다. */
  const evaluate = async (expression: string): Promise<unknown> => {
    const id = next++
    const answer = new Promise<Frame>(resolve => waiting.set(id, resolve))
    socket.send(JSON.stringify({
      id, method: 'Runtime.evaluate',
      params: { expression, awaitPromise: true, returnByValue: true },
    }))
    const frame = await answer
    if (frame.error) throw new Error(frame.error.message ?? 'CDP 오류')
    return frame.result?.result?.value
  }

  const size = await evaluate(
    '({ w: innerWidth, h: innerHeight, dpr: devicePixelRatio })') as
    { w: number; h: number; dpr: number }
  console.log(`화면 ${size.w} x ${size.h} · 픽셀 밀도 ${size.dpr}`)
  console.log(`실제로 칠하는 픽셀 ${Math.round(size.w * size.dpr)} x ${Math.round(size.h * size.dpr)}`)

  const fps = await evaluate(`(async () => {
    const wait = ms => new Promise(r => setTimeout(r, ms))
    let frames = 0, running = true
    const tick = () => { frames++; if (running) requestAnimationFrame(tick) }
    requestAnimationFrame(tick)
    await wait(${WARMUP})
    frames = 0
    const began = performance.now()
    await wait(${SPAN})
    const took = performance.now() - began
    running = false
    return { frames, took }
  })()`) as { frames: number; took: number }

  console.log(`프레임 ${fps.frames}개 / ${Math.round(fps.took)}ms = ` +
    `${(fps.frames / (fps.took / 1000)).toFixed(1)} fps`)

  socket.close()
  return 0
}

main().then(code => process.exit(code)).catch((error: unknown) => {
  console.log(String(error))
  process.exit(1)
})
