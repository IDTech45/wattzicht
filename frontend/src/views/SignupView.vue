<script setup lang="ts">
/**
 * Aanmelden, voor bewoners en voor installateurs.
 *
 * Twee doelgroepen op één pagina, met de keuze vooraf in plaats van twee kolommen naast elkaar: een
 * bewoner en een installateur vullen bijna niets hetzelfde in, en alles tegelijk tonen maakt het
 * formulier dubbel zo lang voor beide. Daarom eerst de vraag wie je bent, daarna alleen jouw velden.
 *
 * Over de foutmeldingen: elke fout staat onder het veld waar hij bij hoort en is daaraan gekoppeld met
 * aria-describedby, met daarbovenop een samenvatting die de focus krijgt na een mislukte verzending.
 * Een rode rand of een melding bovenaan alleen laat iemand met een schermlezer zoeken naar wat er mis
 * is, en bij een formulier van acht velden is dat precies het moment waarop mensen afhaken.
 */
import { computed, nextTick, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ApiError, login as apiLogin, register, registerInstaller } from '@/api/client'
import SiteNav from '@/components/SiteNav.vue'
import SiteFooter from '@/components/SiteFooter.vue'

const router = useRouter()

const TOKEN_KEY = 'wz.token'
const ROLE_KEY = 'wz.role'

type Rol = 'bewoner' | 'installateur'
const rol = ref<Rol | null>(null)

const bezig = ref(false)
const gelukt = ref<string | null>(null)
const fouten = ref<Record<string, string>>({})
const algemeneFout = ref<string | null>(null)
const samenvatting = ref<HTMLElement | null>(null)

// Bewoner
const email = ref('')
const wachtwoord = ref('')
const woningnaam = ref('')
const installateurscode = ref('')
const plan = ref<'installer' | 'monthly' | 'yearly'>('installer')

// Installateur
const bedrijf = ref('')
const contactpersoon = ref('')
const plaats = ref('')
const telefoon = ref('')
const aantalKlanten = ref<string>('')

const heeftFouten = computed(() => Object.keys(fouten.value).length > 0 || !!algemeneFout.value)

/** De prijzen die we tonen. Per jaar is twee maanden goedkoper dan twaalf keer per maand. */
const PRIJS_MAAND = 2
const PRIJS_JAAR = 20

function kies(nieuweRol: Rol) {
  rol.value = nieuweRol
  fouten.value = {}
  algemeneFout.value = null
}

function bewaar(token: string, role: string | null) {
  try {
    localStorage.setItem(TOKEN_KEY, token)
    if (role) localStorage.setItem(ROLE_KEY, role)
  } catch {
    /* privémodus: dan werkt het alleen deze sessie */
  }
}

/** Zet de focus op de samenvatting, zodat ook zonder muis duidelijk is wat er mis is. */
async function meldFouten() {
  await nextTick()
  samenvatting.value?.focus()
}

function verwerkFout(e: unknown) {
  fouten.value = {}
  algemeneFout.value = null

  if (e instanceof ApiError && e.status === 400 && e.body && typeof e.body === 'object') {
    const body = e.body as { errors?: Record<string, string[]> }
    if (body.errors) {
      for (const [veld, meldingen] of Object.entries(body.errors)) {
        fouten.value[veld.charAt(0).toLowerCase() + veld.slice(1)] = meldingen[0]
      }
      return
    }
  }

  if (e instanceof ApiError && e.status === 409) {
    fouten.value.email = 'Er bestaat al een account met dit e-mailadres. Log in in plaats van aanmelden.'
    return
  }

  algemeneFout.value = e instanceof Error ? e.message : 'Aanmelden is niet gelukt.'
}

async function meldBewonerAan() {
  bezig.value = true
  gelukt.value = null
  try {
    const res = await register({
      email: email.value.trim(),
      password: wachtwoord.value,
      householdName: woningnaam.value.trim(),
      installerSlug: installateurscode.value.trim() || undefined,
      plan: plan.value,
    })
    bewaar(res.token, res.role ?? 'Resident')
    gelukt.value = 'Je account staat klaar. Koppel nu je eerste apparaat.'
    await router.push('/koppelen')
  } catch (e) {
    verwerkFout(e)
    await meldFouten()
  } finally {
    bezig.value = false
  }
}

