# 7. 버전 줄

> [「CLI 도움말」로 돌아가기](../cli-help.md)

---

`--version`이 [ToolVersion](../../../src/ToolVersion.cs)의 값을 그대로 씁니다 — 실행 첫 줄이
쓰는 것과 같은 자리에서 오므로 둘이 어긋날 수 없습니다.

```
$ tabbit --version
Tabbit 1.5.0+8b2a884d84bf
.tcb v107 · .NET 10.0.11 (win-x64)
built 2026-08-22T14:41:52Z
```

로컬 빌드는 마지막 줄이 없습니다.

```
$ tabbit --version
Tabbit 0.0.0+c401cb366657
.tcb v107 · .NET 10.0.11 (win-x64)
```

### 7.1 빌드 시각은 CI만 주입합니다

|후보|판정|
|--|--|
|PE 헤더의 링커 타임스탬프|**안 됩니다.** .NET SDK는 `Deterministic`이 기본값이라 그 필드에 실제 시각이 아니라 콘텐츠 해시가 들어갑니다. 날짜로 읽으면 미래의 값이 나옵니다|
|실행 파일의 mtime|**안 됩니다.** self-contained single-file은 `Assembly.Location`이 빈 문자열이고, `Environment.ProcessPath`로 우회해도 그것은 「이 파일이 여기 놓인 시각」입니다 — 복사·다운로드·체크아웃하면 바뀝니다|
|빌드가 값을 심는 것|**이것만 됩니다.** 단 조건이 붙습니다|

조건은 **로컬 빌드에서는 심지 않는 것**입니다. `$([System.DateTime]::UtcNow)`를 프로퍼티로
평가하면 아무것도 고치지 않아도 컴파일 입력이 매번 달라집니다 — 결정론적 빌드가 깨지고,
증분 빌드가 매번 재컴파일합니다. 이 저장소에서는
[13분 스위트](../../../doc/architecture.md#개발--테스트) 앞에 그것이 매번 붙습니다.

```xml
<!-- CI가 넘기지 않으면 아이템 자체가 붙지 않습니다. -->
<ItemGroup Condition="'$(BuildTimestamp)' != ''">
  <AssemblyMetadata Include="BuildTimestamp" Value="$(BuildTimestamp)" />
</ItemGroup>
```

`AssemblyMetadata` 아이템은 SDK가 `AssemblyMetadataAttribute`로 자동 생성합니다 — 텍스트
파일을 만들어 넣는 단계가 필요 없습니다.

주입은 [release.yml](../../../.github/workflows/release.yml) 한 곳입니다. `build/`의 스크립트는
개발자가 손으로 돌리는 것이므로 넘기지 않습니다 — 그쪽 산출물은 릴리스가 아닙니다.

### 7.2 `ToolVersion.Current`에 붙이지 않습니다

이것이 이 절의 유일한 함정입니다.

|자리|`Current`를 무엇으로 씁니까|빌드 시각이 섞이면|
|--|--|--|
|[BuildCache.cs](../../../src/Caching/BuildCache.cs)|**빌드 캐시의 키**|재빌드마다 전체 변환|
|[BuildReport.cs](../../../src/Reporting/BuildReport.cs)|리포트에 기록하는 `Tool`|리포트 골든이 빌드마다 바뀝니다|

그래서 `Built`는 별개 프로퍼티이고, `Banner`·`Current`는 손대지 않습니다.

### 7.3 실행 첫 줄에는 넣지 않습니다

`--version`에만 넣습니다. 실행 첫 줄이 알려야 하는 것은 「어느 빌드가 돌고 있나」이고
버전과 커밋이 이미 그것을 정합니다. 빌드 시각을 원하는 사람은 **바이너리를 손에 들고
이게 뭔지 확인하려는 사람**이고, 그 사람이 치는 것이 `--version`입니다.

[ToolVersion.cs](../../../src/ToolVersion.cs)가 「버전과 포맷 번호와 런타임과 플랫폼을 한 줄에
담으면 아무도 아무것도 못 찾는다」고 적어 둔 것과 같은 판단입니다. 그리고 실행 출력을
건드리지 않으므로 골든에 닿는 경로가 없습니다.

### 7.4 커밋 날짜 — 이번에 하지 않는 것

로컬 빌드의 `--version`에는 날짜가 한 줄도 없게 됩니다. 커밋 해시(`+1d074e8340db`)는
있지만 **해시로는 앞뒤를 읽을 수 없습니다.**

커밋 날짜를 심으면 그것이 해결되고, 같은 커밋이면 같은 값이므로 **결정론도 깨지지
않습니다.** 하지 않는 이유는 하나입니다 — 빌드 중에 `git`을 실행하는 단계가 새로
생기고, 그것은 이번 변경에 승인된 범위가 아닙니다. 필요해지면 이 조각을 그대로 넣으면
됩니다.

```xml
<Target Name="StampSourceDate" BeforeTargets="GetAssemblyAttributes">
  <!-- git이 없는 곳(소스 아카이브, 도커 빌드)에서도 빌드는 되어야 하므로 실패를 넘깁니다. -->
  <Exec Command="git show -s --format=%%cI HEAD"
        ConsoleToMSBuild="true" ContinueOnError="true" StandardOutputImportance="low">
    <Output TaskParameter="ConsoleOutput" PropertyName="SourceDate" />
  </Exec>
  <ItemGroup Condition="'$(SourceDate)' != ''">
    <AssemblyMetadata Include="SourceDate" Value="$(SourceDate)" />
  </ItemGroup>
</Target>
```

> 아이템 추가가 **타겟 안에** 있는 것이 중요합니다. 어셈블리 속성은 그것을 추가할 수 있는
> 타겟이 돌기 전에 수집되므로, 바깥의 `ItemGroup`은 `$(SourceDate)`가 아직 빈 상태로
> 평가됩니다. [Tabbit.csproj](../../../src/Tabbit.csproj)의 `EmbedRuleCompilationReferences`
> 위에 같은 함정이 이미 적혀 있습니다 — 리소스에서 겪은 것이고, 어셈블리 속성에도 같이
> 적용됩니다.
