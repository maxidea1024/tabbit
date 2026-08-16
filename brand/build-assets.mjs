// 원본 시안에서 실제로 쓰는 파일들을 만듭니다.
//
//   node build-assets.mjs
//
// 원본은 `source/` 이고 손으로 고치지 않습니다. 좌표는 `inspect.mjs` 가 흰 배경 위의 패널
// 경계를 찾아 준 값입니다 — 눈으로 재지 않았습니다.

import sharp from 'sharp'
import { mkdir, writeFile, rm, copyFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const src = (f) => path.join(here, 'source', f)
const dist = path.join(here, 'dist')
const siteImg = path.join(here, '..', 'website', 'static', 'img')

// 원본 안에서 각 자산이 차지하는 사각형.
const BOX = {
  banner: { left: 10, top: 64, width: 917, height: 784 },
  appIconPanel: { left: 952, top: 154, width: 570, height: 658 },

  lockup: { left: 210, top: 34, width: 834, height: 859 },
  horizontal: { left: 219, top: 1113, width: 480, height: 128 },
  mark: { left: 219, top: 1113, width: 106, height: 128 },
  wordmark: { left: 354, top: 1113, width: 345, height: 128 },
}

// 아이콘 변형 6종. 세로 구간은 같고 가로만 다릅니다.
const VARIANTS = [
  { name: 'head-light', left: 56 },
  { name: 'bust-purple', left: 250 },
  { name: 'with-sheet', left: 444 },
  { name: 'cubes', left: 638 },
  { name: 'reading-tcb', left: 831 },
  { name: 'inspecting', left: 1029 },
].map((v) => ({ ...v, top: 902, width: 178, height: 188 }))

// 팔레트. 원본 배너의 색 분포에서 뽑았습니다 (`inspect.mjs` 와 같은 방식의 히스토그램).
export const PALETTE = {
  ink: '#24186c', // 배경 딥 퍼플
  inkDeep: '#180c60',
  violet: '#846cf0', // 워드마크 T와 큐브
  pink: '#fc8490', // 귀 안쪽과 i의 점
  amber: '#fcc024', // `</>` 큐브
}

// ── 흰 배경을 투명하게. 가장자리에서 흰색을 따라 들어가며 지웁니다.
// 안쪽의 흰색(토끼 몸통)은 어두운 외곽선이 막아 주므로 남습니다 — 밝기 임계값만으로 지우면
// 토끼에 구멍이 뚫립니다.
function floodTransparent(data, width, height, thr = 236) {
  const seen = new Uint8Array(width * height)
  const queue = []
  const isPale = (p) => data[p * 4] >= thr && data[p * 4 + 1] >= thr && data[p * 4 + 2] >= thr

  const push = (x, y) => {
    if (x < 0 || y < 0 || x >= width || y >= height) return
    const p = y * width + x
    if (seen[p] || !isPale(p)) return
    seen[p] = 1
    queue.push(p)
  }

  for (let x = 0; x < width; x++) {
    push(x, 0)
    push(x, height - 1)
  }
  for (let y = 0; y < height; y++) {
    push(0, y)
    push(width - 1, y)
  }

  while (queue.length) {
    const p = queue.pop()
    data[p * 4 + 3] = 0
    const x = p % width
    const y = (p - x) / width
    push(x - 1, y)
    push(x + 1, y)
    push(x, y - 1)
    push(x, y + 1)
  }
  return data
}

async function cutout(file, box) {
  const { data, info } = await sharp(src(file))
    .extract(box)
    .ensureAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true })
  floodTransparent(data, info.width, info.height)
  return sharp(data, { raw: { width: info.width, height: info.height, channels: 4 } })
    .png()
    .toBuffer()
    .then((b) => sharp(b).trim({ threshold: 1 }).png().toBuffer())
}

