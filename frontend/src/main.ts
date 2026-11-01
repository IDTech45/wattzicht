import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import Aura from '@primeuix/themes/aura'
import { definePreset } from '@primeuix/themes'
import 'primeicons/primeicons.css'

import App from './App.vue'
import router from './router'
import './style.css'
import { gekozenThema, zetThema } from './utils/thema'

/*
  Aura-preset met de tokens van docs/DESIGN-TOKENS.md. Primary is het gele accent, surface is de koele
  grijsschaal van LaadCash met navy als 950. Op geel staat navy-tekst (contrastColor): geel met wit
  haalt geen 4,5:1.

  In dark mode is navy de achtergrond en blijft het accent gelijk, zoals in de app.
*/
const SURFACE = {
  0: '#FFFFFF',
  50: '#F6F8F7',
  100: '#EEF2F0',
  200: '#E6EBE8',
  300: '#DFE6E2',
  400: '#C6D1CB',
  500: '#AEBAB5',
  600: '#5F6F75',
  700: '#5C6B66',
  800: '#36474C',
  900: '#0E3A4A',
  950: '#0A2540',
}

const WattzichtPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#FFFBEB',
      100: '#FEF3C7',
      200: '#FDE68A',
      300: '#FFE89A',
      400: '#F5C518',
      500: '#F5C518',
      600: '#D9AC0F',
      700: '#A9850B',
      800: '#7A5F08',
      900: '#4C3B05',
      950: '#2A2103',
    },
    colorScheme: {
      light: {
        surface: SURFACE,
        primary: {
          color: '{primary.500}',
          contrastColor: '#0A2540',
          hoverColor: '{primary.600}',
          activeColor: '{primary.700}',
        },
        highlight: {
          background: '{primary.50}',
          focusBackground: '{primary.100}',
          color: '{primary.800}',
          focusColor: '{primary.900}',
        },
      },
      dark: {
        surface: SURFACE,
        primary: {
          color: '{primary.400}',
          contrastColor: '#0A2540',
          hoverColor: '{primary.300}',
          activeColor: '{primary.200}',
        },
        highlight: {
          background: 'color-mix(in srgb, {primary.400}, transparent 84%)',
          focusBackground: 'color-mix(in srgb, {primary.400}, transparent 76%)',
          color: 'rgba(255,255,255,.87)',
          focusColor: 'rgba(255,255,255,.87)',
        },
      },
    },
  },
})

// Het thema staat al op <html> uit het scriptje in index.html; dit houdt alleen de opslag,
// de themakleur van de telefoonbalk en PrimeVue gelijk.
zetThema(gekozenThema())

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.use(PrimeVue, {
  theme: {
    preset: WattzichtPreset,
    options: {
      // Donker is de standaard en de bezoeker zet het zelf om; <html data-theme> is de bron.
      // Niet 'system': dan zou PrimeVue de telefoon volgen terwijl de rest van de site de knop volgt.
      darkModeSelector: '[data-theme="dark"]',
      cssLayer: {
        name: 'primevue',
        order: 'base, primevue, app',
      },
    },
  },
})
app.use(ToastService)
app.mount('#app')
