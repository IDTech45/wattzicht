<script setup lang="ts">
import { reactive, computed } from 'vue'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Message from 'primevue/message'
import PhonePreview from '@/components/PhonePreview.vue'
import { useLeadsStore } from '@/stores/leads'
import { partners } from '@/utils/partners'
import FaqList from '@/components/FaqList.vue'

const featured = partners[0]!

const leads = useLeadsStore()

const form = reactive({
  name: '',
  company: '',
  email: '',
  phone: '',
  customerCount: null as number | null,
})
const touched = reactive({ name: false, company: false, email: false })

const emailOk = computed(() => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email))
const valid = computed(() => form.name.trim() && form.company.trim() && emailOk.value)

async function send() {
  touched.name = touched.company = touched.email = true
  if (!valid.value || leads.state === 'sending') return
  const ok = await leads.submit({ ...form, name: form.name.trim(), company: form.company.trim() })
  if (ok) {
    form.name = form.company = form.email = form.phone = ''
    form.customerCount = null
    touched.name = touched.company = touched.email = false
  }
}

const steps = [
  {
    title: 'Je klant installeert Wattzicht',
    text: 'Met jouw installateurscode. Jij staat er meteen in als "jouw installateur", met je telefoonnummer en een offerteknop.',
  },
  {
    title: 'Je klant koppelt z’n apparaten',
    text: 'Omvormer, batterij en laadpaal van elk merk, in twee minuten. De klant geeft zelf toestemming dat jij meekijkt.',
  },
  {
    title: 'Jij ziet al je klanten in één overzicht',
    text: 'Wat ze hebben, hoe het draait, waar een storing is en waar een kans ligt. En je kunt op afstand helpen.',
  },
]

const scenarios = [
  {
    tone: 'danger',
    title: 'Storing? Jij belt eerst',
    text: 'Omvormer offline of levert niets terwijl de zon schijnt: jij krijgt de melding met merk en serienummer, vóór de klant belt.',
  },
  {
    tone: 'sun',
    title: 'Levert minder dan het weer toelaat',
    text: 'Wattzicht vergelijkt de opwek met het weer van die dag. Structureel te laag? Tijd voor schoonmaken of een controle.',
  },
  {
    tone: 'grid',
    title: 'Hulp op afstand',
    text: 'Opnieuw uitlezen, de laatste foutmelding van het merk zien en de klant een bericht sturen dat als melding in de app binnenkomt.',
  },
  {
    tone: 'battery',
    title: 'Kansen per klant',
    text: 'Veel teruglevering: batterij. Auto ’s avonds laden: slim laden op zon. Je ziet het per klant, met wat het oplevert.',
  },
]

const vragen = [
  {
    vraag: 'Wat moet ik zelf doen om te beginnen?',
    antwoord:
      'Je meldt je bedrijf aan, wij keuren het goed, en dan krijg je een code. Die code geef je aan je klanten; zij maken er zelf een account mee aan. Je hoeft bij niemand langs en er komt geen kastje bij.',
  },
  {
    vraag: 'Welke merken kunnen jullie uitlezen?',
    antwoord:
      'Nu Growatt, SolaX, Sessy en de SolaX-laadpaal. Daarna HomeWizard, SolarEdge, Enphase, Zaptec en Easee. Plaats je vooral één merk dat er nog niet bij staat, zeg dat dan bij je aanmelding: wij bouwen op volgorde van wat er echt bij klanten hangt.',
  },
  {
    vraag: 'Zien mijn klanten mijn naam of die van Wattzicht?',
    antwoord:
      'Wattzicht blijft het merk van de app. Jouw bedrijfsnaam staat erbij als "jouw installateur", bij contact en bij storingen, zodat je klant weet wie hij moet bellen.',
  },
  {
    vraag: 'Kan ik bij de gegevens van mijn klanten?',
    antwoord:
      'Alleen als die klant dat aanzet. Hij ziet in zijn app wanneer jij hebt meegekeken, en kan die toestemming op elk moment weer intrekken. Zonder toestemming staat hij niet in jouw portal.',
  },
  {
    vraag: 'Wat kost het na de eerste vijftig klanten?',
    antwoord:
      'Dat spreken we met je af voordat er iets verandert. We bellen je ruim van tevoren; je krijgt geen factuur die je niet aan zag komen.',
  },
]
</script>

