import { defineConfig } from 'vite'

export default defineConfig({
  base: './',
  build: {
    target: 'es2022',
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      // 나란히 놓고 보는 페이지들도 같이 굽습니다 — 에디션 셰이더와 덱 15종의 뒷면.
      input: {
        main: new URL('./index.html', import.meta.url).pathname,
        editions: new URL('./editions.html', import.meta.url).pathname,
        backs: new URL('./backs.html', import.meta.url).pathname,
        artcheck: new URL('./artcheck.html', import.meta.url).pathname,
      },
    },
  },
  // 개발에서만 씁니다. **배포에서는 같은 곳에서 서빙하거나 앞단이 넘깁니다** — 주소를
  // 코드에 두면 배포마다 빌드를 다시 해야 합니다.
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:8787',
        changeOrigin: true,
        rewrite: (path: string) => path.replace(/^\/api/, ''),
      },
    },
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
