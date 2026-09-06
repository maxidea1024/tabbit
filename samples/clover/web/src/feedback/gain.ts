// 이득을 옮기는 한 가지 방법.
//
// **값을 대입하면 그 자리에서 소리가 끊깁니다.** `gain.value = 0` 은 파형을 그 표본에서
// 잘라내는 것이고, 잘린 자리의 불연속이 「퍽」 소리로 들립니다 — 배경음을 끌 때 나던 것이
// 그것입니다. 옵션의 음량이 20%씩 뛰는 것도 같은 크기의 불연속입니다.
//
// **잦아드는 시간은 짧아도 됩니다.** 0 에 닿기만 하면 나지 않으므로, 40밀리초로도 끊긴
// 것으로 들리지 않으면서 옵션을 만지는 사람에게는 즉시입니다.

/** 이득이 옮겨 가는 데 걸리는 시간. */
export const GLIDE = 0.04

/**
 * 이득을 그 값으로 옮깁니다.
 *
 * **예약된 것을 먼저 걷습니다.** 앞서 걸어 둔 램프가 남아 있으면 그것이 이어서 돌아, 방금
 * 정한 값을 지나쳐 갑니다 — 음량 단추를 두 번 빠르게 누르면 그 일이 일어납니다.
 *
 * `setTargetAtTime` 을 쓰지 않는 것은 그것이 목표에 닿지 않기 때문입니다. 끄기는 0 에
 * 닿아야 하고, 닿지 않으면 아주 작은 소리가 계속 남습니다.
 *
 * **지금이 몇 시인지는 부르는 쪽이 넘깁니다.** `AudioParam` 에서 소리 길로 거슬러 갈 수
 * 없습니다.
 */
export function glide(param: AudioParam, to: number, now: number, span = GLIDE): void {
  param.cancelScheduledValues(now)
  param.setValueAtTime(param.value, now)
  param.linearRampToValueAtTime(to, now + span)
}
