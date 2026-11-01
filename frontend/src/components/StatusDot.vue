<script setup lang="ts">
import { computed } from 'vue'
import type { HouseholdStatus } from '@/api/client'

const props = defineProps<{ status: HouseholdStatus; label?: boolean }>()

const text = computed(() => {
  switch (props.status) {
    case 'online':
      return 'Online'
    case 'fault':
      return 'Storing'
    default:
      return 'Geen data'
  }
})
</script>

<template>
  <span class="dot" :class="`dot--${status}`">
    <span class="dot__mark" aria-hidden="true"></span>
    <span v-if="label !== false">{{ text }}</span>
    <span v-else class="sr-only">{{ text }}</span>
  </span>
</template>

<style scoped>
.dot {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-weight: 500;
  white-space: nowrap;
}
.dot__mark {
  width: 10px;
  height: 10px;
  border-radius: 999px;
  background: var(--c);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--c) 22%, transparent);
}
.dot--online {
  --c: var(--wz-energy-battery);
}
.dot--fault {
  --c: var(--wz-error-text);
}
.dot--nodata {
  --c: var(--wz-text-soft);
}
.dot--fault .dot__mark {
  animation: pulse 1.6s ease-in-out infinite;
}
@keyframes pulse {
  0%,
  100% {
    box-shadow: 0 0 0 3px color-mix(in srgb, var(--c) 22%, transparent);
  }
  50% {
    box-shadow: 0 0 0 6px color-mix(in srgb, var(--c) 12%, transparent);
  }
}
</style>