// ── PNG를 담는 .ico. 아이콘 하나에 라이브러리를 들이지 않기 위해 직접 씁니다.
// 형식: ICONDIR(6바이트) + 크기마다 ICONDIRENTRY(16바이트) + PNG 바이트.
function ico(images) {
  const head = Buffer.alloc(6)
  head.writeUInt16LE(0, 0)
  head.writeUInt16LE(1, 2)
  head.writeUInt16LE(images.length, 4)

  let offset = 6 + images.length * 16
  const entries = []
  for (const { size, png } of images) {
    const e = Buffer.alloc(16)
    e.writeUInt8(size >= 256 ? 0 : size, 0)
    e.writeUInt8(size >= 256 ? 0 : size, 1)
    e.writeUInt8(0, 2)
    e.writeUInt8(0, 3)
    e.writeUInt16LE(1, 4)
    e.writeUInt16LE(32, 6)
    e.writeUInt32LE(png.length, 8)
    e.writeUInt32LE(offset, 12)
    entries.push(e)
    offset += png.length
  }
  return Buffer.concat([head, ...entries, ...images.map((i) => i.png)])
}

// ── 실행.
await rm(dist, { recursive: true, force: true })
await mkdir(path.join(dist, 'icons'), { recursive: true })
await mkdir(siteImg, { recursive: true })

const wrote = []
const save = async (rel, buf) => {
  const full = path.join(dist, rel)
  await mkdir(path.dirname(full), { recursive: true })
  await writeFile(full, buf)
  wrote.push(`${rel}  ${(buf.length / 1024).toFixed(0)} KB`)
  return buf
}

// ── 배너 하단의 알약을 지웁니다.
//
// 그림 안에 `TCB — Tabbit Binary` 라고 그려져 있는데, TCB 는 **Tabbit Compiled Binary** 입니다.
// 그리고 파일 형식의 약자는 브랜드 이미지가 말할 것이 아닙니다 — 문서가 말합니다.
//
// 배경이 세로 그라디언트라 단색으로 칠하면 띠가 보입니다. 알약 바로 위아래의 깨끗한 행 둘을
// 컬럼마다 선형 보간해서 채웁니다.
// 알약을 지우면 그것이 앉아 있던 라벤더 띠가 빈 여백으로 남으므로, 평평한 부분을 잘라냅니다 —
// 컨테이너의 둥근 아래 모서리(y 760부터)는 남깁니다. 이어붙인 자리는 위아래를 보간해 지웁니다.
const PILL = { top: 717, bottom: 772 }
const CUT = { from: 726, to: 761 } // 잘라낼 평평한 라벤더 행

async function erasePill(png) {
  const raw = await sharp(png).ensureAlpha().raw().toBuffer({ resolveWithObject: true })
  const { width, height, channels } = raw.info
  const data = raw.data

  // 1. 알약 자리를 위아래 배경으로 채웁니다. 배경이 세로 그라디언트라 단색으로는 띠가 보입니다.
  const above = PILL.top - 1
  const below = PILL.bottom + 1
  for (let x = 0; x < width; x++) {
    const a = (above * width + x) * channels
    const b = (below * width + x) * channels
    for (let y = PILL.top; y <= PILL.bottom; y++) {
      const t = (y - above) / (below - above)
      const i = (y * width + x) * channels
      for (let c = 0; c < 3; c++) data[i + c] = Math.round(data[a + c] * (1 - t) + data[b + c] * t)
    }
  }

  // 2. 빈 띠를 줄입니다.
  const removed = CUT.to - CUT.from + 1
  const out = Buffer.alloc((height - removed) * width * channels)
  const rowBytes = width * channels
  data.copy(out, 0, 0, CUT.from * rowBytes)
  data.copy(out, CUT.from * rowBytes, (CUT.to + 1) * rowBytes)

  // 3. 이음매를 그 위아래로 보간해 없앱니다.
  const seam = CUT.from
  const top = seam - 6
  const bottom = seam + 5
  for (let x = 0; x < width; x++) {
    const a = (top * width + x) * channels
    const b = (bottom * width + x) * channels
    for (let y = top + 1; y < bottom; y++) {
      const t = (y - top) / (bottom - top)
      const i = (y * width + x) * channels
      for (let c = 0; c < 3; c++) out[i + c] = Math.round(out[a + c] * (1 - t) + out[b + c] * t)
    }
  }

  return sharp(out, { raw: { width, height: height - removed, channels } }).png().toBuffer()
}

// 1. 시안에서 그대로 잘라내는 것들.
const banner = await erasePill(
  await sharp(src('brand-sheet.png')).extract(BOX.banner).png().toBuffer(),
)
await save('banner.png', banner)
await save('app-icon-panel.png', await sharp(src('brand-sheet.png')).extract(BOX.appIconPanel).png().toBuffer())
await save('lockup.png', await sharp(src('lockup-sheet.png')).extract(BOX.lockup).png().toBuffer())
await save('app-icon.png', await sharp(src('app-icon.png')).png().toBuffer())

