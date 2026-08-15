import Layout from '@theme/Layout'
import Link from '@docusaurus/Link'
import useBaseUrl from '@docusaurus/useBaseUrl'
import styles from './index.module.css'

const CARDS = [
  {
    title: '13개 언어가 같은 파일을 읽습니다',
    body: 'C#·TypeScript·C++·C·Go·Rust·Python·Java·Kotlin·Ruby·PHP·Dart와 언리얼 모듈. 포맷을 정의하는 것은 writer 하나이고, 회귀 스위트가 하나로 쓰고 13개로 읽어 대조합니다.',
  },
  {
    title: '문제를 게임이 아니라 변환에서',
    body: '걸러낼 수 있는 실수는 걸러내고, 남은 것은 어느 셀인지 알려줍니다. 시트에 적을 수 없는 규칙은 C#으로 적고, 그 게이트가 모든 출력보다 앞에 섭니다.',
  },
  {
    title: '중간에 실패해도 이전 결과가 남습니다',
    body: '파일은 스테이징에 모았다가 한 번에 옮기고, 데이터베이스는 섀도 테이블을 통째로 바꿉니다. 읽는 쪽도 전부 읽고 연결한 다음 교체합니다.',
  },
  {
    title: '따로 설치할 것이 없습니다',
    body: '바이너리를 읽는 코드까지 출력 폴더에 함께 나옵니다. 플러그인을 깔거나 include 경로를 잡을 일이 없고, Go는 go.mod까지 같이 나옵니다.',
  },
]

export default function Home() {
  return (
    <Layout title="Game Data Authoring & Build Tool" description="게임의 정적 데이터를 짜고, 검증하고, 런타임 바이너리로 빌드하는 도구입니다.">
      <header className={styles.hero}>
        <h1 className={styles.title}>Tabbit</h1>
        <p className={styles.tagline}>Game Data Authoring &amp; Build Tool</p>
        <p className={styles.lede}>
          게임 시스템의 근간은 정적 데이터입니다. 코드보다 자주 바뀌고, 손이 더 많이 타고,
          틀렸을 때 가장 늦게 드러납니다. Tabbit은 그 데이터를 <strong>짜고, 검증하고,
          런타임 데이터로 빌드하는</strong> 도구입니다.
        </p>

        <div className={styles.buttons}>
          <Link className="button button--primary button--lg" to="/docs/guide/install">
            시작하기
          </Link>
          <Link className="button button--secondary button--outline button--lg" to="/docs/guide">
            문서 보기
          </Link>
        </div>

        <img
          className={styles.banner}
          src={useBaseUrl('img/banner.png')}
          alt="Tabbit — 시트를 읽어 검증하고 TCB 바이너리와 코드를 냅니다"
        />
      </header>

      <main className={styles.strip}>
        <div className={styles.cards}>
          {CARDS.map((card) => (
            <div className={styles.card} key={card.title}>
              <h3>{card.title}</h3>
              <p>{card.body}</p>
            </div>
          ))}
        </div>
      </main>
    </Layout>
  )
}
