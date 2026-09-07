// 독립 실행 창.
//
// **웹 빌드를 그대로 담습니다.** 게임의 코드는 한 줄도 다르지 않고, 이 파일이 하는 일은 창을
// 하나 띄우고 그 안에 `web/dist` 를 얹는 것뿐입니다 — 그래야 웹과 독립형이 갈라지지 않습니다.
//
// `file://` 로 열지 않는 것이 요점입니다. 게임은 테이블 40개와 그림 202장을 `fetch` 로 읽는데
// 크로미움은 `file://` 에서의 `fetch` 를 막습니다. 그래서 사설 스킴 하나를 등록하고 그 스킴의
// 요청을 파일로 돌려줍니다. **그러면 웹에서 쓰는 상대 경로가 그대로 맞습니다.**

const { app, BrowserWindow, Menu, protocol, shell } = require('electron')
const fs = require('node:fs')
const path = require('node:path')
const { Readable } = require('node:stream')

/**
 * 웹 빌드가 있는 곳. 묶으면 `resources/web` 이 됩니다.
 *
 * **`resources/app` 에 두면 안 됩니다.** 일렉트론은 그 이름의 폴더를 앱 자체로 보고
 * `app.asar` 보다 먼저 집으며, 그 안에 `package.json` 이 없으므로 창도 뜨지 않고
 * 조용히 0으로 끝납니다.
 */
const ROOT = app.isPackaged
  ? path.join(process.resourcesPath, 'web')
  : path.join(__dirname, '..', 'web', 'dist')

const SCHEME = 'clover'

/**
 * 판의 크기. `web/src/render/theme.ts` 의 `SIZE.width` · `SIZE.height` 와 같은 값입니다.
 *
 * **창의 비율이 이것입니다.** 판은 이 사각형을 짧은 쪽에 맞춰 넣고 남는 자리는 잘라 낸
 * 자리로 두므로, 창이 이 비율이면 남는 자리가 없습니다.
 */
const BOARD_W = 1280
const BOARD_H = 800

/**
 * 확장자마다의 갈래.
 *
 * **직접 적습니다.** 토막을 돌려주려면 응답을 손으로 만들어야 하고, 그러면 갈래도 손으로
 * 붙여야 합니다 — 크로미움은 갈래가 없으면 그 파일을 무엇으로 다룰지 모릅니다.
 */
const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.gif': 'image/gif',
  '.ico': 'image/x-icon',
  '.ogg': 'audio/ogg',
  '.oga': 'audio/ogg',
  '.mp3': 'audio/mpeg',
  '.wav': 'audio/wav',
  '.mp4': 'video/mp4',
  '.webm': 'video/webm',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.otf': 'font/otf',
  '.wasm': 'application/wasm',
}

/** 그 파일의 갈래. 모르는 것은 바이트 덩어리입니다. */
function typeOf(file) {
  return TYPES[path.extname(file).toLowerCase()] ?? 'application/octet-stream'
}

/**
 * `Range` 헤더를 읽습니다. **없거나 알아듣지 못하면 `null` 입니다.**
 *
 * `bytes=100-200` · `bytes=100-`(그 뒤 전부) · `bytes=-200`(마지막 200바이트) 셋입니다.
 * 여러 토막을 한 번에 요구하는 형태는 쓰지 않으므로 받지 않습니다 — 미디어 원소는 늘
 * 한 토막만 요구합니다.
 */
function rangeOf(header, size) {
  if (typeof header !== 'string') return null
  const found = /^bytes=(\d*)-(\d*)$/.exec(header.trim())
  if (!found) return null
  const [, from, to] = found
  if (from === '' && to === '') return null
  if (from === '') {
    const want = Number(to)
    if (want <= 0) return { bad: true }
    return { start: Math.max(0, size - want), end: size - 1 }
  }
  const start = Number(from)
  const end = to === '' ? size - 1 : Math.min(Number(to), size - 1)
  if (start > end || start >= size) return { bad: true }
  return { start, end }
}

// 사설 스킴을 표준 스킴으로 등록합니다. **이것이 없으면 상대 경로와 `fetch` 가 동작하지
// 않습니다** — 표준이 아닌 스킴은 오리진이 없는 것으로 다뤄지기 때문입니다.
protocol.registerSchemesAsPrivileged([{
  scheme: SCHEME,
  privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true },
}])

