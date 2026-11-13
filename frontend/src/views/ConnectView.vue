<script setup lang="ts">
/**
 * Koppelen vanaf de website, voor bewoners.
 *
 * De app staat nog in geen store, dus zonder deze pagina kan niemand zijn apparaten koppelen. Hier
 * logt een bewoner in met zijn eigen account en legt hij de koppeling. Enphase gaat via toestemming
 * op de Enphase-inlogpagina; Growatt en SolaX met een sleutel die de klant zelf kan opvragen.
 *
 * Deze pagina staat bewust buiten /portal: dat scherm is voor installateurs en toont de klantenlijst.
 */
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ApiError,
  connectDevice,
  getEnphaseAuthorizeUrl,
  getMyDevices,
  login as apiLogin,
  register as apiRegister,
  type DeviceSummary,
} from '@/api/client'

const route = useRoute()
const router = useRouter()

const TOKEN_KEY = 'wz.token'

const token = ref<string | null>(leesToken())
const email = ref('')
const wachtwoord = ref('')
const bezig = ref(false)
const fout = ref<string | null>(null)
const gelukt = ref<string | null>(null)
const apparaten = ref<DeviceSummary[]>([])

/** Het merk dat de klant gekozen heeft, of null zolang hij nog kiest. */
const merk = ref<'Enphase' | 'Growatt' | 'Solax' | 'Sessy' | 'Zaptec' | null>(null)

/** Inloggen of een nieuw account maken. Zonder dit tweede kan niemand hier beginnen. */
const nieuwAccount = ref(false)
const huishoudnaam = ref('')
const installateurscode = ref('')

const growattToken = ref('')
const sessyIp = ref('')
const sessyGebruiker = ref('')
const sessyWachtwoord = ref('')
const zaptecEmail = ref('')
const zaptecWachtwoord = ref('')
const solaxToken = ref('')
const solaxSerienummer = ref('')

const ingelogd = computed(() => !!token.value)

function leesToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

function bewaarToken(waarde: string | null) {
  try {
    if (waarde === null) localStorage.removeItem(TOKEN_KEY)
    else localStorage.setItem(TOKEN_KEY, waarde)
  } catch {
    /* privémodus: dan werkt het alleen deze sessie */
  }
}

/** Het terugkeeradres dat Enphase moet gebruiken. Exact dit adres staat in de Enphase-app. */
function terugkeeradres(): string {
  return `${window.location.origin}/oauth/enphase`
}

async function inloggen() {
  fout.value = null
  bezig.value = true
  try {
    const res = await apiLogin({ email: email.value.trim(), password: wachtwoord.value })
    token.value = res.token
    bewaarToken(res.token)
    wachtwoord.value = ''
    await apparatenOphalen()
  } catch (e) {
    fout.value = e instanceof ApiError && e.status === 401
      ? 'Dit e-mailadres en wachtwoord horen niet bij elkaar.'
      : melding(e)
  } finally {
    bezig.value = false
  }
}

async function aanmelden() {
  fout.value = null
  bezig.value = true
  try {
    const res = await apiRegister({
      email: email.value.trim(),
      password: wachtwoord.value,
      householdName: huishoudnaam.value.trim(),
      installerSlug: installateurscode.value.trim() || undefined,
    })
    token.value = res.token
    bewaarToken(res.token)
    wachtwoord.value = ''
    await apparatenOphalen()
  } catch (e) {
    fout.value = e instanceof ApiError && e.status === 409
      ? 'Er bestaat al een account met dit e-mailadres. Log in in plaats van aanmelden.'
      : melding(e)
  } finally {
    bezig.value = false
  }
}

function uitloggen() {
  token.value = null
  bewaarToken(null)
  apparaten.value = []
  merk.value = null
}

async function apparatenOphalen() {
  try {
    apparaten.value = await getMyDevices()
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) { uitloggen(); return }
    // 403 betekent hier niet "storing" maar "dit account hoort bij geen woning": een installateur of
    // beheerder die hier binnenloopt heeft geen huishouden in zijn token. Tot 04-11-2026 stond er dan
    // kaal "Forbidden" op het scherm, en daar kan niemand iets mee.
    if (e instanceof ApiError && e.status === 403) {
      fout.value = 'Dit account hoort niet bij een woning. Log in met het account van de bewoner, '
        + 'of maak hieronder een nieuw account aan.'
      return
    }
    fout.value = 'We konden je apparaten even niet ophalen. Probeer het zo nog eens.'
  }
}

