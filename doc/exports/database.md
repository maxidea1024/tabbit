# 데이터베이스 적재

> [「내보내기」로 돌아가기](../exports.md)

---

네 대상 모두 섀도 테이블에 적재한 뒤 원자적으로 교체합니다.

적재 중 실패하면 기존 데이터가 그대로 남습니다.

| 대상 | 교체 방식 |
| --- | --- |
| MySQL | DDL 롤백이 불가하므로 다중 페어 `RENAME TABLE`(원자적) |
| PostgreSQL | DDL이 트랜잭션이므로 적재와 교체 전체를 단일 트랜잭션으로 |
| MongoDB | `renameCollection(dropTarget)` |
| Redis | `MULTI`/`EXEC` 안에서 키 단위 `RENAME` |

타입 매핑에서 배열은 관계형 DB에서 `JSON`이나 `jsonb`가 됩니다.

`timespan`은 정확도 유실을 피하기 위해 틱 값을 `BIGINT`로 저장합니다.
기본 인덱스 필드는 primary key(MongoDB는 `_id`)가 됩니다.

### 자격증명

연결 문자열은 `${환경변수}` 형식의 치환을 지원합니다.

```json
"MySql": [
  {
    "ConnectionString": "Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}",
    "NamePrefix": "tb_"
  }
]
```

비밀값을 recipe 파일에 직접 적지 마세요. recipe는 버전관리에 커밋되므로 히스토리에 영구히
남습니다.

지정한 환경변수가 설정되어 있지 않으면 빈 문자열로 치환하지 않고 오류로 처리합니다.