// 2. 배경을 뺀 것들 — 어느 바탕에도 얹을 수 있어야 합니다.
const mark = await cutout('lockup-sheet.png', BOX.mark)
const wordmark = await cutout('lockup-sheet.png', BOX.wordmark)
const horizontal = await cutout('lockup-sheet.png', BOX.horizontal)
await save('mark.png', mark)
await save('wordmark.png', wordmark)
await save('logo-horizontal-light.png', horizontal)  // 검은 워드마크 — 밝은 바탕 전용

// 3. 아이콘 변형 6종.
for (const v of VARIANTS) {
  const box = { left: v.left, top: v.top, width: v.width, height: v.height }
  await save(`icons/${v.name}.png`, await cutout('lockup-sheet.png', box))
}

// 4. 파비콘. 작은 크기에서 읽히는 것은 워드마크가 없는 타일입니다.
const tile = await sharp(src('lockup-sheet.png'))
  .extract({ left: 250, top: 902, width: 178, height: 178 })
  .png()
  .toBuffer()

const sizes = [16, 32, 48, 64, 128, 180, 512]
const pngs = {}
for (const size of sizes) {
  pngs[size] = await sharp(tile).resize(size, size, { fit: 'cover' }).png({ compressionLevel: 9 }).toBuffer()
  await save(`icons/favicon-${size}.png`, pngs[size])
}
await save('icons/favicon.ico', ico([16, 32, 48].map((size) => ({ size, png: pngs[size] }))))

// 4b. 실행 파일 아이콘. 사용자가 「앱 아이콘용」으로 지정한 그림이고, 원본이 정사각이 아니라
//     흰 배경을 뺀 다음 투명한 정사각 canvas 안에 넣습니다 — 잘라내면 둥근 모서리가 없어집니다.
const appMark = await cutout('brand-sheet.png', BOX.appIconPanel)
const appPngs = []
for (const size of [16, 32, 48, 64, 128, 256]) {
  const png = await sharp(appMark)
    .resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
    .png({ compressionLevel: 9 })
    .toBuffer()
  await save(`icons/app-${size}.png`, png)
  appPngs.push({ size, png })
}
await save('icons/tabbit.ico', ico(appPngs))

// 5. readme 헤더와 소셜 카드.
//
// 가로 락업으로 만들지 않습니다 — 그 워드마크는 **흰 바탕용**(검은 글자)이라 브랜드 배경에
// 얹으면 읽히지 않고, 글자 안쪽의 흰 구멍이 얼룩으로 보입니다. 배너가 이미 브랜드 배경 위에
// 완성된 구성이므로 그것을 씁니다.
// readme 는 JPEG 입니다. 같은 그림의 PNG 가 1.5 MB 이고, 저장소 첫 화면에서 받는 파일로는
// 큽니다 — 그라디언트가 많은 일러스트라 PNG 가 잘 줄지 않습니다.
await save(
  'readme-header.jpg',
  await sharp(banner).resize({ width: 1100 }).jpeg({ quality: 86, mozjpeg: true }).toBuffer(),
)

await save(
  'og-card.png',
  await sharp(banner)
    .resize(1200, 630, { fit: 'contain', background: PALETTE.ink })
    .png()
    .toBuffer(),
)

// 6. 사이트가 읽는 자리로. website/static/img/ 는 Docusaurus 가 그대로 서빙합니다.
const toSite = {
  'favicon.ico': path.join(dist, 'icons', 'favicon.ico'),
  'favicon-32.png': path.join(dist, 'icons', 'favicon-32.png'),
  'favicon-180.png': path.join(dist, 'icons', 'favicon-180.png'),
  'logo.png': path.join(dist, 'icons', 'favicon-128.png'),
  'og-card.png': path.join(dist, 'og-card.png'),
  'banner.png': path.join(dist, 'banner.png'),
}
for (const [name, from] of Object.entries(toSite)) {
  await copyFile(from, path.join(siteImg, name))
}

console.log(wrote.join('\n'))
console.log(`\nwebsite/static/img/ 로 ${Object.keys(toSite).length}개 복사했습니다.`)
