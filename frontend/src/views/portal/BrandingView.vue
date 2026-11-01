<script setup lang="ts">
/*
  Let op: het logo dat hier geüpload kan worden, wordt sinds de huisstijl-gelijktrekking nergens meer
  getoond. Wattzicht blijft het merk; een installateur staat erbij als naam ("jouw installateur"),
  niet als tweede logo. Het veld is bewust blijven staan omdat het weghalen een functionele wijziging
  is en de opgeslagen logo's dan weg zouden vallen; dat is een aparte beslissing.
*/
import { onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import InputText from 'primevue/inputtext'
import ColorPicker from 'primevue/colorpicker'
import Message from 'primevue/message'
import PhonePreview from '@/components/PhonePreview.vue'
import { getBranding, putBranding, type Branding } from '@/api/client'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const toast = useToast()

const form = reactive<Branding>({
  name: auth.installer?.name ?? '',
  logoDataUrl: auth.installer?.logoDataUrl ?? null,
  primaryColor: auth.installer?.primaryColor ?? '#F5C518',
  quoteUrl: auth.installer?.quoteUrl ?? '',
})
const saving = ref(false)
const loadError = ref<string | null>(null)
const logoError = ref<string | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

const presets = ['#F5C518', '#0E7490', '#16A34A', '#2563EB', '#DC2626', '#0F172A', '#7C3AED', '#EA580C']

onMounted(async () => {
  try {
    const b = await getBranding()
    Object.assign(form, b, { primaryColor: normalize(b.primaryColor) || '#F5C518' })
  } catch {
    loadError.value = 'Je bedrijfsgegevens konden niet geladen worden. Je ziet de laatst bekende instellingen.'
  }
})

function normalize(c: string | null | undefined): string {
  if (!c) return ''
  const v = c.startsWith('#') ? c : `#${c}`
  return /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test(v) ? v.toUpperCase() : ''
}

/** ColorPicker geeft hex zonder '#'. */
function onPicker(v: string | undefined) {
  if (typeof v === 'string') form.primaryColor = normalize(v) || form.primaryColor
}

function onLogo(e: Event) {
  logoError.value = null
  const file = (e.target as HTMLInputElement).files?.[0]
  if (!file) return
  if (!['image/png', 'image/svg+xml', 'image/jpeg', 'image/webp'].includes(file.type)) {
    logoError.value = 'Kies een PNG, SVG, JPG of WebP.'
    return
  }
  if (file.size > 400 * 1024) {
    logoError.value = 'Het logo is groter dan 400 kB. Maak het kleiner en probeer opnieuw.'
    return
  }
  const reader = new FileReader()
  reader.onload = () => (form.logoDataUrl = String(reader.result))
  reader.onerror = () => (logoError.value = 'Het bestand kon niet gelezen worden.')
  reader.readAsDataURL(file)
}

function removeLogo() {
  form.logoDataUrl = null
  if (fileInput.value) fileInput.value.value = ''
}

const urlOk = () => !form.quoteUrl || /^https?:\/\/.+/i.test(form.quoteUrl)

async function save() {
  if (!form.name.trim()) {
    toast.add({ severity: 'warn', summary: 'Vul een bedrijfsnaam in.', life: 3000 })
    return
  }
  if (!urlOk()) {
    toast.add({ severity: 'warn', summary: 'De offerte-URL moet met https:// beginnen.', life: 3000 })
    return
  }
  saving.value = true
  try {
    const saved = await putBranding({ ...form, name: form.name.trim() })
    Object.assign(form, saved)
    auth.updateInstaller({
      name: saved.name,
      logoDataUrl: saved.logoDataUrl,
      primaryColor: saved.primaryColor,
      quoteUrl: saved.quoteUrl,
    })
    toast.add({ severity: 'success', summary: 'Opgeslagen', detail: 'Je klanten zien dit bij de volgende keer openen van de app.', life: 4000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Opslaan is niet gelukt', detail: e instanceof Error ? e.message : undefined, life: 5000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="page">
    <header>
      <h1>Jouw bedrijf in Wattzicht</h1>
      <p class="wz-help">Zo ziet de app eruit voor je klanten. Wijzigingen zie je rechts direct.</p>
    </header>

    <Message v-if="loadError" severity="warn" :closable="false">{{ loadError }}</Message>

    <div class="grid">
      <form class="form wz-panel" novalidate @submit.prevent="save">
        <div class="wz-field">
          <label for="b-name">Bedrijfsnaam</label>
          <InputText id="b-name" v-model="form.name" maxlength="40" />
          <span class="wz-help">Staat bovenaan in de app en in meldingen.</span>
        </div>

        <div class="wz-field">
          <label for="b-logo">Logo</label>
          <div class="logo-row">
            <div class="logo-box" :class="{ 'is-empty': !form.logoDataUrl }">
              <img v-if="form.logoDataUrl" :src="form.logoDataUrl" alt="Je logo" />
              <span v-else class="wz-help">Geen logo</span>
            </div>
            <div class="logo-actions">
              <label class="wz-btn wz-btn--ghost" for="b-logo">
                <i class="pi pi-upload" aria-hidden="true"></i> {{ form.logoDataUrl ? 'Ander logo' : 'Kies logo' }}
              </label>
              <input id="b-logo" ref="fileInput" type="file" accept="image/png,image/svg+xml,image/jpeg,image/webp" class="sr-only" @change="onLogo" />
              <button v-if="form.logoDataUrl" type="button" class="linkbtn" @click="removeLogo">Verwijderen</button>
            </div>
          </div>
          <span v-if="logoError" class="err">{{ logoError }}</span>
          <span v-else class="wz-help">PNG of SVG, liefst vierkant, maximaal 400 kB.</span>
        </div>

        <div class="wz-field">
          <label for="b-color">Kleur van je offerteknop</label>
          <div class="color-row">
            <ColorPicker :model-value="form.primaryColor.replace('#', '')" input-id="b-color" format="hex" @update:model-value="onPicker" />
            <InputText
              v-model="form.primaryColor"
              aria-label="Hex-kleurcode"
              class="hex"
              maxlength="7"
              @blur="form.primaryColor = normalize(form.primaryColor) || '#F5C518'"
            />
            <div class="presets" role="group" aria-label="Voorbeeldkleuren">
              <button
                v-for="p in presets"
                :key="p"
                type="button"
                class="preset"
                :class="{ 'is-active': form.primaryColor.toUpperCase() === p }"
                :style="{ background: p }"
                :aria-label="`Kleur ${p}`"
                @click="form.primaryColor = p"
              ></button>
            </div>
          </div>
          <span class="wz-help">Wattzicht houdt zijn eigen stijl; jouw kleur zit op je eigen knop bij de klant.</span>
        </div>

        <div class="wz-field">
          <label for="b-quote">Offerte-URL</label>
          <InputText id="b-quote" v-model="form.quoteUrl" type="url" placeholder="https://jouwbedrijf.nl/offerte" :invalid="!urlOk()" />
          <span class="wz-help">Hier komt je klant uit bij 'Vraag offerte aan', bijvoorbeeld voor een thuisbatterij.</span>
        </div>

        <button type="submit" class="wz-btn wz-btn--primary save" :disabled="saving">
          <i v-if="saving" class="pi pi-spin pi-spinner" aria-hidden="true"></i>
          {{ saving ? 'Opslaan…' : 'Opslaan' }}
        </button>
      </form>

      <aside class="preview">
        <PhonePreview :name="form.name" :logo-data-url="form.logoDataUrl" :primary-color="form.primaryColor" :quote-url="form.quoteUrl" />
        <p class="wz-help preview__cap">Voorbeeld van het startscherm. Getallen zijn fictief.</p>
      </aside>
    </div>
  </div>
</template>

<style scoped>
.page {
  display: grid;
  gap: 22px;
}
.page h1 {
  font-size: 1.8rem;
}
.grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 360px;
  gap: 28px;
  align-items: start;
}
.form {
  padding: clamp(20px, 3vw, 32px);
  display: grid;
  gap: 24px;
}
.logo-row {
  display: flex;
  gap: 16px;
  align-items: center;
}
.logo-box {
  width: 84px;
  height: 84px;
  border-radius: var(--wz-radius-panel);
  border: 1px solid var(--wz-line);
  background: #fff;
  display: grid;
  place-items: center;
  overflow: hidden;
  padding: 8px;
}
.logo-box.is-empty {
  background: var(--wz-surface-2);
  border-style: dashed;
}
.logo-box img {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
}
.logo-actions {
  display: grid;
  gap: 8px;
  justify-items: start;
}
.linkbtn {
  background: none;
  border: 0;
  padding: 0;
  color: var(--wz-text-soft);
  cursor: pointer;
  text-decoration: underline;
}
.color-row {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.hex {
  width: 110px;
  font-family: ui-monospace, monospace;
  text-transform: uppercase;
}
.presets {
  display: flex;
  gap: 6px;
}
.preset {
  width: 26px;
  height: 26px;
  border-radius: 999px;
  border: 2px solid transparent;
  cursor: pointer;
  box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.1);
}
.preset.is-active {
  border-color: var(--wz-text);
}
.save {
  justify-self: start;
}
.err {
  color: var(--wz-error-text);
  font-size: 0.9rem;
}
.preview {
  position: sticky;
  top: 24px;
  display: grid;
  gap: 12px;
}
.preview__cap {
  text-align: center;
}
@media (max-width: 980px) {
  .grid {
    grid-template-columns: 1fr;
  }
  .preview {
    position: static;
    order: -1;
  }
}
</style>
