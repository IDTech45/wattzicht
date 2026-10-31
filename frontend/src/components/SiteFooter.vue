<script setup lang="ts">
const year = new Date().getFullYear()
</script>

<template>
  <footer class="foot">
    <div class="wz-wrap foot__grid">
      <div class="foot__brand">
        <span class="foot__logo" role="img" aria-label="Wattzicht">watt<span class="foot__logo-sun">zicht</span></span>
        <p>Zie wat je opwekt, gebruikt en bespaart. Welk merk je ook hebt.</p>
      </div>

      <nav class="foot__col" aria-label="Site">
        <h4>Wattzicht</h4>
        <RouterLink to="/">Voor thuis</RouterLink>
        <RouterLink to="/installateurs">Voor installateurs</RouterLink>
        <RouterLink to="/app">Download de app</RouterLink>
        <RouterLink to="/portal">Installateursportal</RouterLink>
      </nav>

      <!--
        Hier stonden een verzonnen telefoonnummer (053 - 123 45 67) en een verzonnen KvK-nummer
        (12345678). Op een pagina die klanten en installateurs lezen is dat erger dan niets: wie het
        probeert, belt een vreemde. Het e-mailadres blijft staan omdat het verkeer ergens heen moet;
        telefoon en KvK komen er pas bij zodra de echte waarden bekend zijn.
      -->
      <div class="foot__col">
        <h4>Contact</h4>
        <a href="mailto:hallo@wattzicht.nl">hallo@wattzicht.nl</a>
        <span>Enschede</span>
      </div>

      <div class="foot__col">
        <h4>Juridisch</h4>
        <RouterLink to="/privacy">Privacy</RouterLink>
      </div>
    </div>
    <div class="wz-wrap foot__bottom">
      <span>Wattzicht is een product van Dogantech B.V.</span>
      <span>© {{ year }}</span>
    </div>
  </footer>
</template>

<style scoped>
.foot {
  border-top: 1px solid var(--wz-line);
  background: var(--wz-surface);
  padding-top: 56px;
  font-size: 0.95rem;
}
.foot__grid {
  display: grid;
  grid-template-columns: 1.6fr 1fr 1fr 1fr;
  gap: 32px;
}
.foot__brand p {
  color: var(--wz-text-soft);
  max-width: 32ch;
  margin-top: 14px;
}
.foot__logo {
  font-family: var(--wz-font-display);
  font-size: 1.6rem;
  font-weight: 800;
  letter-spacing: -0.03em;
  color: var(--wz-ink);
}
.foot__logo-sun {
  color: var(--wz-sun);
}
.foot__col {
  display: grid;
  gap: 8px;
  align-content: start;
}
.foot__col h4 {
  font-size: 0.95rem;
  margin-bottom: 4px;
}
.foot__col a,
.foot__col span {
  color: var(--wz-text-soft);
  text-decoration: none;
}
.foot__col a:hover {
  color: var(--wz-text);
}
.foot__bottom {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  padding: 28px 0;
  margin-top: 40px;
  border-top: 1px solid var(--wz-line);
  color: var(--wz-text-soft);
}
@media (max-width: 820px) {
  .foot__grid {
    grid-template-columns: 1fr 1fr;
  }
  .foot__brand {
    grid-column: 1 / -1;
  }
}
</style>
