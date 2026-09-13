# UI 원화

`tools/ui.py` 가 이 폴더의 고해상도 원화를 `web/public/ui` 의 런타임 자산으로 굽습니다.

- `plate-source.png`, `well-source.png`, `button-source.png`: 공통 9분할 판·칸·단추
- `hud-shell-source.png`, `blind-badge-source.png`: 플레이 화면 왼쪽 HUD 전용 외피
- `title-*-source.png`: 타이틀의 시작·콜렉션·리더보드 전용 삽화

고정 UI와 그림 자리는 이 원화에서 냅니다. 개발 중 원화가 비었다고 선·줄무늬·막대 같은
도형을 최종 화면에 대신 두지 않습니다. 마젠타 배경 원화와 실제 알파 원화는 모두 굽기 도구가
투명 PNG로 정규화합니다.