/**
 * Eigen Enphase-sleutel.
 *
 * Enphase rekent per developer-app en niet per woning, dus met onze gedeelde sleutel delen alle
 * klanten samen duizend aanroepen per maand. Daarom lezen we een Enphase maar eens per vijf uur uit.
 * Wie zelf een app aanmaakt heeft zijn eigen duizend, en dan kan het vaker en met meer detail.
 *
 * Het is nadrukkelijk een keuze: wie niets invult gaat gewoon met onze sleutel mee.
 */
const eigenSleutelOpen = ref(false)
const eigenClientId = ref('')
const eigenSecret = ref('')
const eigenApiKey = ref('')

const EIGEN_SLEUTEL_KEY = 'wz.enphase-eigen'

/**
 * De sleutels moeten de omleiding naar Enphase overleven: pas als de bewoner terugkomt met een code
 * wisselen we ze in. In sessionStorage en niet in localStorage, en meteen weg zodra het klaar is;
 * het zijn zijn eigen sleutels, maar ze horen niet langer te blijven staan dan deze ene handeling.
 */
function bewaarEigenSleutel() {
  try {
    if (!eigenClientId.value.trim()) {
      sessionStorage.removeItem(EIGEN_SLEUTEL_KEY)
      return
    }
    sessionStorage.setItem(
      EIGEN_SLEUTEL_KEY,
      JSON.stringify({
        client_id: eigenClientId.value.trim(),
        client_secret: eigenSecret.value.trim(),
        api_key: eigenApiKey.value.trim(),
      }),
    )
  } catch {
    /* geen opslag beschikbaar: dan gaat het gewoon via onze sleutel */
  }
}

function leesEigenSleutel(): Record<string, string> {
  try {
    const rauw = sessionStorage.getItem(EIGEN_SLEUTEL_KEY)
    if (!rauw) return {}
    const gelezen = JSON.parse(rauw) as Record<string, string>
    return gelezen.client_id && gelezen.client_secret && gelezen.api_key ? gelezen : {}
  } catch {
    return {}
  }
}

async function naarEnphase() {
  fout.value = null

  if (eigenClientId.value.trim() && !(eigenSecret.value.trim() && eigenApiKey.value.trim())) {
    fout.value = 'Vul bij je eigen sleutel alle drie de velden in, of laat ze alle drie leeg.'
    return
  }

  bezig.value = true
  try {
    bewaarEigenSleutel()
    const { url } = await getEnphaseAuthorizeUrl(terugkeeradres(), eigenClientId.value.trim() || undefined)
    window.location.href = url
  } catch (e) {
    fout.value = melding(e)
    bezig.value = false
  }
}

async function koppelGrowatt() {
  await koppel({ brand: 'Growatt', type: 'Inverter', credentials: { token: growattToken.value.trim() } })
}

async function koppelSolax() {
  await koppel({
    brand: 'Solax',
    type: 'Inverter',
    credentials: { token: solaxToken.value.trim(), sn: solaxSerienummer.value.trim() },
  })
}

async function koppelSessy() {
  await koppel({
    brand: 'Sessy',
    type: 'Battery',
    credentials: {
      host: sessyIp.value.trim(),
      username: sessyGebruiker.value.trim(),
      password: sessyWachtwoord.value,
    },
  })
}

async function koppelZaptec() {
  await koppel({
    brand: 'Zaptec',
    type: 'Charger',
    credentials: { username: zaptecEmail.value.trim(), password: zaptecWachtwoord.value },
  })
}

async function koppel(body: Parameters<typeof connectDevice>[0]) {
  fout.value = null
  gelukt.value = null
  bezig.value = true
  try {
    const gekoppeld = await connectDevice(body)
    gelukt.value = `Gekoppeld: ${gekoppeld.map((d) => d.name || d.brand).join(', ')}.`
    merk.value = null
    growattToken.value = ''
    solaxToken.value = ''
    solaxSerienummer.value = ''
    await apparatenOphalen()
  } catch (e) {
    fout.value = melding(e)
  } finally {
    bezig.value = false
  }
}

/** Komt de klant terug van Enphase, dan staat de code in de URL en wisselen we die hier in. */
async function enphaseAfronden(code: string) {
  fout.value = null
  gelukt.value = null
  bezig.value = true
  try {
    const gekoppeld = await connectDevice({
      brand: 'Enphase',
      type: 'Inverter',
      credentials: { code, redirect_uri: terugkeeradres(), ...leesEigenSleutel() },
    })
    gelukt.value = `Enphase gekoppeld: ${gekoppeld.map((d) => d.name || 'zonnepanelen').join(', ')}. De eerste meting komt binnen een uur binnen.`
    await apparatenOphalen()
  } catch (e) {
    fout.value = melding(e)
  } finally {
    bezig.value = false
    try {
      sessionStorage.removeItem(EIGEN_SLEUTEL_KEY)
    } catch {
      /* niets te wissen */
    }
    // De code is eenmalig: uit de adresbalk halen, anders mislukt een verversing van de pagina.
    await router.replace({ path: '/koppelen' })
  }
}

