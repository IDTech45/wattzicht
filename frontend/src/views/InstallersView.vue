<script setup lang="ts">
import { reactive, computed } from 'vue'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Message from 'primevue/message'
import PhonePreview from '@/components/PhonePreview.vue'
import { useLeadsStore } from '@/stores/leads'

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
    title: 'Jij zet je huisstijl klaar',
    text: 'Naam, logo, kleur en de link naar je offerteformulier. Kwartiertje werk in het portal.',
  },
  {
    title: 'Je klant koppelt z’n apparaten',
    text: 'De app vraagt om de token of inloggegevens van de omvormer, batterij of laadpaal. Daarna loopt alles vanzelf.',
  },
  {
    title: 'Jij ziet alles in één overzicht',
    text: 'Welke installaties online zijn, welke een storing hebben en wie veel teruglevert. Met een melding als het misgaat.',
  },
]
</script>

<template>
  <section class="top">
    <div class="wz-wrap top__grid">
      <div class="top__copy">
        <p class="wz-kicker">Voor installateurs</p>
        <h1>De app die je klanten openen draagt jouw naam</h1>
        <p class="wz-lead">
          Je plaatst panelen van het ene merk, een batterij van het andere en een laadpaal van een derde. Je klant krijgt
          drie apps en belt jou als er iets niet klopt. Wattzicht brengt het samen in één app, met jouw logo erop.
        </p>
        <div class="top__cta">
          <a href="#demo" class="wz-btn wz-btn--primary">Plan een demo</a>
          <RouterLink to="/portal/login" class="wz-btn wz-btn--ghost">Ik heb al een account</RouterLink>
        </div>
      </div>
      <div class="top__phone">
        <PhonePreview name="Triple Energie" primary-color="#0E7490" />
        <p class="top__caption">Zo ziet de app eruit voor klanten van Triple Energie in Enschede.</p>
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
          <li>Jouw huisstijl in de app, zonder meerprijs</li>
          <li>Nieuwe merkkoppelingen zonder extra kosten</li>
        </ul>
      </div>
    </div>
  </section>

  <section class="wz-section" id="demo">
    <div class="wz-wrap demo">
      <div class="demo__copy">
        <h2>Plan een demo</h2>
        <p class="wz-lead">
          In een half uur laten we het portal en de app zien, met jouw logo erin. Laat je gegevens achter en we bellen je
          binnen één werkdag.
        </p>
        <p class="demo__alt">
          Liever direct contact? <a href="mailto:hallo@wattzicht.nl">hallo@wattzicht.nl</a> of
          <a href="tel:+31531234567">053 – 123 45 67</a>.
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
.top {
  padding-block: clamp(40px, 7vw, 96px) clamp(40px, 6vw, 72px);
  background:
    radial-gradient(ellipse 50% 60% at 90% 40%, color-mix(in srgb, var(--wz-sun) 12%, transparent), transparent 70%),
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
  font-weight: 800;
  background: var(--wz-sun);
  color: var(--wz-night);
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
  color: var(--wz-brand-fg);
}
.price__rule {
  height: 1px;
  background: var(--wz-line);
}
.price__after b {
  color: var(--wz-brand-fg);
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
  background: var(--wz-battery);
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
  color: var(--wz-danger);
  font-size: 0.9rem;
}

@media (max-width: 900px) {
  .top__grid,
  .price,
  .demo {
    grid-template-columns: 1fr;
  }
  .steps {
    grid-template-columns: 1fr;
  }
  .top__phone {
    order: -1;
  }
}
</style>
