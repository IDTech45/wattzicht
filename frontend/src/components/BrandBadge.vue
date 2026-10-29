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
  border-radius: var(--wz-radius-m);
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
.badge__meta {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  font-size: 0.85rem;
}
.badge__state {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-weight: 600;
}
.is-connected .badge__state {
  color: var(--wz-battery);
}
.badge__state .pi {
  font-size: 0.75rem;
}
</style>
