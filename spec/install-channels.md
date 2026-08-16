# 설치 경로 — 패키지 관리자 배포

> [문서 목록으로](../doc/readme.md)

`tabbit`을 **세 OS 전부에서 한 줄로 설치하고, PATH를 손대지 않으며, 한 줄로 갱신**하게 하는
배포 채널의 설계입니다.

바뀌는 것은 **바이너리를 어디에서 받는가**뿐입니다. 빌드도, 아카이브의 내용도, 도구 자체도
그대로입니다 — 4절의 선행 정리 하나를 제외하면. 그래서 이 문서의 대부분은 설계가 아니라
**어느 채널을 쓰고 어느 채널을 쓰지 않는가**와 그 근거입니다.

---

## 1. 요약

|무엇|판단|
|--|--|
|1단계|**설치 스크립트 2개**(`sh`·`ps1`). 세 OS 전부, 심사 없음, 재실행이 곧 갱신입니다|
|2단계|**Scoop 버킷**(Windows)과 **Homebrew tap**(macOS·Linux). 둘 다 우리가 소유하므로 심사가 없고, 릴리즈 워크플로가 매니페스트를 갱신합니다|
|3단계|**winget**. 심사가 있으나 Windows 사용자가 가장 먼저 조회하는 곳입니다|
|보류|npm · Chocolatey · `dotnet tool` — 각각 조건이 붙습니다(8절)|
|하지 않음|apt/rpm 자체 저장소 · snap · flatpak(9절)|

**선결 사항이 2개 있습니다.** 릴리즈가 아직 0개이고(2절), 아카이브에 실행 파일 외의 파일이
들어 있습니다(4절). 어느 채널도 이 둘 위에서만 성립합니다.

**결정을 요청하는 항목이 2개입니다** — macOS 코드 서명(7절)과 npm 이름(8절).

## 2. 현재 상태

`doc/install.md`가 안내하는 절차는 **내려받기 → 압축 해제 → PATH 등록 → (macOS) 격리 해제**
4단계입니다. 각 단계의 비용입니다.

|단계|비용|
|--|--|
|내려받기|플랫폼별 파일 6개 중 자기 것을 고릅니다. `win-arm64`와 `win-x64`의 구분은 사용자가 판정합니다|
|압축 해제|설치 위치를 사용자가 정합니다|
|**PATH 등록**|Windows 안내는 `$env:PATH` 대입이므로 **그 세션에서만 유효**하고, 영구 등록은 「시스템 환경변수에 추가하세요」라는 문장으로 남아 있습니다|
|**macOS 격리 해제**|`xattr -d com.apple.quarantine`를 사용자가 실행합니다|
|**갱신**|수단이 없습니다. 같은 4단계를 다시 수행합니다|

**그리고 릴리즈가 0개입니다.** 태그도 릴리즈도 없어(`git tag`·`gh api releases` 모두 비어
있음, 2026-08-16 확인) 문서가 가리키는 내려받기 주소가 아직 존재하지 않습니다. 릴리즈
워크플로는 완성되어 있으므로 `v0.1.0` 태그 하나가 이 상태를 해소합니다.

## 3. 요구사항과 판정 기준

|요구|판정 기준|
|--|--|
|세 OS|Windows · macOS · Linux에서 각각 한 줄|
|실행 경로 자동|설치 직후 **새 셸에서** `tabbit --version`이 동작할 것|
|갱신 한 줄|버전을 기재하지 않고 최신으로 이동할 것|
|되돌리기|제거가 한 줄일 것. 남는 파일이 없을 것|
|우리 쪽 비용|릴리즈 태그 하나로 모든 채널이 갱신될 것 — 손으로 갱신하는 채널은 곧 낡습니다|

## 4. 선행 정리 — 아카이브를 실행 파일 하나로

**모든 채널이 「아카이브 안의 어느 파일이 실행 파일인가」를 매니페스트에 기재합니다.** 그 답이
「하나뿐」이면 채널마다의 기재가 한 줄로 끝납니다.

