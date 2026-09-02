# clover 리더보드 서버

> [clover 로](../readme.md) · [설계](../doc/leaderboard.md)

---

**계정 · 랭크 시드 · 제출과 재현 · 순위.** Express · Redis · PostgreSQL 이고, 셋 다 흔한
것이라서 골랐습니다 — 이 서버의 역할에 특별한 것이 없습니다.

**점수를 받지 않고 리플레이를 받습니다.** 같은 코어로 다시 돌려 지표를 세고 그것만 올립니다.

## 띄우기

```
docker compose up            # postgres · redis · server · worker
curl localhost:8787/health
```

호스트 포트가 **55432(postgres)** 와 **56379(redis)** 입니다. 기본 포트를 비켜 둔 것은 이
기계에 다른 스택이 이미 떠 있기 때문이고, 컨테이너 안에서는 기본 포트 그대로이므로 서버의
설정은 바뀌지 않습니다.

도커 없이 돌리려면 데이터베이스 둘만 띄우고 나머지는 손으로 돕니다.

```
docker compose up -d postgres redis
npm install
npm run dev        # 서버
npm run worker     # 재현. **따로 뜹니다**
```

**worker 가 따로 뜨는 것은 개발에서도 같습니다.** 한 프로세스에 합치면 개발에서는 되고
배포에서 큐에 적체가 생기는 것을 개발이 보지 못합니다.

## 시험

PostgreSQL 과 Redis 가 떠 있어야 합니다.

```
docker compose up -d postgres redis
npm test
```

**판정은 구워 둔 리플레이 13개입니다.** API 로 넣어 전부 `accepted` 이고, 서버가 센 지표가
[골든](../design-data/out/replay)에 적힌 것과 같아야 합니다 — 클라이언트가 보낸 숫자를 쓰지
않는다는 것의 증거가 그것입니다.

## 여기 있는 것

|파일|무엇|
|--|--|
|`src/app.ts`|Express. 부팅에서 환경변수 · 마이그레이션 · 시즌의 지문을 확인합니다|
|`src/worker.ts`|큐에서 꺼내 재현합니다. **별도 프로세스**|
|`src/judge.ts`|재현과 판정. 웹의 코어를 그대로 부릅니다|
|`src/core.ts`|`../web/src/core` 를 임포트하는 자리. **복사하지 않습니다**|
|`src/auth/`|제공자 4개 · 세션|
|`src/ranked.ts`|시드 발급과 한 번 쓰기|
|`src/runs.ts`|제출과 조회|
|`src/accounts.ts`|계정 · 신원 · 표시 이름|
|`src/boards.ts`|보드 64개 · Redis 순위 · 등급. **어느 보드가 있는가는 시트가 정합니다**|
|`src/season.ts`|시즌과 규칙 지문|
|`src/db.ts` · `src/db/migrations/`|knex. 마이그레이션은 부팅할 때 스스로 돕니다|

## 만명의 순위표

```
npx tsx tools/seed-fake.ts --accounts 10000
npm run bench
```

**어느 쪽 순위도 100ms 안에 응답해야 합니다.** `COUNT(*) WHERE score > ?` 로는 보드마다 한 번
전체를 세게 되므로 Redis 의 정렬 집합을 두었고, 그 값이 여기에서 확인됩니다.

## 환경변수

`.env.example` 을 `.env` 로 복사해서 채웁니다. **`.env` 는 커밋하지 않습니다.**

|이름|기본값|무엇|
|--|--|--|
|`AUTH_PROVIDERS`|`github`|켤 제공자. **`NODE_ENV=production` 에서 `github` 이 있으면 부팅하지 않습니다**|
|`DATABASE_URL`|`…@localhost:55432/clover`|PostgreSQL|
|`REDIS_URL`|`redis://localhost:56379`|Redis|
|`PUBLIC_URL`|`http://localhost:8787`|제공자에 등록한 callback 의 앞부분|
|`RETURN_ALLOWLIST`|`http://localhost:5173`|로그인을 마치고 돌아갈 수 있는 주소. **여기 없는 곳으로는 보내지 않습니다**|
|`JWT_SECRET`|개발에만 기본값|access token 의 서명|

## 배포 전에 정하는 셋

1. 제공자에 등록할 callback 주소 — `<PUBLIC_URL>/auth/<제공자>/callback`
2. `RETURN_ALLOWLIST`
3. **지문이 바뀌는 배포 앞에 `season` 행을 넣는 것.** 넣지 않으면 서버가 뜨지 않습니다

셋째가 실수하기 쉬운 자리입니다. 규칙이 바뀌면 예전 점수와 견줄 수 없고, 그것을 알리는
자리가 시즌입니다 — [규칙 지문](../doc/leaderboard/submission.md#규칙-지문).

---

EOD