<template>
  <section class="top">
    <div class="wz-wrap top__grid">
      <div class="top__copy">
        <p class="wz-kicker">Voor installateurs</p>
        <h1>Al je klanten in <span class="c-sun">één overzicht</span></h1>
        <p class="wz-lead">
          Je plaatst panelen van het ene merk, een batterij van het andere en een laadpaal van een derde. Je klant gebruikt
          Wattzicht voor alles, met jou erin als vaste installateur. Jij ziet in het portal wat iedereen heeft, hoe het
          draait en waar je kunt helpen.
        </p>
        <div class="top__cta">
          <a href="#demo" class="wz-btn wz-btn--primary">Plan een demo</a>
          <RouterLink to="/portal/login" class="wz-btn wz-btn--ghost">Ik heb al een account</RouterLink>
        </div>
      </div>
      <div class="top__phone">
        <PhonePreview :name="featured.name" />
        <p class="top__caption">Wattzicht bij een klant van Triple Energie in Enschede.</p>
      </div>
    </div>
  </section>

  <section class="wz-section partners" aria-labelledby="partners-title">
    <div class="wz-wrap partners__row">
      <p id="partners-title" class="wz-kicker partners__label">Installateurs die met Wattzicht werken</p>
      <ul class="partners__list">
        <li v-for="p in partners" :key="p.name">
          <a :href="p.url" target="_blank" rel="noopener" class="partner">
            <span class="partner__plate">{{ p.name }}</span>
            <span class="partner__city">{{ p.city }}</span>
          </a>
        </li>
      </ul>
    </div>
  </section>

  <section class="wz-section">
    <div class="wz-wrap">
      <h2 class="h2-tight">Wat jij ermee kunt</h2>
      <div class="scenarios">
        <article v-for="s in scenarios" :key="s.title" class="scenario" :class="`scenario--${s.tone}`">
          <h3>{{ s.title }}</h3>
          <p>{{ s.text }}</p>
        </article>
      </div>
    </div>
  </section>

  <section class="wz-section">
    <div class="wz-wrap">
      <h2 class="h2-tight">Zo werkt het</h2>
      <ol class="steps">
        <li v-for="(s, i) in steps" :key="s.title">
          <span class="steps__n" aria-hidden="true">{{ i + 1 }}</span>
          <div>
            <h3>{{ s.title }}</h3>
            <p>{{ s.text }}</p>
          </div>
        </li>
      </ol>
    </div>
  </section>

  <section class="wz-section">
    <div class="wz-wrap price">
      <div class="price__copy">
        <h2>Wat het kost</h2>
        <p class="wz-lead">
          Je begint zonder risico. Pas als het werkt voor je klanten en voor jou, ga je betalen.
        </p>
      </div>
      <div class="price__card wz-panel">
        <div class="price__line">
          <span class="price__big">Gratis</span>
          <span>voor je eerste 50 klanten</span>
        </div>
        <div class="price__rule"></div>
        <p class="price__after">
          Daarna <b>€2 per actieve klant per maand</b>, of <b>€99 eenmalig</b> per klant via je eigen offerte. Een
          klant telt als actief zodra er een apparaat gekoppeld is.
        </p>
        <ul class="price__list">
          <li>Onbeperkt installaties in het portal</li>
          <li>Storingsmeldingen voor jou en je klant</li>
          <li>Jouw naam, telefoon en offerteknop bij elke klant</li>
          <li>Nieuwe merkkoppelingen zonder extra kosten</li>
        </ul>
      </div>
    </div>
  </section>

  <section class="wz-section" id="vragen">
    <div class="wz-wrap">
      <p class="wz-kicker">Vragen</p>
      <h2 class="h2-tight">Wat installateurs ons vragen</h2>
      <FaqList :items="vragen" class="vragen" />
    </div>
  </section>

  <section class="wz-section" id="demo">
    <div class="wz-wrap demo">
      <div class="demo__copy">
        <h2>Plan een demo</h2>
        <p class="wz-lead">
          In een half uur laten we het portal en de app zien, met jouw klanten als voorbeeld. Laat je gegevens achter en we
          bellen je binnen één werkdag.
        </p>
        <p class="demo__alt">
          Liever direct contact? <a href="mailto:hallo@wattzicht.nl">hallo@wattzicht.nl</a>.
        </p>
      </div>

      <form class="demo__form wz-panel" novalidate @submit.prevent="send">
        <div class="wz-field">
          <label for="f-name">Je naam</label>
          <InputText
            id="f-name"
            v-model="form.name"
            autocomplete="name"
            :invalid="touched.name && !form.name.trim()"
            @blur="touched.name = true"
          />
          <small v-if="touched.name && !form.name.trim()" class="err">Vul je naam in.</small>
        </div>
        <div class="wz-field">
          <label for="f-company">Bedrijf</label>
          <InputText
            id="f-company"
            v-model="form.company"
            autocomplete="organization"
            :invalid="touched.company && !form.company.trim()"
            @blur="touched.company = true"
          />
          <small v-if="touched.company && !form.company.trim()" class="err">Vul de naam van je bedrijf in.</small>
        </div>
        <div class="wz-field">
          <label for="f-email">E-mail</label>
          <InputText
            id="f-email"
            v-model="form.email"
            type="email"
            autocomplete="email"
            :invalid="touched.email && !emailOk"
            @blur="touched.email = true"
          />
          <small v-if="touched.email && !emailOk" class="err">Dit e-mailadres klopt niet.</small>
        </div>
        <div class="wz-field">
          <label for="f-phone">Telefoon <span class="opt">(mag leeg)</span></label>
          <InputText id="f-phone" v-model="form.phone" type="tel" autocomplete="tel" />
        </div>
        <div class="wz-field">
          <label for="f-count">Aantal klanten met zonnepanelen <span class="opt">(schatting)</span></label>
          <InputNumber id="f-count" v-model="form.customerCount" :min="0" :max="100000" :use-grouping="true" locale="nl-NL" />
        </div>

        <Message v-if="leads.state === 'sent'" severity="success" :closable="false">{{ leads.message }}</Message>
        <Message v-else-if="leads.state === 'failed'" severity="warn" :closable="false">{{ leads.message }}</Message>

        <button type="submit" class="wz-btn wz-btn--primary demo__submit" :disabled="leads.state === 'sending'">
          <i v-if="leads.state === 'sending'" class="pi pi-spin pi-spinner" aria-hidden="true"></i>
          {{ leads.state === 'sending' ? 'Versturen…' : 'Plan een demo' }}
        </button>
        <p class="wz-help">We gebruiken je gegevens alleen om deze demo te plannen. <RouterLink to="/privacy">Privacy</RouterLink></p>
      </form>
    </div>
  </section>
