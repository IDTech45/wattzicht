<script setup lang="ts">
import EnergyFlowIllustration from '@/components/EnergyFlowIllustration.vue'
import BrandBadge from '@/components/BrandBadge.vue'

const brands = [
  { name: 'Growatt', kind: 'Omvormer', connected: true },
  { name: 'SolaX', kind: 'Hybride omvormer', connected: true },
  { name: 'Sessy', kind: 'Thuisbatterij', connected: true },
  { name: 'SolarEdge', kind: 'Omvormer', connected: false },
  { name: 'Enphase', kind: 'Micro-omvormers', connected: false },
  { name: 'Deye', kind: 'Hybride omvormer', connected: false },
  { name: 'SolaX laadpaal', kind: 'Laadpaal (MID)', connected: true },
  { name: 'Alfen', kind: 'Laadpaal', connected: false },
  { name: 'Zaptec', kind: 'Laadpaal', connected: false },
  { name: 'HomeWizard', kind: 'P1-meter', connected: false },
]

const features = [
  {
    title: 'Energiestroom nu',
    text: 'Zon, huis, batterij en net in één beeld. Je ziet per seconde waar je stroom heen gaat, ook als je apparaten van drie verschillende merken hebt.',
    tone: 'sun',
    icon: 'pi-bolt',
  },
  {
    title: 'Bespaard in euro’s',
    text: 'Geen kilowatturen die je moet omrekenen. Je ziet per dag, maand en jaar wat je panelen, batterij en laadpaal je hebben opgeleverd, met jouw tarief.',
    tone: 'battery',
    icon: 'pi-euro',
  },
  {
    title: 'Melding bij storing',
    text: 'Levert je omvormer niets terwijl de zon schijnt, of is je batterij al uren offline? Je krijgt een melding voordat je het op de jaarafrekening merkt.',
    tone: 'danger',
    icon: 'pi-bell',
  },
  {
    title: 'Slim sturen',
    text: 'Wattzicht kijkt naar de stroomprijs per uur en je eigen zon, en zegt wanneer je de auto laadt of de wasmachine aanzet. Met de besparing erbij in euro’s.',
    tone: 'sun',
    icon: 'pi-sliders-h',
  },
]

</script>

<template>
  <!-- Hero -->
  <section class="hero">
    <div class="wz-wrap hero__grid">
      <div class="hero__copy">
        <h1>Eén app voor je zonnepanelen, thuisbatterij en laadpaal</h1>
        <p class="wz-lead hero__lead">
          Welk merk je ook hebt. Zie live wat je opwekt, gebruikt en bespaart.
        </p>
        <div class="hero__cta">
          <RouterLink to="/app" class="wz-btn wz-btn--primary">Vraag het je installateur</RouterLink>
          <RouterLink to="/installateurs" class="wz-btn wz-btn--ghost">Voor installateurs</RouterLink>
        </div>
        <p class="hero__note">
          Je installateur zet Wattzicht voor je klaar. Zelf proberen kan ook: download de app en koppel je omvormer in
          twee minuten.
        </p>
      </div>
      <div class="hero__art">
        <EnergyFlowIllustration />
      </div>
    </div>
  </section>

  <!-- Werkt met -->
  <section class="wz-section" id="werkt-met">
    <div class="wz-wrap">
      <div class="section-head">
        <h2>Werkt met wat er al op je dak en in je meterkast zit</h2>
        <p class="wz-lead">
          Elk merk heeft z’n eigen app. Wattzicht legt ze naast elkaar in één scherm. We beginnen met de merken die
          onze installateurs het meest plaatsen en breiden elke maand uit.
        </p>
      </div>
      <div class="brands">
        <BrandBadge v-for="b in brands" :key="b.name" v-bind="b" />
      </div>
      <p class="brands__foot">
        Mis je een merk? <a href="mailto:hallo@wattzicht.nl?subject=Merk%20toevoegen">Laat het ons weten</a>, dan zetten we het
        op de lijst.
      </p>
    </div>
  </section>

  <!-- Wat je ziet -->
  <section class="wz-section" id="wat-je-ziet">
    <div class="wz-wrap">
      <div class="section-head">
        <h2>Wat je ziet</h2>
        <p class="wz-lead">Drie dingen die elke app voor zonnepanelen zou moeten laten zien, en bijna geen enkele doet.</p>
      </div>
      <div class="features">
        <article v-for="f in features" :key="f.title" class="feature" :class="`feature--${f.tone}`">
          <span class="feature__icon" aria-hidden="true"><i class="pi" :class="f.icon"></i></span>
          <h3>{{ f.title }}</h3>
          <p>{{ f.text }}</p>
        </article>
      </div>
    </div>
  </section>

  <!-- Na 2027 -->
  <section class="wz-section after" id="na-2027">
    <div class="wz-wrap after__grid">
      <div>
        <h2>Na 2027 telt elke kilowattuur die je zelf gebruikt</h2>
        <p class="wz-lead">
          Op 1 januari 2027 stopt de salderingsregeling. Stroom die je teruglevert aan het net levert dan nog maar een
          paar cent op, terwijl je voor stroom die je afneemt het volle tarief betaalt.
        </p>
      </div>
      <div class="after__points">
        <div class="after__point">
          <span class="after__bar" style="--c: var(--wz-export)"></span>
          <div>
            <h3>Zie hoeveel stroom het net op gaat</h3>
            <p>
              Wattzicht laat per dag zien welk deel van je opwek je zelf gebruikt en welk deel je weggeeft. Dat getal
              bepaalt of een thuisbatterij voor jou uit kan.
            </p>
          </div>
        </div>
        <div class="after__point">
          <span class="after__bar" style="--c: var(--wz-battery)"></span>
          <div>
            <h3>Reken met je eigen tarief</h3>
            <p>
              Vast of dynamisch contract, met of zonder terugleververgoeding: je stelt het één keer in en de besparing
              klopt vanaf dat moment.
            </p>
          </div>
        </div>
        <div class="after__point">
          <span class="after__bar" style="--c: var(--wz-sun)"></span>
          <div>
            <h3>Verschuif verbruik naar de zon</h3>
            <p>Zie wanneer je overschot hebt, zodat je de wasmachine, warmtepomp of auto op dat moment laat draaien.</p>
          </div>
        </div>
      </div>
    </div>
  </section>