async function meldInstallateurAan() {
  bezig.value = true
  gelukt.value = null
  try {
    const aantal = aantalKlanten.value.trim()
    const res = await registerInstaller({
      company: bedrijf.value.trim(),
      contactPerson: contactpersoon.value.trim(),
      email: email.value.trim(),
      password: wachtwoord.value,
      phone: telefoon.value.trim() || undefined,
      city: plaats.value.trim() || undefined,
      customerCount: aantal ? Number(aantal) : undefined,
    })
    bewaar(res.token, res.role ?? 'Installer')
    gelukt.value =
      'Je bedrijf staat erin. Je kunt nu inloggen en je logo en kleuren invullen; wij bekijken je aanmelding en zetten je code daarna open.'
    await router.push('/portal')
  } catch (e) {
    verwerkFout(e)
    await meldFouten()
  } finally {
    bezig.value = false
  }
}

async function alIngelogd() {
  // Iemand die zich vergist en al een account heeft: direct inloggen met wat hij net typte.
  bezig.value = true
  try {
    const res = await apiLogin({ email: email.value.trim(), password: wachtwoord.value })
    bewaar(res.token, res.role ?? null)
    await router.push(res.role === 'Resident' ? '/koppelen' : '/portal')
  } catch (e) {
    verwerkFout(e)
    await meldFouten()
  } finally {
    bezig.value = false
  }
}
</script>

