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

// Aura-preset met geel als primary en lacivert als surface (huisstijl, altijd donker).
const WattzichtPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#FFFDE6',
      100: '#FFF9BF',
      200: '#FFF38A',
      300: '#FFEB66',
      400: '#FFE11A',
      500: '#FFE11A',
      600: '#E6C800',
      700: '#B39C00',
      800: '#806F00',
      900: '#4D4300',
      950: '#262100',
    },
    colorScheme: {
      light: {
        surface: {
          0: '#FFFFFF',
          50: '#F4F6FB',
          100: '#DCE2EF',
          200: '#B8C3DA',
          300: '#9AA8C4',
          400: '#6A7899',
          500: '#4A5C82',
          600: '#2E4573',
          700: '#22365F',
          800: '#172C5C',
          900: '#12234A',
          950: '#0A1630',
        },
        primary: {
          color: '{primary.500}',
          contrastColor: '#0A1630',
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
        surface: {
          0: '#FFFFFF',
          50: '#F4F6FB',
          100: '#DCE2EF',
          200: '#B8C3DA',
          300: '#9AA8C4',
          400: '#6A7899',
          500: '#4A5C82',
          600: '#2E4573',
          700: '#22365F',
          800: '#172C5C',
          900: '#12234A',
          950: '#0A1630',
        },
        primary: {
          color: '{primary.400}',
          contrastColor: '#0A1630',
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

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.use(PrimeVue, {
  theme: {
    preset: WattzichtPreset,
    options: {
      // Altijd donker, net als de app: <html class="wz-dark"> in index.html.
      darkModeSelector: '.wz-dark',
      cssLayer: {
        name: 'primevue',
        order: 'base, primevue, app',
      },
    },
  },
})
app.use(ToastService)
app.mount('#app')
