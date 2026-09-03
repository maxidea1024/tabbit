# 제공자 등록

> [리더보드로](../leaderboard.md)

---

**코드는 넷 다 들어가 있습니다.** 남은 것은 제공자마다 앱을 하나 만들고 열쇠 두 개를
`.env` 에 적는 것뿐입니다.

|제공자|어디에|무엇이 필요합니까|
|--|--|--|
|Google|배포 · 개발|`GOOGLE_CLIENT_ID` · `GOOGLE_CLIENT_SECRET`|
|Discord|배포 · 개발|`DISCORD_CLIENT_ID` · `DISCORD_CLIENT_SECRET`|
|Apple|배포 · 개발|`APPLE_CLIENT_ID` · `APPLE_TEAM_ID` · `APPLE_KEY_ID` · `APPLE_PRIVATE_KEY`|
|GitHub|**개발만**|`GITHUB_CLIENT_ID` · `GITHUB_CLIENT_SECRET`|

## 셋에 공통인 것

**되돌아오는 주소가 하나입니다.**

```
<PUBLIC_URL>/auth/<제공자>/callback
```

|어디|`PUBLIC_URL`|등록할 주소|
|--|--|--|
|개발|`http://localhost:8787`|`http://localhost:8787/auth/google/callback`|
|배포|`https://<서버 주소>`|`https://<서버 주소>/auth/google/callback`|

**제공자에 등록하는 주소는 서버의 것이지 게임의 것이 아닙니다.** 게임으로 돌아가는 것은
그다음이고 그 주소는 `RETURN_ALLOWLIST` 가 정합니다 — 제공자는 그것을 모릅니다.

```
사람 → 게임 → 서버 → 제공자 → 서버(callback) → 게임(RETURN_ALLOWLIST 안의 주소)
```

## Google

1. [Google Cloud Console](https://console.cloud.google.com/) 에서 프로젝트를 하나
2. **API 및 서비스 → OAuth 동의 화면** — 외부, 앱 이름과 지원 이메일
   - 범위는 `openid` 하나면 됩니다. **이메일과 프로필을 요청하지 않습니다** — 받지 않는
     것을 요청하면 동의 화면에 그 줄이 뜹니다
3. **사용자 인증 정보 → OAuth 클라이언트 ID → 웹 애플리케이션**
4. 승인된 리디렉션 URI 에 위의 callback 주소
5. 나온 클라이언트 ID 와 보안 비밀번호를 `.env` 에

```
GOOGLE_CLIENT_ID=...apps.googleusercontent.com
GOOGLE_CLIENT_SECRET=...
```

## Discord

1. [Discord Developer Portal](https://discord.com/developers/applications) 에서
   **New Application**
2. **OAuth2 → Redirects** 에 callback 주소
3. **OAuth2 → Client information** 의 CLIENT ID 와 CLIENT SECRET

```
DISCORD_CLIENT_ID=...
DISCORD_CLIENT_SECRET=...
```

범위는 `identify` 입니다. **`email` 을 요청하지 않습니다.**

## Apple

**넷 중 유일하게 `client_secret` 이 고정 문자열이 아닙니다.** 팀과 열쇠로 우리가 서명하는
짧은 JWT 이고, [`providers.ts`](../../server/src/auth/providers.ts) 의 `secretsOf` 가
요청마다 새로 만듭니다 — 만료를 관리할 것이 없습니다.

1. [Apple Developer](https://developer.apple.com/account/resources/identifiers/list)
   → **Identifiers → App IDs** 에서 앱 하나. Sign In with Apple 켜기
2. **Identifiers → Services IDs** 에서 서비스 ID 하나. 이것이 `APPLE_CLIENT_ID` 입니다
   - Sign In with Apple 의 **Configure** 에서 도메인과 callback 주소
   - **`localhost` 를 받지 않습니다.** 개발에서 Apple 을 시험하려면 터널이 하나 필요합니다
3. **Keys** 에서 Sign In with Apple 열쇠 하나. `.p8` 파일이 **한 번만** 내려받아집니다
4. 열쇠 ID 와 팀 ID 를 적습니다

```
APPLE_CLIENT_ID=dev.tabbit.clover.web      # 서비스 ID
APPLE_TEAM_ID=XXXXXXXXXX
APPLE_KEY_ID=YYYYYYYYYY
APPLE_PRIVATE_KEY="-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----"
```

**`\n` 을 그대로 적습니다.** `.env` 는 여러 줄을 담지 못하므로 코드가 그것을 줄바꿈으로
되돌립니다.

## GitHub — 개발만

1. [Settings → Developer settings → OAuth Apps](https://github.com/settings/developers)
2. Authorization callback URL 에 `http://localhost:8787/auth/github/callback`

```
GITHUB_CLIENT_ID=...
GITHUB_CLIENT_SECRET=...
```

**배포에 나가지 않습니다.** `NODE_ENV=production` 에서 `AUTH_PROVIDERS` 에 `github` 이
있으면 서버가 부팅하지 않고, 그 단추는 `import.meta.env.DEV` 안에 있으므로 배포 빌드에
코드 자체가 없습니다.

## 켜기

`.env` 에 열쇠를 적고 `AUTH_PROVIDERS` 에 이름을 나열합니다.

```
# samples/clover/server/.env
AUTH_PROVIDERS=google,discord,apple
PUBLIC_URL=https://<서버 주소>
RETURN_ALLOWLIST=https://<게임 주소>
JWT_SECRET=<무작위 32바이트 이상>
```

**켠 것만 화면에 나옵니다.** 서버가 `/auth/providers` 로 알리고 화면이 그것을 그립니다 —
열쇠가 없는 제공자를 켜면 그 단추를 눌렀을 때 500 이 나므로, **켜기 전에 열쇠를 먼저
적습니다.**

## 확인

```
curl <PUBLIC_URL>/health
curl <PUBLIC_URL>/auth/providers
```

둘째가 켜 둔 목록을 그대로 돌려줍니다. 그다음은 브라우저에서 한 번 지나 보는 것이
유일한 확인입니다 — 제공자의 동의 화면은 우리가 흉내 낼 수 없습니다.

**개발 중에는 지나지 않아도 됩니다.** 「개발용 로그인」이 계정 하나를 그 자리에서
만들어 주므로, 화면을 고치는 동안 제공자를 매번 거칠 이유가 없습니다.

---

EOD
