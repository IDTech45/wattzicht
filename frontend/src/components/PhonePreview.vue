<script setup lang="ts">
/**
 * Statische weergave van het app-startscherm in de huisstijl van een installateur.
 * Gebruikt in de brandingpagina (live preview) en op de downloadpagina.
 */
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    name?: string
    logoDataUrl?: string | null
    primaryColor?: string
    quoteUrl?: string
    compact?: boolean
  }>(),
  {
    name: 'Wattzicht',
    logoDataUrl: null,
    primaryColor: '#FFE11A',
    quoteUrl: '',
    compact: false,
  },
)

const color = computed(() => (/^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test(props.primaryColor) ? props.primaryColor : '#FFE11A'))

/** Kies zwart of wit op de primaire kleur op basis van luminantie. */
const onColor = computed(() => {
  const hex = color.value.replace('#', '')
  const full = hex.length === 3 ? hex.split('').map((c) => c + c).join('') : hex
  const r = parseInt(full.slice(0, 2), 16)
  const g = parseInt(full.slice(2, 4), 16)
  const b = parseInt(full.slice(4, 6), 16)
  const lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255
  return lum > 0.6 ? '#0A1630' : '#FFFFFF'
})

const initials = computed(() =>
  props.name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase())
    .join(''),
)
</script>

<template>
  <div class="phone" :class="{ 'phone--compact': compact }" :style="{ '--brand': color, '--on-brand': onColor }">
    <div class="phone__notch" aria-hidden="true"></div>
    <div class="screen">
      <header class="screen__top">
        <div class="screen__brand">
          <img v-if="logoDataUrl" :src="logoDataUrl" alt="" class="screen__logo" />
          <span v-else class="screen__initials">{{ initials || 'W' }}</span>
          <span class="screen__name">{{ name || 'Jouw bedrijf' }}</span>
        </div>
        <span class="screen__live">live</span>
      </header>

      <section class="screen__hero">
        <span class="screen__hero-label">Nu opgewekt</span>
        <span class="screen__hero-value">3,4 <small>kW</small></span>
        <span class="screen__hero-sub">Vandaag bespaard <b>€ 4,12</b></span>
      </section>

      <section class="screen__flows">
        <div class="flowrow">
          <span class="swatch" style="--c: var(--wz-sun)"></span><span>Zon</span><b>3,4 kW</b>
        </div>
        <div class="flowrow">
          <span class="swatch" style="--c: var(--wz-battery)"></span><span>Batterij 72%</span><b>+1,3 kW</b>
        </div>
        <div class="flowrow">
          <span class="swatch" style="--c: var(--wz-export)"></span><span>Naar het net</span><b>0,9 kW</b>
        </div>
        <div class="flowrow">
          <span class="swatch" style="--c: var(--wz-charger)"></span><span>Laadpaal</span><b>0 W</b>
        </div>
      </section>

      <section class="screen__cta">
        <span>Ook een thuisbatterij?</span>
        <span class="screen__btn">Vraag offerte aan</span>
      </section>

      <nav class="screen__tabs" aria-hidden="true">
        <span class="is-active">Nu</span><span>Dag</span><span>Maand</span><span>Meldingen</span>
      </nav>
    </div>
  </div>
</template>

<style scoped>
.phone {
  --w: 300px;
  position: relative;
  width: var(--w);
  max-width: 100%;
  aspect-ratio: 9 / 18.6;
  padding: 10px;
  border-radius: 44px;
  background: var(--wz-night);
  box-shadow:
    0 0 0 2px #2e4573,
    0 30px 60px -30px rgba(0, 0, 0, 0.5);
  margin-inline: auto;
}
.phone--compact {
  --w: 240px;
}
.phone__notch {
  position: absolute;
  top: 18px;
  left: 50%;
  transform: translateX(-50%);
  width: 84px;
  height: 22px;
  border-radius: 999px;
  background: var(--wz-night);
  z-index: 2;
}
.screen {
  height: 100%;
  border-radius: 36px;
  overflow: hidden;
  background: var(--wz-paper);
  color: var(--wz-ink);
  display: grid;
  grid-template-rows: auto auto 1fr auto auto;
  font-size: 13px;
}

.screen__top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 48px 18px 14px;
  background: var(--brand);
  color: var(--on-brand);
}
.screen__brand {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.screen__logo {
  width: 26px;
  height: 26px;
  object-fit: contain;
  border-radius: 6px;
  background: var(--wz-card);
  padding: 2px;
}
.screen__initials {
  width: 26px;
  height: 26px;
  border-radius: 7px;
  background: color-mix(in srgb, var(--on-brand) 20%, transparent);
  display: grid;
  place-items: center;
  font-weight: 800;
  font-size: 11px;
}
.screen__name {
  font-weight: 700;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.screen__live {
  font-size: 10px;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 999px;
  background: color-mix(in srgb, var(--on-brand) 18%, transparent);
}

.screen__hero {
  padding: 18px 18px 8px;
  display: grid;
  gap: 2px;
}
.screen__hero-label {
  color: var(--wz-muted);
}
.screen__hero-value {
  font-size: 40px;
  font-weight: 800;
  letter-spacing: -0.03em;
  line-height: 1;
  color: var(--wz-night);
}
.screen__hero-value small {
  font-size: 16px;
  font-weight: 600;
  color: var(--wz-muted);
}
.screen__hero-sub {
  color: var(--wz-muted);
  margin-top: 4px;
}
.screen__hero-sub b {
  color: var(--wz-ink);
}

.screen__flows {
  margin: 10px 14px;
  padding: 6px 12px;
  background: var(--wz-card);
  border: 1px solid var(--wz-line);
  border-radius: 14px;
  display: grid;
  align-content: start;
}
.flowrow {
  display: grid;
  grid-template-columns: 10px 1fr auto;
  gap: 10px;
  align-items: center;
  padding: 8px 0;
  border-bottom: 1px solid var(--wz-line);
}
.flowrow:last-child {
  border-bottom: 0;
}
.flowrow b {
  font-variant-numeric: tabular-nums;
}
.swatch {
  width: 10px;
  height: 10px;
  border-radius: 999px;
  background: var(--c);
}

.screen__cta {
  margin: 0 14px 10px;
  padding: 10px 12px;
  border-radius: 12px;
  background: color-mix(in srgb, var(--brand) 18%, var(--wz-card));
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  font-size: 12px;
}
.screen__btn {
  padding: 6px 10px;
  border-radius: 999px;
  background: var(--brand);
  color: var(--on-brand);
  font-weight: 700;
  white-space: nowrap;
}

.screen__tabs {
  display: flex;
  justify-content: space-around;
  padding: 10px 6px 18px;
  border-top: 1px solid var(--wz-line);
  background: var(--wz-card);
  font-size: 11px;
  color: var(--wz-muted);
}
.screen__tabs .is-active {
  color: var(--brand);
  font-weight: 700;
}
</style>
