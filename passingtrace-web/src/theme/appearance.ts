import { ref } from 'vue'

export type AppearancePalette = 'pine' | 'tide' | 'plum' | 'dune'
export type AppearanceMode = 'system' | 'light' | 'dark'

const paletteKey = 'passingtrace.appearance.palette'
const modeKey = 'passingtrace.appearance.mode'
const palettes: AppearancePalette[] = ['pine', 'tide', 'plum', 'dune']
const modes: AppearanceMode[] = ['system', 'light', 'dark']

export const appearancePalette = ref<AppearancePalette>('pine')
export const appearanceMode = ref<AppearanceMode>('system')

let mediaQuery: MediaQueryList | null = null

function isPalette(value: string | null): value is AppearancePalette {
  return palettes.includes(value as AppearancePalette)
}

function isMode(value: string | null): value is AppearanceMode {
  return modes.includes(value as AppearanceMode)
}

function applyAppearance() {
  const dark =
    appearanceMode.value === 'dark' || (appearanceMode.value === 'system' && mediaQuery?.matches)
  document.documentElement.dataset.palette = appearancePalette.value
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
}

export function initializeAppearance() {
  if (typeof window === 'undefined') return
  const storedPalette = window.localStorage.getItem(paletteKey)
  const storedMode = window.localStorage.getItem(modeKey)
  if (isPalette(storedPalette)) appearancePalette.value = storedPalette
  if (isMode(storedMode)) appearanceMode.value = storedMode
  mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
  mediaQuery.addEventListener('change', applyAppearance)
  applyAppearance()
}

export function setAppearancePalette(value: AppearancePalette) {
  appearancePalette.value = value
  window.localStorage.setItem(paletteKey, value)
  applyAppearance()
}

export function setAppearanceMode(value: AppearanceMode) {
  appearanceMode.value = value
  window.localStorage.setItem(modeKey, value)
  applyAppearance()
}
