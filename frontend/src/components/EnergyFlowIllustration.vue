<script setup lang="ts">
/**
 * Geanimeerde energiestroom: zon → huis → batterij / net.
 * Kleuren per stroom komen uit BRAND (zon = sun, batterij = battery, net = grid, teruglevering = export).
 * De getallen "ademen" licht mee zodat het schema leeft zonder dat het echte data is.
 */
import { onMounted, onBeforeUnmount, ref } from 'vue'

const pv = ref(3420)
const load = ref(1180)
const battery = ref(1300)
const exportW = ref(940)

let timer: number | undefined

function jitter(base: number, spread: number) {
  return Math.round((base + (Math.random() - 0.5) * spread) / 10) * 10
}

onMounted(() => {
  const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
  if (reduce) return
  timer = window.setInterval(() => {
    pv.value = jitter(3420, 260)
    load.value = jitter(1180, 160)
    battery.value = jitter(1300, 120)
    exportW.value = Math.max(0, pv.value - load.value - battery.value)
  }, 2400)
})
onBeforeUnmount(() => {
  if (timer) window.clearInterval(timer)
})

const fmt = (w: number) => (w >= 1000 ? `${(w / 1000).toFixed(1).replace('.', ',')} kW` : `${w} W`)
</script>

<template>
  <svg
    class="flow"
    viewBox="0 0 640 480"
    role="img"
    aria-labelledby="flow-title flow-desc"
    xmlns="http://www.w3.org/2000/svg"
  >
    <title id="flow-title">Energiestroom in huis</title>
    <desc id="flow-desc">
      De zon levert {{ fmt(pv) }}. Het huis gebruikt {{ fmt(load) }}, de batterij laadt met
      {{ fmt(battery) }} en {{ fmt(exportW) }} gaat terug naar het net.
    </desc>

    <defs>
      <linearGradient id="sunFill" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stop-color="#FFEB66" />
        <stop offset="1" stop-color="#FFE11A" />
      </linearGradient>
      <filter id="glow" x="-50%" y="-50%" width="200%" height="200%">
        <feGaussianBlur stdDeviation="10" />
      </filter>
    </defs>

    <!-- Achtergrondsporen (stil) -->
    <g class="rails" fill="none" stroke-width="3" stroke-linecap="round">
      <path d="M126 112 C 200 112, 240 240, 300 240" />
      <path d="M320 268 C 320 330, 220 340, 150 380" />
      <path d="M344 240 C 420 240, 460 240, 528 240" />
    </g>

    <!-- Bewegende stromen -->
    <g fill="none" stroke-width="3.5" stroke-linecap="round">
      <path class="stream stream--sun" d="M126 112 C 200 112, 240 240, 300 240" />
      <path class="stream stream--battery" d="M320 268 C 320 330, 220 340, 150 380" />
      <path class="stream stream--export" d="M344 240 C 420 240, 460 240, 528 240" />
    </g>

    <!-- Zon -->
    <g class="node node--sun" transform="translate(92 92)">
      <circle r="44" fill="url(#sunFill)" opacity=".35" filter="url(#glow)" class="sun-glow" />
      <circle r="30" fill="url(#sunFill)" />
      <g class="rays" stroke="#FFE11A" stroke-width="3.5" stroke-linecap="round">
        <line x1="0" y1="-44" x2="0" y2="-52" />
        <line x1="31" y1="-31" x2="37" y2="-37" />
        <line x1="44" y1="0" x2="52" y2="0" />
        <line x1="31" y1="31" x2="37" y2="37" />
        <line x1="0" y1="44" x2="0" y2="52" />
        <line x1="-31" y1="31" x2="-37" y2="37" />
        <line x1="-44" y1="0" x2="-52" y2="0" />
        <line x1="-31" y1="-31" x2="-37" y2="-37" />
      </g>
    </g>
    <text x="92" y="172" class="label label--sun" text-anchor="middle">Zon</text>
    <text x="92" y="194" class="value" text-anchor="middle">{{ fmt(pv) }}</text>

    <!-- Huis -->
    <g class="node node--house" transform="translate(320 240)">
      <circle r="52" class="house-ring" />
      <path d="M-26 6 L0 -20 L26 6 V28 H8 V14 H-8 V28 H-26 Z" class="house" stroke-linejoin="round" />
      <path d="M12 -14 V-24 H20 V-6" class="house" />
    </g>
    <text x="320" y="320" class="label" text-anchor="middle">Je huis</text>
    <text x="320" y="342" class="value" text-anchor="middle">{{ fmt(load) }}</text>

    <!-- Batterij -->
    <g class="node node--battery" transform="translate(120 400)">
      <rect x="-30" y="-16" width="56" height="32" rx="6" class="bat-shell" />
      <rect x="26" y="-7" width="7" height="14" rx="2" class="bat-cap" />
      <rect x="-25" y="-11" width="12" height="22" rx="2" fill="#5EEAD4" />
      <rect x="-10" y="-11" width="12" height="22" rx="2" fill="#5EEAD4" />
      <rect x="5" y="-11" width="12" height="22" rx="2" fill="#5EEAD4" class="bat-cell--last" />
    </g>
    <text x="120" y="442" class="label label--battery" text-anchor="middle">Batterij · 72%</text>
    <text x="120" y="464" class="value" text-anchor="middle">+{{ fmt(battery) }}</text>

    <!-- Net -->
    <g class="node node--grid" transform="translate(560 240)">
      <path
        d="M-18 34 L-8 -34 H8 L18 34 M-24 -14 H24 M-20 2 H20 M-12 -34 L-26 -22 M12 -34 L26 -22 M-14 34 L14 -4 M14 34 L-14 -4"
        class="pylon"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
    </g>
    <text x="560" y="300" class="label label--export" text-anchor="middle">Terug naar net</text>
    <text x="560" y="322" class="value" text-anchor="middle">{{ fmt(exportW) }}</text>
  </svg>
