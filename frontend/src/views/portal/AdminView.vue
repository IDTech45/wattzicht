<script setup lang="ts">
/**
 * Beheerpaneel van Wattzicht zelf.
 *
 * Het installateursportal laat één bedrijf zijn eigen klanten zien. Dit scherm doet het omgekeerde:
 * de hele keten in één beeld, zodat je ziet wie welke klanten heeft, waar het haakt en wat het per
 * maand oplevert. Bewust één pagina met drie blokken en geen rij tabbladen: zolang er een handvol
 * installateurs is, is scrollen sneller dan klikken.
 */
import { computed, onMounted, ref } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import StatTile from '@/components/StatTile.vue'
import {
  getAdminOverview,
  getAdminHouseholds,
  getAdminAccessLog,
  type AdminOverview,
  type AdminHousehold,
  type AdminAccessLogRow,
} from '@/api/client'
import { euro, relativeTime } from '@/utils/format'

const overview = ref<AdminOverview | null>(null)
const households = ref<AdminHousehold[]>([])
const log = ref<AdminAccessLogRow[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const search = ref('')
const installerFilter = ref<string | null>(null)

const zichtbaar = computed(() => {
  const term = search.value.trim().toLowerCase()
  return households.value.filter((h) => {
    if (installerFilter.value && h.installerId !== installerFilter.value) return false
    if (!term) return true
    return (
      h.name.toLowerCase().includes(term) ||
      (h.city ?? '').toLowerCase().includes(term) ||
      h.installerName.toLowerCase().includes(term)
    )
  })
})

/** Een huishouden waar iets aan hapert: een apparaat met een fout of een openstaande melding. */
function hapert(h: AdminHousehold) {
  return h.devicesWithProblem > 0 || h.openAlerts > 0
}

async function load() {
  loading.value = true
  error.value = null
  try {
    const [o, h, l] = await Promise.all([getAdminOverview(), getAdminHouseholds(), getAdminAccessLog()])
    overview.value = o
    households.value = h
    log.value = l
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Laden is niet gelukt.'
  } finally {
    loading.value = false
  }
}
onMounted(load)
</script>

<template>
  <section class="admin">
    <header class="admin__head">
      <h1>Beheer</h1>
      <p>Alle installateurs en hun klanten.</p>
    </header>

    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>

    <div v-if="loading" class="admin__tiles">
      <Skeleton v-for="n in 5" :key="n" height="88px" />
    </div>

    <div v-else-if="overview" class="admin__tiles">
      <StatTile label="Installateurs" :value="String(overview.installers)" />
      <StatTile label="Klanten" :value="String(overview.households)" />
      <StatTile label="Apparaten" :value="String(overview.devices)" />
      <StatTile
        label="Haperende apparaten"
        :value="String(overview.devicesWithProblem + overview.devicesSilent24h)"
      />
      <StatTile
        label="Per maand"
        :value="euro(overview.monthlyRevenueEur)"
        :hint="`${euro(overview.pricePerHouseholdEur)} per klant`"
      />
    </div>

    <h2>Installateurs</h2>
    <DataTable
      :value="overview?.installers_ ?? []"
      :loading="loading"
      data-key="id"
      class="admin__table"
      selection-mode="single"
      @row-click="(e: any) => (installerFilter = installerFilter === e.data.id ? null : e.data.id)"
    >
      <Column field="name" header="Bedrijf" sortable />
      <Column field="households" header="Klanten" sortable />
      <Column header="Mag meekijken">
        <template #body="{ data }">{{ data.householdsWithAccess }} van {{ data.households }}</template>
      </Column>
      <Column field="devices" header="Apparaten" sortable />
      <Column header="Hapert">
        <template #body="{ data }">
          <span :class="{ 'admin__bad': data.devicesWithProblem > 0 }">{{ data.devicesWithProblem }}</span>
        </template>
      </Column>
      <Column header="Laatste data">
        <template #body="{ data }">{{ data.lastDataAt ? relativeTime(data.lastDataAt) : 'nog nooit' }}</template>
      </Column>
      <Column header="Per maand" sortable field="monthlyRevenueEur">
        <template #body="{ data }">{{ euro(data.monthlyRevenueEur) }}</template>
      </Column>
    </DataTable>
    <p class="admin__hint">Klik op een bedrijf om de klantenlijst hieronder te filteren.</p>

    <h2>Klanten</h2>
    <InputText v-model="search" placeholder="Zoek op naam, plaats of installateur…" class="admin__search" />
    <DataTable :value="zichtbaar" :loading="loading" data-key="id" class="admin__table" :rows="25" paginator>
      <Column field="name" header="Klant" sortable />
      <Column field="installerName" header="Installateur" sortable />
      <Column field="city" header="Plaats" sortable />
      <Column field="devices" header="Apparaten" sortable />
      <Column header="Status">
        <template #body="{ data }">
          <span v-if="hapert(data)" class="admin__bad">
            {{ data.devicesWithProblem }} fout, {{ data.openAlerts }} melding(en)
          </span>
          <span v-else>in orde</span>
        </template>
      </Column>
      <Column header="Laatste data">
        <template #body="{ data }">{{ data.lastSeen ? relativeTime(data.lastSeen) : 'nog nooit' }}</template>
      </Column>
      <Column header="Meekijken">
        <template #body="{ data }">{{ data.installerAccessGranted ? 'ja' : 'nee' }}</template>
      </Column>
    </DataTable>

    <h2>Wie keek er bij wie</h2>
    <p class="admin__hint">
      Elke keer dat een installateur een klant opent of op afstand iets doet, staat het hier. Handig als een
      klant vraagt wie er bij zijn gegevens is geweest.
    </p>
    <DataTable :value="log" :loading="loading" class="admin__table" :rows="15" paginator>
      <Column header="Wanneer">
        <template #body="{ data }">{{ relativeTime(data.at) }}</template>
      </Column>
      <Column field="installerName" header="Installateur" />
      <Column field="householdName" header="Klant" />
      <Column field="action" header="Actie" />
    </DataTable>
  </section>
</template>

<style scoped>
.admin__head h1 {
  margin: 0;
}
.admin__head p {
  margin: 4px 0 18px;
  color: var(--wz-text-soft, #94a3b8);
}
.admin__tiles {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
  gap: 12px;
  margin-bottom: 26px;
}
.admin h2 {
  margin: 28px 0 10px;
}
.admin__table {
  font-size: 0.92rem;
}
.admin__search {
  width: 100%;
  max-width: 420px;
  margin-bottom: 10px;
}
.admin__hint {
  color: var(--wz-text-soft, #94a3b8);
  font-size: 0.88rem;
  margin: 8px 0 0;
}
.admin__bad {
  color: #f97316;
  font-weight: 600;
}

/* Op een telefoon is een brede tabel onleesbaar; laat hem scrollen in plaats van de pagina
   zijwaarts te laten uitrekken. */
@media (max-width: 760px) {
  .admin__table {
    display: block;
    overflow-x: auto;
  }
}
</style>