지금은 그렇지 않습니다. 실측입니다(`bin/win-x64`).

```
tabbit.exe                      67,808,909 바이트
aspnetcorev2_inprocess.dll         392,488 바이트
```

`aspnetcorev2_inprocess.dll`은 ASP.NET Core의 IIS 인프로세스 모듈입니다. 이 도구의 serve
모드는 Kestrel로 자체 호스팅하므로 IIS 뒤에서 동작하지 않고, 따라서 이 파일은 사용되지
않습니다. `PublishSingleFile`이 네이티브 자산을 번들에 넣지 않고 실행 파일 옆에 두기
때문에 나온 것입니다.

**정리 방법은 2가지이고 어느 쪽이든 결과는 같습니다.**

|방법|내용|
|--|--|
|제외|`.pdb`를 제거하는 `DropNativeDebugFiles` 타깃과 같은 자리에서 제외합니다|
|번들|`IncludeNativeLibrariesForSelfExtract=true`. 실행 시 임시 폴더로 추출되므로 **첫 실행이 느려지고 추출 폴더가 생깁니다**|

앞의 것을 권고합니다. 뒤의 것은 사용되지 않는 파일을 위해 실행 시 비용을 지불합니다.

> **나머지 5개 RID는 미확인입니다.** 이 저장소에서 게시한 것이 `win-x64` 하나이므로, Linux와
> macOS의 게시 산출물에 실행 파일 외에 무엇이 들어가는지는 릴리즈 워크플로를
> `workflow_dispatch`로 1회 예행하여 아티팩트를 확인하는 것이 정확합니다. 매니페스트 작성보다
> 앞에 두어야 하는 확인입니다.

## 5. 1단계 — 설치 스크립트

**패키지 관리자보다 앞에 두는 이유는 심사가 없고, 우리 저장소만으로 완결되며, 세 OS를 한 번에
덮기 때문입니다.** 그리고 뒤의 모든 채널이 준비될 때까지 문서의 첫 줄을 대신합니다.

```bash
curl -fsSL https://maxidea1024.github.io/tabbit/install.sh | sh
```

```powershell
irm https://maxidea1024.github.io/tabbit/install.ps1 | iex
```

문서 사이트가 이미 GitHub Pages로 게시되고 있으므로 `website/static/`에 파일 2개를 두면
그 주소가 됩니다. 별도 호스팅이 필요하지 않습니다.

|스크립트가 하는 일|내용|
|--|--|
|플랫폼 판정|`uname -s`·`uname -m` / `RuntimeInformation.OSArchitecture`. 사용자가 RID를 고르지 않습니다|
|버전 결정|인자가 없으면 `releases/latest`. `TABBIT_VERSION`으로 고정할 수 있습니다|
|검증|`SHA256SUMS`와 대조합니다. **릴리즈가 이미 게시하고 있으므로 새로 만들 것이 없습니다**|
|설치 위치|`~/.tabbit/bin` · `%LOCALAPPDATA%\Programs\tabbit`. 관리자 권한이 필요하지 않습니다|
|**PATH 등록**|셸 프로필(`.zshrc`·`.bashrc`·`.profile`)에 1줄 추가 / Windows는 사용자 환경변수를 `[Environment]::SetEnvironmentVariable`로 영구 등록|
|**갱신**|같은 명령을 다시 실행합니다. 같은 버전이면 아무 것도 하지 않고 종료합니다|
|제거|`~/.tabbit` 삭제와 프로필 1줄 삭제. 스크립트가 표식 주석과 함께 기재하므로 기계적으로 제거할 수 있습니다|

**PATH 등록에는 규칙이 하나 필요합니다** — 이미 등록되어 있으면 추가하지 않습니다. 재실행이
곧 갱신이므로, 그 검사가 없으면 프로필에 같은 줄이 누적됩니다.

