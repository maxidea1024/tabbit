// 독립 실행 창.
//
// **웹 빌드를 그대로 담습니다.** 게임의 코드는 한 줄도 다르지 않고, 이 파일이 하는 일은 창을
// 하나 띄우고 그 안에 `web/dist` 를 얹는 것뿐입니다 — 그래야 웹과 독립형이 갈라지지 않습니다.
//
// `file://` 로 열지 않는 것이 요점입니다. 게임은 테이블 40개와 그림 202장을 `fetch` 로 읽는데
// 크로미움은 `file://` 에서의 `fetch` 를 막습니다. 그래서 사설 스킴 하나를 등록하고 그 스킴의
// 요청을 파일로 돌려줍니다. **그러면 웹에서 쓰는 상대 경로가 그대로 맞습니다.**

const { app, BrowserWindow, Menu, protocol, net, shell } = require('electron')
const fs = require('node:fs')
const path = require('node:path')
const { pathToFileURL } = require('node:url')

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

// 사설 스킴을 표준 스킴으로 등록합니다. **이것이 없으면 상대 경로와 `fetch` 가 동작하지
// 않습니다** — 표준이 아닌 스킴은 오리진이 없는 것으로 다뤄지기 때문입니다.
protocol.registerSchemesAsPrivileged([{
  scheme: SCHEME,
  privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true },
}])

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

  window.once('ready-to-show', () => window.show())

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

    try {
      return await net.fetch(pathToFileURL(target).toString())
    } catch (error) {
      return new Response('Read failed: ' + String(error), { status: 500 })
    }
  })

  const window = createWindow()

  // F11 전체 화면. 다른 단축키는 게임이 받습니다.
  window.webContents.on('before-input-event', (event, input) => {
    if (input.type === 'keyDown' && input.key === 'F11') {
      window.setFullScreen(!window.isFullScreen())
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
