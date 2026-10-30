<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import SelectButton from 'primevue/selectbutton'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import StatusDot from '@/components/StatusDot.vue'
import StatTile from '@/components/StatTile.vue'
import { getInstallerHouseholds, type HouseholdSummary, type HouseholdStatus } from '@/api/client'
import { kwh, relativeTime } from '@/utils/format'

const router = useRouter()
const rows = ref<HouseholdSummary[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const search = ref('')
const statusFilter = ref<'all' | HouseholdStatus>('all')

const statusOptions = [
  { label: 'Alles', value: 'all' },
  { label: 'Online', value: 'online' },
  { label: 'Storing', value: 'fault' },
  { label: 'Geen data', value: 'nodata' },
]

async function load() {
  loading.value = true
  error.value = null
  try {
    rows.value = await getInstallerHouseholds()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Laden is niet gelukt.'
  } finally {
    loading.value = false
  }
}
onMounted(load)

const counts = computed(() => ({
  total: rows.value.length,
  online: rows.value.filter((r) => r.status === 'online').length,
  fault: rows.value.filter((r) => r.status === 'fault').length,
  nodata: rows.value.filter((r) => r.status === 'nodata').length,
  pv: rows.value.reduce((s, r) => s + (r.todayPvKwh ?? 0), 0),
}))

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  return rows.value.filter((r) => {
    if (statusFilter.value !== 'all' && r.status !== statusFilter.value) return false
    if (!q) return true
    return (
      r.customerName.toLowerCase().includes(q) ||
      (r.city ?? '').toLowerCase().includes(q) ||
      r.devices.some((d) => d.brand.toLowerCase().includes(q))
    )
  })
})

const typeLabel: Record<string, string> = {
  Inverter: 'omvormer',
  Battery: 'batterij',
  Charger: 'laadpaal',
  P1Meter: 'P1-meter',
}

function open(row: HouseholdSummary) {
  router.push({ name: 'portal-household', params: { id: row.id } })
}
</script>

<template>
  <div class="page">
    <header class="page__head">
      <div>
        <h1>Klanten</h1>
        <p class="wz-help">Je klanten die in de app <strong>Installateur mag meekijken</strong> hebben aangezet. Elke keer dat je een klant opent, ziet die klant dat terug in de app.</p>
      </div>
      <button type="button" class="wz-btn wz-btn--ghost" :disabled="loading" @click="load">
        <i class="pi pi-refresh" :class="{ 'pi-spin': loading }" aria-hidden="true"></i> Vernieuwen
      </button>
    </header>

    <div class="tiles">
      <StatTile label="Klanten" :value="counts.total" tone="neutral" />
      <StatTile label="Online" :value="counts.online" tone="battery" />
      <StatTile label="Storing" :value="counts.fault" tone="danger" />
      <StatTile label="Geen data" :value="counts.nodata" tone="neutral" />
      <StatTile label="Vandaag opgewekt" :value="kwh(counts.pv)" unit="kWh" tone="sun" />
    </div>

    <Message v-if="error" severity="error" :closable="false">
      {{ error }} <a href="#" @click.prevent="load">Opnieuw proberen</a>
    </Message>

    <div class="toolbar">
      <span class="search">
        <i class="pi pi-search" aria-hidden="true"></i>
        <InputText v-model="search" placeholder="Zoek op naam, plaats of merk" aria-label="Zoeken" />
      </span>
      <SelectButton
        v-model="statusFilter"
        :options="statusOptions"
        option-label="label"
        option-value="value"
        :allow-empty="false"
        aria-label="Filter op status"
      />
    </div>

    <div v-if="loading" class="skeletons">
      <Skeleton v-for="i in 6" :key="i" height="52px" />
    </div>

    <DataTable
      v-else
      :value="filtered"
      data-key="id"
      striped-rows
      row-hover
      paginator
      :rows="25"
      sort-field="status"
      :sort-order="1"
      class="table"
      @row-click="(e) => open(e.data as HouseholdSummary)"
    >
      <template #empty>
        <div class="empty">
          <p v-if="rows.length === 0">
            Nog geen klanten gekoppeld. Nodig je eerste klant uit via de app, of vraag ons om een import.
          </p>
          <p v-else>Geen klanten met dit filter.</p>
        </div>
      </template>

      <Column field="customerName" header="Klant" sortable>
        <template #body="{ data }">
          <RouterLink :to="{ name: 'portal-household', params: { id: data.id } }" class="cust" @click.stop>
            <b>{{ data.customerName }}</b>
            <span v-if="data.city" class="cust__city">{{ data.city }}</span>
          </RouterLink>
        </template>
      </Column>

      <Column header="Apparaten">
        <template #body="{ data }">
          <span class="devices">
            <span v-for="d in data.devices" :key="d.id" class="chip" :class="`chip--${d.type}`">
              {{ d.brand }} <small>{{ typeLabel[d.type] ?? d.type }}</small>
            </span>
            <span v-if="!data.devices.length" class="wz-help">geen</span>
          </span>
        </template>
      </Column>

      <Column field="status" header="Status" sortable style="width: 140px">
        <template #body="{ data }"><StatusDot :status="data.status" /></template>
      </Column>

      <Column field="todayPvKwh" header="Vandaag opgewekt" sortable style="width: 170px" class="num">
        <template #body="{ data }"><span class="num">{{ kwh(data.todayPvKwh) }} kWh</span></template>
      </Column>

      <Column field="lastSeen" header="Laatst gezien" sortable style="width: 160px">
        <template #body="{ data }">
          <span :title="data.lastSeen ?? ''">{{ relativeTime(data.lastSeen) }}</span>
        </template>
      </Column>
    </DataTable>
  </div>
</template>

<style scoped>
.page {
  display: grid;
  gap: 22px;
}
.page__head {
  display: flex;
  justify-content: space-between;
  align-items: start;
  gap: 16px;
  flex-wrap: wrap;
}
.page__head h1 {
  font-size: 1.8rem;
}
.tiles {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 12px;
}
.toolbar {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.search {
  position: relative;
  flex: 1 1 280px;
  max-width: 420px;
}
.search .pi {
  position: absolute;
  left: 12px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--wz-text-soft);
}
.search :deep(.p-inputtext) {
  width: 100%;
  padding-left: 36px;
}
.skeletons {
  display: grid;
  gap: 8px;
}
.table {
  border: 1px solid var(--wz-line);
  border-radius: var(--wz-radius-m);
  overflow: hidden;
}
.table :deep(.p-datatable-tbody > tr) {
  cursor: pointer;
}
.cust {
  display: grid;
  text-decoration: none;
  color: inherit;
}
.cust__city {
  font-size: 0.85rem;
  color: var(--wz-text-soft);
}
.devices {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.chip {
  --c: var(--wz-grid);
  display: inline-flex;
  align-items: baseline;
  gap: 5px;
  padding: 3px 9px;
  border-radius: 999px;
  font-size: 0.82rem;
  font-weight: 600;
  background: color-mix(in srgb, var(--c) 14%, transparent);
  color: var(--c);
  white-space: nowrap;
}
.chip small {
  font-weight: 400;
  opacity: 0.85;
}
.chip--Inverter { --c: var(--wz-sun); }
.chip--Battery { --c: var(--wz-battery); }
.chip--Charger { --c: var(--wz-charger); }
.chip--P1Meter { --c: var(--wz-muted); }
.num {
  font-variant-numeric: tabular-nums;
}
.empty {
  padding: 32px 16px;
  text-align: center;
  color: var(--wz-text-soft);
}
@media (max-width: 1100px) {
  .tiles {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}
@media (max-width: 640px) {
  .tiles {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
