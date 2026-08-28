import Layout from '@theme/Layout'
import Link from '@docusaurus/Link'
import useBaseUrl from '@docusaurus/useBaseUrl'
import Targets from '@site/src/components/Targets'
import styles from './index.module.css'

// 아래 시트와 코드는 doc/concepts.md 가 처음부터 끝까지 따라가는 예제이고, 그 문서의 그림은
// `core` 픽스처를 그대로 옮긴 것입니다 — doc/figures/concepts-figures.py 가 정본입니다.
// 여기를 고치면 그쪽도 함께 고칩니다.
const SHEET = `:table Item      References ItemCategory by record.

:field          index          Name           CategoryId             GradeField   Price
:type           int            string         foreign ItemCategory   Grade        int
:desc           primary index  item name      owning category        item grade   shop price
:target         cs             cs             cs                     cs           s

                1              Short Sword    1                      Common       100
                2              Leather Armor  2                      Rare         250
                3              Small Potion   3                      Epic          50`

const FIGURES = [
  { value: '27배', label: '같은 데이터를 JSON으로 낼 때와 비교한 크기. 그만큼 덜 읽고 덜 만듭니다' },
  { value: '549개', label: '샘플 하나가 담은 테이블. 워크북 42개를 한 번에 읽습니다' },
  { value: 'C#부터 Lua까지', label: '쓰는 언어로 읽는 코드가 나옵니다. 손으로 파서를 쓰지 않습니다' },
  { value: '1,752개', label: '커밋마다 도는 검사. 생성된 코드를 언어마다 실제로 컴파일해서 돌려 봅니다' },
]

const REASONS = [
  {
    title: '문제를 게임이 아니라 변환에서 만납니다',
    body: '걸러낼 수 있는 실수는 걸러내고, 남은 것은 어느 셀인지 알려줍니다. 구글 시트라면 링크를 눌러 그 자리로 갑니다. 한 번에 모아서 보고하므로 고치고 다시 돌리기를 반복하지 않습니다.',
  },
  {
    title: '시트로 표현할 수 없는 규칙까지',
    body: '「전설 등급은 강화 재료가 있어야 한다」 같은 규칙을 C# 파일 하나로 적습니다. 검사는 무엇을 내보내기 전에 끝나므로, 규칙을 어긴 데이터는 게임에 도달하지 않습니다.',
  },
  {
    title: '반쯤 바뀐 데이터가 나오지 않습니다',
    body: '빌드가 중간에 실패해도 직전 결과가 그대로 남습니다. 새 데이터는 전부 준비된 다음에 한 번에 바뀌고, 게임이 읽는 쪽도 마찬가지입니다.',
  },
  {
    title: '쓰던 시트를 그대로 가져옵니다',
    body: '이 도구를 몰랐던 시트도 읽습니다. 이미 몇 년치가 쌓인 시트를 다시 쓰는 것은 현실적이지 않으니, 읽는 규칙을 시트마다 지정하고 새 것과 옛 것을 한 번에 변환합니다.',
  },
]

export default function Home() {
  return (
    <Layout
      title="Game Data Authoring & Build Tool"
      description="게임의 정적 데이터를 작성하고, 검증하고, 런타임 바이너리로 빌드하는 도구입니다."
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
                {/* 링크가 닿는 곳이 예제 하나를 끝까지 도는 문서이므로, 그렇게 적습니다. */}
                <Link className="button button--primary button--lg" to="/docs/guide/concepts">
                  예제로 시작하기
                </Link>
                {/*
                  테마의 `secondary` 를 쓰지 않습니다. 그 색은 밝은 배경을 전제로 고른 것이라
                  어두운 히어로에서는 글자가 배경에 묻힙니다 — 라이트 모드에서 실제로 그렇게
                  보인다는 보고를 받은 자리입니다. 이 버튼의 색은 전부 여기서 정합니다.
                */}
                <Link className={`button button--lg ${styles.ghostButton}`} to="/docs/guide/install">
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

        {/*
          히어로 바로 다음입니다. 이 도구를 처음 보는 사람이 하는 일은 「이게 뭔가」가
          아니라 「내가 뭘 하면 되나」이고, 그 답이 두 갈래입니다. 목록 안쪽에 두면
          없는 것과 같습니다 — 실제로 그랬습니다.
        */}
        <section className={styles.entry}>
          <div className={styles.entryGrid}>
            <Link className={styles.entryCard} to="/docs/guide/quickstart-designer">
              <span className={styles.entryWho}>시트에 데이터를 적는다면</span>
              <strong>기획자용 빠른 시작</strong>
              <span className={styles.entryWhat}>
                엑셀만 있으면 됩니다. 알아야 하는 특수문자는 두 개이고, 표 하나를 그 자리에서
                만들어 봅니다.
              </span>
            </Link>

            <Link className={styles.entryCard} to="/docs/guide/quickstart-developer">
              <span className={styles.entryWho}>도구를 붙인다면</span>
              <strong>개발자용 빠른 시작</strong>
              <span className={styles.entryWhat}>
                설치하고, recipe를 하나 쓰고, 생성된 코드로 값을 읽는 데까지.
              </span>
            </Link>
          </div>
        </section>

        <section className={styles.flow}>
          <div className={styles.section}>
            <div className={styles.sectionHead}>
              <h2>시트 한 장이 이렇게 됩니다</h2>
              <p>
                왼쪽은 기획자가 스프레드시트에 적는 그대로입니다. 특별한 도구도, 별도의 스키마
                파일도 없습니다. 오른쪽은 그것으로 만들어진 코드이고, 프로그래머는 이걸
                받아 씁니다.
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
public int CategoryId => _categoryId;
`}<span className={styles.mark}>{`public ItemCategoryRecord ItemCategoryByCategoryId`}</span>{`
public Grade GradeField => _gradeField;

await GameData.ReadAllAsync("./data");

var sword = GameData.Item.FindByIndex(1);
sword.Name;                                // Short Sword
`}<span className={styles.mark}>{`sword.ItemCategoryByCategoryId.Name;       // Weapon`}</span>{`
sword.GradeField;                          // Common`}
                </pre>
              </div>
            </div>

            <p className={styles.note}>
              <strong>분류 이름을 아이템 시트에 다시 적지 않아도 됩니다.</strong>{' '}
              타입 칸에 <code>foreign ItemCategory</code>라고만 적으면, 코드에서는 분류 자체가
              따라옵니다. 분류 이름이 바뀌면 한 곳만 고치면 되고, 없는 분류를 가리키면
              변환이 멈추고 어느 셀인지 알려줍니다.
            </p>

            <Targets />
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
              <p>
                데이터가 잘못됐다는 것을 게임을 띄우고 나서 알게 되면, 그때는 이미 재현 경로를
                찾고 있습니다. 빌드하는 자리에서 걸러낼 수 있는 것은 최대한 걸러냅니다.
              </p>
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
          <h2>먼저 둘러보셔도 좋습니다</h2>
          <p>
            시트 한 장으로 시작해 코드가 나오는 데까지, 예제를 따라가며 읽을 수 있게 써
            두었습니다. 쓰다가 막히는 자리에 대한 답도 대체로 그 안에 있습니다.
          </p>
          <Link className="button button--primary button--lg" to="/docs/guide">
            문서 읽기
          </Link>
        </section>
      </div>
    </Layout>
  )
}
