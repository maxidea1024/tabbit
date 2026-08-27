# 바우처와 태그

> [대조표로](../parity.md)

---

## 바우처 32종

바우처는 런 전체에 남는 영구 강화입니다. 16쌍이고, 각 쌍의 상위는 하위를 산 뒤에만 상점에
나옵니다. 값은 전부 $10입니다.

|하위|효과|상위|효과|
|--|--|--|--|
|`Overstock`|상점 카드 칸 +1 (3칸)|`Overstock Plus`|칸 +1 더 (4칸)|
|`Clearance Sale`|상점 전체 25% 할인|`Liquidation`|50% 할인|
|`Hone`|포일·홀로·폴리크롬이 2배 자주|`Glow Up`|4배 자주|
|`Reroll Surplus`|리롤 비용 $2 감소|`Reroll Glut`|$2 더 감소|
|`Crystal Ball`|소모품 슬롯 +1|`Omen Globe`|아르카나 팩에 유령 카드가 섞입니다|
|`Telescope`|천체 팩에 최다 사용 족보의 행성이 반드시 들어갑니다|`Observatory`|해당 족보에 행성 카드가 배수 ×1.5|
|`Grabber`|라운드당 핸드 +1|`Nacho Tong`|핸드 +1 더|
|`Wasteful`|라운드당 버리기 +1|`Recyclomancy`|버리기 +1 더|
|`Tarot Merchant`|상점 타로 등장 2배|`Tarot Tycoon`|4배|
|`Planet Merchant`|상점 행성 등장 2배|`Planet Tycoon`|4배|
|`Seed Money`|이자 상한 $10|`Money Tree`|이자 상한 $20|
|`Blank`|없습니다|`Antimatter`|조커 슬롯 +1|
|`Magic Trick`|상점에서 플레잉 카드를 살 수 있습니다|`Illusion`|그 카드가 강화·에디션·인장을 가집니다|
|`Hieroglyph`|안테 -1, 라운드당 핸드 -1|`Petroglyph`|안테 -1 더, 라운드당 버리기 -1|
|`Director's Cut`|안테당 1회, $10에 보스 블라인드 리롤|`Retcon`|횟수 제한 없이 $10에 리롤|
|`Paint Brush`|패 크기 +1|`Palette`|패 크기 +1 더|

`Blank`가 아무것도 하지 않는 것은 결함이 아닙니다 — 상위인 `Antimatter`를 열기 위한 자리
입니다. **효과 VM에 「효과 없음」 변종이 필요한 근거가 여기 있습니다.**

## 태그 24종

블라인드를 스킵하면 태그 하나를 받습니다. 태그는 즉시 발동하거나 다음 상점까지 기다립니다.

|태그|효과|안테 조건|
|--|--|--|
|`Uncommon Tag`|상점에 무료 언커먼 조커|—|
|`Rare Tag`|상점에 무료 레어 조커|—|
|`Negative Tag`|다음 상점 조커가 무료이고 네거티브가 됩니다|2 이상|
|`Foil Tag`|다음 상점 조커가 무료이고 포일이 됩니다|—|
|`Holographic Tag`|다음 상점 조커가 무료이고 홀로그래픽이 됩니다|—|
|`Polychrome Tag`|다음 상점 조커가 무료이고 폴리크롬이 됩니다|—|
|`Investment Tag`|다음 보스 격파 후 $25|—|
|`Voucher Tag`|다음 상점에 바우처 하나 추가|—|
|`Boss Tag`|보스 블라인드를 다시 뽑습니다|—|
|`Standard Tag`|무료 메가 스탠다드 팩|2 이상|
|`Charm Tag`|무료 메가 아르카나 팩|—|
|`Meteor Tag`|무료 메가 천체 팩|2 이상|
|`Buffoon Tag`|무료 메가 광대 팩|2 이상|
|`Ethereal Tag`|무료 유령 팩|2 이상|
|`Handy Tag`|이번 런에 낸 핸드마다 $1|2 이상|
|`Garbage Tag`|이번 런에 쓰지 않은 버리기마다 $1|2 이상|
|`Coupon Tag`|다음 상점의 처음 카드와 팩이 무료|—|
|`D6 Tag`|다음 상점의 리롤이 $0에서 시작합니다|—|
|`Double Tag`|다음에 고르는 태그의 사본을 하나 줍니다. 자기 자신은 제외입니다|—|
|`Juggle Tag`|다음 라운드 패 크기 +3|—|
|`Economy Tag`|보유 금액을 2배로. **상한 $40**|—|
|`Speed Tag`|이번 런에 스킵한 블라인드마다 $5|—|
|`Orbital Tag`|무작위 족보 하나를 3레벨 올립니다|2 이상|
|`Top-up Tag`|커먼 조커를 최대 2장 만듭니다|2 이상|

`Double Tag`가 자기 자신을 제외하는 것과 `Economy Tag`의 상한이 **효과 VM이 자기 참조와
상한을 표현해야 하는 근거**입니다.

## 데이터의 자리

|무엇|테이블|
|--|--|
|바우처 32종과 쌍 관계|`Voucher` — 상위가 하위를 `foreign` 으로 가리킵니다|
|태그 24종|`Tag`|
|바우처·태그의 효과|`VoucherEffect` · `TagEffect` — 조커와 같은 효과 계열을 공유합니다|

---

EOD
