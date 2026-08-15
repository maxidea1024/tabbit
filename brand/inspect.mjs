// 시안 시트에서 자산의 경계를 찾습니다. 흰 배경 위에 놓인 패널들이라, 흰색이 아닌 픽셀의
// 행·열 분포를 보면 경계가 나옵니다. 자르기 좌표를 손으로 재지 않기 위한 도구입니다.
//
//   node inspect.mjs <파일> [x0 y0 x1 y1] [--thr 244]
import sharp from 'sharp'

const file = process.argv[2]
const nums = process.argv.slice(3).filter((a) => /^\d+$/.test(a)).map(Number)
const thrArg = process.argv.indexOf('--thr')
const thr = thrArg === -1 ? 244 : Number(process.argv[thrArg + 1])

const { data, info } = await sharp(file).raw().toBuffer({ resolveWithObject: true })
const { width, height, channels } = info

const [x0 = 0, y0 = 0, x1 = width - 1, y1 = height - 1] = nums

const colHits = new Array(width).fill(0)
const rowHits = new Array(height).fill(0)

for (let y = y0; y <= y1; y++) {
  for (let x = x0; x <= x1; x++) {
    const i = (y * width + x) * channels
    if (data[i] < thr || data[i + 1] < thr || data[i + 2] < thr) {
      colHits[x]++
      rowHits[y]++
    }
  }
}

function runs(hits, limit) {
  const out = []
  let start = -1
  for (let i = 0; i < hits.length; i++) {
    const on = hits[i] > limit
    if (on && start === -1) start = i
    if (!on && start !== -1) {
      out.push([start, i - 1, i - start])
      start = -1
    }
  }
  if (start !== -1) out.push([start, hits.length - 1, hits.length - start])
  return out.filter((r) => r[2] > 8)
}

console.log(`${file}  ${width}x${height}  영역 ${x0},${y0} → ${x1},${y1}`)
console.log('열 구간:', JSON.stringify(runs(colHits, Math.max(2, (y1 - y0) * 0.01))))
console.log('행 구간:', JSON.stringify(runs(rowHits, Math.max(2, (x1 - x0) * 0.01))))
