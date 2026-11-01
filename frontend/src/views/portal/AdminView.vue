<script setup lang="ts">
/**
 * Beheerpaneel van Wattzicht zelf.
 *
 * Het installateursportal laat één bedrijf zijn eigen klanten zien. Dit scherm doet het omgekeerde:
 * de hele keten in één beeld. Het is opgezet rond de vragen die er voor Wattzicht echt toe doen, in
 * die volgorde: wat kan ik factureren, wie moet ik bellen, en welke koppeling moet ik bouwen.
 *
 * Die laatste vraag was eerder niet te beantwoorden en is de belangrijkste van de drie. Welk merk er
 * bij klanten hangt bepaalt wat je als volgende bouwt, en een merk waar vijf apparaten op een fout
 * staan is dringender dan een merk uit een marktonderzoek.
 *
 * Bewust één pagina met blokken en geen rij tabbladen: zolang er een handvol installateurs is, is
 * scrollen sneller dan klikken.
 */
import { computed, onMounted, ref } from 'vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import Tag from 'primevue/tag'
import StatTile from '@/components/StatTile.vue'
import {
  getAdminOverview,
  getAdminHouseholds,
  getAdminAccessLog,
  getAdminLeads,
  setInstallerApproval,
  type AdminInstaller,
  type AdminOverview,
  type AdminHousehold,
  type AdminAccessLogRow,
  type AdminLead,
} from '@/api/client'
import { euro, relativeTime } from '@/utils/format'

