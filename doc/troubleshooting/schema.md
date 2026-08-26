# 스키마가 바뀐 뒤

> [「트러블슈팅」으로 돌아가기](../troubleshooting.md)

---

여기 모인 메시지는 대부분 **차단이 목적**입니다. 이미 배포된 클라이언트가 못 읽을 데이터를 쓰기
전에 멈추는 것이고, 규칙과 근거는 [바이너리 형식](../binary-format.md)에 있습니다.

### `... has '@...' where a wire tag belongs, and a tag is a whole number from 1`

`@` 뒤가 1 이상의 정수가 아닙니다. `price@3`처럼 씁니다.

### `Table 'Item' tags some fields and not others: ...`

태그는 테이블 단위로 **전부 또는 전무**입니다. 반쯤 단 표는 어느 쪽 보장도 못 하기 때문입니다.
나열된 필드에 `@N`을 달거나, 전부에서 떼세요.

### `Field 'Item.Price' declares wire tag 3, which field 'Name' already holds`

같은 테이블에서 태그가 겹쳤습니다. `#`로 제외된 컬럼이 예약한 태그와 겹친 경우도 같은 메시지가
나옵니다 — 그 태그는 이미 데이터를 실었으므로 비어 있는 것이 아닙니다.

### `The schema changed in ... way(s) that a reader already built from the previous schema would not survive`

`SchemaBaseline` 검사가 차단했습니다. **아무것도 쓰이지 않았습니다.** 아래 항목들이 뒤따라
나오며, 각각이 무엇을 해야 하는지 함께 적습니다.

### `... is gone from the schema. Tombstone it in the sheet as ... so nothing takes its tag`

컬럼을 지웠는데 태그가 남지 않았습니다. 시트에 `#이름@N`을 남기세요 — 태그가 비면 다음에
추가하는 컬럼이 그것을 가져가고, 그러면 삭제 전에 만들어진 테이블 리더가 새 컬럼을 옛 필드로
읽습니다.

정말 태그까지 버릴 생각이라면 `AcceptSchemaChanges`에 `"테이블.컬럼"`을 적으세요. 그래도
베이스라인은 그 태그를 retired로 계속 기억합니다.

### `... refuses this column rather than reading it wrongly`

컬럼의 타입이나 구조가 바뀌었습니다.

넓히는 변경(int에서 bigint 등)이라도 구 테이블 리더는 거부합니다.
잘릴 수 있는 값을 읽지 않는 것이 설계이기 때문입니다.

그래서 이 변경은 재생성된 코드와 함께 나가야 하고, 그 사실을
`AcceptSchemaChanges: ["테이블.컬럼"]`로 적어 확인합니다.

한 번 통과하면 베이스라인이 갱신되므로 목록에서 지워도 됩니다.

### `... has taken tag ..., which ... used and gave up`

한 번 쓰이고 버려진 태그를 다른 컬럼이 가져갔습니다. **승인 방법이 없습니다** — 다른 변경은 구
테이블 리더가 맞게 읽거나 거부하지만, 이것만은 틀린 컬럼을 성공적으로 읽습니다. 그 컬럼에
아무도 쓴 적 없는 새 태그를 주세요.

### `... has no explicit tags, and tag ... was ... and is now ...`

`@N`이 없는 테이블에서 컬럼이 밀렸습니다. 태그가 위치이므로 중간을 지우거나 삽입하면 그 뒤
전부의 태그가 바뀝니다. 그 테이블에 `@N`을 달거나(권장), 밀린 것이 의도라면 승인하세요.

### `The schema baseline ... could not be read`

베이스라인 파일이 깨졌습니다. 고치거나 지우세요 — 지우면 한 번은 검사를 건너뛰고 현재 스키마로
새로 씁니다. 도구가 자동으로 새로 쓰지 않는 이유는, 누가 파일을 깨뜨린 바로 그 순간이 검사를
건너뛸 때가 아니기 때문입니다.

### 생성된 테이블 리더가 컬럼을 거부함

런타임에 다음과 같은 메시지가 납니다.

```
Item.Price: the file carries element type 3, which this member cannot read
(accepts 2, 0). The column changed type incompatibly; regenerate the code
or rebuild the data.
```

파일의 컬럼과 그것을 읽는 멤버의 타입이 맞지 않습니다. 데이터가 코드보다 새롭다면(넓어진 컬럼)
코드를 재생성하고, 코드가 새롭다면 데이터를 다시 뽑으세요. **읽고 나서 틀린 값을 주는 것보다
여기서 멈추는 것이 낫다**는 판단이고, `SchemaBaseline`을 켜두면 이 상황이 배포 전에 잡힙니다.

