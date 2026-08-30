// 저장소의 문서를 사이트가 읽는 자리로 복사합니다.
//
// 원본을 옮기지 않는 이유: 문서 60여 편이 상대 경로로 촘촘히 엮여 있고, GitHub에서 그대로
// 읽히는 것이 지금의 사용 방식입니다. 그래서 복사하면서 링크만 새 자리에 맞게 고칩니다.
//
//   doc/                → docs/guide/
//   spec/               → docs/spec/
//   samples/readme.md   → docs/samples/readme.md
//   samples/*/readme.md → docs/samples/*/readme.md
//   samples/*/doc/      → docs/samples/*/doc/
//
// 디렉터리 깊이를 보존하므로 문서끼리의 링크는 대부분 그대로 맞습니다. 실제로 고쳐지는 것은
// `doc/` 이름이 `guide/` 로 바뀌면서 어긋나는 것들뿐이고, 복사 대상 밖을 가리키는 링크
// (`src/` · `lib/` · 워크북 · 샘플 readme)는 GitHub 주소로 바꿉니다 — 사이트에 그 파일이
// 없으므로 상대 경로로는 닿을 수 없기 때문입니다.

import { readdir, readFile, writeFile, mkdir, rm, copyFile } from 'node:fs/promises'
import { existsSync, statSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const repo = path.resolve(here, '..')
const outDir = path.join(here, 'docs')

const GITHUB_BLOB = 'https://github.com/maxidea1024/tabbit/blob/main'
const GITHUB_TREE = 'https://github.com/maxidea1024/tabbit/tree/main'

/** 복사할 원본 디렉터리와 사이트 안에서의 자리. */
const roots = [
  { from: 'doc', to: 'guide' },
  { from: 'spec', to: 'spec' },
]

for (const sample of await readdir(path.join(repo, 'samples'), { withFileTypes: true })) {
  if (!sample.isDirectory()) continue
  const rel = `samples/${sample.name}/doc`
  if (existsSync(path.join(repo, rel))) roots.push({ from: rel, to: rel })
}

/**
 * Single files copied as they are. A sample's `readme.md` is its documentation - two of the
 * three samples have no `doc/` folder at all - and the index beside them says which sample
 * answers which question.
 */
const files = ['samples/readme.md']
for (const sample of await readdir(path.join(repo, 'samples'), { withFileTypes: true })) {
  if (!sample.isDirectory()) continue
  const rel = `samples/${sample.name}/readme.md`
  if (existsSync(path.join(repo, rel))) files.push(rel)
}

const posix = (p) => p.split(path.sep).join('/')

async function walk(dir) {
  const found = []
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) found.push(...(await walk(full)))
    else found.push(full)
  }
  return found
}

// ── 복사 계획. 원본 저장소 경로 → 사이트 안 경로.
const plan = new Map()
for (const file of files) plan.set(file, file)
for (const root of roots) {
  for (const full of await walk(path.join(repo, root.from))) {
    const srcRel = posix(path.relative(repo, full))
    const tail = srcRel.slice(root.from.length + 1)
    plan.set(srcRel, `${root.to}/${tail}`)
  }
}

// ── 언어 탭.
//
// `doc/figures/showcase.py` 가 생성한 문서는 언어마다 코드를 `<details>` 하나에 담아 둡니다.
// GitHub 에서는 그것이 그대로 접히는 목록으로 읽히고, 사이트에서는 탭이 낫습니다 - 열다섯
// 언어를 세로로 늘어놓으면 어느 것도 옆의 시트 그림과 나란히 보이지 않기 때문입니다.
//
// **`groupId` 를 두는 이유**는 고른 언어가 페이지를 넘어 유지되게 하기 위해서입니다. 한 곳에서
// C# 을 고르면 다른 문서도 C# 으로 열립니다.
//
// 이 변환을 거친 문서만 `.mdx` 로 나갑니다. 나머지는 `.md` 그대로이고, 그래서 `<Field>` 나
// `{}` 를 그냥 적어 둔 문서 60여 편은 이 변환과 무관합니다.
const TABS = /<!--\s*tabbit:tabs\s+([\w-]+)\s*-->\n([\s\S]*?)<!--\s*\/tabbit:tabs\s*-->/g
const TAB = /<details\s+data-lang="([\w-]+)"(?:\s+open)?>\n<summary>([^<]+)<\/summary>\n([\s\S]*?)<\/details>/g

// 오른쪽 목차를 접습니다. 이 문서들은 절이 하나뿐이라 목차에 담을 것이 없고, 그 자리를
// 비우면 시트 그림과 코드를 좌우로 놓을 폭이 나옵니다.
const MDX_HEAD =
  '---\nhide_table_of_contents: true\n---\n\n'
  + "import Tabs from '@theme/Tabs'\nimport TabItem from '@theme/TabItem'\n"

/** 시트 그림과 코드를 좌우로 놓을 수 있게 묶습니다. 좁은 화면에서는 CSS 가 다시 위아래로 폅니다. */
function pairs(text) {
  return text.replace(/<!--\s*tabbit:pair\s*-->\n([\s\S]*?)<!--\s*\/tabbit:pair\s*-->/g,
    (whole, body) => {
      const at = body.indexOf('<Tabs')
      if (at === -1) return whole

      const sheet = body.slice(0, at).trim()
      const code = body.slice(at).trim()

      return ['<div className="tabbit-pair">',
              '<div className="tabbit-pair-sheet">', '', sheet, '', '</div>',
              '<div className="tabbit-pair-code">', '', code, '', '</div>',
              '</div>'].join('\n')
    })
}

