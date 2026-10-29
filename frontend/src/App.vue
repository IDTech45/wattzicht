<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import Toast from 'primevue/toast'
import SiteNav from '@/components/SiteNav.vue'
import SiteFooter from '@/components/SiteFooter.vue'

const route = useRoute()
const isPortal = computed(() => route.meta.layout === 'portal')
</script>

<template>
  <a class="skip" href="#inhoud">Naar de inhoud</a>
  <SiteNav v-if="!isPortal" />
  <main id="inhoud">
    <RouterView />
  </main>
  <SiteFooter v-if="!isPortal" />
  <Toast position="bottom-right" />
</template>

<style scoped>
.skip {
  position: absolute;
  left: 16px;
  top: -60px;
  z-index: 100;
  padding: 10px 14px;
  background: var(--wz-sun);
  color: var(--wz-night);
  border-radius: var(--wz-radius-s);
  font-weight: 600;
  text-decoration: none;
}
.skip:focus {
  top: 12px;
}
main {
  min-height: 60vh;
}
</style>
