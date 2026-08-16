import Layout from '@theme/Layout'
import Link from '@docusaurus/Link'
import useBaseUrl from '@docusaurus/useBaseUrl'
import styles from './index.module.css'

// 아래 시트와 코드는 이 저장소의 `core` 픽스처와 그 골든 트리에서 그대로 가져온 것입니다.
// 회귀 스위트가 매 실행마다 바이트 단위로 대조하는 그 시트입니다.
const SHEET = `~~table:Item~~
References ItemCategory by record.

index          Name           CategoryId       GradeField   Price
primary index  item name      owning category  item grade   shop price
int            string         foreign          enum         int
                              ItemCategory     Grade
                                                            s

1              Short Sword    1                Common       100
2              Leather Armor  2                Rare         250
3              Small Potion   3                Epic          50`

const FIGURES = [
  { value: '13개 언어', label: '하나의 writer가 정한 형식을, 13개 리더가 각자 구현합니다. 스위트가 하나로 쓰고 13개로 읽어 대조합니다' },
  { value: '269,870행', label: '라이브 서비스 중인 프로젝트의 워크북 20개·테이블 275개를 한 모델로. 135초' },
  { value: '35.5%', label: '컬럼 인코딩으로 줄어든 크기. 4,111,118 바이트가 1,460,895 바이트' },
  { value: '1,001개', label: '매번 도는 게이트. 골든 비교부터 실제 언리얼 헤더 툴까지' },
]

const REASONS = [
  {
    title: '문제를 게임이 아니라 변환에서 만납니다',
    body: '걸러낼 수 있는 실수는 걸러내고, 남은 것은 어느 셀인지 알려줍니다. 구글 시트라면 링크를 눌러 그 자리로 갑니다. 한 번에 모아서 보고하므로 고치고 다시 돌리기를 반복하지 않습니다.',
  },
  {
    title: '시트에 적을 수 없는 규칙은 C#으로',
    body: '프로젝트의 .cs 파일을 변환할 때 컴파일해서 돌립니다. 그 게이트가 모든 출력보다 앞이라, 실패한 실행은 파일에도 데이터베이스에도 흔적을 남기지 않습니다.',
  },
  {
    title: '중간에 실패해도 이전 결과가 남습니다',
    body: '파일은 스테이징에 모았다가 한 번에 옮기고, 데이터베이스는 섀도 테이블을 통째로 바꿉니다. 읽는 쪽도 전부 읽고 참조까지 연결한 다음 교체합니다.',
  },
  {
    title: '시트를 먼저 고치지 않아도 됩니다',
    body: '이 도구의 규칙으로 쓰이지 않은 시트도 그대로 읽습니다. 레이아웃은 소스마다 지정하므로 한 recipe에서 섞어 읽고, 한쪽에서 선언한 enum을 다른 쪽 테이블이 씁니다.',
  },
]

const TARGETS = [
  'binary', 'json', 'mysql', 'postgresql', 'mongodb', 'redis',
  'csharp', 'typescript', 'cpp', 'c', 'unreal', 'go', 'rust',
  'python', 'java', 'kotlin', 'ruby', 'php', 'dart', 'html', 'summary', 'history',
]