const overview = ref<AdminOverview | null>(null)
const households = ref<AdminHousehold[]>([])
const log = ref<AdminAccessLogRow[]>([])
const leads = ref<AdminLead[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const search = ref('')
const installerFilter = ref<string | null>(null)

/** Alleen de klanten waar iets te doen is: geen apparaat, of een apparaat dat hapert. */
const alleenActie = ref(false)

/** Welke aanmelding op dit moment wordt goedgekeurd, zodat alleen die knop op "bezig" staat. */
const bezigMet = ref<string | null>(null)

/** Installateurs die zich aanmeldden en nog wachten. Hun code werkt pas na goedkeuring. */
const aanmeldingen = computed(() => (overview.value?.installers_ ?? []).filter((i) => !i.approvedAt))

async function zetGoedkeuring(installer: AdminInstaller, goedgekeurd: boolean) {
  bezigMet.value = installer.id
  error.value = null
  try {
    const bijgewerkt = await setInstallerApproval(installer.id, goedgekeurd)
    const lijst = overview.value?.installers_
    if (lijst) {
      const plek = lijst.findIndex((i) => i.id === bijgewerkt.id)
      if (plek >= 0) lijst[plek] = bijgewerkt
    }
    if (overview.value) {
      overview.value.pendingInstallers = lijst?.filter((i) => !i.approvedAt).length ?? 0
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Dat lukte niet.'
  } finally {
    bezigMet.value = null
  }
}

const zichtbaar = computed(() => {
  const term = search.value.trim().toLowerCase()
  return households.value.filter((h) => {
    if (installerFilter.value && h.installerId !== installerFilter.value) return false
    if (alleenActie.value && !teDoen(h)) return false
    if (!term) return true
    return (
      h.name.toLowerCase().includes(term) ||
      (h.city ?? '').toLowerCase().includes(term) ||
      h.installerName.toLowerCase().includes(term)
    )
  })
})

const gefilterdOp = computed(
  () => overview.value?.installers_.find((i) => i.id === installerFilter.value)?.name ?? null,
)

/** Merken die bij klanten hangen terwijl wij ze niet kunnen uitlezen: dat is verloren data. */
const merkenZonderKoppeling = computed(() => (overview.value?.brands ?? []).filter((b) => !b.supported))

/** Een huishouden waar iets aan hapert: een apparaat met een fout of een openstaande melding. */
function hapert(h: AdminHousehold) {
  return h.devicesWithProblem > 0 || h.openAlerts > 0
}

/** Waar valt werk te verzetten: nog niets gekoppeld, of het hapert. */
function teDoen(h: AdminHousehold) {
  return h.devices === 0 || hapert(h)
}

function statusVan(h: AdminHousehold) {
  if (h.devices === 0) return { tekst: 'niets gekoppeld', ernst: 'warn' as const }
  if (hapert(h)) {
    return { tekst: `${h.devicesWithProblem} fout, ${h.openAlerts} melding(en)`, ernst: 'bad' as const }
  }
  if (!h.lastSeen) return { tekst: 'wacht op eerste meting', ernst: 'warn' as const }
  return { tekst: 'in orde', ernst: 'ok' as const }
}

async function load() {
  loading.value = true
  error.value = null
  try {
    const [o, h, l, d] = await Promise.all([
      getAdminOverview(),
      getAdminHouseholds(),
      getAdminAccessLog(),
      getAdminLeads(),
    ])
    overview.value = o
    households.value = h
    log.value = l
    leads.value = d
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
      <p>De hele keten: installateurs, hun klanten en wat daar hangt.</p>
    </header>

    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>

    <div v-if="loading" class="admin__tiles">
      <Skeleton v-for="n in 6" :key="n" height="88px" />
    </div>

    <template v-else-if="overview">
      <!-- Wat er te factureren valt en waar werk ligt -->
      <div class="admin__tiles">
        <StatTile
          label="Per maand te factureren"
          :value="euro(overview.monthlyRevenueEur)"
          :hint="`${overview.billableHouseholds} boven de eerste ${overview.freeHouseholdsPerInstaller} gratis, ${euro(overview.pricePerHouseholdEur)} per klant`"
        />
        <StatTile
          label="Actieve klanten"
          :value="String(overview.activeHouseholds)"
          :hint="`van ${overview.households} aangemeld`"
        />
        <StatTile
          label="Niets gekoppeld"
          :value="String(overview.householdsWithoutDevice)"
          hint="account gemaakt, verder niets"
        />
        <StatTile
          label="Hapert"
          :value="String(overview.devicesWithProblem + overview.devicesSilent24h)"
          :hint="`${overview.devicesWithProblem} met fout, ${overview.devicesSilent24h} een etmaal stil`"
        />
        <StatTile
          label="Nieuw deze week"
          :value="String(overview.newLast7Days)"
          :hint="`${overview.newLast30Days} in dertig dagen`"
        />
        <StatTile
          label="App op de telefoon"
          :value="String(overview.appInstalls)"
          :hint="`${overview.appInstallsAndroid} Android, ${overview.appInstallsIos} iPhone`"
        />
        <StatTile
          label="Accounts"
          :value="String(overview.accounts)"
          hint="bewoners, installateurs en beheer"
        />
        <StatTile
          label="Demo-aanvragen"
          :value="String(overview.openLeads)"
          hint="installateurs die zich meldden"
        />
      </div>

      <!-- Aanmeldingen die wachten: dit is het enige blok met een knop, dus het staat bovenaan -->
      <template v-if="aanmeldingen.length">
        <h2>Aanmeldingen die wachten</h2>
        <p class="admin__hint">
          Deze bedrijven meldden zich via de site aan. Ze kunnen inloggen en hun logo invullen, maar hun
          code werkt nog niet bij het aanmelden van bewoners. Dat gaat open als je goedkeurt.
        </p>
        <DataTable :value="aanmeldingen" data-key="id" class="admin__table">
          <Column field="name" header="Bedrijf" />
          <Column field="contactPerson" header="Contactpersoon" />
          <Column header="Contact">
            <template #body="{ data }">
              <a v-if="data.contactEmail" :href="`mailto:${data.contactEmail}`">{{ data.contactEmail }}</a>
              <span v-if="data.phone"> · {{ data.phone }}</span>
            </template>
          </Column>
          <Column field="city" header="Plaats" />
          <Column field="customerCount" header="Klanten (opgave)" />
          <Column field="slug" header="Code" />
          <Column header="Aangemeld">
            <template #body="{ data }">{{ relativeTime(data.createdAt) }}</template>
          </Column>
          <Column header="">
            <template #body="{ data }">
              <button
                type="button"
                class="admin__knop"
                :disabled="bezigMet === data.id"
                @click="zetGoedkeuring(data, true)"
              >
                {{ bezigMet === data.id ? 'Bezig…' : 'Goedkeuren' }}
              </button>
            </template>
          </Column>
        </DataTable>
      </template>

      <!-- Abonnementen van bewoners -->
      <h2>Wat bewoners kozen</h2>
      <div class="trechter">
        <div class="trechter__stap">
          <strong>{{ overview.planViaInstaller }}</strong>
          <span>via hun installateur</span>
        </div>
        <div class="trechter__stap">
          <strong>{{ overview.planMonthly }}</strong>
          <span>per maand</span>
        </div>
        <div class="trechter__stap">
          <strong>{{ overview.planYearly }}</strong>
          <span>per jaar</span>
        </div>
      </div>
      <p class="admin__hint">
        Dit is de keuze die ze bij het aanmelden maakten. Er wordt nog niets afgeschreven: er is geen
        betaalpartij aangesloten, dus van bewoners komt vandaag geen euro binnen.
      </p>

      <!-- De trechter: aanmelden is nog geen werkende koppeling -->
      <h2>Van aanmelding tot data</h2>
      <div class="trechter">
        <div class="trechter__stap">
          <strong>{{ overview.households }}</strong>
          <span>aangemeld</span>
        </div>
        <div class="trechter__stap">
          <strong>{{ overview.activeHouseholds }}</strong>
          <span>iets gekoppeld</span>
        </div>
        <div class="trechter__stap">
          <strong>{{ overview.householdsWithDataToday }}</strong>
          <span>data in de laatste 24 uur</span>
        </div>
      </div>
      <p class="admin__hint">
        Het gat tussen de eerste en de tweede stap is de bellijst: die mensen hebben een account en
        verder niets. Het gat tussen de tweede en de derde is techniek die het niet doet.
      </p>

      <!-- Welke merken hangen er echt -->
      <h2>Merken bij klanten</h2>
      <Message v-if="merkenZonderKoppeling.length" severity="warn" :closable="false">
        Bij klanten hangt
        {{ merkenZonderKoppeling.map((b) => `${b.brand} (${b.devices})`).join(', ') }}
        zonder dat wij het kunnen uitlezen. Die data komt nu nergens binnen.
      </Message>
      <DataTable :value="overview.brands ?? []" class="admin__table" data-key="brand">
        <Column field="brand" header="Merk" sortable />
        <Column field="devices" header="Apparaten" sortable />
        <Column field="households" header="Bij klanten" sortable />
        <Column header="Hapert">
          <template #body="{ data }">
            <span :class="{ admin__bad: data.withProblem > 0 }">{{ data.withProblem }}</span>
          </template>
        </Column>
        <Column header="Koppeling">
          <template #body="{ data }">
            <Tag
              :severity="data.supported ? 'success' : 'warning'"
              :value="data.supported ? 'gebouwd' : 'nog niet gebouwd'"
            />
          </template>
        </Column>
      </DataTable>
      <p class="admin__hint">
        Hier staat welke koppeling je als volgende bouwt. Een merk met veel apparaten die falen gaat
        voor een merk dat nog nergens hangt.
      </p>

      <!-- Per installateur -->
      <h2>Installateurs</h2>
      <DataTable
        :value="overview.installers_"
        data-key="id"
        class="admin__table"
        selection-mode="single"
        @row-click="(e: any) => (installerFilter = installerFilter === e.data.id ? null : e.data.id)"
      >
        <Column field="name" header="Bedrijf" sortable />
        <Column field="households" header="Klanten" sortable />
        <Column field="activeHouseholds" header="Actief" sortable />
        <Column header="Niets gekoppeld">
          <template #body="{ data }">
            <span :class="{ admin__warn: data.householdsWithoutDevice > 0 }">
              {{ data.householdsWithoutDevice }}
            </span>
          </template>
        </Column>
        <Column field="newLast30Days" header="Nieuw (30d)" sortable />
        <Column header="Mag meekijken">
          <template #body="{ data }">{{ data.householdsWithAccess }} van {{ data.households }}</template>
        </Column>
        <Column field="devices" header="Apparaten" sortable />
        <Column header="Hapert">
          <template #body="{ data }">
            <span :class="{ admin__bad: data.devicesWithProblem > 0 }">{{ data.devicesWithProblem }}</span>
          </template>
        </Column>
        <Column header="Laatste data">
          <template #body="{ data }">
            {{ data.lastDataAt ? relativeTime(data.lastDataAt) : 'nog nooit' }}
          </template>
        </Column>
        <Column header="Per maand" sortable field="monthlyRevenueEur">
          <template #body="{ data }">
            {{ euro(data.monthlyRevenueEur) }}
            <span v-if="data.billableHouseholds === 0" class="admin__zacht">(nog gratis)</span>
          </template>
        </Column>
        <Column header="Goedgekeurd">
          <template #body="{ data }">
            <Tag
              :severity="data.approvedAt ? 'success' : 'warning'"
              :value="data.approvedAt ? 'ja' : 'wacht'"
            />
            <button
              type="button"
              class="admin__knop admin__knop--zacht"
              :disabled="bezigMet === data.id"
              @click.stop="zetGoedkeuring(data, !data.approvedAt)"
            >
              {{ data.approvedAt ? 'Intrekken' : 'Goedkeuren' }}
            </button>
          </template>
        </Column>
      </DataTable>
      <p class="admin__hint">Klik op een bedrijf om de klantenlijst hieronder te filteren.</p>
    </template>

    <!-- Klanten -->
    <h2>
      Klanten
      <span v-if="gefilterdOp" class="admin__zacht">van {{ gefilterdOp }}</span>
    </h2>
    <div class="admin__filters">
      <InputText v-model="search" placeholder="Zoek op naam, plaats of installateur…" class="admin__search" />
      <label class="admin__vink">
        <input v-model="alleenActie" type="checkbox" />
        Alleen waar werk ligt
      </label>
      <button v-if="installerFilter" type="button" class="admin__link" @click="installerFilter = null">
        Filter op bedrijf weghalen
      </button>
    </div>
    <DataTable :value="zichtbaar" :loading="loading" data-key="id" class="admin__table" :rows="25" paginator>
      <Column field="name" header="Klant" sortable />
      <Column field="installerName" header="Installateur" sortable />
      <Column field="city" header="Plaats" sortable />
      <Column field="devices" header="Apparaten" sortable />
      <Column header="Status">
        <template #body="{ data }">
          <span
            :class="{
              admin__bad: statusVan(data).ernst === 'bad',
              admin__warn: statusVan(data).ernst === 'warn',
            }"
          >
            {{ statusVan(data).tekst }}
          </span>
        </template>
      </Column>
      <Column header="Laatste data">
        <template #body="{ data }">{{ data.lastSeen ? relativeTime(data.lastSeen) : 'nog nooit' }}</template>
      </Column>
      <Column header="Sinds">
        <template #body="{ data }">{{ relativeTime(data.createdAt) }}</template>
      </Column>
      <Column header="Meekijken">
        <template #body="{ data }">{{ data.installerAccessGranted ? 'ja' : 'nee' }}</template>
      </Column>
    </DataTable>

    <!-- Demo-aanvragen -->
    <h2>Demo-aanvragen</h2>
    <p class="admin__hint">
      Installateurs die het formulier op de site invulden. Hier kwamen ze eerder nergens aan: ze gingen
      de database in en niemand kreeg bericht.
    </p>
    <DataTable :value="leads" :loading="loading" data-key="id" class="admin__table" :rows="15" paginator>
      <Column header="Wanneer">
        <template #body="{ data }">{{ relativeTime(data.createdAt) }}</template>
      </Column>
      <Column field="company" header="Bedrijf" sortable />
      <Column field="name" header="Naam" />
      <Column header="Contact">
        <template #body="{ data }">
          <a :href="`mailto:${data.email}`">{{ data.email }}</a>
          <span v-if="data.phone"> · {{ data.phone }}</span>
        </template>
      </Column>
      <Column field="customerCount" header="Klanten" sortable />
      <Column field="source" header="Vanaf" />
      <Column header="Status">
        <template #body="{ data }">
          <Tag
            :severity="data.isCustomer ? 'success' : 'info'"
            :value="data.isCustomer ? 'al klant' : 'nabellen'"
          />
        </template>
      </Column>
    </DataTable>

    <!-- Logboek -->
    <h2>Wie keek er bij wie</h2>
    <p class="admin__hint">
      Elke keer dat een installateur een klant opent of op afstand iets doet, staat het hier. Handig als
      een klant vraagt wie er bij zijn gegevens is geweest.
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
.admin__filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 14px;
  margin-bottom: 10px;
}
.admin__search {
  width: 100%;
  max-width: 420px;
}
.admin__vink {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.9rem;
}
.admin__link {
  background: none;
  border: 0;
  padding: 0;
  font: inherit;
  color: inherit;
  text-decoration: underline;
  cursor: pointer;
  opacity: 0.8;
}
/* Een echte knop, niet een klikbare div: toetsenbord, focus en schermlezer werken dan gewoon. */
.admin__knop {
  min-height: 36px;
  padding: 7px 14px;
  border-radius: 999px;
  border: 1.5px solid var(--wz-accent-500, #f5c518);
  background: var(--wz-accent-500, #f5c518);
  color: var(--wz-navy-900, #0a2540);
  font: inherit;
  font-weight: 700;
  font-size: 0.86rem;
  cursor: pointer;
  transition: filter 150ms ease;
}
.admin__knop:hover:not(:disabled) {
  filter: brightness(1.08);
}
.admin__knop:disabled {
  opacity: 0.6;
  cursor: default;
}
.admin__knop--zacht {
  margin-left: 8px;
  background: none;
  border-color: var(--wz-line-strong, #2e4573);
  color: inherit;
  font-weight: 600;
}

@media (prefers-reduced-motion: reduce) {
  .admin__knop {
    transition: none;
  }
}

.admin__hint {
  color: var(--wz-text-soft, #94a3b8);
  font-size: 0.88rem;
  margin: 8px 0 0;
}
.admin__zacht {
  color: var(--wz-text-soft, #94a3b8);
  font-weight: 400;
  font-size: 0.88em;
}
.admin__bad {
  color: #f97316;
  font-weight: 600;
}
.admin__warn {
  color: #eab308;
  font-weight: 600;
}

/* De trechter: drie getallen naast elkaar, geen grafiek. Met drie stappen leest een rij sneller. */
.trechter {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 12px;
}
.trechter__stap {
  padding: 14px 16px;
  border-radius: 12px;
  background: rgb(255 255 255 / 0.04);
  border: 1px solid rgb(255 255 255 / 0.08);
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.trechter__stap strong {
  font-size: 1.6rem;
  line-height: 1.1;
}
.trechter__stap span {
  color: var(--wz-text-soft, #94a3b8);
  font-size: 0.86rem;
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