function melding(e: unknown): string {
  return e instanceof Error ? e.message : 'Er ging iets mis.'
}

onMounted(async () => {
  const code = route.query.code
  const geweigerd = route.query.error

  if (typeof geweigerd === 'string' && geweigerd) {
    fout.value = 'Je hebt bij Enphase geen toestemming gegeven, dus er is niets gekoppeld.'
    await router.replace({ path: '/koppelen' })
    return
  }

  if (ingelogd.value) await apparatenOphalen()

  if (typeof code === 'string' && code) {
    if (!ingelogd.value) {
      fout.value = 'Log eerst in, dan kun je de koppeling met Enphase afronden.'
      return
    }
    await enphaseAfronden(code)
  }
})
</script>

<template>
  <main class="wz-section">
    <div class="wz-wrap koppel">
      <h1>Apparaten koppelen</h1>

      <p v-if="!ingelogd" class="wz-lead">
        {{
          nieuwAccount
            ? 'Maak een account aan voor je woning. Daarna koppel je je omvormer, batterij of laadpaal.'
            : 'Log in met het account dat je van je installateur kreeg. Daarna koppel je je omvormer, batterij of laadpaal.'
        }}
      </p>

      <p v-if="fout" class="melding melding--fout">{{ fout }}</p>
      <p v-if="gelukt" class="melding melding--goed">{{ gelukt }}</p>

      <!-- Inloggen of aanmelden -->
      <form
        v-if="!ingelogd"
        class="wz-panel kaart"
        @submit.prevent="nieuwAccount ? aanmelden() : inloggen()"
      >
        <label for="k-email">E-mailadres</label>
        <input id="k-email" v-model="email" type="email" autocomplete="username" required />

        <label for="k-wachtwoord">Wachtwoord</label>
        <input
          id="k-wachtwoord"
          v-model="wachtwoord"
          type="password"
          :autocomplete="nieuwAccount ? 'new-password' : 'current-password'"
          :minlength="nieuwAccount ? 8 : undefined"
          required
        />
        <p v-if="nieuwAccount" class="hint">Minimaal acht tekens.</p>

        <template v-if="nieuwAccount">
          <label for="k-huis">Naam van je woning</label>
          <input id="k-huis" v-model="huishoudnaam" placeholder="Bijvoorbeeld: Thuis" required />

          <label for="k-code">Code van je installateur <span class="hint">(mag leeg)</span></label>
          <input id="k-code" v-model="installateurscode" />
        </template>

        <button class="wz-btn wz-btn--primary" type="submit" :disabled="bezig">
          {{ bezig ? 'Bezig…' : nieuwAccount ? 'Account aanmaken' : 'Inloggen' }}
        </button>

        <p class="wissel">
          <button type="button" class="link" @click="nieuwAccount = !nieuwAccount; fout = null">
            {{ nieuwAccount ? 'Ik heb al een account' : 'Ik heb nog geen account' }}
          </button>
        </p>
      </form>

      <!-- Gekoppeld -->
      <template v-else>
        <section v-if="apparaten.length" class="wz-panel kaart">
          <h2>Wat er nu gekoppeld is</h2>
          <ul class="lijst">
            <li v-for="d in apparaten" :key="d.id">
              <strong>{{ d.name || d.brand }}</strong>
              <span class="lijst__merk">{{ d.brand }}</span>
              <span v-if="d.lastError" class="lijst__fout">{{ d.lastError }}</span>
              <span v-else-if="d.lastSeen" class="lijst__ok">laatste meting binnen</span>
              <span v-else class="lijst__wacht">wacht op de eerste meting</span>
            </li>
          </ul>
        </section>

        <section class="wz-panel kaart">
          <h2>Koppeling toevoegen</h2>

          <div v-if="!merk" class="keuze">
            <button class="wz-btn wz-btn--primary" type="button" @click="merk = 'Enphase'">Enphase</button>
            <button class="wz-btn wz-btn--primary" type="button" @click="merk = 'Growatt'">Growatt</button>
            <button class="wz-btn wz-btn--primary" type="button" @click="merk = 'Solax'">SolaX</button>
            <button class="wz-btn wz-btn--primary" type="button" @click="merk = 'Sessy'">Sessy</button>
            <button class="wz-btn wz-btn--primary" type="button" @click="merk = 'Zaptec'">Zaptec</button>
          </div>

          <!-- Enphase: toestemming, geen sleutel -->
          <div v-else-if="merk === 'Enphase'">
            <p>
              Enphase werkt met toestemming in plaats van een sleutel. Je gaat naar de inlogpagina van
              Enphase, geeft daar toestemming, en komt hier terug.
            </p>
            <p class="let-op">
              Van Enphase krijgen wij wat je panelen opwekken. Wat je huis uit het net haalt zit bij
              Enphase achter een betaald plan, dus de besparing kunnen we pas berekenen als er ook een
              P1-meter bij zit.
            </p>
            <!-- Eigen sleutel. Dichtgeklapt, want het is een omweg voor wie het leuk vindt en
                 geen stap in het koppelen. Wie hem openklapt krijgt wel meteen het waarom. -->
            <button class="uitklap" type="button" @click="eigenSleutelOpen = !eigenSleutelOpen">
              {{ eigenSleutelOpen ? '▾' : '▸' }} Eigen Enphase-sleutel (vaker bijwerken)
            </button>

            <div v-if="eigenSleutelOpen" class="eigen">
              <p>
                Enphase telt aanroepen per app en niet per woning. Met onze sleutel delen alle klanten
                samen duizend per maand, en daarom lezen we je panelen maar eens per vijf uur uit. Maak
                je zelf een gratis app aan bij Enphase, dan heb je je eigen duizend en kan het vaker,
                met cijfers per paneel.
              </p>
              <ol>
                <li>Ga naar developer-v4.enphase.com en maak een account.</li>
                <li>Maak een app aan op het gratis Watt-plan.</li>
                <li>Zet dit adres als Redirect URI in die app: <code>{{ terugkeeradres() }}</code></li>
                <li>Neem de Client ID, Client Secret en API key hieronder over.</li>
              </ol>

              <label for="k-enphase-id">Client ID</label>
              <input id="k-enphase-id" v-model="eigenClientId" autocomplete="off" spellcheck="false" />

              <label for="k-enphase-secret">Client Secret</label>
              <input id="k-enphase-secret" v-model="eigenSecret" type="password" autocomplete="off" />

              <label for="k-enphase-key">API key</label>
              <input id="k-enphase-key" v-model="eigenApiKey" autocomplete="off" spellcheck="false" />

              <p class="hint">
                Laat je dit leeg, dan gaat het gewoon via onze sleutel. Je kunt het later altijd nog doen.
              </p>
            </div>

            <div class="rij">
              <button class="wz-btn wz-btn--primary" type="button" :disabled="bezig" @click="naarEnphase">
                {{ bezig ? 'Bezig…' : 'Verbind met Enphase' }}
              </button>
              <button class="wz-btn wz-btn--ghost" type="button" @click="merk = null">Terug</button>
            </div>
          </div>

          <!-- Growatt: één token -->
          <form v-else-if="merk === 'Growatt'" @submit.prevent="koppelGrowatt">
            <label for="k-growatt">Growatt API-token</label>
            <input id="k-growatt" v-model="growattToken" required />
            <p class="hint">Te vinden in de ShinePhone-app onder Ik → API Token.</p>
            <div class="rij">
              <button class="wz-btn wz-btn--primary" type="submit" :disabled="bezig">
                {{ bezig ? 'Bezig…' : 'Koppelen' }}
              </button>
              <button class="wz-btn wz-btn--ghost" type="button" @click="merk = null">Terug</button>
            </div>
          </form>

          <!-- SolaX: token + serienummer van de dongle -->
          <form v-else-if="merk === 'Solax'" @submit.prevent="koppelSolax">
            <label for="k-solax-token">SolaX TokenID</label>
            <input id="k-solax-token" v-model="solaxToken" required />
            <p class="hint">In SolaXCloud onder Service → API.</p>

            <label for="k-solax-sn">Registratienummer van de dongle</label>
            <input id="k-solax-sn" v-model="solaxSerienummer" required />
            <p class="hint">Tien tekens; staat op de wifi-dongle en in SolaXCloud bij het apparaat.</p>

            <div class="rij">
              <button class="wz-btn wz-btn--primary" type="submit" :disabled="bezig">
                {{ bezig ? 'Bezig…' : 'Koppelen' }}
              </button>
              <button class="wz-btn wz-btn--ghost" type="button" @click="merk = null">Terug</button>
            </div>
          </form>

          <!-- Sessy: praat alleen op het eigen netwerk, dus het adres van de dongle erbij -->
          <form v-else-if="merk === 'Sessy'" @submit.prevent="koppelSessy">
            <label for="k-sessy-ip">IP-adres van je Sessy</label>
            <input id="k-sessy-ip" v-model="sessyIp" required placeholder="192.168.1.50" />
            <p class="hint">Staat in je router of in de Sessy-app. Je batterij moet vanaf buitenaf bereikbaar zijn.</p>

            <label for="k-sessy-gebruiker">Gebruikersnaam</label>
            <input id="k-sessy-gebruiker" v-model="sessyGebruiker" required />
            <label for="k-sessy-wachtwoord">Wachtwoord</label>
            <input id="k-sessy-wachtwoord" v-model="sessyWachtwoord" type="password" required />
            <p class="hint">Allebei te vinden op de sticker van de dongle.</p>

            <div class="rij">
              <button class="wz-btn wz-btn--primary" type="submit" :disabled="bezig">
                {{ bezig ? 'Bezig…' : 'Koppelen' }}
              </button>
              <button class="wz-btn wz-btn--ghost" type="button" @click="merk = null">Terug</button>
            </div>
          </form>

          <!-- Zaptec: hetzelfde account als in hun eigen app -->
          <form v-else @submit.prevent="koppelZaptec">
            <label for="k-zaptec-email">E-mailadres van je Zaptec-account</label>
            <input id="k-zaptec-email" v-model="zaptecEmail" type="email" required />
            <label for="k-zaptec-wachtwoord">Wachtwoord</label>
            <input id="k-zaptec-wachtwoord" v-model="zaptecWachtwoord" type="password" required />
            <p class="hint">Dezelfde gegevens als waarmee je in de Zaptec-app inlogt. Wij lezen alleen uit.</p>

            <div class="rij">
              <button class="wz-btn wz-btn--primary" type="submit" :disabled="bezig">
                {{ bezig ? 'Bezig…' : 'Koppelen' }}
              </button>
              <button class="wz-btn wz-btn--ghost" type="button" @click="merk = null">Terug</button>
            </div>
          </form>
        </section>

        <p class="uitloggen">
          <button type="button" class="link" @click="uitloggen">Uitloggen</button>
        </p>
      </template>
    </div>
  </main>

