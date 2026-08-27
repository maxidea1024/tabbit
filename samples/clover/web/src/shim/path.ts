// 브라우저 빌드에서 `path` 자리에 놓이는 것.
//
// 생성된 접근자가 `path.join` 하나만 씁니다. 브라우저에서는 그 경로를 지나지 않지만,
// 번들러가 임포트를 해석하므로 같은 이름이 있어야 합니다.

export function join(...parts: string[]): string {
  return parts.filter(Boolean).join('/').replace(/\/+/g, '/')
}

export default { join }
