import { readFileSync } from 'fs'
import { defineConfig } from 'vite'

// **판 번호는 `package.json` 하나에서 옵니다.** 화면에 적어 두면 두 곳이 되고, 올릴 때
// 한쪽을 잊습니다.
const pkg = JSON.parse(
  readFileSync(new URL('./package.json', import.meta.url), 'utf8')) as { version: string }

export default defineConfig({
  base: './',
  define: { __APP_VERSION__: JSON.stringify(pkg.version) },
  build: {
    target: 'es2022',
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      // 나란히 놓고 보는 페이지들도 같이 굽습니다 — 에디션 셰이더 · 덱 15종의 뒷면 ·
      // 그림 없이 그린 얼굴 52장 · 카드에 붙는 표시.
      input: {
        main: new URL('./index.html', import.meta.url).pathname,
        editions: new URL('./editions.html', import.meta.url).pathname,
        backs: new URL('./backs.html', import.meta.url).pathname,
        faces: new URL('./faces.html', import.meta.url).pathname,
        artcheck: new URL('./artcheck.html', import.meta.url).pathname,
        marks: new URL('./marks.html', import.meta.url).pathname,
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
})
