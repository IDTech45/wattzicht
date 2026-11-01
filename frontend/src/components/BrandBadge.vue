<script setup lang="ts">
/**
 * Merknaam als tekstbadge. `connected` = nu al gekoppeld, anders "binnenkort".
 */
defineProps<{
  name: string
  kind: string
  connected?: boolean
}>()
</script>

<template>
  <div class="badge" :class="{ 'is-connected': connected }">
    <span class="badge__name">{{ name }}</span>
    <span class="badge__meta">
      <span class="badge__kind">{{ kind }}</span>
      <span class="badge__state">
        <i :class="connected ? 'pi pi-check' : 'pi pi-clock'" aria-hidden="true"></i>
        {{ connected ? 'Gekoppeld' : 'Binnenkort' }}
      </span>
    </span>
  </div>
</template>

<style scoped>
.badge {
  display: grid;
  gap: 8px;
  padding: 18px 20px;
  border-radius: var(--wz-radius-panel);
  border: 1.5px dashed var(--wz-line-strong);
  background: transparent;
  color: var(--wz-text-soft);
}
.badge.is-connected {
  border-style: solid;
  border-color: var(--wz-line);
  background: var(--wz-surface);
  color: var(--wz-text);
}
.badge__name {
  font-size: 1.25rem;
  font-weight: 800;
  letter-spacing: -0.02em;
}
/*
 * Merknaam en status staan naast elkaar zolang dat past. Op een telefoon paste het niet: het raster
 * houdt daar twee kolommen aan, en in zo'n kaart van nog geen 180px liggen "Hybride omvormer" en
 * "Gekoppeld" niet naast elkaar. Zonder afbreekruimte liep de status de kaart uit, dwars over de
 * buurkaart heen (gezien op een Android-telefoon, 31-10-2026). Vandaar wrap en min-width: 0; zonder
 * dat laatste weigert een flex-item smaller te worden dan zijn langste woord.
 */
.badge__meta {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 4px 8px;
  font-size: 0.85rem;
  min-width: 0;
}
.badge__kind {
  min-width: 0;
}
.badge__state {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-weight: 600;
}
.is-connected .badge__state {
  color: var(--wz-energy-battery);
}
.badge__state .pi {
  font-size: 0.75rem;
}

/* Op een smal scherm onder elkaar: twee regels in een smalle kaart lezen prettiger dan afgeknepen
   tekst op een regel, en de status blijft binnen zijn eigen kaart. */
@media (max-width: 560px) {
  .badge {
    padding: 14px 16px;
  }
  .badge__name {
    font-size: 1.1rem;
  }
  .badge__meta {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
