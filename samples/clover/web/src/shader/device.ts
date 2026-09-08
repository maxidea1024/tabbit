// 이 기계가 어떤 기계인가.
//
// **손가락으로 짚는 화면인지를 봅니다.** 핸드폰은 화면이 200만 픽셀이 넘고 GPU 는 데스크탑의
// 것이 아닙니다 — MSAA 를 끄고, 재의 셰이더를 가벼운 것으로 바꾸고, 노이즈 그림을 작은 것으로
// 읽는 판정이 전부 이 하나입니다. 세 곳이 저마다 적고 있던 것을 한 자리에 모았습니다.

export function coarsePointer(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches
}