<template>
  <SiteNav />

  <main class="wz-section">
    <div class="wz-wrap aanmeld">
      <h1 id="aanmeld-titel">Aanmelden</h1>

      <!-- Stap 1: wie ben je -->
      <template v-if="!rol">
        <p class="wz-lead">
          Wattzicht is er voor twee soorten gebruikers. Kies wat bij je past, dan vul je alleen in wat
          voor jou nodig is.
        </p>

        <div class="keuze" role="group" aria-labelledby="aanmeld-titel">
          <button type="button" class="keuze__kaart" @click="kies('bewoner')">
            <span class="keuze__titel">Ik heb zonnepanelen, een batterij of een laadpaal</span>
            <span class="keuze__uitleg">
              Je ziet live wat je opwekt, gebruikt en bespaart. Welk merk je ook hebt.
            </span>
            <span class="keuze__prijs">Gratis via je installateur, of € {{ PRIJS_MAAND }} per maand</span>
          </button>

          <button type="button" class="keuze__kaart" @click="kies('installateur')">
            <span class="keuze__titel">Ik ben installateur</span>
            <span class="keuze__uitleg">
              Je klanten krijgen de app met jouw logo, en jij ziet in één scherm welke installatie
              aandacht nodig heeft.
            </span>
            <span class="keuze__prijs">Gratis voor je eerste vijftig klanten</span>
          </button>
        </div>

        <p class="admin__hint">
          Heb je al een account?
          <RouterLink to="/koppelen">Inloggen</RouterLink>.
        </p>
      </template>

      <!-- Stap 2: het formulier -->
      <template v-else>
        <button type="button" class="terug" @click="rol = null">Andere keuze maken</button>

        <div
          v-if="heeftFouten"
          ref="samenvatting"
          class="samenvatting"
          role="alert"
          tabindex="-1"
          aria-labelledby="samenvatting-titel"
        >
          <h2 id="samenvatting-titel">Er moet nog iets aangepast worden</h2>
          <p v-if="algemeneFout">{{ algemeneFout }}</p>
          <ul v-if="Object.keys(fouten).length">
            <li v-for="(melding, veld) in fouten" :key="veld">
              <a :href="`#f-${veld}`">{{ melding }}</a>
            </li>
          </ul>
        </div>

        <p v-if="gelukt" class="melding melding--goed" role="status">{{ gelukt }}</p>

        <!-- Bewoner -->
        <form
          v-if="rol === 'bewoner'"
          class="wz-panel kaart"
          novalidate
          @submit.prevent="meldBewonerAan"
        >
          <h2>Je gegevens</h2>

          <div class="veld">
            <label for="f-email">E-mailadres</label>
            <input
              id="f-email"
              v-model="email"
              type="email"
              autocomplete="username"
              :aria-invalid="!!fouten.email"
              :aria-describedby="fouten.email ? 'e-email' : undefined"
            />
            <p v-if="fouten.email" id="e-email" class="veld__fout">{{ fouten.email }}</p>
          </div>

          <div class="veld">
            <label for="f-password">Wachtwoord</label>
            <input
              id="f-password"
              v-model="wachtwoord"
              type="password"
              autocomplete="new-password"
              :aria-invalid="!!fouten.password"
              :aria-describedby="fouten.password ? 'e-password' : 'h-password'"
            />
            <p v-if="fouten.password" id="e-password" class="veld__fout">{{ fouten.password }}</p>
            <p v-else id="h-password" class="veld__hint">Minimaal acht tekens.</p>
          </div>

          <div class="veld">
            <label for="f-householdName">Naam van je woning</label>
            <input
              id="f-householdName"
              v-model="woningnaam"
              :aria-invalid="!!fouten.householdName"
              :aria-describedby="fouten.householdName ? 'e-householdName' : 'h-householdName'"
            />
            <p v-if="fouten.householdName" id="e-householdName" class="veld__fout">
              {{ fouten.householdName }}
            </p>
            <p v-else id="h-householdName" class="veld__hint">
              Alleen om je installatie een naam te geven, bijvoorbeeld Thuis.
            </p>
          </div>

          <div class="veld">
            <label for="f-installerSlug">Code van je installateur</label>
            <input
              id="f-installerSlug"
              v-model="installateurscode"
              :aria-invalid="!!fouten.installerSlug"
              :aria-describedby="fouten.installerSlug ? 'e-installerSlug' : 'h-installerSlug'"
            />
            <p v-if="fouten.installerSlug" id="e-installerSlug" class="veld__fout">
              {{ fouten.installerSlug }}
            </p>
            <p v-else id="h-installerSlug" class="veld__hint">
              Mag leeg. Heb je er een gekregen, dan ziet jouw installateur je installatie en krijgt de
              app zijn logo.
            </p>
          </div>

          <h2>Wat je betaalt</h2>
          <fieldset class="plannen">
            <legend class="veld__hint">
              Er wordt nu nog niets afgeschreven; we leggen alleen vast wat je kiest.
            </legend>

            <label class="plan" :class="{ 'plan--aan': plan === 'installer' }">
              <input v-model="plan" type="radio" value="installer" name="plan" />
              <span class="plan__kop">Via mijn installateur</span>
              <span class="plan__prijs">€ 0</span>
              <span class="plan__uitleg">Je installateur betaalt. Kies dit als je een code hebt.</span>
            </label>

            <label class="plan" :class="{ 'plan--aan': plan === 'monthly' }">
              <input v-model="plan" type="radio" value="monthly" name="plan" />
              <span class="plan__kop">Per maand</span>
              <span class="plan__prijs">€ {{ PRIJS_MAAND }}<small> / maand</small></span>
              <span class="plan__uitleg">Maandelijks opzegbaar.</span>
            </label>

            <label class="plan" :class="{ 'plan--aan': plan === 'yearly' }">
              <input v-model="plan" type="radio" value="yearly" name="plan" />
              <span class="plan__kop">Per jaar</span>
              <span class="plan__prijs">€ {{ PRIJS_JAAR }}<small> / jaar</small></span>
              <span class="plan__uitleg">
                Twee maanden goedkoper dan twaalf keer € {{ PRIJS_MAAND }}.
              </span>
            </label>
          </fieldset>

          <div class="acties">
            <button class="wz-btn wz-btn--primary" type="submit" :disabled="bezig">
              {{ bezig ? 'Bezig…' : 'Account aanmaken' }}
            </button>
            <button type="button" class="link" :disabled="bezig" @click="alIngelogd">
              Ik heb al een account
            </button>
          </div>
        </form>

        <!-- Installateur -->
        <form v-else class="wz-panel kaart" novalidate @submit.prevent="meldInstallateurAan">
          <h2>Je bedrijf</h2>

          <div class="veld">
            <label for="f-company">Bedrijfsnaam</label>
            <input
              id="f-company"
              v-model="bedrijf"
              autocomplete="organization"
              :aria-invalid="!!fouten.company"
              :aria-describedby="fouten.company ? 'e-company' : undefined"
            />
            <p v-if="fouten.company" id="e-company" class="veld__fout">{{ fouten.company }}</p>
          </div>

          <div class="veld">
            <label for="f-contactPerson">Jouw naam</label>
            <input
              id="f-contactPerson"
              v-model="contactpersoon"
              autocomplete="name"
              :aria-invalid="!!fouten.contactPerson"
              :aria-describedby="fouten.contactPerson ? 'e-contactPerson' : undefined"
            />
            <p v-if="fouten.contactPerson" id="e-contactPerson" class="veld__fout">
              {{ fouten.contactPerson }}
            </p>
          </div>

          <div class="veld">
            <label for="f-email">E-mailadres</label>
            <input
              id="f-email"
              v-model="email"
              type="email"
              autocomplete="username"
              :aria-invalid="!!fouten.email"
              :aria-describedby="fouten.email ? 'e-email' : undefined"
            />
            <p v-if="fouten.email" id="e-email" class="veld__fout">{{ fouten.email }}</p>
          </div>

          <div class="veld">
            <label for="f-password">Wachtwoord</label>
            <input
              id="f-password"
              v-model="wachtwoord"
              type="password"
              autocomplete="new-password"
              :aria-invalid="!!fouten.password"
              :aria-describedby="fouten.password ? 'e-password' : 'h-password'"
            />
            <p v-if="fouten.password" id="e-password" class="veld__fout">{{ fouten.password }}</p>
            <p v-else id="h-password" class="veld__hint">Minimaal acht tekens.</p>
          </div>

          <div class="veld-rij">
            <div class="veld">
              <label for="f-city">Plaats <span class="veld__bij">(mag leeg)</span></label>
              <input id="f-city" v-model="plaats" autocomplete="address-level2" />
            </div>
            <div class="veld">
              <label for="f-phone">Telefoon <span class="veld__bij">(mag leeg)</span></label>
              <input id="f-phone" v-model="telefoon" type="tel" autocomplete="tel" />
            </div>
          </div>

          <div class="veld">
            <label for="f-customerCount">
              Aantal klanten met zonnepanelen <span class="veld__bij">(schatting, mag leeg)</span>
            </label>
            <input
              id="f-customerCount"
              v-model="aantalKlanten"
              type="number"
              min="0"
              inputmode="numeric"
              :aria-describedby="'h-customerCount'"
            />
            <p id="h-customerCount" class="veld__hint">
              Hiermee weten we welke merkkoppelingen voor jou het eerst nodig zijn.
            </p>
          </div>

          <p class="veld__hint">
            Je eerste vijftig klanten zijn gratis. Wat het daarna kost spreken we met je af; we bellen
            je voordat er iets verandert.
          </p>

          <div class="acties">
            <button class="wz-btn wz-btn--primary" type="submit" :disabled="bezig">
              {{ bezig ? 'Bezig…' : 'Bedrijf aanmelden' }}
            </button>
            <button type="button" class="link" :disabled="bezig" @click="alIngelogd">
              Ik heb al een account
            </button>
          </div>
        </form>
      </template>
    </div>
  </main>

  <SiteFooter />