export default function Home() {
  return (
    <Layout
      title="Game Data Authoring & Build Tool"
      description="게임의 정적 데이터를 짜고, 검증하고, 런타임 바이너리로 빌드하는 도구입니다."
    >
      <div className={styles.page}>
        <header className={styles.hero}>
          <div className={styles.heroInner}>
            <div>
              <span className={styles.eyebrow}>Game Data Authoring &amp; Build Tool</span>
              <h1 className={styles.title}>
                시트에 적고,
                <br />
                <em>런타임이 그대로 읽습니다</em>
              </h1>
              <p className={styles.lede}>
                아이템·스테이지·밸런스는 코드보다 자주 바뀌고, 손이 더 많이 타고, 틀렸을 때 가장
                늦게 드러납니다. Tabbit은 그 데이터를 기획자가 쓰는 자리에서 받아 검증하고,
                파싱이 필요 없는 바이너리와 그것을 읽는 코드로 냅니다.
              </p>
              <div className={styles.buttons}>
                <Link className="button button--primary button--lg" to="/docs/guide/concepts">
                  5분이면 감이 옵니다
                </Link>
                <Link className="button button--secondary button--outline button--lg" to="/docs/guide/install">
                  설치
                </Link>
              </div>
            </div>

            <img
              className={styles.heroArt}
              src={useBaseUrl('img/banner.png')}
              alt="Tabbit — 시트를 읽어 검증하고 바이너리와 코드를 냅니다"
            />
          </div>
        </header>

        <section className={styles.flow}>
          <div className={styles.section}>
            <div className={styles.sectionHead}>
              <h2>시트 한 장이 이렇게 됩니다</h2>
              <p>
                아래 시트와 코드는 지어낸 예제가 아니라 저장소의 <code>core</code> 픽스처와 그
                골든 트리에서 그대로 가져온 것입니다 — 회귀 스위트가 매 실행마다 바이트 단위로
                대조하는 그 시트입니다.
              </p>
            </div>

            <div className={styles.pair}>
              <div className={styles.panel}>
                <div className={styles.panelHead}>엑셀 · 구글 스프레드시트</div>
                <pre>{SHEET}</pre>
              </div>

              <div className={styles.arrow}>→</div>

              <div className={styles.panel}>
                <div className={styles.panelHead}>생성된 C#</div>
                <pre>
{`public string Name => _name;
`}<span className={styles.mark}>{`public ItemCategoryTable.Record CategoryId`}</span>{`
public Grade GradeField => _gradeField;
public int Price => _price;

await GameData.ReadAllAsync("./data");

var sword = GameData.Item.FindByIndex(1);
sword.Name;                 // Short Sword
`}<span className={styles.mark}>{`sword.CategoryId.Name;      // Weapon`}</span>{`
sword.GradeField;           // Common`}
                </pre>
              </div>
            </div>

            <p className={styles.note}>
              <strong>
                <code>CategoryId</code>를 <code>foreign</code>이라고 적었더니 <code>int</code>가
                아니라 레코드가 나왔습니다.
              </strong>{' '}
              파일에는 인덱스로 실리고, 읽은 뒤에 연결됩니다. 그래서{' '}
              <code>sword.CategoryId.Name</code>은 조회가 아니라 필드 접근입니다 — 같은 값을 여러
              시트에 베껴 적을 이유가 없어집니다.
            </p>

            <div className={styles.targets}>
              {TARGETS.map((t) => (
                <span className={styles.chip} key={t}>{t}</span>
              ))}
            </div>
          </div>
        </section>

        <section className={styles.figures}>
          <div className={styles.figureGrid}>
            {FIGURES.map((f) => (
              <div className={styles.figure} key={f.value}>
                <strong>{f.value}</strong>
                <span>{f.label}</span>
              </div>
            ))}
          </div>
        </section>

        <section className={styles.reasons}>
          <div className={styles.section}>
            <div className={styles.sectionHead}>
              <h2>왜 이걸 쓰나</h2>
              <p>데이터가 틀렸다는 걸 게임에서 알게 되는 대신, 변환에서 알게 됩니다.</p>
            </div>
            <div className={styles.cards}>
              {REASONS.map((r) => (
                <div className={styles.card} key={r.title}>
                  <h3>{r.title}</h3>
                  <p>{r.body}</p>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section className={styles.closing}>
          <h2>형식이 왜 지금의 모양인지도 적어 두었습니다</h2>
          <p>
            무엇을 얻고 무엇을 포기했는지, 무엇을 거절했는지, 그리고 예측이 어디서 틀렸는지까지.
          </p>
          <Link className="button button--primary button--lg" to="/docs/guide">
            문서 읽기
          </Link>
        </section>
      </div>
    </Layout>
  )
}
