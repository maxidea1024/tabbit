// 계정과 순위의 정본.
//
// **Redis 가 비어도 여기에서 전부 다시 만듭니다.** 그래서 지워지면 안 되는 것만 여기 있고,
// 다시 만들 수 있는 것(순위 · 시드 · 한도 · 큐)은 Redis 에 있습니다.
//
// **스키마를 `knex.raw` 로 적습니다.** 빌더로 적으면 부분 인덱스와 `NUMERIC` 이 방언별
// 우회를 타고, 그러면 표를 읽어도 실제로 무엇이 만들어지는지 알 수 없게 됩니다.

import type { Knex } from 'knex'

export async function up(knex: Knex): Promise<void> {
  await knex.raw(`
    CREATE TABLE account (
      id          BIGSERIAL PRIMARY KEY,
      created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
    );

    -- 한 계정에 신원이 여럿입니다. Google 로 만든 계정에 Discord 를 붙이면 여기 한 줄이
    -- 늘어나고 기록은 그대로입니다.
    --
    -- **이메일과 이름과 사진을 두지 않습니다.** 제공자가 주지만 받지 않습니다 — 들고
    -- 있지 않은 것은 새지 않습니다.
    CREATE TABLE identity (
      provider    TEXT NOT NULL,
      subject     TEXT NOT NULL,
      account_id  BIGINT NOT NULL REFERENCES account(id) ON DELETE CASCADE,
      created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
      PRIMARY KEY (provider, subject)
    );
    CREATE INDEX identity_account ON identity(account_id);

    CREATE TABLE profile (
      account_id         BIGINT PRIMARY KEY REFERENCES account(id) ON DELETE CASCADE,
      -- 대소문자를 구분하지 않고 유일합니다. 보이는 대로 두고 찾을 때 접습니다.
      handle             TEXT,
      handle_folded      TEXT UNIQUE,
      handle_changed_at  TIMESTAMPTZ,
      tier               TEXT NOT NULL DEFAULT '',
      last_season_tier   TEXT NOT NULL DEFAULT ''
    );

    -- **한 계정에 세션이 여럿입니다.** 기계마다 한 줄이고, 한 줄을 지우는 것이 그 기계의
    -- 로그아웃입니다.
    CREATE TABLE session (
      id            BIGSERIAL PRIMARY KEY,
      account_id    BIGINT NOT NULL REFERENCES account(id) ON DELETE CASCADE,
      -- token 의 원문을 두지 않습니다. 표가 새어도 남의 세션이 되지 않습니다.
      refresh_hash  TEXT NOT NULL UNIQUE,
      -- 바로 앞의 token. **네트워크가 끊긴 재시도를 위한 짧은 유예입니다** — 서버가 바꾼
      -- 응답을 받지 못한 기계가 예전 token 으로 한 번 더 오는 일이 실제로 있습니다.
      prev_hash     TEXT,
      rotated_at    TIMESTAMPTZ,
      -- 사람이 자기 기계를 알아보는 표시. 신뢰하지 않습니다 — 표시일 뿐입니다.
      label         TEXT NOT NULL DEFAULT '',
      expires_at    TIMESTAMPTZ NOT NULL,
      created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
      used_at       TIMESTAMPTZ NOT NULL DEFAULT now()
    );
    CREATE INDEX session_account ON session(account_id, used_at DESC);
    CREATE INDEX session_prev ON session(prev_hash) WHERE prev_hash IS NOT NULL;

    -- 시즌은 규칙 지문에 묶입니다. **지문이 다르면 점수가 견주어지지 않습니다.**
    CREATE TABLE season (
      id           BIGSERIAL PRIMARY KEY,
      fingerprint  TEXT NOT NULL,
      starts_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
      ends_at      TIMESTAMPTZ
    );
    -- 열려 있는 시즌은 지문마다 하나입니다.
    CREATE UNIQUE INDEX season_open ON season(fingerprint) WHERE ends_at IS NULL;

    -- 랭크 시드. Redis 에도 있고 그쪽이 빠른 길입니다.
    --
    -- \`challenge\` 를 \`NULL\` 이 아니라 '' 로 둡니다 — \`WHERE challenge = ''\` 가
    -- 「챌린지가 아닌 런」이고, \`NULL\` 이면 그 조건이 \`IS NULL\` 이 되어 한 곳만
    -- \`= ''\` 로 적으면 조용히 0행입니다.
    CREATE TABLE ranked_seed (
      seed        TEXT PRIMARY KEY,
      account_id  BIGINT NOT NULL REFERENCES account(id) ON DELETE CASCADE,
      deck        TEXT NOT NULL,
      stake       TEXT NOT NULL,
      pool        TEXT NOT NULL,
      challenge   TEXT NOT NULL DEFAULT '',
      issued_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
      expires_at  TIMESTAMPTZ NOT NULL,
      used_at     TIMESTAMPTZ
    );
    CREATE INDEX ranked_seed_account ON ranked_seed(account_id, issued_at DESC);

    CREATE TABLE submission (
      id            BIGSERIAL PRIMARY KEY,
      account_id    BIGINT NOT NULL REFERENCES account(id) ON DELETE CASCADE,
      season_id     BIGINT REFERENCES season(id),
      seed          TEXT NOT NULL,
      deck          TEXT NOT NULL,
      stake         TEXT NOT NULL,
      pool          TEXT NOT NULL,
      challenge     TEXT NOT NULL DEFAULT '',
      -- 다시 재현할 일이 있습니다 — 시즌 감사 · 코어를 고친 뒤의 확인.
      replay        JSONB NOT NULL,
      status        TEXT NOT NULL DEFAULT 'pending',
      reason        TEXT NOT NULL DEFAULT '',
      submitted_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
      judged_at     TIMESTAMPTZ
    );
    CREATE INDEX submission_account ON submission(account_id, submitted_at DESC);
    CREATE INDEX submission_season ON submission(season_id, status);

    -- 지표를 제출과 나눈 것은 지표가 늘 때 \`submission\` 을 건드리지 않기 위해서입니다.
    CREATE TABLE run_metric (
      submission_id  BIGINT PRIMARY KEY REFERENCES submission(id) ON DELETE CASCADE,
      ascent         INTEGER NOT NULL,
      -- DOUBLE 은 2^53 위에서 정확하지 않습니다. 지금은 그 아래지만 자리를 남깁니다.
      best_hand      NUMERIC NOT NULL,
      hands_played   INTEGER NOT NULL,
      money          INTEGER NOT NULL,
      skips          INTEGER NOT NULL,
      won            BOOLEAN NOT NULL
    );
  `)
}

export async function down(knex: Knex): Promise<void> {
  await knex.raw(`
    DROP TABLE IF EXISTS run_metric, submission, ranked_seed, season,
                         session, profile, identity, account CASCADE;
  `)
}
