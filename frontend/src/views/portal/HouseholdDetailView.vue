<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import Textarea from 'primevue/textarea'
import { useToast } from 'primevue/usetoast'
import StatusDot from '@/components/StatusDot.vue'
import StatTile from '@/components/StatTile.vue'
import {
  getHousehold,
  messageHousehold,
  refreshHousehold,
  type Alert,
  type HouseholdDetail,
  type OpportunityKind,
} from '@/api/client'
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

const toast = useToast()

// ---- Acties op afstand ----
const refreshing = ref(false)
async function refresh() {
  if (!data.value || refreshing.value) return
  refreshing.value = true
  try {
    const r = await refreshHousehold(data.value.id)
    toast.add({ severity: r.ok ? 'success' : 'warn', summary: 'Opnieuw uitlezen', detail: r.message, life: 5000 })
    await load(true)
  } catch (e) {
    toast.add({ severity: 'warn', summary: 'Opnieuw uitlezen', detail: e instanceof Error ? e.message : undefined, life: 5000 })
  } finally {
    refreshing.value = false
  }
}

const messageText = ref('')
const sending = ref(false)
const messageLeft = computed(() => 300 - messageText.value.length)
async function sendMessage() {
  const text = messageText.value.trim()
  if (!data.value || !text || text.length > 300 || sending.value) return
  sending.value = true
  try {
    const r = await messageHousehold(data.value.id, text)
    toast.add({ severity: r.ok ? 'success' : 'warn', summary: 'Bericht', detail: r.message, life: 6000 })
    if (r.ok) messageText.value = ''
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Bericht niet verstuurd', detail: e instanceof Error ? e.message : undefined, life: 6000 })
  } finally {
    sending.value = false
  }
}

const opportunityTone: Record<OpportunityKind, string> = {
  battery: 'battery',
  smartcharging: 'charger',
  inspection: 'danger',
  dynamiccontract: 'grid',
}

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

      <section class="two">
        <div class="wz-panel act">
          <h3>Op afstand helpen</h3>
          <p class="wz-help">Alles wat je hier doet, ziet je klant terug in de app bij "Laatst bekeken".</p>
          <button type="button" class="wz-btn wz-btn--ghost" :disabled="refreshing" @click="refresh">
            <i class="pi" :class="refreshing ? 'pi-spin pi-spinner' : 'pi-refresh'" aria-hidden="true"></i>
            {{ refreshing ? 'Bezig met uitlezen…' : 'Nu opnieuw uitlezen' }}
          </button>
          <form class="act__msg" @submit.prevent="sendMessage">
            <label for="msg" class="act__label">Bericht aan de klant</label>
            <Textarea
              id="msg"
              v-model="messageText"
              rows="3"
              maxlength="300"
              auto-resize
              placeholder="Bijvoorbeeld: we zien dat je omvormer offline is, we bellen je vandaag."
            />
            <div class="act__row">
              <span class="wz-help">Komt als melding in Wattzicht · nog {{ messageLeft }} tekens</span>
              <button type="submit" class="wz-btn wz-btn--primary" :disabled="sending || !messageText.trim()">
                {{ sending ? 'Versturen…' : 'Verstuur' }}
              </button>
            </div>
          </form>
        </div>

        <div class="wz-panel act">
          <h3>Kansen</h3>
          <p v-if="data.performance" class="perf" :class="`perf--${data.performance.status}`">
            <b v-if="data.performance.percentOfExpected != null" class="wz-num">{{ data.performance.percentOfExpected }}%</b>
            {{ data.performance.message }}
            <span v-if="data.performance.status === 'low'" class="wz-help">
              ~{{ Math.round(data.performance.lostKwh7Days) }} kWh gemist deze week ({{ euro(data.performance.lostEur7Days) }}).
            </span>
          </p>
          <ul v-if="data.opportunities?.length" class="opps">
            <li v-for="o in data.opportunities" :key="o.kind" class="opp" :class="`opp--${opportunityTone[o.kind]}`">
              <div>
                <b>{{ o.title }}</b>
                <span class="wz-help">{{ o.detail }}</span>
              </div>
              <span v-if="o.yearValueEur" class="opp__value wz-num">{{ euro(o.yearValueEur) }}<small>/jaar</small></span>
            </li>
          </ul>
          <p v-else class="wz-help">Geen kansen op dit moment. Alles draait zoals het hoort.</p>
          <p class="wz-help">Bedragen: wat het de klant per jaar oplevert na de saldering (vanaf 2027).</p>
        </div>
      </section>

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
          <StatTile label="Laadpaal" :value="watt(live.chargerPowerW)" tone="charger" />
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
            <span v-if="d.externalId" class="wz-help">Serienummer {{ d.externalId }}</span>
            <span v-if="d.lastError" class="dev__err">{{ d.lastError }}</span>
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
.act {
  padding: 22px;
  display: grid;
  gap: 14px;
  align-content: start;
}
.act .wz-btn {
  justify-self: start;
}
.act__msg {
  display: grid;
  gap: 8px;
}
.act__label {
  font-weight: 600;
}
.act__row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.perf {
  display: grid;
  gap: 4px;
  padding: 12px 14px;
  border-radius: var(--wz-radius-m);
  border-left: 4px solid var(--wz-line-strong);
  background: var(--wz-surface-2);
}
.perf .wz-num {
  font-size: 1.6rem;
}
.perf--ok {
  border-left-color: var(--wz-battery);
}
.perf--low {
  border-left-color: var(--wz-danger);
}
.opps {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 8px;
}
.opp {
  --c: var(--wz-sun);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 12px 14px;
  border-radius: var(--wz-radius-m);
  border-left: 4px solid var(--c);
  background: var(--wz-surface-2);
}
.opp > div {
  display: grid;
  gap: 2px;
}
.opp--battery { --c: var(--wz-battery); }
.opp--charger { --c: var(--wz-charger); }
.opp--danger { --c: var(--wz-danger); }
.opp--grid { --c: var(--wz-grid); }
.opp__value {
  color: var(--c);
  font-size: 1.1rem;
  white-space: nowrap;
}
.opp__value small {
  font-family: var(--wz-font);
  font-weight: 500;
  font-size: 0.75rem;
  color: var(--wz-text-soft);
  margin-left: 2px;
}
.dev__err {
  color: var(--wz-danger);
  font-size: 0.85rem;
}
</style>
