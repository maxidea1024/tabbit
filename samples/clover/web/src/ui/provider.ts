// 로그인 제공자의 모습.
//
// **색과 이름을 한 자리에 둡니다.** 로그인 화면의 단추와 계정 자리의 표시가 같은 색이어야
// 「그때 누른 그것」으로 읽히는데, 두 파일에 따로 적혀 있으면 하나를 고친 날부터 색이
// 갈립니다.
//
// **이름은 고유명사이므로 번역하지 않습니다.** `Google` 은 어느 말에서도 `Google` 입니다.

/** 제공자마다의 색. **알아볼 수 있는 색이어야 고르는 것이 빨라집니다.** */
const TINT: Record<string, number> = {
  google: 0x3a6ea5,
  discord: 0x4f5fc4,
  apple: 0x4a5568,
  github: 0x3f4a5a,
}

/** 모르는 제공자의 색. 서버가 우리보다 앞서 있을 수 있습니다. */
const UNKNOWN = 0x5c6a7d

export function providerTint(id: string): number {
  return TINT[id] ?? UNKNOWN
}

/** 사람이 읽는 이름. 서버가 켠 것 중 우리가 모르는 것은 첫 글자만 올립니다. */
export function providerLabel(id: string): string {
  const known: Record<string, string> = {
    google: 'Google',
    discord: 'Discord',
    apple: 'Apple',
    github: 'GitHub',
  }
  return known[id] ?? (id === '' ? '' : id[0].toUpperCase() + id.slice(1))
}
