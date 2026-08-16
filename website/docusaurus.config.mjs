// @ts-check
import { themes as prismThemes } from 'prism-react-renderer'

const repo = 'https://github.com/maxidea1024/tabbit'

/**
 * 편집 링크는 사이트가 읽는 사본이 아니라 저장소의 원본을 가리켜야 합니다.
 * `sync-docs.mjs` 가 `doc/` 를 `guide/` 로 옮기므로 그 하나만 되돌립니다.
 */
function editUrl({ docPath }) {
  const source = docPath.startsWith('guide/') ? `doc/${docPath.slice('guide/'.length)}` : docPath
  return `${repo}/edit/main/${source}`
}

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'Tabbit',
  tagline: 'Game Data Authoring & Build Tool',

  favicon: 'img/favicon.ico',

  url: 'https://maxidea1024.github.io',
  baseUrl: '/tabbit/',
  organizationName: 'maxidea1024',
  projectName: 'tabbit',
  trailingSlash: false,

  // 깨진 링크는 빌드를 세웁니다. 문서에 게이트가 없던 자리라 이것이 게이트입니다.
  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',

  i18n: { defaultLocale: 'ko', locales: ['ko'] },

  markdown: {
    // `.md` 를 MDX 가 아니라 보통 마크다운으로 읽습니다. 문서에 `<Field>` · `{}` 같은 표기가
    // 그대로 들어 있어서, MDX 로 파싱하면 그것들이 전부 문법 오류가 됩니다.
    format: 'md',
    hooks: { onBrokenMarkdownLinks: 'throw' },
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          path: 'docs',
          routeBasePath: 'docs',
          sidebarPath: './sidebars.mjs',
          editUrl,
          showLastUpdateTime: false,
        },
        blog: false,
        theme: { customCss: './src/css/custom.css' },
      }),
    ],
  ],

  themes: [
    [
      // 검색은 빌드할 때 색인을 만들어 사이트 안에 넣습니다. Algolia DocSearch 는 신청과
      // 승인이 필요하고 크롤러가 사이트에 닿아야 하는데, 이 저장소는 아직 배포 전입니다.
      //
      // `ko` 를 함께 넣는 것이 이 문서 묶음에서는 필수입니다 — 한국어는 띄어쓰기만으로
      // 자르면 조사가 붙은 낱말이 서로 다른 토큰이 되어, 「시트를」로 찾으면 「시트」가
      // 안 나옵니다.
      // 이름 문자열로 씁니다 — 이 파일은 ESM 이라 `require.resolve` 가 없습니다.
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        language: ['ko', 'en'],
        indexBlog: false,
        docsRouteBasePath: '/docs',
        highlightSearchTermsOnTargetPage: true,
        searchResultLimits: 10,
      },
    ],
  ],

  headTags: [
    {
      tagName: 'link',
      attributes: { rel: 'apple-touch-icon', sizes: '180x180', href: '/tabbit/img/favicon-180.png' },
    },
  ],

  themeConfig: /** @type {import('@docusaurus/preset-classic').ThemeConfig} */ ({
    // 링크를 공유했을 때 나오는 카드.
    image: 'img/og-card.png',
    // 토글은 라이트와 다크 둘만 오갑니다.
    //
    // `respectPrefersColorScheme` 를 켜면 상태가 셋이 됩니다 — 시스템 · 라이트 · 다크. 그러면
    // OS가 라이트인 사람의 첫 클릭이 「시스템 → 라이트」라서 화면이 그대로이고, 버튼이 한 번은
    // 먹지 않는 것처럼 보입니다. 그 값과 OS 설정을 따라가는 편의를 맞바꿉니다.
    colorMode: { defaultMode: 'light', respectPrefersColorScheme: false },
    navbar: {
      title: 'Tabbit',
      // 마스코트 타일 하나로 밝은 바탕과 어두운 바탕을 모두 씁니다 — 배경이 보라라
      // 어느 쪽에서도 대비가 납니다.
      logo: { alt: 'Tabbit', src: 'img/logo.png' },
      items: [
        { type: 'doc', docId: 'guide/readme', position: 'left', label: '문서' },
        { to: '/docs/guide/install', position: 'left', label: '설치' },
        { to: '/docs/spec/tcb-column-oriented-rationale', position: 'left', label: '설계 노트' },
        { href: `${repo}/releases`, label: '릴리즈', position: 'right' },
        { href: repo, label: 'GitHub', position: 'right' },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: '문서',
          items: [
            { label: '시작하기', to: '/docs/guide/install' },
            { label: '시트 작성', to: '/docs/guide/sheets' },
            { label: '언어별 가이드', to: '/docs/guide/languages' },
          ],
        },
        {
          title: '형식',
          items: [
            { label: '바이너리 형식', to: '/docs/guide/binary-format' },
            { label: '왜 컬럼 지향인가', to: '/docs/spec/tcb-column-oriented-rationale' },
            { label: '벤치마크', to: '/docs/guide/benchmark' },
          ],
        },
        {
          title: '저장소',
          items: [
            { label: 'GitHub', href: repo },
            { label: '릴리즈', href: `${repo}/releases` },
            { label: '이슈', href: `${repo}/issues` },
          ],
        },
      ],
      copyright: 'Tabbit — MIT License',
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: [
        'csharp',
        'cpp',
        'c',
        'go',
        'rust',
        'python',
        'java',
        'kotlin',
        'ruby',
        'php',
        'dart',
        'json',
        'bash',
        'powershell',
        'sql',
        'toml',
      ],
    },
  }),
}

export default config