**macOS의 격리 속성은 이 경로에서 발생하지 않습니다.** 격리는 내려받는 프로그램이 부여하는
것이고 `curl`은 부여하지 않습니다. 지금 문서의 `xattr` 안내는 브라우저로 받은 경우에
한정됩니다 — 스크립트 설치가 기본이 되면 그 안내는 수동 설치 절로 이동합니다.

## 6. 2단계 — Scoop과 Homebrew

**둘 다 우리가 소유하는 저장소에 매니페스트를 두는 방식이라 심사가 없습니다.** 사용자가
버킷·tap을 1회 추가하는 대가로, 등록 지연 없이 릴리즈와 같은 시각에 최신이 됩니다.

### 6.1 Scoop — Windows

버킷은 **이 저장소의 `bucket/` 폴더**로 충분합니다. Scoop은 임의의 git 저장소를 버킷으로
받으며 루트의 `bucket/`을 조회하므로, 저장소를 새로 만들 필요가 없습니다.

```powershell
scoop bucket add tabbit https://github.com/maxidea1024/tabbit
scoop install tabbit
scoop update tabbit          # 갱신
scoop uninstall tabbit       # 제거
```

`bucket/tabbit.json`의 골자입니다.

```json
{
  "version": "0.1.0",
  "description": "Game Data Authoring & Build Tool",
  "homepage": "https://maxidea1024.github.io/tabbit/",
  "license": "MIT",
  "architecture": {
    "64bit": { "url": "https://github.com/maxidea1024/tabbit/releases/download/v0.1.0/tabbit-0.1.0-win-x64.zip" },
    "arm64": { "url": "https://github.com/maxidea1024/tabbit/releases/download/v0.1.0/tabbit-0.1.0-win-arm64.zip" }
  },
  "bin": "tabbit.exe",
  "checkver": "github",
  "autoupdate": { "hash": { "url": "$baseurl/SHA256SUMS" } }
}
```

|항목|효과|
|--|--|
|`bin`|`~/scoop/shims`에 shim을 생성합니다. **그 폴더는 Scoop 설치 시 이미 PATH에 있으므로 실행 경로 문제가 발생하지 않습니다**|
|`checkver`·`autoupdate`|`scoop update`가 새 릴리즈를 조회합니다. 해시는 릴리즈의 `SHA256SUMS`에서 읽으므로 우리가 기재하지 않습니다|

### 6.2 Homebrew — macOS와 Linux

tap은 **저장소 이름이 `homebrew-tabbit`이어야 합니다.** 이것만 별도 저장소가 필요합니다.

```bash
brew install maxidea1024/tabbit/tabbit
brew upgrade tabbit
brew uninstall tabbit
```

`Formula/tabbit.rb`의 골자입니다. **Cask가 아니라 Formula입니다** — CLI 실행 파일이고,
`bin/`에 심볼릭 링크를 생성하는 것이 Formula의 동작입니다.

```ruby
class Tabbit < Formula
  desc "Game data authoring and build tool"
  homepage "https://maxidea1024.github.io/tabbit/"
  version "0.1.0"
  license "MIT"

  on_macos do
    on_arm   { url "...tabbit-0.1.0-osx-arm64.tar.gz";   sha256 "..." }
    on_intel { url "...tabbit-0.1.0-osx-x64.tar.gz";     sha256 "..." }
  end
  on_linux do
    on_arm   { url "...tabbit-0.1.0-linux-arm64.tar.gz"; sha256 "..." }
    on_intel { url "...tabbit-0.1.0-linux-x64.tar.gz";   sha256 "..." }
  end

  def install
    bin.install "tabbit"
  end

  test do
    assert_match "0.1.0", shell_output("#{bin}/tabbit --version")
  end
end
```

**Homebrew가 Linux를 함께 덮는 것이 이 채널의 값입니다.** 같은 formula가 `linux-x64`·
`linux-arm64`에 동작하므로, 배포판별 저장소를 운영하지 않고도 Linux에 패키지 관리자 설치
경로가 생깁니다.

