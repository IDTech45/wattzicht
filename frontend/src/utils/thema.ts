import { ref } from 'vue'

/**
 * Licht of donker. Donker is de standaard: de app laat energie zien en dat leest op navy rustiger
 * dan op wit. De keuze van de bezoeker gaat voor en blijft in deze browser bewaard.
 *
 * Bewust niet op prefers-color-scheme. Dan ziet iemand met een lichte telefoon een andere site dan
 * wij bedoelen, en een eigen keuze zou het systeem niet kunnen overrulen.
 */
export type Thema = 'dark' | 'light'

export const BEWAARSLEUTEL = 'wz-thema'

/** Wat er in de opslag staat, of donker als er niets (bruikbaars) staat. */
export function gekozenThema(): Thema {
  try {
    return localStorage.getItem(BEWAARSLEUTEL) === 'light' ? 'light' : 'dark'
  } catch {
    // Privémodus of site-data uit: dan gewoon donker, niet struikelen.
    return 'dark'
  }
}

/** Reactief, zodat de knop in de balk meteen meedraait. */
export const thema = ref<Thema>(gekozenThema())

export function zetThema(nieuw: Thema) {
  thema.value = nieuw
  document.documentElement.dataset.theme = nieuw
  // De telefoonbalk mee: op licht is wit minder schreeuwerig dan navy.
  document.querySelector('meta[name="theme-color"]')
    ?.setAttribute('content', nieuw === 'light' ? '#f6f8f7' : '#0a2540')
  try { localStorage.setItem(BEWAARSLEUTEL, nieuw) } catch { /* niet kunnen onthouden is geen fout */ }
}

export function wisselThema() {
  zetThema(thema.value === 'dark' ? 'light' : 'dark')
}
