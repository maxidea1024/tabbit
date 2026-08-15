# bench — 형식 벤치마크 하네스

[doc/benchmark.md](../doc/benchmark.md)의 숫자를 만든 코드입니다. 무엇을 왜 재는지, 결과를 어떻게 읽는지는 그 문서에 있습니다 — 여기는 돌리는 법만 적습니다.

## 돌리는 법

저장소 루트에서:

```
# 1. 데이터 생성 — rescue 워크북을 세 형식으로 내보냅니다 (bench/data/, 커밋되지 않음)
dotnet run --project src/Tabbit.csproj -- --recipe bench/recipe.jsonc

# 2. 세 형식이 같은 값으로 로드되는지 확인
dotnet run --project bench/Tabbit.Bench.csproj -c Release -- verify bench/data

# 3. 측정
dotnet run --project bench/Tabbit.Bench.csproj -c Release -- sizes bench/data
dotnet run --project bench/Tabbit.Bench.csproj -c Release -- binary bench/data 30
dotnet run --project bench/Tabbit.Bench.csproj -c Release -- json bench/data 30
dotnet run --project bench/Tabbit.Bench.csproj -c Release -- json-compact bench/data 30
```

결과는 `##RESULT##`로 시작하는 JSON 한 줄로 나옵니다. 마지막 인자는 반복 횟수이고, 문서의 수치는 각 형식을 프로세스 새로 띄워 5번 돌린 중앙값입니다 — 형식마다 프로세스를 나누는 것은 앞 형식이 데워둔 힙과 풀이 다음 형식의 수치에 섞이지 않게 하기 위해서입니다.

`probe`는 진단용입니다 — 이름 있는 JSON과 compact JSON의 테이블별 상주 메모리를 나란히 보여줍니다.

## 구조

|파일|내용|
|--|--|
|`recipe.jsonc`|rescue 워크북 → `bench/data/{binary, json, json-compact}`. 한 변환에서 세 형식이 나오므로 내용이 다를 수 없습니다|
|`Program.cs`|측정과 검증. 힙 기준선을 어디서 잡는지, 왜 로드를 `await`하지 않는지가 주석에 있습니다|
|`JsonPath.cs`|JSON 쪽 로더. 생성된 `Record`를 거울처럼 본뜬 DTO 타입을 시작 시 만들어, 데이터셋이 재생성되어도 하네스가 따라갑니다|

바이너리 경로는 [samples/rescue/out/cs](../samples/rescue/out/cs)의 생성된 리더를 그대로 컴파일해 씁니다 — 소비자 프로젝트가 컴파일했을 바로 그 코드입니다.
