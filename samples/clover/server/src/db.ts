// PostgreSQL — knex.
//
// **마이그레이션 실행기를 손으로 만들지 않습니다.** 무엇까지 돌렸는지를 적는 표와 트랜잭션과
// 순서가 knex 에 이미 있고, 그것이 마이그레이션 도구가 하는 일의 전부입니다.

import * as path from 'path'
import { fileURLToPath } from 'url'
import knexFactory, { type Knex } from 'knex'
import pg from 'pg'

const HERE = path.dirname(fileURLToPath(import.meta.url))

// `BIGINT` 를 문자열로 돌려주는 것이 드라이버의 기본값입니다. 계정 식별자를 그대로 쓰므로
// 수로 바꿉니다 — 2^53 을 넘는 계정 수는 이 게임에 오지 않습니다.
pg.types.setTypeParser(pg.types.builtins.INT8, value => Number(value))
// `NUMERIC` 도 문자열로 옵니다. `best_hand` 가 그 타입입니다.
pg.types.setTypeParser(pg.types.builtins.NUMERIC, value => Number(value))

export type Db = Knex

export function newDb(url: string): Db {
  return knexFactory({
    client: 'pg',
    connection: url,
    pool: { min: 0, max: 10 },
    migrations: {
      directory: path.join(HERE, 'db', 'migrations'),
      // `tsx` 로 도므로 마이그레이션도 `.ts` 그대로입니다. 빌드 단계를 하나 두면 그것을
      // 잊은 채로 배포하는 날이 옵니다.
      loadExtensions: ['.ts'],
      extension: 'ts',
      tableName: 'migration',
    },
  })
}

/**
 * 아직 돌지 않은 마이그레이션을 돌립니다.
 *
 * 돌린 것의 이름을 돌려줍니다 — 부팅 로그에 적히는 것이 그것입니다.
 */
export async function migrate(db: Db): Promise<string[]> {
  const [, files] = await db.migrate.latest()
  return (files as string[]).map(one => path.basename(one))
}