/** 탭 묶음을 MDX 의 `<Tabs>` 로. 바꿀 것이 없으면 원문을 그대로 돌려줍니다. */
function tabify(text) {
  let changed = false

  const next = text.replace(TABS, (whole, group, body) => {
    const items = []
    for (const [, value, label, content] of body.matchAll(TAB)) {
      items.push(
        `<TabItem value="${value}" label="${label}">\n${content.trim()}\n</TabItem>`,
      )
    }

    if (items.length === 0) return whole
    changed = true
    return `<Tabs groupId="${group}">\n${items.join('\n')}\n</Tabs>`
  })

  return { text: changed ? next : text, changed }
}

/** 탭이 들어 있어 `.mdx` 로 나갈 문서들. 링크를 고칠 때 확장자를 함께 맞춥니다. */
const asMdx = new Set()
for (const [srcRel, destRel] of plan) {
  if (!srcRel.endsWith('.md')) continue
  if (tabify(await readFile(path.join(repo, srcRel), 'utf8')).changed) asMdx.add(destRel)
}

// ── 링크 하나를 새 자리에 맞게 고칩니다.
function rewriteTarget(target, srcRel, destRel) {
  if (/^(https?:|mailto:|#|<)/.test(target)) return target

  const hashAt = target.indexOf('#')
  const filePart = hashAt === -1 ? target : target.slice(0, hashAt)
  const hash = hashAt === -1 ? '' : target.slice(hashAt)
  if (!filePart) return target

  const resolved = path.posix.normalize(
    path.posix.join(path.posix.dirname(srcRel), decodeURI(filePart)),
  )
  const bare = resolved.replace(/\/$/, '')

  const moved = plan.get(bare)
  if (moved) {
    const at = asMdx.has(moved) ? moved.replace(/\.md$/, '.mdx') : moved
    let rel = path.posix.relative(path.posix.dirname(destRel), at)
    if (!rel.startsWith('.')) rel = `./${rel}`
    return rel + hash
  }

  // 복사 대상 밖. 저장소에 실제로 있는 것만 GitHub 주소로 바꾸고, 나머지는 건드리지 않습니다
  // (코드 블록 안의 괄호 표현 같은 것이 링크로 잘못 잡히는 경우가 있습니다).
  if (!existsSync(path.join(repo, bare))) return target
  const isDir = statSync(path.join(repo, bare)).isDirectory()
  return `${isDir ? GITHUB_TREE : GITHUB_BLOB}/${bare}${hash}`
}

const LINK = /\]\(([^)\s]+)((?:\s+"[^"]*")?)\)/g

function rewriteLinks(text, srcRel, destRel, report) {
  let inFence = false
  return text
    .split('\n')
    .map((line) => {
      if (/^\s*(```|~~~)/.test(line)) {
        inFence = !inFence
        return line
      }
      if (inFence) return line
      return line.replace(LINK, (whole, target, title) => {
        const next = rewriteTarget(target, srcRel, destRel)
        if (next !== target) report.rewritten++
        return `](${next}${title})`
      })
    })
    .join('\n')
}

// ── 실행.
await rm(outDir, { recursive: true, force: true })

/**
 * `jsonc` 코드 펜스를 `json5` 로. **Prism 에 `jsonc` 가 없어서 칠해지지 않고 나갑니다** -
 * recipe 가 주석 달린 JSON 이라 저장소의 문서는 63곳에서 그렇게 적고 있습니다.
 *
 * 저장소 쪽을 고치지 않는 이유는 그 표기가 맞기 때문입니다 - recipe 는 JSON5 가 아니라
 * 주석이 있는 JSON 이고, GitHub 은 `jsonc` 를 그대로 칠합니다. 사이트가 못 읽는 것이므로
 * 사이트로 들어오는 사본에서만 바꿉니다.
 */
const JSONC_FENCE = /^(\s*(?:```|~~~))jsonc\b/gm

function paintJsonc(text) {
  return text.replace(JSONC_FENCE, '$1json5')
}

const report = { md: 0, asset: 0, rewritten: 0, tabs: 0 }

for (const [srcRel, destRel] of plan) {
  const src = path.join(repo, srcRel)
  const dest = path.join(outDir, destRel)
  await mkdir(path.dirname(dest), { recursive: true })

  if (!srcRel.endsWith('.md')) {
    await copyFile(src, dest)
    report.asset++
    continue
  }

  const text = paintJsonc(rewriteLinks(await readFile(src, 'utf8'), srcRel, destRel, report))
  const tabbed = tabify(text)

  // 링크를 먼저 고치고 탭으로 바꿉니다. 반대 순서이면 `<TabItem>` 안의 링크가 코드 펜스
  // 바깥인지 안인지를 세는 자리에서 어긋납니다.
  if (tabbed.changed) {
    // MDX 에는 HTML 주석이 없습니다. 남아 있는 것은 「손으로 고치지 마십시오」 한 줄이고,
    // 사이트에서도 소스에 남아 있어야 하므로 지우지 않고 MDX 의 주석으로 바꿉니다.
    const mdx = pairs(tabbed.text).replace(/<!--([\s\S]*?)-->/g, '{/*$1*/}')
    await writeFile(`${dest}x`, MDX_HEAD + '\n' + mdx, 'utf8')
    report.tabs++
  } else {
    await writeFile(dest, text, 'utf8')
  }
  report.md++
}

console.log(
  `문서 ${report.md}편 · 이미지 등 ${report.asset}개를 복사했고, 링크 ${report.rewritten}개를 새 자리에 맞췄습니다.`
    + ` 그중 ${report.tabs}편은 언어 탭이 있어 .mdx 로 나갔습니다.`,
)
