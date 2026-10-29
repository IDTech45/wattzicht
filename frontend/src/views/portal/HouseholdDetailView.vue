<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import StatusDot from '@/components/StatusDot.vue'
import StatTile from '@/components/StatTile.vue'
import { getHousehold, type HouseholdDetail, type Alert } from '@/api/client'
import { dateTime, euro, kwh, relativeTime, watt } from '@/utils/format'

const route = useRoute()
const data = ref<HouseholdDetail | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
let timer: number | undefined

async function load(silent = false) {
  if (!silent) loading.value = true
  error.value = null
  try {
    data.value = await getHousehold(String(route.params.id))
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Laden is niet gelukt.'
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  load()
  // Live-waardes: elke 30 s stil verversen.
  timer = window.setInterval(() => load(true), 30000)
})
onBeforeUnmount(() => timer && window.clearInterval(timer))
watch(() => route.params.id, () => load())

const live = computed(() => data.value?.live ?? null)
const gridLabel = computed(() => ((live.value?.gridPowerW ?? 0) < 0 ? 'Teruglevering' : 'Van het net'))
const gridTone = computed(() => ((live.value?.gridPowerW ?? 0) < 0 ? 'export' : 'grid'))
const batLabel = computed(() => ((live.value?.batteryPowerW ?? 0) < 0 ? 'Batterij laadt' : 'Batterij ontlaadt'))

const openAlerts = computed(() => (data.value?.alerts ?? []).filter((a) => !a.resolvedAt))
const closedAlerts = computed(() => (data.value?.alerts ?? []).filter((a) => a.resolvedAt).slice(0, 10))

const alertText: Record<Alert['type'], { title: string; hint: string }> = {
  NoData: { title: 'Geen data', hint: 'We krijgen geen metingen meer binnen. Controleer de wifi van het apparaat of de koppeling.' },
  ZeroProduction: { title: 'Geen opwek', hint: 'De omvormer levert niets terwijl het licht is. Mogelijk een storing of uitgeschakelde omvormer.' },
  Offline: { title: 'Offline', hint: 'Het apparaat is niet bereikbaar bij de fabrikant.' },
}

const typeLabel: Record<string, string> = {
  Inverter: 'Omvormer',
  Battery: 'Thuisbatterij',
  Charger: 'Laadpaal',
  P1Meter: 'P1-meter',
}
</script>

<template>
  <div class="page">
    <RouterLink :to="{ name: 'portal-home' }" class="back"><i class="pi pi-arrow-left" aria-hidden="true"></i> Alle klanten</RouterLink>

    <Message v-if="error" severity="error" :closable="false">
      {{ error }} <a href="#" @click.prevent="load()">Opnieuw proberen</a>
    </Message>

    <div v-if="loading && !data" class="skeletons">
      <Skeleton height="48px" width="40%" />
      <Skeleton height="120px" />
      <Skeleton height="200px" />
    </div>

    <template v-else-if="data">
      <header class="head">
        <div>
          <h1>{{ data.customerName }}</h1>
          <p class="wz-help">
            <span v-if="data.address">{{ data.address }}</span>
            <span v-else-if="data.city">{{ data.city }}</span>
            <span v-if="data.email"> · {{ data.email }}</span>
          </p>
        </div>
        <div class="head__status">
          <StatusDot :status="data.status" />
          <span class="wz-help">laatst gezien {{ relativeTime(data.lastSeen) }}</span>
        </div>
      </header>

      <section>
        <h2 class="sec">Nu</h2>
        <div v-if="live" class="tiles">
          <StatTile label="Zon" :value="watt(live.pvPowerW)" tone="sun" />
          <StatTile label="Verbruik" :value="watt(live.loadPowerW)" tone="neutral" />
          <StatTile :label="gridLabel" :value="watt(live.gridPowerW)" :tone="gridTone" />
          <StatTile
            :label="batLabel"
            :value="watt(live.batteryPowerW)"
            :hint="live.batterySoc != null ? `${Math.round(live.batterySoc)}% vol` : undefined"
            tone="battery"
          />
          <StatTile label="Laadpaal" :value="watt(live.chargerPowerW)" tone="grid" />
        </div>
        <p v-else class="empty">Nog geen live-meting. Zodra een apparaat gekoppeld is, verschijnen hier de waardes.</p>
        <p v-if="live" class="wz-help ts">Meting van {{ dateTime(live.timestamp) }}. Ververst elke 30 seconden.</p>
      </section>

      <section v-if="data.today || data.month" class="two">
        <div v-if="data.today" class="wz-panel sum">
          <h3>Vandaag</h3>
          <dl>
            <div><dt>Opgewekt</dt><dd>{{ kwh(data.today.pvKwh) }} kWh</dd></div>
            <div><dt>Zelf gebruikt</dt><dd>{{ kwh(data.today.selfUseKwh) }} kWh</dd></div>
            <div><dt>Teruggeleverd</dt><dd class="c-export">{{ kwh(data.today.exportKwh) }} kWh</dd></div>
            <div><dt>Bespaard</dt><dd class="c-battery">{{ euro(data.today.savedEur) }}</dd></div>
          </dl>
        </div>
        <div v-if="data.month" class="wz-panel sum">
          <h3>Deze maand</h3>
          <dl>
            <div><dt>Opgewekt</dt><dd>{{ kwh(data.month.pvKwh) }} kWh</dd></div>
            <div><dt>Zelf gebruikt</dt><dd>{{ kwh(data.month.selfUseKwh) }} kWh</dd></div>
            <div><dt>Teruggeleverd</dt><dd class="c-export">{{ kwh(data.month.exportKwh) }} kWh</dd></div>
            <div><dt>Bespaard</dt><dd class="c-battery">{{ euro(data.month.savedEur) }}</dd></div>
          </dl>
        </div>
      </section>

      <section>
        <h2 class="sec">Apparaten</h2>
        <ul class="devices">
          <li v-for="d in data.devices" :key="d.id" class="wz-panel dev">
            <span class="dev__type">{{ typeLabel[d.type] ?? d.type }}</span>
            <b>{{ d.brand }}</b>
            <span v-if="d.name" class="wz-help">{{ d.name }}</span>
          </li>
          <li v-if="!data.devices.length" class="empty">Nog geen apparaten gekoppeld.</li>
        </ul>
      </section>

      <section>
        <h2 class="sec">Meldingen</h2>
        <ul v-if="openAlerts.length" class="alerts">
          <li v-for="a in openAlerts" :key="a.id" class="alert alert--open">
            <i class="pi pi-exclamation-triangle" aria-hidden="true"></i>
            <div>
              <b>{{ alertText[a.type]?.title ?? a.type }}</b>
              <span v-if="a.deviceName"> · {{ a.deviceName }}</span>
              <p>{{ alertText[a.type]?.hint }}</p>
              <small>Sinds {{ dateTime(a.since) }} ({{ relativeTime(a.since) }})</small>
            </div>
          </li>
        </ul>
        <p v-else class="ok"><i class="pi pi-check-circle" aria-hidden="true"></i> Geen open meldingen.</p>

        <details v-if="closedAlerts.length" class="history">
          <summary>Eerder opgelost ({{ closedAlerts.length }})</summary>
          <ul class="alerts">
            <li v-for="a in closedAlerts" :key="a.id" class="alert">
              <i class="pi pi-check" aria-hidden="true"></i>
              <div>
                <b>{{ alertText[a.type]?.title ?? a.type }}</b>
                <span v-if="a.deviceName"> · {{ a.deviceName }}</span>
                <small>{{ dateTime(a.since) }} – {{ dateTime(a.resolvedAt) }}</small>
              </div>
            </li>
          </ul>
        </details>
      </section>
    </template>
  </div>
