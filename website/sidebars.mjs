// 사이드바는 손으로 적습니다. 자동 생성은 파일 이름 순서라 「읽는 순서」가 나오지 않습니다.
// 구성은 doc/readme.md 와 같습니다 — 그쪽이 GitHub에서 읽을 때의 목록이고, 이쪽이 사이트의 목록입니다.

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docs: [
    'guide/readme',

    {
      type: 'category',
      label: '시작하기',
      collapsed: false,
      items: [
        'guide/install',
        'guide/concepts',
        'guide/sheets',
        'guide/cli',
        'guide/recipe',
      ],
    },

    {
      type: 'category',
      label: '쓰는 법',
      items: [
        'guide/features',
        {
          type: 'category',
          label: '언어별 가이드',
          link: { type: 'doc', id: 'guide/languages/readme' },
          items: [
            'guide/languages/csharp',
            'guide/languages/typescript',
            'guide/languages/cpp',
            'guide/languages/c',
            'guide/languages/unreal',
            'guide/languages/go',
            'guide/languages/rust',
            'guide/languages/python',
            'guide/languages/java',
            'guide/languages/kotlin',
            'guide/languages/ruby',
            'guide/languages/php',
            'guide/languages/dart',
            'guide/languages/html',
          ],
        },
        'guide/validation',
        'guide/exports',
        'guide/history',
        'guide/troubleshooting',
      ],
    },

    {
      type: 'category',
      label: '형식 — TCB',
      items: [
        'guide/binary-format',
        'spec/tcb-column-oriented-rationale',
        'guide/benchmark',
        {
          type: 'category',
          label: '개정 기록',
          items: [
            'spec/tcb-v102-column-encoding',
            'spec/tcb-v103-presence-bitmap',
            'spec/tcb-v104-composed-encodings',
            'spec/tcb-v105-bit-width-packing',
            'spec/tcb-mac-and-signature',
          ],
        },
      ],
    },

    {
      type: 'category',
      label: '설계 노트',
      items: [
        {
          type: 'category',
          label: '값의 모양',
          items: [
            'spec/nested-fields',
            'spec/nested-multi-level',
            'spec/matrix-tables',
            'spec/variable-length-record-arrays',
            'spec/bitset',
            'spec/optional-fields',
            'spec/array-optionality',
            'spec/record-member-optionality',
          ],
        },
        {
          type: 'category',
          label: '참조',
          items: [
            'spec/multi-target-references',
            'spec/reference-key-types',
            'spec/references-in-records',
            'spec/reference-optionality',
          ],
        },
        {
          type: 'category',
          label: '검증',
          items: [
            'spec/column-constraints',
            'spec/validation-pipeline',
            'spec/rule-priority',
            'spec/validation-usability-and-assembly-output',
          ],
        },
        {
          type: 'category',
          label: '읽기와 내기',
          items: [
            'spec/streaming-workbook-reader',
            'spec/keyed-layout',
            'spec/generated-naming',
            'spec/accessor-instances',
            'spec/target-section-unification',
          ],
        },
      ],
    },

    {
      type: 'category',
      label: '사례',
      items: [
        'samples/rescue/doc/적용-기록',
        'samples/named-range/doc/레이아웃-분석-20260808',
        'samples/named-range/doc/검증-이식-견적-20260811',
      ],
    },

    {
      type: 'category',
      label: '저장소',
      items: ['guide/architecture', 'guide/dependencies', 'guide/roadmap'],
    },
  ],
}

export default sidebars
