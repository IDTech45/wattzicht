const nl = 'nl-NL'

export function kwh(v: number | null | undefined, digits = 1): string {
  if (v == null || Number.isNaN(v)) return '–'
  return `${v.toLocaleString(nl, { minimumFractionDigits: digits, maximumFractionDigits: digits })}`
}

export function watt(w: number | null | undefined): string {
  if (w == null || Number.isNaN(w)) return '–'
  const abs = Math.abs(w)
  if (abs >= 1000) return `${(abs / 1000).toLocaleString(nl, { minimumFractionDigits: 1, maximumFractionDigits: 1 })} kW`
  return `${Math.round(abs)} W`
}

export function euro(v: number | null | undefined): string {
  if (v == null || Number.isNaN(v)) return '–'
  return v.toLocaleString(nl, { style: 'currency', currency: 'EUR' })
}

export function relativeTime(iso: string | null | undefined): string {
  if (!iso) return 'nooit'
  const t = new Date(iso).getTime()
  if (Number.isNaN(t)) return '–'
  const diff = Math.round((Date.now() - t) / 1000)
  if (diff < 60) return 'zojuist'
  if (diff < 3600) return `${Math.floor(diff / 60)} min geleden`
  if (diff < 86400) return `${Math.floor(diff / 3600)} uur geleden`
  const days = Math.floor(diff / 86400)
  return days === 1 ? 'gisteren' : `${days} dagen geleden`
}

export function dateTime(iso: string | null | undefined): string {
  if (!iso) return '–'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '–'
  return d.toLocaleString(nl, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })
}
