import * as si from 'simple-icons'
import styles from './Targets.module.css'

// 로고는 각 브랜드의 것이고, 여기서는 「이 도구가 그 자리로 낸다」는 표시로만 씁니다.
// Simple Icons 의 경로 데이터는 CC0 이지만 로고 자체는 상표이므로, 색을 입히지 않고
// 본문 색(currentColor)으로 그립니다 — 후원이나 제휴로 읽히지 않게 하는 것이 하나이고,
// 다크 모드에서 검정 로고(Rust · OpenJDK)가 보이지 않는 것이 다른 하나입니다.
const brand = (icon, label) => ({ label, path: icon.path })

// 우리가 내는 형식에는 로고가 없으므로 직접 그립니다. 24×24, 획 없이 채우기만 —
// 브랜드 로고와 같은 자리에 같은 무게로 놓이게.
const own = (label, path) => ({ label, path, own: true })

const GROUPS = [
  {
    title: '데이터',
    items: [
      // 컬럼 지향이라는 사실이 그림입니다 — 세로 블록 셋에 길이가 다른 값들.
      own('binary', 'M3 3h4v18H3V3zm6 0h4v12H9V3zm6 0h6v7h-6V3zm0 9h6v9h-6v-9zM9 17h4v4H9v-4z'),
      brand(si.siJson, 'json'),
      brand(si.siHtml5, 'html'),
      // 한 장으로 묶인 보고서 — 줄과 그 아래 요약 막대.
      own('summary', 'M5 2h9l5 5v15H5V2zm8 1.5V8h4.5L13 3.5zM7.5 11h9v1.6h-9V11zm0 3.4h9V16h-9v-1.6zm0 3.4h5.4v1.6H7.5v-1.6z'),
      // 커밋이 이어진 선 — 왼쪽에 시간, 오른쪽에 그 시점의 값.
      own('history', 'M4 4.8a1.8 1.8 0 110 3.6 1.8 1.8 0 010-3.6zm0 6.6a1.8 1.8 0 110 3.6 1.8 1.8 0 010-3.6zm0 6.6a1.8 1.8 0 110 3.6 1.8 1.8 0 010-3.6zM3.1 8.4h1.8v3.6H3.1V8.4zm0 6.6h1.8v3.6H3.1V15zM8.4 5.7H21v1.8H8.4V5.7zm0 6.6H21v1.8H8.4v-1.8zm0 6.6H21v1.8H8.4v-1.8z'),
    ],
  },
  {
    title: '데이터베이스',
    items: [
      brand(si.siMysql, 'mysql'),
      brand(si.siPostgresql, 'postgresql'),
      brand(si.siMongodb, 'mongodb'),
      brand(si.siRedis, 'redis'),
    ],
  },
  {
    title: '읽는 코드',
    items: [
      // C# 로고는 Simple Icons 에 없습니다(상표로 내려갔습니다). 그 자리는 .NET 이 대신합니다.
      brand(si.siDotnet, 'csharp'),
      brand(si.siCplusplus, 'cpp'),
      brand(si.siC, 'c'),
      brand(si.siUnrealengine, 'unreal'),
      brand(si.siTypescript, 'typescript'),
      brand(si.siGo, 'go'),
      brand(si.siRust, 'rust'),
      brand(si.siPython, 'python'),
      brand(si.siOpenjdk, 'java'),
      brand(si.siKotlin, 'kotlin'),
      brand(si.siSwift, 'swift'),
      brand(si.siRuby, 'ruby'),
      brand(si.siPhp, 'php'),
      brand(si.siDart, 'dart'),
      brand(si.siLua, 'lua'),
    ],
  },
]

export default function Targets() {
  return (
    <div className={styles.groups}>
      {GROUPS.map((group) => (
        <div className={styles.group} key={group.title}>
          <div className={styles.groupTitle}>{group.title}</div>
          <div className={styles.row}>
            {group.items.map((item) => (
              <span
                className={item.own ? `${styles.chip} ${styles.ours}` : styles.chip}
                key={item.label}
              >
                <svg
                  className={styles.icon}
                  role="img"
                  viewBox="0 0 24 24"
                  aria-hidden="true"
                  focusable="false"
                >
                  <path d={item.path} fill="currentColor" />
                </svg>
                {item.label}
              </span>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}
