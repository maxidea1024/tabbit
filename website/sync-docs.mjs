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
    let rel = path.posix.relative(path.posix.dirname(destRel), moved)
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

const report = { md: 0, asset: 0, rewritten: 0 }

for (const [srcRel, destRel] of plan) {
  const src = path.join(repo, srcRel)
  const dest = path.join(outDir, destRel)
  await mkdir(path.dirname(dest), { recursive: true })

  if (!srcRel.endsWith('.md')) {
    await copyFile(src, dest)
    report.asset++
    continue
  }

  const text = await readFile(src, 'utf8')
  await writeFile(dest, rewriteLinks(text, srcRel, destRel, report), 'utf8')
  report.md++
}

console.log(
  `문서 ${report.md}편 · 이미지 등 ${report.asset}개를 복사했고, 링크 ${report.rewritten}개를 새 자리에 맞췄습니다.`,
)
