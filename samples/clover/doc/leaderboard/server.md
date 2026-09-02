# 서버

> [리더보드로](../leaderboard.md)

---

**Express · Redis · PostgreSQL.** 셋 다 흔한 것이고 그 이유로 고릅니다 — 이 서버가 하는
일에 특별한 것이 없습니다. 데이터베이스는 **knex** 로 다룹니다.

**구현이 [`samples/clover/server/`](../../server/readme.md) 에 있습니다.**

## 자리

```
samples/clover/server/
├── src/
│   ├── app.ts            Express. 라우트를 모아 듭니다
│   ├── auth/             제공자 4개 · 세션
│   ├── ranked.ts         시드 발급
│   ├── runs.ts           제출 받기 · 조회
│   ├── worker.ts         큐에서 꺼내 재현. **별도 프로세스**
│   ├── boards.ts         Redis 순위 · 조회
│   ├── season.ts         시즌 · 지문 확인
│   └── db/               스키마 · 마이그레이션
├── docker-compose.yml
├── Dockerfile
└── package.json
```

**코어를 `../web/src/core` 에서 그대로 임포트합니다.** 복사하지 않습니다 — 복사하면 둘이
갈라지고, 갈라지면 `invalid_action` 이 정직한 제출에서 납니다. `headless.ts` 가 하는 것과
같은 임포트입니다. 데이터는 `../web/public/data` 의 `.tcb` 를 `loadFromDisk` 로 읽습니다.

## 세 가지의 몫

|무엇|어디에|왜|
|--|--|--|
|계정 · 신원 · 세션 · 시즌 · 제출과 리플레이 · 지표|**PostgreSQL**|정본. 지워지면 안 되는 것|
|보드의 순위 · 랭크 시드 · 한도 · 큐 · 교환 code|**Redis**|빠르고 다시 만들 수 있는 것. **Redis 가 비어도 PostgreSQL 에서 전부 다시 만듭니다**|
|API · 재현|**Express** + worker|—|

**Redis 는 캐시입니다.** 순위표를 PostgreSQL 만으로도 낼 수 있지만 「내 순위」가 `COUNT(*)
WHERE score > ?` 이고 그것은 보드마다 한 번 전체를 셉니다. `ZREVRANK` 는 O(log N) 입니다.
부팅할 때 `run_metric` 을 전부 읽어 ZSET 을 다시 채우는 데 100만 행에 1분 안입니다.

## 스키마

```sql
account      (id, created_at)
identity     (provider, subject, account_id, created_at)        PK (provider, subject)
profile      (account_id PK, handle UNIQUE, handle_changed_at,
              tier, last_season_tier)
-- **한 계정에 여러 줄입니다.** 기계마다 하나이고, 한 줄을 지우는 것이 그 기계의 로그아웃.
session      (id, account_id, refresh_hash, prev_hash, rotated_at,
              label, expires_at, created_at, used_at)
season       (id, fingerprint, starts_at, ends_at)
ranked_seed  (seed PK, account_id, deck, stake, pool, challenge,
              issued_at, expires_at, used_at)
submission   (id, account_id, season_id, seed, deck, stake, pool, challenge,
              replay JSONB, status, reason, submitted_at, judged_at)
run_metric   (submission_id PK, ascent, best_hand, fewest_hands,
              money_at_win, skips, won)
```

|선택|이유|
|--|--|
|`replay` 를 `JSONB` 로|다시 재현할 일이 있습니다 — 시즌 감사 · 코어 수정 뒤 확인. 압축하지 않습니다. 완주가 50KB 이고 10만 제출이 5GB 입니다|
|`run_metric` 을 제출과 나눔|지표가 6개에서 늘 때 `submission` 을 건드리지 않습니다|
|`best_hand` 를 `NUMERIC`|`DOUBLE` 은 2^53 위에서 정확하지 않고, 지금은 그 아래지만 표기를 바꿀 자리를 남깁니다|
|`Wins` · `ChallengesBeaten` 에 표가 없음|계정의 값이므로 제출이 받아들여질 때 세어 ZSET 에 씁니다. `COUNT(*) WHERE won` 과 `COUNT(DISTINCT challenge) WHERE won AND challenge <> ''`|
|`challenge` 를 `''` 로 두고 `NULL` 을 쓰지 않음|`WHERE challenge = ''` 가 「챌린지가 아닌 런」입니다. `NULL` 이면 그 조건이 `IS NULL` 이 되고, 한 곳에서 `= ''` 로 적으면 조용히 0행입니다|

