/** Installateurs die met Wattzicht werken (getoond op de website). */
export interface Partner {
  name: string
  city: string
  logo: string
  url: string
}

export const partners: Partner[] = [
  {
    name: 'Triple Energie',
    city: 'Enschede',
    // Logo van hun eigen site; vervangen door een eigen kopie zodra ze die aanleveren.
    logo: 'https://www.triple-energie.nl/wp-content/uploads/2025/04/Triple-energie-logo-768x351.png',
    url: 'https://www.triple-energie.nl/',
  },
]