</template>

<style scoped>
.page {
  display: grid;
  gap: 28px;
  max-width: 1000px;
}
.back {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  text-decoration: none;
  color: var(--wz-text-soft);
  font-weight: 500;
  justify-self: start;
}
.back:hover {
  color: var(--wz-text);
}
.skeletons {
  display: grid;
  gap: 16px;
}
.head {
  display: flex;
  justify-content: space-between;
  align-items: start;
  gap: 16px;
  flex-wrap: wrap;
}
.head h1 {
  font-size: 1.8rem;
}
.head__status {
  display: grid;
  gap: 4px;
  justify-items: end;
}
.sec {
  font-size: 1.1rem;
  margin-bottom: 12px;
  color: var(--wz-text-soft);
  font-weight: 600;
}
.tiles {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 12px;
}
.ts {
  margin-top: 10px;
}
.two {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}
.sum {
  padding: 20px 22px;
}
.sum h3 {
  margin-bottom: 12px;
}
.sum dl {
  margin: 0;
  display: grid;
  gap: 8px;
}
.sum dl div {
  display: flex;
  justify-content: space-between;
  gap: 12px;
}
.sum dt {
  color: var(--wz-text-soft);
}
.sum dd {
  margin: 0;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}
.devices {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 12px;
}
.dev {
  padding: 14px 16px;
  display: grid;
  gap: 2px;
}
.dev__type {
  font-size: 0.82rem;
  color: var(--wz-text-soft);
}
.alerts {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 10px;
}
.alert {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 14px;
  padding: 14px 16px;
  border-radius: var(--wz-radius-m);
  border: 1px solid var(--wz-line);
  background: var(--wz-surface);
  color: var(--wz-text-soft);
}
.alert .pi {
  margin-top: 3px;
}
.alert--open {
  border-color: color-mix(in srgb, var(--wz-danger) 40%, transparent);
  background: color-mix(in srgb, var(--wz-danger) 6%, var(--wz-surface));
  color: var(--wz-text);
}
.alert--open .pi {
  color: var(--wz-danger);
}
.alert p {
  margin: 4px 0;
  color: var(--wz-text-soft);
}
.alert small {
  color: var(--wz-text-soft);
}
.ok {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: var(--wz-battery);
  font-weight: 600;
}
.history {
  margin-top: 14px;
}
.history summary {
  cursor: pointer;
  color: var(--wz-text-soft);
  margin-bottom: 10px;
}
.empty {
  color: var(--wz-text-soft);
}
@media (max-width: 1000px) {
  .tiles {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}
@media (max-width: 640px) {
  .tiles,
  .two {
    grid-template-columns: 1fr 1fr;
  }
}
</style>