</template>

<style scoped>
.vragen {
  margin-top: 24px;
}

.top {
  padding-block: clamp(40px, 7vw, 96px) clamp(40px, 6vw, 72px);
  background:
    radial-gradient(ellipse 50% 60% at 90% 40%, color-mix(in srgb, var(--wz-accent-500) 12%, transparent), transparent 70%),
    var(--wz-bg);
}
.top__grid {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(0, 0.8fr);
  gap: clamp(32px, 6vw, 72px);
  align-items: center;
}
.top__copy {
  display: grid;
  gap: 20px;
  justify-items: start;
}
.top__cta {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
.top__caption {
  text-align: center;
  color: var(--wz-text-soft);
  font-size: 0.9rem;
  margin-top: 16px;
}

.partners {
  padding-block: 32px;
}
.partners__row {
  display: flex;
  align-items: center;
  gap: 24px 40px;
  flex-wrap: wrap;
}
.partners__label {
  margin: 0;
}
.partners__list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
}
.partner {
  display: flex;
  align-items: center;
  gap: 12px;
  text-decoration: none;
  color: var(--wz-text-soft);
  font-size: 0.9rem;
}
.partner__plate {
  /* Alleen de naam: Wattzicht blijft het merk, een installateurslogo zou een tweede merk zijn. */
  display: inline-flex;
  align-items: center;
  min-height: 44px;
  padding: 8px 16px;
  border-radius: var(--wz-radius-pill);
  border: 1px solid var(--wz-border);
  background: var(--wz-surface);
  font-weight: 600;
}
.partner__plate img {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
}
.partner:hover .partner__plate {
  outline: 2px solid var(--wz-accent-500);
}

