# 설치

현재는 소스에서 직접 빌드합니다.

릴리즈가 제공되면 내려받아 압축을 푸는 것만으로 끝나며, .NET 런타임도 별도로 설치할 필요가
없습니다. 런타임이 실행 파일 안에 들어 있기 때문입니다.

> [문서 목록으로](readme.md)

---

## 소스에서 빌드하기

`.NET 10 SDK`가 필요합니다. 버전은 저장소 루트의 `global.json`에 고정되어 있습니다.

```bash
dotnet build Tabbit.slnx -c Release
```

개발과 테스트 절차는 [아키텍처와 개발](architecture.md#개발--테스트)에 있습니다.

## 릴리즈로 설치하기

> **아직 릴리즈가 제공되지 않습니다.**
> 저장소가 비공개인 동안 Actions가 실행되지 않아 릴리즈 워크플로가 동작하지 못합니다.
> 아래는 릴리즈가 제공된 뒤의 절차입니다.

[릴리즈](https://github.com/maxidea1024/tabbit/releases)에 플랫폼별로 올라갑니다.

| 플랫폼 | 파일 |
| --- | --- |
| Linux | `tabbit-<버전>-linux-x64.tar.gz` · `linux-arm64` |
| Windows | `tabbit-<버전>-win-x64.zip` · `win-arm64` |
| macOS | `tabbit-<버전>-osx-x64.tar.gz` · `osx-arm64` (애플 실리콘) |

터미널에서 받는 편이 편하면 아래를 그대로 사용하세요.

`VERSION`만 원하는 버전으로 바꾸면 됩니다.

### Linux · macOS

```bash
VERSION=0.1.0
RID=linux-x64            # linux-arm64 · osx-x64 · osx-arm64 중 하나

curl -fsSL "https://github.com/maxidea1024/tabbit/releases/download/v$VERSION/tabbit-$VERSION-$RID.tar.gz" \
  | tar -xz -C /usr/local/bin tabbit

tabbit --help
```

`/usr/local/bin`에 권한이 없으면 `sudo`를 붙이거나 `-C ~/.local/bin`처럼 쓰기 가능한 위치로
바꾸세요.

macOS는 서명되지 않은 바이너리를 격리합니다. 한 번만 풀어주면 됩니다.

```bash
xattr -d com.apple.quarantine /usr/local/bin/tabbit
```

### Windows (PowerShell)

```powershell
$Version = '0.1.0'
$Rid     = 'win-x64'      # 또는 win-arm64
$Dest    = "$env:LOCALAPPDATA\Programs\tabbit"

New-Item -ItemType Directory -Force $Dest | Out-Null
Invoke-WebRequest "https://github.com/maxidea1024/tabbit/releases/download/v$Version/tabbit-$Version-$Rid.zip" -OutFile "$env:TEMP\tabbit.zip"
Expand-Archive "$env:TEMP\tabbit.zip" -DestinationPath $Dest -Force

# 이번 세션에서만 적용됩니다. 계속 사용하려면 시스템 환경변수 PATH에 $Dest를 추가하세요.
$env:PATH = "$Dest;$env:PATH"
tabbit --help
```

### 최신 버전을 자동으로

`jq`가 필요합니다.

```bash
VERSION=$(curl -fsSL https://api.github.com/repos/maxidea1024/tabbit/releases/latest | jq -r .tag_name)
VERSION=${VERSION#v}
```

### 받은 파일 확인

릴리즈마다 `SHA256SUMS`가 함께 올라갑니다.

```bash
curl -fsSLO "https://github.com/maxidea1024/tabbit/releases/download/v$VERSION/SHA256SUMS"
sha256sum -c SHA256SUMS --ignore-missing
```

## 다음

| 문서 | 내용 |
| --- | --- |
| [시트에 무엇을 적을 수 있나](concepts.md) | 시트 한 장이 코드가 되기까지 |
| [CLI](cli.md) | 실행하는 방법과 명령줄 옵션 |
| [Recipe 파일](recipe.md) | 데이터를 어디서 읽고 어디로 출력할지 |