</template>

<style scoped>
/* Hero */
.hero {
  padding-block: clamp(40px, 7vw, 96px) clamp(48px, 7vw, 88px);
  background:
    radial-gradient(ellipse 60% 50% at 85% 20%, color-mix(in srgb, var(--wz-sun) 14%, transparent), transparent 70%),
    var(--wz-bg);
}
.hero__grid {
  display: grid;
  grid-template-columns: minmax(0, 1.05fr) minmax(0, 1fr);
  gap: clamp(32px, 6vw, 72px);
  align-items: center;
}
.hero__copy {
  display: grid;
  gap: 22px;
  justify-items: start;
}
.hero__lead {
  font-size: 1.3rem;
}
.hero__cta {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
.hero__note {
  font-size: 0.95rem;
  color: var(--wz-text-soft);
  max-width: 48ch;
}
.hero__art {
  padding: 16px;
}

/* Sectiekoppen */
.section-head {
  display: grid;
  gap: 14px;
  max-width: 64ch;
  margin-bottom: clamp(28px, 4vw, 48px);
}

/* Merken */
.brands {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
}
.brands__foot {
  margin-top: 22px;
  color: var(--wz-text-soft);
}
.brands__foot a {
  color: var(--wz-text);
  font-weight: 600;
}

/* Kenmerken */
.features {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18px;
}
.feature {
  --c: var(--wz-sun);
  display: grid;
  gap: 12px;
  align-content: start;
  padding: 28px;
  border-radius: var(--wz-radius-l);
  background: var(--wz-surface);
  border: 1px solid var(--wz-line);
}
.feature--battery { --c: var(--wz-battery); }
.feature--danger { --c: var(--wz-danger); }
.feature__icon {
  width: 46px;
  height: 46px;
  border-radius: 14px;
  display: grid;
  place-items: center;
  background: color-mix(in srgb, var(--c) 16%, transparent);
  color: var(--c);
  font-size: 1.15rem;
  margin-bottom: 6px;
}
.feature p {
  color: var(--wz-text-soft);
}

/* Na 2027 */
.after__grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.2fr);
  gap: clamp(32px, 6vw, 80px);
  align-items: start;
}
.after__grid h2 {
  margin-bottom: 16px;
}
.after__points {
  display: grid;
  gap: 26px;
}
.after__point {
  display: grid;
  grid-template-columns: 6px 1fr;
  gap: 18px;
}
.after__bar {
  border-radius: 999px;
  background: var(--c);
}
.after__point h3 {
  margin-bottom: 6px;
}
.after__point p {
  color: var(--wz-text-soft);
}


@media (max-width: 980px) {
  .brands {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
  .features {
    grid-template-columns: 1fr;
  }
}
@media (max-width: 820px) {
  .hero__grid,
  .after__grid {
    grid-template-columns: 1fr;
  }
  .hero__art {
    order: -1;
    padding: 0;
    max-width: 480px;
    margin-inline: auto;
  }
}
</style>