## Redis 의 열쇠

|열쇠|타입|무엇|
|--|--|--|
|`lb:{season}:{board_id}`|ZSET|시즌 순위. member 는 `account_id`, score 는 지표. **`ZADD GT` 로 더 나을 때만 바뀝니다**|
|`lb:all:{board_id}`|ZSET|전체 기간|
|`seed:{seed}`|STRING · TTL 24h|랭크 시드. PostgreSQL 에도 있고 이것은 빠른 길입니다|
|`code:{code}`|STRING · TTL 60s|로그인 뒤 한 번 쓰는 교환 code|
|`rate:{account}:{what}`|STRING · TTL|한도. `seed` · `submit` · `handle`|
|`queue:judge`|LIST|재현 대기. `BRPOP` 으로 worker 가 받습니다|

**동점의 순서.** ZSET 은 같은 score 를 member 의 사전순으로 놓습니다. 그러면 먼저 낸 사람이
아니라 `account_id` 가 작은 사람이 위입니다. score 의 **소수부에 시각을 담아** 먼저 낸 쪽이
위가 되게 합니다 — 정수부가 지표이므로 지표가 다르면 시각이 끼어들지 못하고, `FewestHands`
처럼 작은 것이 위인 보드는 부호를 뒤집어 넣습니다.

|정하는 것|왜|
|--|--|
|소수부가 1이 되지 않게 누릅니다|정확히 1이면 정수부로 올라가고, 그러면 되읽은 값이 하나 어긋납니다. **시각이 정확히 시작점일 때 그렇습니다**|
|뒤집어 넣은 값을 되읽는 것은 부호만 되돌립니다|`−31 + 0.9` 가 `−30.1` 이고 그 내림이 `−31` 입니다|
|`-0` 을 내보내지 않습니다|같은 수이지만 JSON 에 `-0` 으로 적히고 화면이 그것을 그립니다|
|분해능|지표가 클수록 거칩니다. 지금 값(한 손 최고가 10^5 대)에서는 1초보다 곱고, 10^9 을 넘으면 몇십 초 안의 제출이 같은 자리가 됩니다|

**「내 순위」는 ZSET 하나에 한 사람이 한 자리입니다.** 새 제출이 더 나을 때만 `ZADD GT` 로
바뀝니다. 한 사람의 최고 기록이 보드의 뜻이므로, 한 사람이 10위부터 20위까지를 차지하지
않습니다.

## API

|메서드|경로|무엇|로그인|
|--|--|--|--|
|`GET`|`/auth/{provider}`|제공자로 redirect|—|
|`GET`|`/auth/{provider}/callback`|신원 확인 · code 발급 · `return` 으로 redirect|—|
|`POST`|`/auth/exchange`|code → access + refresh|—|
|`POST`|`/auth/refresh`|refresh → 새 쌍|—|
|`POST`|`/auth/logout`|세션 삭제|필요|
|`GET`|`/me`|계정 · 이름 · 등급 · 내 보드별 순위|필요|
|`POST`|`/me/handle`|이름 정하기 · 바꾸기|필요|
|`DELETE`|`/me`|계정 삭제|필요|
|`POST`|`/ranked/seed`|랭크 시드 발급. `{deck, stake, pool}` 또는 `{challenge}`|필요|
|`POST`|`/runs`|제출|필요|
|`GET`|`/runs/{id}`|판정과 순위|필요|
|`GET`|`/boards`|보드 목록 64개. **시트에서 읽은 것을 그대로**|필요|
|`GET`|`/boards/{id}?period=season\|all&page=&around=me`|순위표 한 쪽. `around=me` 면 내 자리를 가운데로|필요|
|`GET`|`/profiles/{handle}`|다른 사람의 등급과 보드별 순위|필요|
|`GET`|`/health`|PostgreSQL · Redis · 지문|—|

