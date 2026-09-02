<script setup lang="ts">
import { computed } from 'vue'
import type { AmapPlaceEvidence, AssistantAction } from '@/api/ai'

const props = withDefaults(
  defineProps<{
    places?: AmapPlaceEvidence[]
    actions?: AssistantAction[]
  }>(),
  { places: () => [], actions: () => [] },
)

const safeActions = computed(() => props.actions.filter(isSafeAction))
const passivePlaces = computed(() => {
  const actionable = new Set(
    safeActions.value.map(
      (action) => `${action.poiId ?? ''}:${action.latitude}:${action.longitude}`,
    ),
  )
  return props.places
    .filter((place) => isFiniteCoordinate(place.latitude, place.longitude))
    .filter((place) => !actionable.has(`${place.poiId ?? ''}:${place.latitude}:${place.longitude}`))
    .slice(0, 3)
})

function isFiniteCoordinate(latitude: number, longitude: number) {
  return (
    Number.isFinite(latitude) &&
    Number.isFinite(longitude) &&
    latitude >= -90 &&
    latitude <= 90 &&
    longitude >= -180 &&
    longitude <= 180
  )
}

function isSafeAction(action: AssistantAction) {
  if (action.provider !== 'amap') return false
  if (action.type === 'amap-trip-map') return isTrustedAmapUrl(action.webUrl)
  return (
    action.type === 'amap-navigation' &&
    action.coordinateSystem === 'GCJ02' &&
    isFiniteCoordinate(action.latitude, action.longitude)
  )
}

function isTrustedAmapUrl(value?: string | null) {
  if (!value) return false
  try {
    const url = new URL(value)
    return (
      url.protocol === 'https:' &&
      (url.hostname === 'uri.amap.com' ||
        url.hostname === 'm.amap.com' ||
        url.hostname.endsWith('.amap.com'))
    )
  } catch {
    return false
  }
}

function navigationUrl(action: AssistantAction) {
  const url = new URL('https://uri.amap.com/navigation')
  url.searchParams.set('to', `${action.longitude},${action.latitude},${action.placeName}`)
  url.searchParams.set('mode', 'car')
  url.searchParams.set('coordinate', 'gaode')
  url.searchParams.set('callnative', '1')
  url.searchParams.set('src', 'passingtrace')
  return url.toString()
}

function actionSubtitle(action: AssistantAction) {
  if (action.type === 'amap-trip-map') return '高德专属地图'
  const source = action.source === 'personal-record' ? '来自你的记录' : '来自高德地图'
  return action.address ? `${source} · ${action.address}` : source
}

function openAction(action: AssistantAction) {
  const target = action.type === 'amap-trip-map' ? action.webUrl! : navigationUrl(action)
  window.open(target, '_blank', 'noopener,noreferrer')
}
</script>

<template>
  <div
    v-if="safeActions.length || passivePlaces.length"
    class="amap-cards"
    aria-label="高德地图结果"
  >
    <article
      v-for="action in safeActions"
      :key="`${action.type}:${action.label}`"
      class="amap-card"
    >
      <span class="amap-card__icon" aria-hidden="true">
        <svg viewBox="0 0 24 24">
          <path d="M12 21s7-5.1 7-12a7 7 0 1 0-14 0c0 6.9 7 12 7 12Z" />
          <circle cx="12" cy="9" r="2.4" />
        </svg>
      </span>
      <span class="amap-card__copy">
        <strong>{{ action.placeName }}</strong>
        <small>{{ actionSubtitle(action) }}</small>
      </span>
      <button type="button" @click="openAction(action)">
        {{ action.type === 'amap-trip-map' ? '打开地图' : '高德导航' }}
      </button>
    </article>

    <article
      v-for="place in passivePlaces"
      :key="place.candidateId"
      class="amap-card amap-card--passive"
    >
      <span class="amap-card__icon" aria-hidden="true">
        <svg viewBox="0 0 24 24">
          <path d="M12 21s7-5.1 7-12a7 7 0 1 0-14 0c0 6.9 7 12 7 12Z" />
          <circle cx="12" cy="9" r="2.4" />
        </svg>
      </span>
      <span class="amap-card__copy">
        <strong>{{ place.name }}</strong>
        <small>
          来自高德地图 ·
          {{
            place.address ||
            [place.city, place.district].filter(Boolean).join(' · ') ||
            '地址未提供'
          }}
        </small>
      </span>
      <span class="amap-card__source">高德</span>
    </article>
  </div>
</template>

<style scoped>
.amap-cards {
  margin-top: 12px;
  display: grid;
  gap: 8px;
}
.amap-card {
  min-width: 0;
  padding: 10px;
  display: grid;
  grid-template-columns: 40px minmax(0, 1fr) auto;
  align-items: center;
  gap: 10px;
  border: 1px solid color-mix(in srgb, var(--primary) 24%, var(--line));
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--primary-soft) 42%, var(--surface));
}
.amap-card__icon {
  width: 40px;
  height: 40px;
  display: grid;
  place-items: center;
  border-radius: var(--radius-sm);
  color: var(--primary-strong);
  background: var(--primary-soft);
}
.amap-card__icon svg {
  width: 20px;
  height: 20px;
  fill: none;
  stroke: currentColor;
  stroke-linecap: round;
  stroke-linejoin: round;
  stroke-width: 1.8;
}
.amap-card__copy {
  min-width: 0;
  display: grid;
  gap: 3px;
}
.amap-card__copy strong,
.amap-card__copy small {
  overflow-wrap: anywhere;
}
.amap-card__copy strong {
  color: var(--ink);
  font-size: 13px;
}
.amap-card__copy small {
  color: var(--ink-secondary);
  font-size: 10px;
  line-height: 1.45;
}
.amap-card button {
  min-height: 44px;
  padding: 0 12px;
  border: 1px solid var(--primary);
  border-radius: var(--radius-sm);
  color: var(--on-primary);
  background: var(--primary);
  font-weight: 700;
}
.amap-card button:focus-visible {
  outline: 3px solid color-mix(in srgb, var(--primary) 35%, transparent);
  outline-offset: 2px;
}
.amap-card__source {
  color: var(--ink-tertiary);
  font-size: 10px;
}
@media (max-width: 520px) {
  .amap-card {
    grid-template-columns: 40px minmax(0, 1fr);
  }
  .amap-card button,
  .amap-card__source {
    grid-column: 2;
    justify-self: start;
  }
}
</style>
