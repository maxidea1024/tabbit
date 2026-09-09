// `boundsArea` 를 `pin.ts` 밖에서 두지 않는가.
//
// **이 게이트는 원인 쪽을 막습니다.** 필터가 걸릴 통에 `boundsArea` 만 두면 Pixi 가 그 통의
// 필터 사각형에 렌더 그룹의 변환을 두 번 곱하고, 그것은 화면을 옮기며 굽는 자리에서만
// 어긋납니다 — 규격은 `doc/ui/transition.md` 의 「구울 그림의 필터 사각형」 입니다.
//
// **그림으로 잡히는 것이 절반뿐이라 이 게이트가 있습니다.** `check-lost-look.ts` 가 창의
// 비율마다 두 그림을 견주지만, 밀린 사각형이 화면 밖으로 나가는 자리에서는 Pixi 가 필터를
// 건너뛰고 통을 그대로 그립니다 — 셰이더의 무늬만 없어지므로 다른 픽셀이 0.02%이고 문턱을
// 넘지 않습니다. **세로로 긴 창이 실제로 그렇습니다.** 원인은 한 줄이므로 그 한 줄을 봅니다.
//
//     npx tsx tools/check-pin.ts
import * as fs from 'fs/promises'
import * as path from 'path'
import { fileURLToPath } from 'url'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const SRC = path.resolve(HERE, '../src')
/** 이 파일만 `boundsArea` 를 둡니다. `filterArea` 를 같은 자리에서 함께 둡니다. */
const KEEPER = path.join('render', 'pin.ts')

/** `.ts` 파일 전부. */
async function walk(dir: string): Promise<string[]> {
  const found: string[] = []
  for (const entry of await fs.readdir(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) found.push(...await walk(full))
    else if (entry.name.endsWith('.ts')) found.push(full)
  }
  return found
}

async function main(): Promise<number> {
  const files = await walk(SRC)
  const bad: string[] = []
  let keeperFound = false

  for (const file of files) {
    const rel = path.relative(SRC, file)
    const body = await fs.readFile(file, 'utf-8')
    if (rel === KEEPER) {
      keeperFound = body.includes('node.boundsArea') && body.includes('node.filterArea')
      continue
    }
    body.split(/\r?\n/).forEach((line, at) => {
      // 대입하는 자리만 봅니다. 주석에서 이름을 들어 설명하는 것은 그대로 둡니다.
      if (/\bboundsArea\s*=/.test(line) && !line.trimStart().startsWith('//')) {
        bad.push(`${rel}:${at + 1}  ${line.trim()}`)
      }
    })
  }

  if (!keeperFound) {
    bad.push(`${KEEPER} 가 경계와 필터 사각형을 함께 두지 않습니다`)
  }

  console.log(`src 의 ${files.length}개 파일을 보았습니다`)
  if (bad.length === 0) {
    console.log(`\`boundsArea\` 는 ${KEEPER.replace(/\\/g, '/')} 의 \`pinBox\` 만 둡니다`)
    return 0
  }
  console.log(`\n\`boundsArea\` 를 ${KEEPER.replace(/\\/g, '/')} 밖에서 둔 자리가 `
    + `${bad.length}곳입니다 — \`pinBox(통, 너비, 높이)\` 로 바꾸십시오`)
  for (const one of bad) console.log(`  ${one}`)
  return 1
}

main().then(code => process.exit(code))
