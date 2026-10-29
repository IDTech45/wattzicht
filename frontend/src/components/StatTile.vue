<script setup lang="ts">
/**
 * Compacte waarde-tegel. `tone` kleurt het getal met de vaste energiestroomkleur.
 */
defineProps<{
  label: string
  value: string | number
  unit?: string
  hint?: string
  tone?: 'sun' | 'battery' | 'grid' | 'export' | 'danger' | 'neutral'
}>()
</script>

<template>
  <div class="tile" :class="`tile--${tone ?? 'neutral'}`">
    <span class="tile__label">{{ label }}</span>
    <span class="tile__value">
      {{ value }}<small v-if="unit" class="tile__unit">{{ unit }}</small>
    </span>
    <span v-if="hint" class="tile__hint">{{ hint }}</span>
  </div>
</template>

<style scoped>
.tile {
  --c: var(--wz-text);
  display: grid;
  gap: 2px;
  padding: 16px 18px;
  border-radius: var(--wz-radius-m);
  background: var(--wz-surface);
  border: 1px solid var(--wz-line);
  border-left: 4px solid var(--c);
  min-width: 0;
}
.tile--sun { --c: var(--wz-sun); }
.tile--battery { --c: var(--wz-battery); }
.tile--grid { --c: var(--wz-grid); }
.tile--export { --c: var(--wz-export); }
.tile--danger { --c: var(--wz-danger); }
.tile--neutral { --c: var(--wz-line-strong); }

.tile__label {
  font-size: 0.9rem;
  color: var(--wz-text-soft);
}
.tile__value {
  font-size: 1.7rem;
  font-weight: 800;
  letter-spacing: -0.02em;
  font-variant-numeric: tabular-nums;
  line-height: 1.1;
  color: var(--wz-brand-fg);
}
.tile__unit {
  font-size: 0.95rem;
  font-weight: 600;
  margin-left: 4px;
  color: var(--wz-text-soft);
}
.tile__hint {
  font-size: 0.85rem;
  color: var(--wz-text-soft);
}
</style>