/**
 * 소리를 이 프로세스 안에서 냅니다.
 *
 * **크로미움은 소리를 별도의 오디오 서비스 프로세스에서 냅니다.** 그 프로세스는 출력 장치가
 * 바뀌면 스트림을 다시 엽니다 — 원격 데스크탑은 세션이 붙고 떨어질 때마다 그것을 바꾸고,
 * 그때 스트림이 아무 데도 가지 않는 자리로 열리는 일이 있습니다. **소리 길은 `running`
 * 이고 표본율도 채널 수도 멀쩡한데 아무것도 들리지 않습니다** — `destination` 에 곧바로
 * 붙인 오실레이터까지 안 들리므로 게임 쪽에서 고칠 것이 없습니다.
 *
 * 한 프로세스 안에서 내면 그 갈아 끼우기를 지나지 않습니다.
 *
 * **`app.whenReady()` 앞이어야 합니다.** 스위치는 소리 길이 세워지기 전에 읽힙니다.
 */
app.commandLine.appendSwitch('disable-features', 'AudioServiceOutOfProcess,AudioServiceSandbox')

/**
 * 화면을 굽고 끝냅니다.
 *
 * **웹의 `tools/shoot.ts` 와 같은 이유로 있습니다** — 창이 뜨는 것과 그 안이 제대로 그려지는
 * 것은 다른 일이고, 사설 스킴으로 테이블 40개와 그림 202장을 읽는 경로는 눌러 보지 않으면
 * 확인되지 않습니다.
 *
 *     electron . --shot ../design-data/out/shot/17-desktop.png
 */
function shotPathFromArgv(argv) {
  // **묶은 실행 파일에서는 환경 변수로 받습니다.** 크로미움이 모르는 `--` 스위치를 우리
  // 코드가 돌기 전에 거절하므로, 묶은 것을 확인할 길이 인자만으로는 없습니다.
  if (process.env.CLOVER_SHOT) return process.env.CLOVER_SHOT

  const at = argv.findIndex(arg => arg === '--shot')
  if (at >= 0 && at + 1 < argv.length) return argv[at + 1]
  const inline = argv.find(arg => arg.startsWith('--shot='))
  return inline ? inline.slice('--shot='.length) : undefined
}

/**
 * 개발자 도구를 켠 채로 열 것인가.
 *
 * **키에만 의존하지 않습니다.** `before-input-event` 는 창으로 들어온 실제 입력에만 서고,
 * 검증 도구가 CDP 로 넣는 키는 그 자리를 지나지 않습니다 — 그래서 키가 도는지를 자동으로
 * 확인할 길이 없습니다. 이 길이 있으면 도구가 확인할 수 있고, 키가 막힌 기계에서도 콘솔을
 * 볼 수 있습니다.
 *
 *     CLOVER_DEVTOOLS=1 clover.exe
 */
function devToolsWanted(argv) {
  if (process.env.CLOVER_DEVTOOLS) return true
  return argv.includes('--devtools')
}

/** 주소에서 시드를 받습니다. 같은 시드는 같은 판이므로 대조할 때 씁니다. */
function seedFromArgv(argv) {
  if (process.env.CLOVER_SEED) return process.env.CLOVER_SEED

  const at = argv.findIndex(arg => arg === '--seed')
  if (at >= 0 && at + 1 < argv.length) return argv[at + 1]
  const inline = argv.find(arg => arg.startsWith('--seed='))
  return inline ? inline.slice('--seed='.length) : undefined
}

/**
 * 환희의 문턱을 내려서 엽니다. **구운 창에는 주소창이 없습니다.**
 *
 * 40만은 안티 3~4에서 나오는 값이라, 연출이 도는 것을 보려면 그때까지 판을 두어야 합니다.
 *
 *     clover.exe --euphoria=100
 *     CLOVER_EUPHORIA=100 clover.exe
 */
function euphoriaFromArgv(argv) {
  if (process.env.CLOVER_EUPHORIA) return process.env.CLOVER_EUPHORIA

  const at = argv.findIndex(arg => arg === '--euphoria')
  if (at >= 0 && at + 1 < argv.length) return argv[at + 1]
  const inline = argv.find(arg => arg.startsWith('--euphoria='))
  return inline ? inline.slice('--euphoria='.length) : undefined
}

