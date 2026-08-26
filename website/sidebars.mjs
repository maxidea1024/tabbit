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
        'guide/glossary',
        'guide/sheets',
        'guide/cli',
        'guide/recipe',
      ],
    },

    {
      type: 'category',
      label: '쓰는 법',
      // 접어 두면 트러블슈팅이 사이드바에서 보이지 않습니다 — 실제로 「문서에 없다」는
      // 말을 들은 자리이고, 없었던 것이 아니라 두 단계 아래 접혀 있던 것입니다.
      collapsed: false,
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
            'guide/languages/swift',
            'guide/languages/lua',
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
        'spec/wire/tcb-column-oriented-rationale',
        'guide/benchmark',
        {
          type: 'category',
          label: '개정 기록',
          items: [
            'spec/wire/tcb-v102-column-encoding',
            'spec/wire/tcb-v103-presence-bitmap',
            'spec/wire/tcb-v104-composed-encodings',
            'spec/wire/tcb-v105-bit-width-packing',
            'spec/wire/tcb-v106-element-presence',
            'spec/wire/tcb-v107-dynamic-arrays',
            'spec/wire/tcb-mac-and-signature',
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
          label: '값의 형태',
          items: [
            'spec/types/nested-fields',
            'spec/types/nested-multi-level',
            'spec/layout/matrix-tables',
            'spec/types/variable-length-record-arrays',
            'spec/types/bitset',
            'spec/types/composite-value-types',
            'spec/types/optional-fields',
            'spec/types/blank-and-null-cells',
            'spec/types/nullable-array-elements',
            'spec/types/array-optionality',
            'spec/types/record-member-optionality',
          ],
        },
        {
          type: 'category',
          label: '참조',
          items: [
            'spec/references/multi-target-references',
            'spec/references/reference-key-types',
            'spec/references/references-in-records',
            'spec/references/multi-target-accessors',
            'spec/references/reference-optionality',
          ],
        },
        {
          type: 'category',
          label: '검증',
          items: [
            'spec/layout/column-constraints',
            'spec/validation/validation-pipeline',
            'spec/validation/rule-priority',
            'spec/validation/validation-usability-and-assembly-output',
          ],
        },
        {
          type: 'category',
          label: '읽기와 내기',
          items: [
            'spec/import/streaming-workbook-reader',
            'spec/import/xlsb-defined-names',
            'spec/import/xlsb-short-row-repair',
            'spec/layout/google-sheets-named-ranges',
            'spec/types/formula-errors',
            'spec/types/datetime-timezone',
            'spec/ops/known-problems',
            'spec/layout/table-row-sets',
            'spec/targets/generated-naming',
            'spec/targets/naming-conventions',
            'spec/targets/swift-language-support',
            'spec/targets/lua-language-support',
            'spec/targets/accessor-instances',
            'spec/ops/target-section-unification',
            'spec/ops/conversion-time',
            'spec/ops/build-cache',
            'spec/ops/build-report',
          ],
        },
        {
          type: 'category',
          label: '아직 하지 않은 것',
          items: [
            'spec/targets/constant-set-removal',
            'spec/validation/message-ids',
            'spec/ops/cli-help',
            'spec/targets/export-formats',
            'spec/targets/html-documentation',
            'spec/ops/install-channels',
            'spec/targets/godot-support',
            'spec/targets/cocos-creator-support',
            'spec/import/workbook-merge',
            'spec/ops/multi-user-operations',
          ],
        },
      ],
    },

    {
      type: 'category',
      label: '사례',
      items: [
        'samples/readme',
        'samples/sprout/readme',
        'samples/canopy/readme',
        'samples/wildling/readme',
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