</template>

<style scoped>
.aanmeld {
  max-width: 46rem;
}

/* Stap 1: twee kaarten als knop. Een hele kaart aanklikbaar maken scheelt mistikken op een telefoon. */
.keuze {
  display: grid;
  gap: 14px;
  margin: 1.5rem 0;
}

.keuze__kaart {
  display: flex;
  flex-direction: column;
  gap: 6px;
  text-align: left;
  padding: 20px;
  min-height: 44px;
  border-radius: 16px;
  border: 1.5px solid var(--wz-line, #22365f);
  background: var(--wz-surface, #12234a);
  color: inherit;
  font: inherit;
  cursor: pointer;
  transition: border-color 150ms ease, background-color 150ms ease;
}

.keuze__kaart:hover {
  border-color: var(--wz-line-strong, #2e4573);
  background: var(--wz-surface-2, #172c5c);
}

.keuze__titel {
  font-weight: 700;
  font-size: 1.05rem;
}

.keuze__uitleg {
  color: var(--wz-text-soft, #9aa8c4);
  font-size: 0.92rem;
}

.keuze__prijs {
  margin-top: 4px;
  color: var(--wz-accent-500, #f5c518);
  font-weight: 600;
  font-size: 0.9rem;
}

.terug {
  background: none;
  border: 0;
  padding: 0 0 12px;
  font: inherit;
  color: var(--wz-text-soft, #9aa8c4);
  text-decoration: underline;
  cursor: pointer;
  min-height: 44px;
}

.kaart {
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.kaart h2 {
  margin: 1rem 0 0.25rem;
  font-size: 1.05rem;
}

.kaart h2:first-child {
  margin-top: 0;
}

.veld {
  display: flex;
  flex-direction: column;
  gap: 5px;
  margin-top: 0.85rem;
}

.veld-rij {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
  gap: 0 14px;
}

label {
  font-weight: 600;
  font-size: 0.92rem;
}

.veld__bij {
  font-weight: 400;
  color: var(--wz-text-soft, #9aa8c4);
}

input[type='email'],
input[type='password'],
input[type='tel'],
input[type='number'],
input:not([type]) {
  padding: 0.7rem 0.85rem;
  min-height: 44px;
  border-radius: 0.6rem;
  border: 1px solid var(--wz-line-strong, #2e4573);
  background: rgb(255 255 255 / 0.06);
  color: inherit;
  font: inherit;
  width: 100%;
}

input[aria-invalid='true'] {
  border-color: var(--wz-error-text, #ff6b6b);
}

.veld__hint {
  color: var(--wz-text-soft, #9aa8c4);
  font-size: 0.85rem;
  margin: 0;
}

.veld__fout {
  color: var(--wz-error-text, #ff6b6b);
  font-size: 0.85rem;
  margin: 0;
  font-weight: 600;
}

/* Abonnementen: drie kaarten met een echte radio erin, zodat toetsenbord en schermlezer het snappen. */
.plannen {
  border: 0;
  padding: 0;
  margin: 0.5rem 0 0;
  display: grid;
  gap: 10px;
}

.plannen legend {
  padding: 0 0 8px;
}

.plan {
  display: grid;
  grid-template-columns: auto 1fr auto;
  grid-template-areas: 'radio kop prijs' 'radio uitleg uitleg';
  gap: 2px 12px;
  align-items: center;
  padding: 14px 16px;
  border-radius: 12px;
  border: 1.5px solid var(--wz-line, #22365f);
  cursor: pointer;
  font-weight: 400;
  transition: border-color 150ms ease;
}

.plan--aan {
  border-color: var(--wz-accent-500, #f5c518);
}

.plan input {
  grid-area: radio;
  width: 20px;
  height: 20px;
  accent-color: var(--wz-accent-500, #f5c518);
}

.plan__kop {
  grid-area: kop;
  font-weight: 700;
}

.plan__prijs {
  grid-area: prijs;
  font-weight: 700;
  white-space: nowrap;
}

.plan__prijs small {
  font-weight: 400;
  color: var(--wz-text-soft, #9aa8c4);
}

.plan__uitleg {
  grid-area: uitleg;
  color: var(--wz-text-soft, #9aa8c4);
  font-size: 0.86rem;
}

.acties {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 14px;
  margin-top: 1.5rem;
}

.link {
  background: none;
  border: 0;
  padding: 0;
  min-height: 44px;
  color: inherit;
  text-decoration: underline;
  cursor: pointer;
  font: inherit;
  opacity: 0.85;
}

.samenvatting {
  border: 1.5px solid var(--wz-error-text, #ff6b6b);
  border-radius: 12px;
  padding: 14px 16px;
  margin-bottom: 1rem;
}

.samenvatting h2 {
  margin: 0 0 6px;
  font-size: 1rem;
}

.samenvatting ul {
  margin: 0;
  padding-left: 1.1rem;
}

.melding {
  padding: 0.8rem 1rem;
  border-radius: 0.6rem;
  margin: 0 0 1rem;
}

.melding--goed {
  background: rgb(22 163 74 / 0.18);
}

.admin__hint {
  color: var(--wz-text-soft, #9aa8c4);
  font-size: 0.9rem;
}

/* Wie beweging heeft uitgezet krijgt geen overgangen. */
@media (prefers-reduced-motion: reduce) {
  .keuze__kaart,
  .plan {
    transition: none;
  }
}
</style>