**`/boards` 가 로그인을 요구하는 것은 결정 그대로입니다** — 리더보드는 로그인한 사람의
것입니다. 나중에 열려면 그 한 줄을 빼면 됩니다.

한 쪽은 25행입니다. `page` 는 0 부터이고 `around=me` 가 있으면 `page` 를 무시합니다.

## 개발 환경 — docker-compose

```yaml
services:
  postgres:
    image: postgres:16-alpine
    environment: { POSTGRES_DB: clover, POSTGRES_USER: clover, POSTGRES_PASSWORD: clover }
    # **호스트 포트를 비켜 둡니다.** 기계에 다른 스택의 데이터베이스가 이미 떠 있습니다.
    ports: ["55432:5432"]
    volumes: [pg:/var/lib/postgresql/data]
  redis:
    image: redis:7-alpine
    ports: ["56379:6379"]
  server:
    build: .
    command: npx tsx watch src/app.ts
    environment:
      DATABASE_URL: postgres://clover:clover@postgres/clover
      REDIS_URL: redis://redis
      AUTH_PROVIDERS: github            # 개발은 이것 하나로 충분합니다
      PUBLIC_URL: http://localhost:8787
      RETURN_ALLOWLIST: http://localhost:5173
    env_file: .env                      # 제공자의 client id · secret. 커밋하지 않습니다
    volumes: [".:/app", "../web:/web"]  # 코어를 임포트하므로 web 도 마운트합니다
    ports: ["8787:8787"]
    depends_on: [postgres, redis]
  worker:
    build: .
    command: npx tsx watch src/worker.ts
    environment: { DATABASE_URL: …, REDIS_URL: … }
    volumes: [".:/app", "../web:/web"]
    depends_on: [postgres, redis]
volumes: { pg: {} }
```

```
docker compose up             # 넷이 뜹니다
npm run dev                   # web/ 에서. vite 가 /api 를 8787 로 넘깁니다
```

|무엇|어떻게|
|--|--|
|마이그레이션|**knex 의 것을 씁니다.** `server` 가 부팅할 때 `src/db/migrations/*.ts` 를 차례로 돕니다 — 무엇까지 돌렸는지를 적는 표와 트랜잭션과 순서가 이미 거기 있습니다. 스키마는 `knex.raw` 로 적습니다: 빌더로 적으면 부분 인덱스와 `NUMERIC` 이 방언별 우회를 타고, 그러면 표를 읽어도 무엇이 만들어지는지 알 수 없게 됩니다|
|첫 시즌|마이그레이션의 마지막 줄이 `season` 에 지금 지문으로 한 행을 넣습니다. **개발에서는 지문 확인을 끄지 않습니다** — 끄면 배포에서만 걸립니다|
|합성 데이터|`npm run seed:fake -- --accounts 10000` 이 구워 둔 리플레이 13개를 섞어 넣습니다. L2 의 판정이 이것 위에서입니다|
|`.env`|`GITHUB_CLIENT_ID` · `GITHUB_CLIENT_SECRET`. 다른 제공자는 배포 환경에만|

**worker 가 따로 뜨는 것은 개발에서도 같습니다.** 한 프로세스에 합치면 개발에서는 되고
배포에서 큐가 막히는 것을 개발이 보지 못합니다.

## 배포

정하지 않습니다. 컨테이너 셋이므로 어디든 됩니다. 정해야 할 것은 셋입니다 — 제공자에 등록할
callback 주소, `RETURN_ALLOWLIST`, 그리고 **지문이 바뀌는 배포 앞에 `season` 행을 넣는 것**.

---

EOD