function createWindow() {
  const window = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 960,
    minHeight: 600,
    /**
     * **넘긴 크기가 내용 자리입니다.**
     *
     * 기본값은 테두리를 포함한 창 전체입니다 — 1440 × 900 은 정확히 16:10 인데 그 안의
     * 내용 자리는 1424 × 861 이 되어 비율이 1.654 로 어긋났고, 판은 짧은 쪽에 맞춰
     * 들어가므로 **좌우에 23픽셀씩 검은 자리가 남았습니다.**
     */
    useContentSize: true,
    // 판 밖의 색. `web/src/render/theme.ts` 의 `COLOR.crop` 과 같은 값입니다.
    backgroundColor: '#000000',
    // 창이 다 만들어지기 전에 흰 화면이 번쩍이지 않게 합니다.
    show: false,
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      backgroundThrottling: false,
    },
  })

  /**
   * 창의 비율을 판의 비율로 묶습니다.
   *
   * **처음 크기만 맞춰 두면 한 번 끌자마자 어긋납니다.** 판은 1280 × 800 을 짧은 쪽에
   * 맞춰 넣으므로, 창이 그 비율이 아니면 남는 쪽이 잘라 낸 자리로 남습니다 — 그 자리는
   * 배경이 덮지 않습니다(`game.ts` 의 `layout`).
   *
   * **테두리는 비율에서 뺍니다.** 넘기지 않으면 창 전체가 그 비율이 되고, 그러면 내용
   * 자리가 다시 어긋납니다 — 1440 으로 정해 둔 것이 1486 으로 늘어나 좌우에 23픽셀이
   * 그대로 남았습니다.
   *
   * **창이 뜬 뒤에 잽니다.** 뜨기 전에는 테두리가 아직 없어서 창의 크기와 내용의 크기가
   * 같게 나오고, 그러면 뺄 것이 0 이 되어 위와 같은 일이 그대로 일어납니다. 테두리의
   * 크기는 기계와 겉면 설정에 따라 다르므로 상수로 적을 수 없습니다.
   *
   * **비율을 걸고 나서 크기를 다시 정합니다.** 거는 것만으로는 지금 크기가 맞춰지지
   * 않습니다 — 다음에 끌 때부터 걸립니다.
   *
   * **전체 화면과 최대화에서는 걸리지 않습니다.** 그때는 화면의 비율이 창의 비율이므로
   * 16:9 화면에서는 위아래가 남고, 그 자리는 판 밖의 색입니다.
   */
  const fitToBoard = () => {
    const outer = window.getSize()
    const inner = window.getContentSize()
    window.setAspectRatio(BOARD_W / BOARD_H, {
      width: outer[0] - inner[0],
      height: outer[1] - inner[1],
    })
    // 처음 크기를 판의 비율로 되돌립니다. 높이를 지키고 너비를 그것에서 셉니다.
    const height = inner[1]
    window.setContentSize(Math.round(height * (BOARD_W / BOARD_H)), height)
  }

  window.once('ready-to-show', () => {
    window.show()
    fitToBoard()
    if (devToolsWanted(process.argv)) window.webContents.openDevTools()
  })

  // 페이지를 읽지 못하면 한 번 다시 읽습니다.
  //
  // **게임이 끝나고 새 판으로 넘어갈 때 페이지를 다시 읽습니다.** 그때 렌더러 프로세스가
  // 사라지면 — 옛 WebGL 문맥이 아직 정리되지 않은 채 새 문맥이 만들어지면서 — 창은
  // 그대로 남고 안은 빈 화면이 됩니다. 다시 읽으면 대개 복구됩니다.
  let retried = false
  const retry = why => {
    console.error('Window failed: ' + why)
    // 한 번만 다시 읽습니다. 계속 다시 읽으면 그 자체가 멈춘 것과 같습니다.
    if (retried) return
    retried = true
    setTimeout(() => window.webContents.reload(), 400)
  }

  // 렌더러 프로세스가 죽거나 강제 종료된 경우입니다.
  window.webContents.on('render-process-gone', (_event, details) => retry(details.reason))

  window.webContents.on('did-fail-load', (_event, code, note, url, isMain) => {
    // 부수 자원(그림 한 장 같은 것)의 실패는 페이지 전체와 무관합니다.
    if (!isMain) return
    // 코드 -3 은 ERR_ABORTED 입니다. 새 주소로 넘어가면서 앞의 읽기가 중단된 것이므로
    // 실패가 아닙니다.
    if (code === -3) return
    retry(`${code} ${note} ${url}`)
  })

  // 페이지를 끝까지 읽었으면 다시 읽기 횟수를 초기화합니다. 다음에 또 실패하면 그때
  // 한 번 더 시도할 수 있습니다.
  window.webContents.on('did-finish-load', () => {
    retried = false
  })

  // 바깥 링크는 기본 브라우저로. 창 안에서 다른 곳으로 가지 않게 합니다.
  window.webContents.setWindowOpenHandler(({ url }) => {
    void shell.openExternal(url)
    return { action: 'deny' }
  })

  // 주소에 붙는 것들. **게임은 웹과 같은 주소 매개변수를 읽습니다** — 창에 주소창이
  // 없을 뿐이므로, 명령줄에서 받은 것을 여기서 주소로 옮깁니다.
  const asked = new URLSearchParams()
  const seed = seedFromArgv(process.argv)
  if (seed) asked.set('seed', seed)
  const euphoria = euphoriaFromArgv(process.argv)
  if (euphoria) asked.set('euphoria', euphoria)
  const query = asked.size > 0 ? `?${asked.toString()}` : ''
  void window.loadURL(`${SCHEME}://game/index.html${query}`)

  return window
}