구조가 바뀐 경우(스칼라 ↔ 배열, 고정배열 길이 변경)에는 `does not match the generated member`가
붙은 메시지가 같은 자리에서 납니다.

### `the file and the generated member disagree about whether this column is optional`

컬럼에 `?`가 붙거나 떨어진 뒤에 **한쪽만** 다시 만들었습니다. 옵셔널 컬럼은 블록 앞에 presence
비트맵을 달고 있어서, 그것을 기다리지 않는 코드는 비트맵을 값으로 읽습니다. 그래서 이것도 구조
변경으로 취급해 거부합니다 — 코드를 다시 생성하거나 데이터를 다시 내보내세요.

**모든 리더가 비트 6을 nullability로 읽으므로**, 옛 데이터든 옛 코드든 위의 메시지로 나옵니다.
롤아웃 중에는 아직 지원하지 않는 리더들이 비트 6을 kind 쪽에 함께 읽어서 **kind가 맞지
않는다**는 메시지를 냈는데 — 비트맵을 값으로 읽는 것보다 낫기 때문이었고 — 그 리더들이 전부
지원하고 있으므로, 현재는 그 경로가 없습니다.

### 테이블 리더가 파일 버전을 거부함

```
table format version 103 is not supported (expected 104)
```

이 빌드가 모르는 형식의 파일입니다. 호환 경로는 없으므로 **데이터를 다시 뽑으세요** — 형식
버전은 하나뿐이고, 모르는 버전을 추측해서 읽지 않는 것이 설계입니다.

103은 컬럼이 로우마다 값의 유무를 담을 수 있게 되면서, 104는 인코딩이 4종 늘면서 올라갔습니다.
새 인코딩이 하나도 이기지 않은 파일은 **버전 4바이트만** 달라지지만, 그 4바이트가 다른 파일을
읽지 않는 것이 이 형식의 규칙입니다.

### `the table is encrypted and was not decrypted - pass the key through Open first`

암호화된 파일의 바이트를 `envelope`을 열지 않고 리더에 그대로 넘겼습니다.

로드 경로에서 `envelope`을 여는 호출을 먼저 거치세요.

암호화되지 않은 파일은 그 호출에서 그대로 돌아오므로, 키를 쓰는지 여부로 경로를 나눌 필요가
없습니다. [파일 암호화](../binary-format/security.md#파일-암호화)

### `the file did not decrypt to a table - the key is not the one it was written with`

파일이 쓰인 키와 지금 쓰는 키가 다릅니다. 암호문 머리의 `keyCheck` 4바이트가 이것을 「파일이
손상됐다」와 구분해 주고, 값을 하나도 읽기 전에 멈춥니다. 키를 바꿨다면 **그 키로 내보낸
데이터를 함께 배포해야 합니다** — 데이터와 클라이언트가 따로 갱신되는 구조라면 두 쪽이 같은
시점에 바뀌어야 합니다.

> 이 메시지를 사용자에게 그대로 보이지 말고 「데이터를 다시 받으십시오」 정도로 바꾸는 편이 낫습니다.

### `the file does not match its MAC - it was altered after it was exported`

파일의 바이트가 내보낸 시점과 다릅니다. **정상 경로에서는 나오지 않는 메시지**이므로, 나왔다면
둘 중 하나입니다.

- **누군가 파일을 고쳤습니다.** MAC이 검출하려고 있는 것이 이것입니다.
- **MAC 키가 데이터와 안 맞습니다.** 키를 바꾸고 데이터를 다시 내보내지 않았거나, 그 반대입니다.

전송 중 손상은 보통 이쪽이 아니라 업데이터의 매니페스트 해시에서 먼저 걸립니다.

### `the file carries no MAC and this build expects one`

클라이언트에는 MAC 키가 있는데 데이터에는 MAC이 없습니다. **켜는 순서가 뒤집힌 경우**가
대부분입니다 — recipe에 `MacKeyVariable`을 먼저 넣고 데이터를 다시 내보낸 다음, 클라이언트에
키를 넣습니다.

거부하는 이유는 그렇지 않으면 **16바이트를 0으로 덮는 것만으로 검사가 없어지기** 때문입니다.
「[변조 검출](../binary-format/security.md#변조-검출--mac)」

### `the file does not begin with the table file signature`

`.tcb`가 아닌 파일을 리더에 넘겼습니다. 모든 테이블 파일은 암호화 여부와 무관하게
`54 43 42 00`(`TCB\0`)으로 시작합니다 — 경로가 잘못됐거나, 빌드 단계에서 파일이 다른 것으로
덮였는지 보세요.

---