</template>

<style scoped>
.flow {
  width: 100%;
  height: auto;
  font-family: var(--wz-font);
}

.rails path {
  stroke: var(--wz-line);
}

.stream {
  stroke-dasharray: 2 14;
  animation: travel 1.4s linear infinite;
}
.stream--sun {
  stroke: var(--wz-sun);
}
.stream--battery {
  stroke: var(--wz-battery);
  animation-duration: 2s;
}
.stream--export {
  stroke: var(--wz-export);
  animation-duration: 2.4s;
}
@keyframes travel {
  to {
    stroke-dashoffset: -16;
  }
}

.sun-glow {
  /* SVG: zonder fill-box draait/schaalt alles om het midden van de hele tekening en schiet de zon weg. */
  transform-box: fill-box;
  transform-origin: center;
  animation: breathe 4s ease-in-out infinite;
}
.rays {
  transform-box: fill-box;
  transform-origin: center;
  animation: spin 60s linear infinite;
}
@keyframes breathe {
  0%,
  100% {
    transform: scale(1);
    opacity: 0.3;
  }
  50% {
    transform: scale(1.15);
    opacity: 0.45;
  }
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.house-ring {
  fill: var(--wz-surface);
  stroke: var(--wz-line-strong);
  stroke-width: 2;
}
.house {
  fill: none;
  stroke: var(--wz-brand-fg);
  stroke-width: 3;
}
.bat-shell {
  fill: var(--wz-surface);
  stroke: var(--wz-brand-fg);
  stroke-width: 2.5;
}
.bat-cap {
  fill: var(--wz-brand-fg);
}
.bat-cell--last {
  animation: fill 2.4s ease-in-out infinite;
}
@keyframes fill {
  0%,
  100% {
    opacity: 0.25;
  }
  50% {
    opacity: 1;
  }
}
.pylon {
  fill: none;
  stroke: var(--wz-brand-fg);
  stroke-width: 2.5;
}

.label {
  font-size: 15px;
  font-weight: 600;
  fill: var(--wz-text-soft);
}
.label--sun {
  fill: var(--wz-sun);
}
.label--battery {
  fill: var(--wz-battery);
}
.label--export {
  fill: var(--wz-export);
}
.value {
  font-size: 19px;
  font-weight: 800;
  letter-spacing: -0.02em;
  fill: var(--wz-brand-fg);
  font-variant-numeric: tabular-nums;
}

@media (prefers-reduced-motion: reduce) {
  .stream,
  .sun-glow,
  .rays,
  .bat-cell--last {
    animation: none;
  }
  .stream {
    stroke-dasharray: none;
    opacity: 0.8;
  }
}
</style>
