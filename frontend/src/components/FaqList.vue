<script setup lang="ts">
/**
 * Veelgestelde vragen.
 *
 * Met details/summary en niet met eigen klik-afhandeling: dan werkt openklappen met het toetsenbord,
 * kondigt een schermlezer de open of dichte staat aan, en vindt de zoekfunctie van de browser ook
 * tekst in een dichtgeklapt antwoord. Dat laatste scheelt bij een FAQ meer dan het eruitziet.
 */
defineProps<{
  items: { vraag: string; antwoord: string }[]
}>()
</script>

<template>
  <div class="faq">
    <details v-for="item in items" :key="item.vraag" class="faq__item">
      <summary class="faq__vraag">
        <span>{{ item.vraag }}</span>
        <svg class="faq__pijl" width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
          <path d="M3 6l5 5 5-5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
        </svg>
      </summary>
      <p class="faq__antwoord">{{ item.antwoord }}</p>
    </details>
  </div>
</template>

<style scoped>
.faq {
  display: grid;
  gap: 10px;
  max-width: 70ch;
}

.faq__item {
  border: 1px solid var(--wz-border);
  border-radius: var(--wz-radius-card);
  background: var(--wz-surface);
}

.faq__vraag {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 18px;
  min-height: 48px;
  font-weight: 600;
  cursor: pointer;
  list-style: none;
}

.faq__vraag::-webkit-details-marker {
  display: none;
}

.faq__vraag:focus-visible {
  outline: 3px solid var(--wz-focus);
  outline-offset: -3px;
  border-radius: var(--wz-radius-card);
}

.faq__pijl {
  flex: none;
  color: var(--wz-text-soft);
  transition: transform 150ms ease;
}

.faq__item[open] .faq__pijl {
  transform: rotate(180deg);
}

.faq__antwoord {
  padding: 0 18px 18px;
  color: var(--wz-text-soft);
  max-width: 65ch;
}

@media (prefers-reduced-motion: reduce) {
  .faq__pijl {
    transition: none;
  }
}
</style>
