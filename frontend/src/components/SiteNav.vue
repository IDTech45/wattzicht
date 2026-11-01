<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRoute } from 'vue-router'

const open = ref(false)
const route = useRoute()
watch(() => route.fullPath, () => (open.value = false))

const links = [
  { to: '/', label: 'Voor thuis' },
  { to: '/installateurs', label: 'Voor installateurs' },
  { to: '/app', label: 'De app' },
]
</script>

<template>
  <header class="nav">
    <div class="wz-wrap nav__bar">
      <RouterLink to="/" class="nav__brand" aria-label="Wattzicht, naar de homepage">
        <img src="/wattzicht-icon.svg" alt="" width="34" height="34" />
        <span class="nav__word">watt<span class="nav__word-sun">zicht</span></span>
      </RouterLink>

      <nav class="nav__links" :class="{ 'is-open': open }" aria-label="Hoofdmenu">
        <RouterLink v-for="l in links" :key="l.to" :to="l.to" class="nav__link">{{ l.label }}</RouterLink>
        <RouterLink to="/portal" class="nav__link nav__link--portal">Installateursportal</RouterLink>
        <RouterLink to="/aanmelden" class="nav__link nav__link--cta">Aanmelden</RouterLink>
      </nav>

      <button
        class="nav__toggle"
        type="button"
        :aria-expanded="open"
        aria-controls="menu"
        @click="open = !open"
      >
        <span class="sr-only">Menu</span>
        <i :class="open ? 'pi pi-times' : 'pi pi-bars'" aria-hidden="true"></i>
      </button>
    </div>
  </header>
</template>

<style scoped>
/* De aanmeldknop is de enige actie in de balk, dus die mag eruit springen; de rest blijft tekst. */
.nav__link--cta {
  background: var(--wz-cta-bg);
  color: var(--wz-cta-fg);
  border-radius: 999px;
  padding: 9px 16px;
  font-weight: 700;
  min-height: 44px;
  display: inline-flex;
  align-items: center;
}

.nav {
  position: sticky;
  top: 0;
  z-index: 50;
  background: color-mix(in srgb, var(--wz-bg) 86%, transparent);
  backdrop-filter: blur(12px);
  border-bottom: 1px solid var(--wz-line);
}
.nav__bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  height: 64px;
}
.nav__brand {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  text-decoration: none;
  color: var(--wz-heading);
}
.nav__brand img {
  border-radius: 9px;
}
.nav__word {
  font-family: var(--wz-font-display);
  font-size: 1.15rem;
  font-weight: 800;
  letter-spacing: -0.03em;
  color: var(--wz-text);
}
.nav__word-sun {
  /*
    Niet het accent zelf: #F5C518 op een witte balk haalt 1,7:1 en is dan onleesbaar. Dit is het
    accent twee stappen donkerder (dezelfde waarde als in het woordmerk-SVG). In dark mode staat de
    balk op navy en mag het accent zelf weer.
  */
  color: #a9850b;
}

@media (prefers-color-scheme: dark) {
  .nav__word-sun {
    color: var(--wz-accent-500);
  }
}
.nav__links {
  display: flex;
  align-items: center;
  gap: 4px;
}
.nav__link {
  padding: 8px 14px;
  border-radius: 999px;
  text-decoration: none;
  font-weight: 500;
  color: var(--wz-text-soft);
  transition: color 120ms ease, background-color 120ms ease;
}
.nav__link:hover,
.nav__link.router-link-active {
  color: var(--wz-text);
}
.nav__link.router-link-exact-active {
  background: var(--wz-surface-2);
}
.nav__link--portal {
  margin-left: 12px;
  color: var(--wz-text);
  border: 1.5px solid var(--wz-line-strong);
}
.nav__toggle {
  display: none;
  width: 42px;
  height: 42px;
  border-radius: 999px;
  border: 1.5px solid var(--wz-line-strong);
  background: transparent;
  color: var(--wz-text);
  cursor: pointer;
}

@media (max-width: 820px) {
  .nav__toggle {
    display: inline-grid;
    place-items: center;
  }
  .nav__links {
    display: none;
    position: absolute;
    left: 0;
    right: 0;
    top: 64px;
    flex-direction: column;
    align-items: stretch;
    padding: 12px var(--wz-gutter) 20px;
    background: var(--wz-bg);
    border-bottom: 1px solid var(--wz-line);
  }
  .nav__links.is-open {
    display: flex;
  }
  .nav__link {
    padding: 14px;
    border-radius: var(--wz-radius-panel);
  }
  .nav__link--portal {
    margin: 8px 0 0;
    text-align: center;
  }
}
</style>
