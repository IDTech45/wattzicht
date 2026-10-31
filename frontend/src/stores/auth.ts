import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { configureClient, login as apiLogin, type Installer } from '@/api/client'

const TOKEN_KEY = 'wz.token'
const INSTALLER_KEY = 'wz.installer'
const ROLE_KEY = 'wz.role'

function readStorage<T>(key: string, parse: (raw: string) => T): T | null {
  try {
    const raw = localStorage.getItem(key)
    return raw ? parse(raw) : null
  } catch {
    return null
  }
}

function writeStorage(key: string, value: string | null) {
  try {
    if (value === null) localStorage.removeItem(key)
    else localStorage.setItem(key, value)
  } catch {
    /* privémodus of geblokkeerde opslag: sessie werkt dan alleen in-memory */
  }
}

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(readStorage(TOKEN_KEY, (r) => r))
  const installer = ref<Installer | null>(
    readStorage(INSTALLER_KEY, (r) => JSON.parse(r) as Installer),
  )
  const role = ref<string | null>(readStorage(ROLE_KEY, (r) => r))
  const busy = ref(false)
  const error = ref<string | null>(null)

  const isLoggedIn = computed(() => !!token.value)

  /** Beheerder van Wattzicht zelf: ziet alle installateurs, niet alleen eigen klanten. */
  const isAdmin = computed(() => role.value === 'Admin')

  function setSession(nextToken: string | null, nextInstaller: Installer | null, nextRole: string | null = null) {
    token.value = nextToken
    installer.value = nextInstaller
    role.value = nextRole
    writeStorage(TOKEN_KEY, nextToken)
    writeStorage(INSTALLER_KEY, nextInstaller ? JSON.stringify(nextInstaller) : null)
    writeStorage(ROLE_KEY, nextRole)
  }

  async function login(email: string, password: string): Promise<boolean> {
    busy.value = true
    error.value = null
    try {
      const res = await apiLogin({ email, password })
      setSession(res.token, res.installer, res.role ?? null)
      return true
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Inloggen is niet gelukt.'
      error.value =
        msg.includes('401') || msg.toLowerCase().includes('unauthorized')
          ? 'E-mailadres of wachtwoord klopt niet.'
          : msg
      return false
    } finally {
      busy.value = false
    }
  }

  function logout() {
    setSession(null, null)
  }

  function updateInstaller(patch: Partial<Installer>) {
    if (!installer.value) return
    const next = { ...installer.value, ...patch }
    installer.value = next
    writeStorage(INSTALLER_KEY, JSON.stringify(next))
  }

  configureClient({
    getToken: () => token.value,
    onUnauthorized: () => logout(),
  })

  return { token, installer, role, busy, error, isLoggedIn, isAdmin, login, logout, updateInstaller }
})
