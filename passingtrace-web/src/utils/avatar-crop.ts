/// <reference lib="es2022.intl" />

export function avatarCropRect(width: number, height: number, zoom: number, x: number, y: number) {
  const side = Math.min(width, height) / Math.min(3, Math.max(1, zoom))
  return {
    left: (width - side) * Math.max(0, Math.min(1, x)),
    top: (height - side) * Math.max(0, Math.min(1, y)),
    side,
  }
}

export function profileTextLength(text: string) {
  return [...new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(text)].length
}

export function accountReturnPath(from: unknown): string {
  if (
    typeof from !== 'string' ||
    !/^\/(events|storylines|assistant|product)([/?#]|$)/.test(from) ||
    from.includes('\\')
  )
    return '/events'
  return from
}
