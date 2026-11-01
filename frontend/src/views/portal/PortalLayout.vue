<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()

function logout() {
  auth.logout()
  router.replace({ name: 'portal-login' })
}
</script>

<template>
  <div class="portal">
    <aside class="side">
      <RouterLink to="/portal" class="side__brand">
        <img src="/wattzicht-icon.svg" alt="" width="32" height="32" />
        <span><b>Watt</b>zicht</span>
      </RouterLink>

      <div class="side__tenant">
        <img v-if="auth.installer?.logoDataUrl" :src="auth.installer.logoDataUrl" alt="" class="side__logo" />
        <span class="side__tenant-name">{{ auth.installer?.name ?? 'Installateur' }}</span>
      </div>

      <nav class="side__nav" aria-label="Portal">
        <RouterLink :to="{ name: 'portal-home' }" class="side__link">
          <i class="pi pi-home" aria-hidden="true"></i> Klanten
        </RouterLink>
        <RouterLink :to="{ name: 'portal-branding' }" class="side__link">
          <i class="pi pi-palette" aria-hidden="true"></i> Jouw bedrijf
        </RouterLink>
      </nav>

      <div class="side__foot">
        <RouterLink to="/" class="side__link side__link--quiet">
          <i class="pi pi-external-link" aria-hidden="true"></i> Naar de site
        </RouterLink>
        <button type="button" class="side__link side__link--quiet" @click="logout">
          <i class="pi pi-sign-out" aria-hidden="true"></i> Uitloggen
        </button>
      </div>
    </aside>

    <section class="content">
      <RouterView />
    </section>
  </div>
</template>

<style scoped>
.portal {
  min-height: 100vh;
  display: grid;
  grid-template-columns: 240px minmax(0, 1fr);
}
.side {
  background: var(--wz-surface);
  border-right: 1px solid var(--wz-line);
  padding: 20px 14px;
  display: grid;
  grid-template-rows: auto auto 1fr auto;
  gap: 20px;
  position: sticky;
  top: 0;
  height: 100vh;
}
.side__brand {
  display: flex;
  align-items: center;
  gap: 10px;
  text-decoration: none;
  color: var(--wz-heading);
  font-size: 1.15rem;
  padding: 4px 8px;
}
.side__brand img {
  border-radius: 8px;
}
.side__tenant {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: var(--wz-radius-panel);
  background: var(--wz-surface-2);
  font-size: 0.9rem;
  font-weight: 600;
  min-width: 0;
}
.side__logo {
  width: 24px;
  height: 24px;
  object-fit: contain;
  border-radius: 5px;
  background: #fff;
}
.side__tenant-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.side__nav,
.side__foot {
  display: grid;
  gap: 4px;
}
.side__link {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: var(--wz-radius-panel);
  text-decoration: none;
  color: var(--wz-text);
  font-weight: 500;
  background: transparent;
  border: 0;
  cursor: pointer;
  text-align: left;
  width: 100%;
}
.side__link:hover {
  background: var(--wz-surface-2);
}
.side__link.router-link-active {
  background: color-mix(in srgb, var(--wz-accent-500) 16%, transparent);
  color: var(--wz-heading);
  font-weight: 600;
}
.side__link--quiet {
  color: var(--wz-text-soft);
  font-size: 0.95rem;
}
.content {
  padding: clamp(20px, 3vw, 40px);
  min-width: 0;
}
@media (max-width: 820px) {
  .portal {
    grid-template-columns: 1fr;
  }
  .side {
    position: static;
    height: auto;
    grid-template-rows: auto;
    grid-template-columns: 1fr 1fr;
    border-right: 0;
    border-bottom: 1px solid var(--wz-line);
  }
  .side__nav {
    grid-template-columns: 1fr 1fr;
  }
  .side__foot {
    grid-template-columns: 1fr 1fr;
  }
}
</style>
