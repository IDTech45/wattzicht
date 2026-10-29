<script setup lang="ts">
import PhonePreview from '@/components/PhonePreview.vue'

// Store-links zijn nog placeholders tot de apps live staan.
const APP_STORE_URL = '#'
const PLAY_STORE_URL = '#'

const guides = [
  {
    brand: 'Growatt',
    kind: 'Omvormer',
    connected: true,
    steps: [
      'Open de ShinePhone-app en ga naar Ik → API Token.',
      'Maak een token aan en kopieer die.',
      'Plak de token in Wattzicht bij Apparaat toevoegen → Growatt.',
    ],
  },
  {
    brand: 'Sessy',
    kind: 'Thuisbatterij',
    connected: true,
    steps: [
      'Zoek de gebruikersnaam en het wachtwoord op de sticker van je Sessy-dongle.',
      'Vul ze in bij Apparaat toevoegen → Sessy.',
      'Je batterij verschijnt binnen vijf minuten met laadstatus.',
    ],
  },
  {
    brand: 'Deye / Solarman',
    kind: 'Hybride omvormer',
    connected: false,
    steps: ['Koppeling via je Solarman-account.', 'Beschikbaar in de volgende release.'],
  },
  {
    brand: 'Alfen en Zaptec',
    kind: 'Laadpaal',
    connected: false,
    steps: ['Koppeling via je laadpaalaccount of OCPP.', 'We testen dit nu met de eerste installateurs.'],
  },
  {
    brand: 'HomeWizard',
    kind: 'P1-meter',
    connected: false,
    steps: ['Lokale uitlezing via de P1-meter in je meterkast.', 'Komt zodra lokale uitlezing in de app zit.'],
  },
]
</script>

<template>
  <section class="top">
    <div class="wz-wrap top__grid">
      <div class="top__copy">
        <h1>Download de app</h1>
        <p class="wz-lead">
          Gratis voor iOS en Android. Heeft je installateur Wattzicht voor je klaargezet? Log dan in met het e-mailadres
          dat je van hen kreeg. Anders maak je zelf een account.
        </p>
        <div class="stores">
          <a :href="APP_STORE_URL" class="store" aria-label="Download in de App Store (binnenkort)">
            <i class="pi pi-apple" aria-hidden="true"></i>
            <span><small>Download in de</small><b>App Store</b></span>
          </a>
          <a :href="PLAY_STORE_URL" class="store" aria-label="Ontdek het op Google Play (binnenkort)">
            <i class="pi pi-google" aria-hidden="true"></i>
            <span><small>Ontdek het op</small><b>Google Play</b></span>
          </a>
        </div>
        <p class="top__note">De apps staan nog niet in de stores. Laat je e-mailadres achter bij je installateur, dan krijg je bericht.</p>
      </div>
      <div class="top__phone">
        <PhonePreview />
      </div>
    </div>
  </section>

  <section class="wz-section">
    <div class="wz-wrap">
      <div class="head">
        <h2>Zo koppel je je apparaten</h2>
        <p class="wz-lead">
          Per merk is het net even anders. Je doet dit één keer; daarna leest Wattzicht elke vijf minuten je gegevens
          uit.
        </p>
      </div>
      <div class="guides">
        <article v-for="g in guides" :key="g.brand" class="guide wz-panel" :class="{ 'guide--soon': !g.connected }">
          <header class="guide__head">
            <div>
              <h3>{{ g.brand }}</h3>
              <span class="guide__kind">{{ g.kind }}</span>
            </div>
            <span class="guide__state">{{ g.connected ? 'Beschikbaar' : 'Binnenkort' }}</span>
          </header>
          <ol class="guide__steps">
            <li v-for="s in g.steps" :key="s">{{ s }}</li>
          </ol>
        </article>
      </div>
    </div>
  </section>
</template>

<style scoped>
.top {
  padding-block: clamp(40px, 7vw, 96px) clamp(40px, 6vw, 72px);
}
.top__grid {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(0, 0.8fr);
  gap: clamp(32px, 6vw, 72px);
  align-items: center;
}
.top__copy {
  display: grid;
  gap: 22px;
  justify-items: start;
}
.top__note {
  font-size: 0.95rem;
  color: var(--wz-text-soft);
  max-width: 50ch;
}
.stores {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
.store {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  padding: 10px 20px 10px 16px;
  border-radius: 14px;
  background: var(--wz-night);
  color: #fff;
  text-decoration: none;
  min-width: 190px;
  border: 1.5px solid transparent;
  transition: transform 120ms ease;
}
.store:hover {
  transform: translateY(-1px);
}
@media (prefers-color-scheme: dark) {
  .store {
    background: #fff;
    color: var(--wz-night);
  }
}
.store .pi {
  font-size: 1.6rem;
}
.store span {
  display: grid;
  line-height: 1.15;
}
.store small {
  font-size: 0.72rem;
  opacity: 0.8;
}
.store b {
  font-size: 1.15rem;
  font-weight: 700;
}

.head {
  display: grid;
  gap: 14px;
  max-width: 64ch;
  margin-bottom: clamp(24px, 4vw, 40px);
}
.guides {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(290px, 1fr));
  gap: 16px;
}
.guide {
  padding: 24px;
  display: grid;
  gap: 16px;
  align-content: start;
}
.guide--soon {
  background: transparent;
  border-style: dashed;
}
.guide__head {
  display: flex;
  justify-content: space-between;
  align-items: start;
  gap: 12px;
}
.guide__kind {
  font-size: 0.9rem;
  color: var(--wz-text-soft);
}
.guide__state {
  font-size: 0.8rem;
  font-weight: 700;
  padding: 4px 10px;
  border-radius: 999px;
  background: color-mix(in srgb, var(--wz-battery) 16%, transparent);
  color: var(--wz-battery);
  white-space: nowrap;
}
.guide--soon .guide__state {
  background: var(--wz-surface-2);
  color: var(--wz-text-soft);
}
.guide__steps {
  margin: 0;
  padding-left: 20px;
  display: grid;
  gap: 8px;
  color: var(--wz-text-soft);
}
.guide__steps li::marker {
  font-weight: 700;
  color: var(--wz-text);
}

@media (max-width: 820px) {
  .top__grid {
    grid-template-columns: 1fr;
  }
}
</style>
