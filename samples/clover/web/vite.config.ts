import { defineConfig } from 'vite'

export default defineConfig({
  base: './',
  build: {
    target: 'es2022',
    outDir: 'dist',
    sourcemap: true,
  },
  resolve: {
    alias: {
      // 생성된 테이블 코드가 `fs` 와 `path` 를 정적으로 임포트합니다. 브라우저에서는 그
      // 경로를 지나지 않지만(우리는 `readBinaryFrom` 만 씁니다) 번들러는 임포트를 해석하므로
      // 빈 것으로 바꿔 둡니다.
      //
      // **이것이 `doc/tool-findings.md` §6 의 다른 쪽 얼굴입니다** — 생성 코드가 파일에서
      // 읽는 것을 알고 있어서, 그 사실이 브라우저 빌드까지 따라옵니다.
      fs: new URL('./src/shim/empty.ts', import.meta.url).pathname,
      path: new URL('./src/shim/path.ts', import.meta.url).pathname,
      module: new URL('./src/shim/empty.ts', import.meta.url).pathname,
    },
  },
})
