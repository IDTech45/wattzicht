<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Message from 'primevue/message'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')

async function submit() {
  if (!email.value || !password.value) {
    auth.error = 'Vul je e-mailadres en wachtwoord in.'
    return
  }
  const ok = await auth.login(email.value, password.value)
  if (ok) {
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/portal'
    router.replace(redirect)
  }
}
</script>

<template>
  <div class="login">
    <div class="login__art" aria-hidden="true">
      <img src="/wattzicht-icon.svg" alt="" width="72" height="72" />
      <p class="login__tag">Installateursportal</p>
      <p class="login__sub">Zie in één oogopslag welke installaties van je klanten werken, en welke je aandacht nodig hebben.</p>
    </div>

    <form class="login__form" novalidate @submit.prevent="submit">
      <h1>Inloggen</h1>
      <p class="wz-help">Met het account dat je van Wattzicht kreeg.</p>

      <div class="wz-field">
        <label for="l-email">E-mailadres</label>
        <InputText id="l-email" v-model="email" type="email" autocomplete="username" autofocus />
      </div>
      <div class="wz-field">
        <label for="l-pass">Wachtwoord</label>
        <Password
          id="l-pass"
          v-model="password"
          :feedback="false"
          toggle-mask
          autocomplete="current-password"
          input-class="w-full"
          fluid
        />
      </div>

      <Message v-if="auth.error" severity="error" :closable="false">{{ auth.error }}</Message>

      <button type="submit" class="wz-btn wz-btn--primary" :disabled="auth.busy">
        <i v-if="auth.busy" class="pi pi-spin pi-spinner" aria-hidden="true"></i>
        {{ auth.busy ? 'Even geduld…' : 'Inloggen' }}
      </button>

      <p class="wz-help">
        Nog geen account? <RouterLink to="/installateurs#demo">Plan een demo</RouterLink>.
        <br />
        <RouterLink to="/">Terug naar de site</RouterLink>
      </p>
    </form>
  </div>
</template>

<style scoped>
.login {
  min-height: 100vh;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
}
.login__art {
  background: var(--wz-night);
  color: #e2e8f0;
  padding: clamp(32px, 6vw, 72px);
  display: grid;
  align-content: end;
  gap: 12px;
}
.login__art img {
  border-radius: 18px;
  margin-bottom: 24px;
}
.login__tag {
  font-size: 1.8rem;
  font-weight: 800;
  letter-spacing: -0.02em;
  color: #fff;
}
.login__sub {
  color: #94a3b8;
  max-width: 40ch;
}
.login__form {
  padding: clamp(32px, 6vw, 72px);
  display: grid;
  gap: 18px;
  align-content: center;
  max-width: 460px;
  width: 100%;
  margin-inline: auto;
}
.login__form h1 {
  font-size: 2rem;
}
.login__form .wz-btn {
  justify-self: start;
}
@media (max-width: 820px) {
  .login {
    grid-template-columns: 1fr;
  }
  .login__art {
    min-height: 30vh;
  }
}
</style>