`/opt/homebrew/bin`(Apple Silicon)·`/usr/local/bin`·`~/.linuxbrew/bin`은 Homebrew 설치 시
PATH에 등록되어 있으므로 실행 경로 문제가 발생하지 않습니다. 이 도구는 이미 그 경로들을 알고
있습니다 — 생성된 코드의 툴체인 탐색이 같은 목록을 조회합니다(`doc/architecture.md`).

### 6.3 매니페스트 자동 갱신

**손으로 갱신하는 채널은 곧 낡습니다.** 릴리즈 워크플로의 `release` job 뒤에 갱신 job을
추가합니다.

|채널|방법|
|--|--|
|Scoop|같은 저장소이므로 `bucket/tabbit.json`의 `version`·`url`을 치환하고 커밋합니다. `autoupdate`가 있으므로 해시는 기재하지 않습니다|
|Homebrew|`homebrew-tabbit`에 푸시할 토큰이 필요합니다. `dist/SHA256SUMS`가 이미 산출되어 있으므로 formula의 `sha256` 4개를 거기에서 채웁니다|
|winget|`vedantmgoyal9/winget-releaser` 액션이 매니페스트 3종을 생성하여 PR을 제출합니다|

## 7. macOS 코드 서명 — 결정 요청

**Homebrew Formula로 설치한 CLI 실행 파일은 격리 속성을 받지 않으므로 현재 상태로도 동작할
것으로 판단하나, 이것은 미확인입니다.** Homebrew는 5.0에서 Gatekeeper 정책을 강화하였고
`--no-quarantine`을 폐기 예정으로 두었으며, **Gatekeeper 검사에 실패하는 cask를 2026년 9월
1일부터 비활성화합니다.** 그 조치의 대상은 공식 `homebrew-cask`의 cask이고 우리는 자체 tap의
formula이므로 직접 대상은 아니지만, 방향이 그쪽이라는 사실은 판단에 포함해야 합니다.

|선택지|비용|얻는 것|
|--|--|--|
|**현행 유지**(ad-hoc 서명)|없음. macOS 러너에서 게시하므로 ad-hoc 서명은 이미 부여됩니다|Homebrew·스크립트 설치는 동작합니다. 브라우저로 받은 사용자는 `xattr` 1회|
|**Developer ID 서명 + 공증**|Apple Developer Program 연 99달러, 릴리즈 워크플로에 서명·공증·스테이플 단계 추가|어느 경로로 받아도 경고가 없습니다. 공식 채널로 확장할 여지가 열립니다|

**권고는 현행 유지이고, 재검토 시점은 macOS 사용자에게서 실제 보고가 발생할 때입니다.**
연 99달러가 아니라 릴리즈 파이프라인에 서명 단계가 상주하는 것이 더 큰 비용입니다 — 인증서
만료가 릴리즈 실패로 나타나는 종류의 부담입니다.

> **arm64 실행 파일은 서명이 없으면 실행되지 않습니다.** ad-hoc 서명으로 충족되며, .NET SDK가
> macOS에서 게시할 때 부여합니다. 릴리즈 워크플로는 `macos-latest`·`macos-15-intel`에서
> 각각 네이티브로 빌드하므로 이 조건은 이미 만족합니다. **Linux에서 교차 게시하면 이
> 조건이 깨집니다** — 워크플로의 러너 구성을 바꿀 때 확인할 항목입니다.

## 8. 조건이 붙는 채널

### 8.1 winget — 3단계

Windows 사용자가 가장 먼저 조회하는 곳이고, `winget upgrade`가 전체 도구를 한 번에 갱신하는
자리에 들어갑니다. 채택을 권고하나 **Scoop보다 뒤에 둡니다** — 매니페스트가
`microsoft/winget-pkgs`에 병합되어야 하므로 릴리즈와 반영 사이에 지연이 있고, 우리가 통제하지
않습니다.

기재는 `InstallerType: zip` + `NestedInstallerType: portable`이고, 실행 경로는 winget이
처리합니다 — 압축을 `%LOCALAPPDATA%\Microsoft\WinGet\Packages`에 풀고
`...\WinGet\Links`에 심볼릭 링크를 생성하며, **그 Links 폴더가 PATH에 등록되어 있습니다.**
`archiveBinariesDependOnPath`는 기본값(false)으로 둡니다.

