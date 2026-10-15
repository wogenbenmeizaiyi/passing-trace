export interface WebDeviceSignals {
  userAgent: string
  viewportWidth: number
  coarsePointer: boolean
}

const PHONE_USER_AGENT =
  /Android.+Mobile|iPhone|iPod|Windows Phone|IEMobile|BlackBerry|webOS|Opera Mini/i

function browserSignals(): WebDeviceSignals {
  return {
    userAgent: navigator.userAgent,
    viewportWidth: window.innerWidth,
    coarsePointer: window.matchMedia?.('(pointer: coarse)').matches ?? false,
  }
}

export function isMobileWebClient(signals: WebDeviceSignals = browserSignals()) {
  return (
    PHONE_USER_AGENT.test(signals.userAgent) ||
    (signals.coarsePointer && signals.viewportWidth < 768)
  )
}

export function defaultWebEntry(signals?: WebDeviceSignals) {
  return isMobileWebClient(signals) ? '/product' : '/events'
}
