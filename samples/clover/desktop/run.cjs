// 창을 띄우는 자리.
//
// **`electron .` 을 직접 부르지 않는 이유가 하나 있습니다.** 부모가 일렉트론 앱이면
// `ELECTRON_RUN_AS_NODE=1` 이 환경에 붙어 내려옵니다 — VS Code 처럼 편집기 자체가 일렉트론인
// 경우가 그렇습니다. 그 값이 붙은 채로 일렉트론을 띄우면 **창이 아니라 평범한 Node 가 돌고**,
// `require('electron')` 이 껍데기를 돌려주어 `app` 이 `undefined` 가 됩니다.
//
// 그러면 창도 뜨지 않고 오류도 없이 0으로 끝납니다. 아무 흔적이 없어서 빌드가 깨진 것으로
// 보이고, 실제로 그렇게 한 시간을 썼습니다.

const { spawn } = require('node:child_process')
const electron = require('electron')

const env = { ...process.env }
delete env.ELECTRON_RUN_AS_NODE

const child = spawn(electron, ['.', ...process.argv.slice(2)], { stdio: 'inherit', env })
child.on('exit', code => process.exit(code ?? 0))