### 8.2 npm — 이름 선점과 그 대가

**`tabbit`은 npm에 이미 존재합니다**(들여쓰기 관련 패키지, `1.0.0`). 따라서 `npm i -g tabbit`
과 `npx tabbit`은 사용할 수 없고, `@tabbit/cli`(스코프 미사용 확인) 또는 `tabbit-cli`가
됩니다. **`npx tabbit`이 남의 패키지를 실행한다는 사실이 이 채널의 값을 크게 낮춥니다.**

값이 남는 자리는 하나입니다 — **프로젝트별 버전 고정.** `package.json`의 `devDependencies`에
기재하면 팀 전원과 CI가 같은 `tabbit`을 사용합니다. 데이터 빌드 도구에서 이것은 실질적인
값이지만, 등록소에 릴리즈마다 약 68 MB × 플랫폼 6개를 영구히 게시하는 대가가 붙습니다.

**요청할 때 착수합니다.** 그 전에는 비용만 발생합니다.

### 8.3 `dotnet tool` — 청중의 불일치

`dotnet tool install -g`는 .NET SDK가 이미 있는 환경에서 가장 짧은 경로이나, 이 도구의 청중
대부분에게는 **먼저 .NET을 설치하라는 요구**가 됩니다. 「.NET을 설치하지 않아도 됩니다」라는
현재의 안내와 정면으로 어긋납니다.

기술적 제약도 2개입니다.

|제약|내용|
|--|--|
|ASP.NET Core 런타임|`Microsoft.AspNetCore.App`을 `FrameworkReference`로 참조하므로, 프레임워크 의존 도구 패키지는 **기본 런타임만으로는 실행되지 않습니다**|
|RID별 자체 포함 패키지|.NET 10 SDK가 지원하므로 위 제약을 해소할 수 있으나, **.NET 10 SDK에서만 동작합니다**|

CI에서의 편의가 확인되면 그때 판단합니다.

### 8.4 mise · ubi — 매니페스트 없는 설치

`mise use -g ubi:maxidea1024/tabbit` 형태로, 릴리즈 자산 이름이 플랫폼을 판별할 수 있으면
매니페스트 없이 설치됩니다. **우리 쪽 작업이 없으므로 채널이라기보다 문서 1줄입니다** —
릴리즈를 게시하는 것만으로 성립하는지 1회 확인한 뒤 기재합니다.

## 9. 하지 않기로 함

|후보|이유|
|--|--|
|**apt·rpm 자체 저장소**|서명 키 관리와 저장소 호스팅이 상주 비용입니다. Homebrew on Linux가 같은 사용자를 덮고, 남는 사용자는 설치 스크립트로 충분합니다. `.deb`·`.rpm` **파일**만 릴리즈에 첨부하는 절충안은 저장소 없이는 갱신 수단을 주지 않으므로 요구사항을 만족하지 않습니다|
|**Chocolatey**|Windows 전용인데 Scoop과 winget이 이미 그 자리에 있습니다. 커뮤니티 저장소 심사가 winget보다 무겁고, 자체 nupkg 패키징이 추가됩니다|
|**snap · flatpak**|샌드박스가 임의 경로의 워크북과 출력 폴더를 읽고 쓰는 것을 제한합니다. 이 도구의 동작이 정확히 그것입니다|
|**Nix**|사용자 수 대비 매니페스트 유지 비용이 맞지 않습니다. 필요해지면 사용자가 직접 작성할 수 있는 종류입니다|

## 10. 도구 자신의 몫 — 새 버전 알림

**갱신을 한 줄로 만드는 것과, 갱신할 것이 있다는 사실을 아는 것은 다른 문제입니다.**
설치 채널이 늘어도 사용자는 자기 버전이 낡았다는 것을 알 방법이 없습니다.

