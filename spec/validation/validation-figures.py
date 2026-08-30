# -*- coding: utf-8 -*-
"""validation-pipeline.md 의 단계 그림을 SVG로 생성한다.

실행: `python spec/validation/validation-figures.py`

같은 폴더에 validation-*.svg 를 다시 씁니다. 고쳤으면 다시 실행한 뒤, PNG로 렌더해 눈으로
확인하고 커밋합니다.

그리는 코드는 doc/figures/flow.py 의 것을 씁니다 — 같은 종류의 그림을 두 벌 그리면 문서마다
서로 달라 보이기 시작하는 자리가 되기 때문입니다."""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))

sys.path.insert(0, os.path.join(REPO, "doc", "figures"))
import flow  # noqa: E402


flow.build("validation-pipeline", [
    flow.step("recipe 로드"),

    flow.stage("① 사전 검증", "파일 이름 · 설정 · 환경",
               aside=[("rules/pre/*.cs", "시트를 읽기 전에 확인할 수 있는 것")]),
    flow.edge("실패 → 종료. 아무것도 읽지 않았습니다"),

    flow.step("임포트 (셀 격자)"),
    flow.step("쿠킹 → Model"),

    flow.stage("② 정적 검증 (기존)", "타입 · 인덱스 유니크",
               aside=[("ModelCooker.Validation", ""),
                      ("", "컬럼 제약 · 참조 해석 · 대상 사이드")]),
    flow.edge("실패 → 종료"),

    flow.stage("③ 사후 검증", "이 문서가 추가하는 단계",
               aside=[("rules/tables/*.cs", "테이블별 규칙"),
                      ("rules/global/*.cs", "전역 룰셋 (§4)"),
                      ("rules/runtime/*.cs", "DB · Redis 교차 확인")]),
    flow.edge("실패 → 종료. 어떤 산출물도 쓰지 않았습니다"),

    flow.step("타깃 실행",
              aside=[("", "파일은 스테이징으로, 데이터베이스는 섀도로")]),
    flow.step("커밋",
              aside=[("staging → 실제 경로 · 섀도 교체", "")]),
], out_dir=HERE)