.scenarios {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}
.scenario {
  --c: var(--wz-accent-500);
  padding: 24px;
  border-radius: var(--wz-radius-panel);
  background: var(--wz-surface);
  border: 1px solid var(--wz-line);
  display: grid;
  gap: 8px;
  align-content: start;
}
.scenario::before {
  content: '';
  width: 28px;
  height: 4px;
  border-radius: 2px;
  background: var(--c);
}
.scenario p {
  color: var(--wz-text-soft);
}
.scenario--danger { --c: var(--wz-error-text); }
.scenario--grid { --c: var(--wz-energy-grid); }
.scenario--battery { --c: var(--wz-energy-battery); }

.h2-tight {
  margin-bottom: clamp(24px, 4vw, 40px);
}
.steps {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 28px;
}
.steps li {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 16px;
  align-items: start;
}
.steps__n {
  width: 38px;
  height: 38px;
  border-radius: 999px;
  display: grid;
  place-items: center;
  font-family: var(--wz-font-display);
  font-weight: 800;
  background: var(--wz-accent-500);
  color: var(--wz-navy-900);
}
.steps h3 {
  margin-bottom: 6px;
}
.steps p {
  color: var(--wz-text-soft);
}

.price {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.1fr);
  gap: clamp(28px, 5vw, 64px);
  align-items: start;
}
.price__copy {
  display: grid;
  gap: 14px;
}
.price__card {
  padding: clamp(24px, 4vw, 40px);
  display: grid;
  gap: 18px;
}
.price__line {
  display: flex;
  align-items: baseline;
  gap: 14px;
  flex-wrap: wrap;
  color: var(--wz-text-soft);
  font-size: 1.1rem;
}
.price__big {
  font-size: clamp(2.4rem, 5vw, 3.4rem);
  font-weight: 800;
  letter-spacing: -0.03em;
  color: var(--wz-heading);
}
.price__rule {
  height: 1px;
  background: var(--wz-line);
}
.price__after b {
  color: var(--wz-heading);
}
.price__list {
  margin: 0;
  padding: 0;
  list-style: none;
  display: grid;
  gap: 10px;
  color: var(--wz-text-soft);
}
.price__list li::before {
  content: '';
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 999px;
  background: var(--wz-energy-battery);
  margin-right: 12px;
  vertical-align: middle;
}

.demo {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.1fr);
  gap: clamp(28px, 5vw, 64px);
  align-items: start;
}
.demo__copy {
  display: grid;
  gap: 14px;
}
.demo__alt {
  color: var(--wz-text-soft);
}
.demo__alt a {
  color: var(--wz-text);
  font-weight: 600;
}
.demo__form {
  padding: clamp(22px, 4vw, 36px);
  display: grid;
  gap: 18px;
}
.demo__submit {
  justify-self: start;
}
.opt {
  font-weight: 400;
  color: var(--wz-text-soft);
}
.err {
  color: var(--wz-error-text);
  font-size: 0.9rem;
}

@media (max-width: 900px) {
  .top__grid,
  .price,
  .demo {
    grid-template-columns: 1fr;
  }
  .steps,
  .scenarios {
    grid-template-columns: 1fr;
  }
  .top__phone {
    order: -1;
  }
}
</style>