`--check-update`를 추가합니다. `releases/latest`를 조회하여 새 버전이면 1줄로 보고하고,
**설치 채널에 맞는 명령을 함께 보고합니다.** 채널은 실행 파일의 경로로 판정합니다.

|경로 패턴|안내|
|--|--|
|`scoop\apps\` 또는 `scoop/apps/`|`scoop update tabbit`|
|`Cellar/tabbit` · `/opt/homebrew` · `/home/linuxbrew`|`brew upgrade tabbit`|
|`WinGet\Packages`|`winget upgrade Maxidea1024.Tabbit`|
|`.tabbit/bin`|설치 스크립트 재실행 명령|
|그 밖|릴리즈 페이지 주소|

**자동 조회는 하지 않습니다.** 이 도구는 폐쇄망에서의 실행을 전제하고 있고(히스토리 페이지를
내장하는 근거가 그것입니다), 빌드 파이프라인이 매 실행마다 외부에 접속하는 것은 그 전제와
어긋납니다. 사용자가 물었을 때만 조회합니다.

**자기 갱신(`--self-update`)은 채택하지 않습니다.** 패키지 관리자가 설치한 파일을 도구가
직접 덮어쓰면 관리자의 기록과 실제 파일이 어긋납니다 — 다음 `brew upgrade`가 무엇을 하는지
예측할 수 없게 됩니다. 채널이 관리하는 설치는 채널이 갱신하고, 채널이 없는 설치는 스크립트
재실행이 갱신합니다.

## 11. 문서 개정

`doc/install.md`의 구성이 뒤집힙니다.

|순서|내용|
|--|--|
|1|**패키지 관리자** — Windows(Scoop·winget) · macOS(Homebrew) · Linux(Homebrew) 각각 2줄|
|2|**설치 스크립트** — 패키지 관리자를 쓰지 않는 경우|
|3|**갱신과 제거** — 채널별 1줄씩. 지금은 이 절이 없습니다|
|4|수동 내려받기 — 현재 문서의 내용이 여기로 이동합니다. `xattr` 안내도 여기에 한정됩니다|
|5|소스에서 빌드|

`readme.md`의 「빠른 시작」 첫 줄도 함께 바뀝니다. 현재의 「내려받아 압축을 풀면 끝입니다」는
**압축 해제가 필요 없어지면 사실과 어긋납니다.** 대신 설치 명령 1줄을 기재하고, .NET이
필요 없다는 사실은 유지합니다 — 그것은 채널과 무관하게 참입니다.

## 12. 작업 순서

|순서|무엇|선행|
|--|--|--|
|0|아카이브를 실행 파일 하나로(4절). 6개 RID 전부 확인|—|
|1|**`v0.1.0` 태그** — 릴리즈 게시|0|
|2|설치 스크립트 2개 + 문서 개정|1|
|3|Scoop 버킷 · Homebrew tap + 릴리즈 워크플로의 갱신 job|1|
|4|`--check-update`|3(채널 판정이 채널보다 뒤)|
|5|winget 제출|3|

**0번과 1번이 나머지 전부의 선행입니다.** 매니페스트는 실재하는 자산의 주소와 해시를
기재하므로, 릴리즈 없이 작성한 매니페스트는 검증할 수 없습니다.

## 13. 열려 있는 결정

|항목|선택지|권고|
|--|--|--|
|macOS 서명|현행 유지 / Developer ID + 공증(연 99달러)|현행 유지. 보고가 발생하면 재검토|
|npm 이름|`@tabbit/cli` / `tabbit-cli` / 채택하지 않음|당장은 채택하지 않음. 프로젝트별 버전 고정 요구가 발생하면 `@tabbit/cli`|
|Scoop 버킷 위치|이 저장소의 `bucket/` / 별도 저장소|이 저장소. 릴리즈 워크플로가 같은 저장소에 커밋하므로 토큰이 필요하지 않습니다|
|`winget` 패키지 식별자|`Maxidea1024.Tabbit`|게시자 표기를 확정한 뒤 기재합니다|