app.whenReady().then(() => {
  // 메뉴를 없앱니다. 게임 창에 파일 · 편집 메뉴가 있을 이유가 없습니다.
  Menu.setApplicationMenu(null)

  // **모든 요청에 응답을 돌려줍니다.** 이 처리기가 예외를 던지면 그 요청은 응답 없이
  // 남아 브라우저가 계속 기다리게 되고, 그 요청이 `index.html` 이면 창 안이 빈 화면으로
  // 남습니다. 그래서 없는 파일에는 404, 읽지 못한 것에는 500 을 돌려줍니다.
  protocol.handle(SCHEME, async request => {
    let wanted = '/index.html'
    try {
      wanted = decodeURIComponent(new URL(request.url).pathname)
    } catch {
      return new Response('Bad request URL', { status: 400 })
    }
    // 폴더를 가리키면 그 안의 index.html 입니다.
    if (wanted === '' || wanted.endsWith('/')) wanted += 'index.html'

    // 경로를 빌드 폴더 안으로 제한합니다. `..` 로 그 밖의 파일을 읽지 못하게 합니다.
    const target = path.normalize(path.join(ROOT, wanted))
    if (!target.startsWith(ROOT)) {
      return new Response('Path escapes the build root', { status: 403 })
    }
    if (!fs.existsSync(target) || !fs.statSync(target).isFile()) {
      return new Response('Not found: ' + wanted, { status: 404 })
    }

    // **토막으로 돌려줄 수 있어야 합니다.**
    //
    // 통째로 돌려주고 있었습니다. `fetch` 로 읽는 테이블과 그림은 그래도 되지만, **미디어
    // 원소는 다릅니다** — `<video>` 와 `<audio>` 는 `Range` 를 보내고 206 을 기다리며,
    // 그것이 오지 않으면 되감기와 이어 재생을 할 수 없습니다. 그래서 데스크탑에서는
    // 곡을 갈아 끼울 때(`currentTime = 0` 뒤의 `play()`) 그 자리에서 멈추고, 환희의
    // 영상은 한 바퀴를 돌지 못한 채 처음으로 되돌아갔습니다 — **판에 들어가면 소리가
    // 없어지고 그 연출이 진행되지 않던 것이 이 하나입니다.** 모바일은 커패시터가 http
    // 로 얹으므로 토막이 되었고, 그래서 거기서는 멀쩡했습니다.
    try {
      const size = fs.statSync(target).size
      const type = typeOf(target)
      const head = request.method === 'HEAD'
      const want = rangeOf(request.headers.get('range'), size)

      if (want && want.bad) {
        return new Response(null, {
          status: 416,
          headers: { 'Content-Range': `bytes */${size}`, 'Accept-Ranges': 'bytes' },
        })
      }

      const start = want ? want.start : 0
      const end = want ? want.end : size - 1
      const length = size === 0 ? 0 : end - start + 1
      const headers = {
        'Content-Type': type,
        'Content-Length': String(length),
        // **언제나 알립니다.** 이 표시가 없으면 크로미움은 토막을 요구하지 않고, 요구하지
        // 않으면 되감기가 통째로 다시 읽는 것이 됩니다.
        'Accept-Ranges': 'bytes',
        'Cache-Control': 'no-cache',
      }
      if (want) headers['Content-Range'] = `bytes ${start}-${end}/${size}`

      if (head || length === 0) {
        return new Response(null, { status: want ? 206 : 200, headers })
      }
      const stream = fs.createReadStream(target, { start, end })
      return new Response(Readable.toWeb(stream), { status: want ? 206 : 200, headers })
    } catch (error) {
      return new Response('Read failed: ' + String(error), { status: 500 })
    }
  })

  const window = createWindow()

  // F11 전체 화면, F12 개발자 도구. 다른 단축키는 게임이 받습니다.
  //
  // **메뉴를 없애면 개발자 도구를 여는 길이 함께 없어집니다.** 게임 창에 파일·편집 메뉴가
  // 있을 이유는 없지만, 창 안에서 무슨 일이 일어나는지 볼 길도 같이 사라집니다 — 소리가
  // 안 나는 자리를 `__clover.audio` 로 읽으려면 콘솔이 있어야 하고, 그것을 여는 자리가
  // 여기뿐입니다.
  window.webContents.on('before-input-event', (event, input) => {
    if (input.type !== 'keyDown') return
    if (input.key === 'F11') {
      window.setFullScreen(!window.isFullScreen())
      event.preventDefault()
      return
    }
    // F12 와 Ctrl+Shift+I 둘 다 받습니다. 기계와 습관에 따라 한쪽만 오는 자리가 있습니다.
    if (input.key === 'F12'
        || (input.control && input.shift && input.key.toLowerCase() === 'i')) {
      window.webContents.toggleDevTools()
      event.preventDefault()
    }
  })

  const shot = shotPathFromArgv(process.argv)
  if (shot) {
    // **일렉트론이 스스로 내는 보안 경고는 셈에서 뺍니다.** 패키징하면 사라지는 것이고,
    // 그것을 오류로 세면 진짜 오류가 그 밑에 묻힙니다.
    const problems = []
    window.webContents.on('console-message', (_event, level, message) => {
      // 3 이 오류입니다. 2 는 경고이고, 그것까지 세면 예고 폐기 알림에 오류가 묻힙니다.
      if (level < 3) return
      if (message.includes('Electron Security Warning')) return
      problems.push(message)
    })
    window.webContents.on('render-process-gone', (_event, details) => {
      problems.push(`Renderer gone: ${details.reason}`)
    })

    const capture = async target => {
      const image = await window.webContents.capturePage()
      fs.mkdirSync(path.dirname(target), { recursive: true })
      fs.writeFileSync(target, image.toPNG())
      console.log(target)
    }

    const wait = ms => new Promise(resolve => setTimeout(resolve, ms))

    window.webContents.once('did-finish-load', () => {
      // 데이터와 그림을 읽고 첫 화면이 자리를 잡을 시간을 줍니다.
      setTimeout(async () => {
        await capture(shot)

        // **전체 화면을 켜고 끈 뒤에도 찍습니다.** 창 크기가 바뀔 때 배치가 어긋나던 결함이
        // 있었고, 그것은 한 장만 찍으면 드러나지 않습니다.
        const full = shot.replace(/\.png$/, '-full.png')
        window.setFullScreen(true)
        await wait(1_200)
        await capture(full)

        window.setFullScreen(false)
        await wait(1_200)
        await capture(shot.replace(/\.png$/, '-back.png'))

        if (problems.length > 0) {
          console.error('Window reported errors:')
          for (const problem of problems.slice(0, 10)) console.error('  ' + problem)
          app.exit(1)
          return
        }
        console.log(`${shot}\nNo errors`)
        app.exit(0)
      }, 3_500)
    })
  }

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})
