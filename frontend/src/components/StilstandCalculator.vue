<script setup lang="ts">
/**
 * Wat kost het als je niet ziet dat er iets stilstaat.
 *
 * LaadCash heeft een rekentool in de hero die uitrekent wat je laadpaal opbrengt. Voor Wattzicht is
 * dat niet eerlijk na te maken: inzicht levert op zichzelf geen euro's op, en een tool die een
 * besparing belooft die wij niet kunnen waarmaken is precies het soort getal waar klanten later boos
 * over worden.
 *
 * Wat wél een echt getal is: wat een storing kost die niemand opmerkt. Een omvormer die uitvalt merk
 * je pas op de jaarafrekening, en dat is de reden dat de app bestaat. De rekensom is openlijk:
 * piekvermogen maal de Nederlandse vuistregel van 900 kWh per kWp per jaar, gedeeld door twaalf, maal
 * je eigen stroomprijs. Die vuistregel en de aanname staan er in gewone taal bij.
 */
import { computed, ref } from 'vue'

/** Nederlandse vuistregel: een kWp levert ongeveer 900 kWh per jaar. */
const KWH_PER_KWP_PER_JAAR = 900

const kwp = ref(4)
const prijs = ref(0.3)

const perMaand = computed(() => (kwp.value * KWH_PER_KWP_PER_JAAR) / 12)
const kostenPerMaand = computed(() => perMaand.value * prijs.value)

const euro = (n: number) =>
  new Intl.NumberFormat('nl-NL', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 }).format(n)

const getal = (n: number) => new Intl.NumberFormat('nl-NL', { maximumFractionDigits: 0 }).format(n)
</script>

<template>
  <div class="calc wz-card">
    <h2 class="calc__kop">Wat kost een maand stilstand?</h2>
    <p class="calc__lead">
      Een omvormer die ermee stopt merk je meestal pas op de jaarafrekening. Reken uit wat die maanden
      kosten.
    </p>

    <div class="calc__velden">
      <div class="calc__veld">
        <label for="calc-kwp">Vermogen van je panelen</label>
        <div class="calc__invoer">
          <input id="calc-kwp" v-model.number="kwp" type="number" min="0.5" max="50" step="0.5" />
          <span class="calc__eenheid">kWp</span>
        </div>
      </div>

      <div class="calc__veld">
        <label for="calc-prijs">Jouw stroomprijs</label>
        <div class="calc__invoer">
          <span class="calc__eenheid calc__eenheid--voor">€</span>
          <input id="calc-prijs" v-model.number="prijs" type="number" min="0.05" max="2" step="0.01" />
          <span class="calc__eenheid">/ kWh</span>
        </div>
      </div>
    </div>

    <div class="calc__uitkomst">
      <div>
        <span class="calc__label">Misgelopen per maand</span>
        <strong class="calc__bedrag wz-num">{{ euro(kostenPerMaand) }}</strong>
      </div>
      <p class="calc__onder">
        Dat is ongeveer <span class="wz-num">{{ getal(perMaand) }}</span> kWh die je niet opwekt.
      </p>
    </div>

    <p class="calc__voet">
      Gerekend met de vuistregel van 900 kWh per kWp per jaar, gelijk verdeeld over het jaar. In de
      zomer ligt het hoger en in de winter lager, dus een storing in juni kost meer dan dit en een
      storing in december minder.
    </p>
  </div>
</template>

<style scoped>
.calc {
  padding: 22px;
  display: grid;
  gap: 14px;
}

.calc__kop {
  font-size: 1.25rem;
  margin: 0;
}

.calc__lead {
  color: var(--wz-text-soft);
  font-size: 0.95rem;
}

.calc__velden {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 14px;
}

.calc__veld {
  display: grid;
  gap: 5px;
}

.calc__veld label {
  font-weight: 600;
  font-size: 0.9rem;
}

.calc__invoer {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 0 12px;
  min-height: 48px;
  border: 1px solid var(--wz-border-strong);
  border-radius: var(--wz-radius-input);
  background: var(--wz-surface);
}

.calc__invoer input {
  border: 0;
  background: none;
  color: inherit;
  font-family: var(--wz-font-mono);
  font-size: 1rem;
  width: 100%;
  min-width: 0;
  padding: 12px 0;
}

.calc__invoer input:focus {
  outline: none;
}

.calc__invoer:focus-within {
  border-color: var(--wz-accent-500);
  box-shadow: var(--wz-focus-ring);
}

.calc__eenheid {
  color: var(--wz-text-soft);
  font-size: 0.9rem;
  white-space: nowrap;
}

.calc__eenheid--voor {
  order: -1;
}

.calc__uitkomst {
  background: var(--wz-teal-800);
  color: #fff;
  border-radius: var(--wz-radius-panel);
  padding: 16px 18px;
  display: grid;
  gap: 4px;
}

.calc__label {
  display: block;
  color: var(--wz-text-ondark-muted);
  font-size: 0.85rem;
}

.calc__bedrag {
  font-size: clamp(1.9rem, 5vw, 2.4rem);
  line-height: 1.1;
  color: var(--wz-accent-500);
}

.calc__onder {
  color: var(--wz-text-ondark-muted);
  font-size: 0.9rem;
}

.calc__voet {
  color: var(--wz-text-soft);
  font-size: 0.82rem;
}

/*
  In dark mode is de kaart zelf al teal-800, dus dan valt het uitkomstvlak erin weg. Daar wordt het
  navy met een rand: nog steeds een apart blok, zonder een kleur te gebruiken die niet in de tokens staat.
*/
@media (prefers-color-scheme: dark) {
  .calc__uitkomst {
    background: var(--wz-navy-900);
    border: 1px solid var(--wz-border);
  }
}
</style>