</template>

<style scoped>
.uitklap {
  background: none;
  border: 0;
  padding: 8px 0;
  font: inherit;
  color: var(--wz-accent, #0a7);
  cursor: pointer;
  text-align: left;
}
.eigen {
  border-left: 3px solid var(--wz-accent, #0a7);
  padding: 4px 0 4px 14px;
  margin-bottom: 14px;
}
.eigen ol {
  margin: 8px 0 14px;
  padding-left: 20px;
}
.eigen li {
  margin-bottom: 6px;
}
.eigen code {
  word-break: break-all;
}
.koppel {
  max-width: 44rem;
}

.kaart {
  padding: 1.5rem;
  margin-bottom: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.kaart h2 {
  margin: 0 0 0.5rem;
  font-size: 1.1rem;
}

label {
  font-weight: 600;
  font-size: 0.9rem;
  margin-top: 0.5rem;
}

input {
  padding: 0.7rem 0.85rem;
  border-radius: 0.6rem;
  border: 1px solid rgb(255 255 255 / 0.18);
  background: rgb(255 255 255 / 0.06);
  color: inherit;
  font: inherit;
  width: 100%;
}

.hint,
.let-op {
  font-size: 0.85rem;
  opacity: 0.75;
  margin: 0.15rem 0 0;
}

.let-op {
  opacity: 0.9;
}

.rij,
.keuze {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  margin-top: 1rem;
}

.melding {
  padding: 0.8rem 1rem;
  border-radius: 0.6rem;
  margin: 0 0 1rem;
}

.melding--fout {
  background: rgb(220 38 38 / 0.15);
}

.melding--goed {
  background: rgb(22 163 74 / 0.15);
}

.lijst {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.lijst li {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: baseline;
}

.lijst__merk {
  font-size: 0.8rem;
  opacity: 0.6;
}

.lijst__fout {
  font-size: 0.85rem;
  color: rgb(248 113 113);
}

.lijst__ok,
.lijst__wacht {
  font-size: 0.85rem;
  opacity: 0.75;
}

.uitloggen,
.wissel {
  margin-top: 1.5rem;
}

.wissel {
  margin-bottom: 0;
}

.link {
  background: none;
  border: 0;
  padding: 0;
  color: inherit;
  text-decoration: underline;
  cursor: pointer;
  font: inherit;
  opacity: 0.8;
}
</style>
