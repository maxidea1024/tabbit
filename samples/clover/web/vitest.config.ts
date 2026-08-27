import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    include: ['test/**/*.test.ts'],
    // `tsc -b` 가 `.tsbuild` 에 산출물을 남깁니다. 거기 들어간 사본을 테스트로 보지
    // 않습니다 — 같은 테스트가 두 번 도는 것이고, 사본은 데이터 경로가 다릅니다.
    exclude: ['node_modules/**', '.tsbuild/**', 'dist/**'],
  },
})
