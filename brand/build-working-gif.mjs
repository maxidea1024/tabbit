// 마스코트가 일하는 GIF. `source/mascot-working.png` 한 장에서
// 앞발만 오려 번갈아 두드리게 하고, 나머지 움직임은 브랜드 색으로 그려 얹습니다.
import sharp from 'sharp'
import { mkdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'

const WORK = 512 // 원본을 이 크기로 두고 좌표를 잡습니다.
const CROP = { left: 64, top: 56, size: 392 }
const SIZE = 256
const FRAMES = 16
const DELAY = 65

const violet = '#846cf0'
const pink = '#fc8490'
const amber = '#fcc024'
const green = '#3ddc97'

// WORK 좌표에서 눈으로 측정한 앞발. y1이 시트에 가려지는 선입니다.
const PAWS = [
  { x: 174, w: 56, y0: 352, y1: 391 },
  { x: 285, w: 44, y0: 352, y1: 391 },
]
const LIFT = 8 // 최대로 들리는 높이.
const TAPS = 3 // 한 바퀴에 두드리는 횟수.

const TAU = Math.PI * 2
const r2 = (v) => Math.round(v * 100) / 100

function fade(p, inLen = 0.18, outLen = 0.25) {
  if (p < inLen) return p / inLen
  if (p > 1 - outLen) return (1 - p) / outLen
  return 1
}

// 들어오는 시트 한 장.
function gridCard(x, y, w, h, rot, opacity) {
  const cell = (dx, dy, fill) =>
    `<rect x="${r2(x + dx)}" y="${r2(y + dy)}" width="6" height="4" rx="1" fill="${fill}"/>`
  return `<g opacity="${r2(opacity)}" transform="rotate(${r2(rot)} ${r2(x + w / 2)} ${r2(y + h / 2)})">
    <rect x="${r2(x + 1.5)}" y="${r2(y + 3)}" width="${w}" height="${h}" rx="3" fill="#160b48" opacity="0.4"/>
    <rect x="${r2(x)}" y="${r2(y)}" width="${w}" height="${h}" rx="3" fill="url(#cardFace)"/>
    <rect x="${r2(x)}" y="${r2(y)}" width="${w}" height="5" rx="2.5" fill="${violet}" opacity="0.75"/>
    ${cell(4, 9, violet)}${cell(13, 14, green)}${cell(4, 19, amber)}
    <g stroke="${violet}" stroke-width="0.8" opacity="0.3">
      <line x1="${r2(x + 2)}" y1="${r2(y + 13)}" x2="${r2(x + w - 2)}" y2="${r2(y + 13)}"/>
      <line x1="${r2(x + 2)}" y1="${r2(y + 18)}" x2="${r2(x + w - 2)}" y2="${r2(y + 18)}"/>
    </g>
  </g>`
}

// 이 도구의 산출 셋 — 색과 기호를 함께 가집니다.
const CUBES = [
  { fill: violet, glyph: '{}' },
  { fill: green, grid: true },
  { fill: amber, glyph: '&lt;/&gt;' },
]

function cube(x, y, s, rot, opacity, kind) {
  const c = CUBES[kind % CUBES.length]
  const cx = x + s / 2
  const cy = y + s / 2
  const face = c.grid
    ? [
        [-4.6, -4.6],
        [0.6, -4.6],
        [-4.6, 0.6],
        [0.6, 0.6],
      ]
        .map(([dx, dy]) => `<rect x="${r2(cx + dx)}" y="${r2(cy + dy)}" width="4" height="4" rx="1" fill="#ffffff"/>`)
        .join('')
    : `<text x="${r2(cx)}" y="${r2(cy + 4)}" font-family="DejaVu Sans Mono, Consolas, monospace"
        font-size="${r2(s * 0.46)}" font-weight="700" fill="#ffffff" text-anchor="middle">${c.glyph}</text>`
  return `<g opacity="${r2(opacity)}" transform="rotate(${r2(rot)} ${r2(cx)} ${r2(cy)})">
    <rect x="${r2(x + 1.5)}" y="${r2(y + 3)}" width="${s}" height="${s}" rx="6" fill="#160b48" opacity="0.35"/>
    <rect x="${r2(x)}" y="${r2(y)}" width="${s}" height="${s}" rx="6" fill="${c.fill}"/>
    <rect x="${r2(x)}" y="${r2(y)}" width="${s}" height="${s}" rx="6" fill="url(#cubeLit)"/>
    <rect x="${r2(x + 2)}" y="${r2(y + 2)}" width="${r2(s - 4)}" height="${r2(s * 0.32)}" rx="3" fill="#ffffff" opacity="0.22"/>
    ${face}
  </g>`
}

function sparkle(x, y, r, opacity, fill) {
  return `<path opacity="${r2(opacity)}" fill="${fill}" d="M${r2(x)},${r2(y - r)}
    Q${r2(x + r * 0.22)},${r2(y - r * 0.22)} ${r2(x + r)},${r2(y)}
    Q${r2(x + r * 0.22)},${r2(y + r * 0.22)} ${r2(x)},${r2(y + r)}
    Q${r2(x - r * 0.22)},${r2(y + r * 0.22)} ${r2(x - r)},${r2(y)}
    Q${r2(x - r * 0.22)},${r2(y - r * 0.22)} ${r2(x)},${r2(y - r)}z"/>`
}

// 결과 크기 위에 얹는 것들.
function overlay(i) {
  const t = i / FRAMES
  const out = [
    `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}" viewBox="0 0 ${SIZE} ${SIZE}">`,
    `<defs>
      <linearGradient id="cardFace" x1="0" y1="0" x2="0.3" y2="1">
        <stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#ddd3fb"/>
      </linearGradient>
      <linearGradient id="cubeLit" x1="0" y1="0" x2="0.35" y2="1">
        <stop offset="0" stop-color="#ffffff" stop-opacity="0.28"/>
        <stop offset="0.55" stop-color="#ffffff" stop-opacity="0"/>
        <stop offset="1" stop-color="#0e0640" stop-opacity="0.3"/>
      </linearGradient>
    </defs>`,
  ]

  // 왼쪽에서 들어오는 시트 — 귀에 닿기 전에 사라집니다.
  for (let k = 0; k < 3; k++) {
    const p = (t + k / 3) % 1
    const x = -34 + p * 90
    const y = 26 + k * 15 - Math.sin(p * Math.PI) * 8
    out.push(gridCard(r2(x), r2(y), 28, 25, r2(-20 + p * 26), fade(p) * 0.96))
  }

  // 오른쪽 위로 튀어 나가는 큐브.
  for (let k = 0; k < 2; k++) {
    const p = (t + k / 2) % 1
    const x = 196 + p * 34
    const y = 96 - p * 60
    out.push(cube(r2(x), r2(y), 24, r2(-10 + p * 30), fade(p, 0.2, 0.3), Math.floor(t * 2 + k) % 3))
  }

  // 반짝이 셋 — 위상을 달리해 번갈아 뜁니다.
  const twinkles = [
    { x: 46, y: 104, r: 7, fill: amber },
    { x: 154, y: 18, r: 5.5, fill: pink },
    { x: 232, y: 118, r: 6, fill: '#ffffff' },
  ]
  twinkles.forEach((s, k) => {
    const p = (t + k / 3) % 1
    const pulse = 0.35 + 0.65 * Math.max(0, Math.sin(p * TAU))
    out.push(sparkle(s.x, s.y, r2(s.r * (0.6 + 0.4 * pulse)), r2(pulse), s.fill))
  })

  // 진행 막대 — 한 바퀴에 한 번 찹니다.
  const prog = Math.min(1, 0.08 + t * 1.05)
  out.push(`<rect x="72" y="246" width="112" height="6" rx="3" fill="#12083c" opacity="0.5"/>`)
  out.push(`<rect x="73" y="247" width="${r2(110 * prog)}" height="4" rx="2" fill="${amber}"/>`)

  out.push(`</svg>`)
  return Buffer.from(out.join('\n'))
}

// 앞발을 든 프레임 하나. 사각형째로 옮기면 소맷단에 계단이 생기므로
// 발 모양의 타원으로 오려서 옮깁니다.
async function pose(base, lifts) {
  const layers = []
  for (const [n, paw] of PAWS.entries()) {
    const lift = lifts[n]
    if (lift <= 0) continue
    const h = paw.y1 - paw.y0
    const mask = Buffer.from(
      `<svg xmlns="http://www.w3.org/2000/svg" width="${paw.w}" height="${h}">
        <defs><filter id="f"><feGaussianBlur stdDeviation="1.2"/></filter></defs>
        <ellipse cx="${paw.w / 2}" cy="${h - 12}" rx="${paw.w / 2 - 1}" ry="26" fill="#ffffff" filter="url(#f)"/>
      </svg>`,
    )
    const patch = await sharp(base)
      .extract({ left: paw.x, top: paw.y0, width: paw.w, height: h })
      .ensureAlpha()
      .composite([{ input: mask, blend: 'dest-in' }])
      .png()
      .toBuffer()
    // 발이 비운 자리는 바로 아래 시트를 늘려 덮습니다.
    const band = await sharp(base)
      .extract({ left: paw.x, top: paw.y1 + 1, width: paw.w, height: 5 })
      .resize({ width: paw.w, height: lift + 5, fit: 'fill' })
      .png()
      .toBuffer()
    layers.push({ input: band, left: paw.x, top: paw.y1 - lift })
    layers.push({ input: patch, left: paw.x, top: paw.y0 - lift })
  }
  return layers.length ? await sharp(base).composite(layers).png().toBuffer() : base
}

async function frame(base, i) {
  const t = i / FRAMES
  const tap = Math.sin(t * TAU * TAPS)
  const lifts = [Math.round(Math.max(0, tap) * LIFT), Math.round(Math.max(0, -tap) * LIFT)]
  const posed = await pose(base, lifts)
  // 카메라는 고정입니다 — 흔들면 배경이 프레임마다 달라져 파일이 커집니다.
  const shot = await sharp(posed)
    .extract({ left: CROP.left, top: CROP.top, width: CROP.size, height: CROP.size })
    .resize(SIZE, SIZE)
    .png()
    .toBuffer()
  return await sharp(shot)
    .composite([{ input: overlay(i) }])
    .png()
    .toBuffer()
}

const outPath = resolve(process.argv[2] ?? 'dist/tabbit-working.gif')
mkdirSync(dirname(outPath), { recursive: true })

const base = await sharp('source/mascot-working.png').resize(WORK, WORK).png().toBuffer()
const frames = []
for (let i = 0; i < FRAMES; i++) frames.push(await frame(base, i))

await sharp(frames, { join: { animated: true } })
  .gif({ delay: new Array(FRAMES).fill(DELAY), loop: 0, colours: 160, effort: 10 })
  .toFile(outPath)

if (process.env.SHEET) {
  const picks = (process.env.PICKS ?? '0,4,8,12').split(',').map(Number)
  await sharp({ create: { width: SIZE * picks.length, height: SIZE, channels: 4, background: '#000000' } })
    .composite(picks.map((p, n) => ({ input: frames[p], left: n * SIZE, top: 0 })))
    .png()
    .toFile(process.env.SHEET)
}
console.log('wrote', outPath, FRAMES, 'frames')
