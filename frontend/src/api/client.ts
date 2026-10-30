/**
 * Kleine fetch-wrapper voor de Wattzicht API.
 * Alle paden gaan via /api (dev: Vite-proxy, prod: nginx → api:8080).
 */

export type DeviceType = 'Inverter' | 'Battery' | 'Charger' | 'P1Meter'
export type DeviceBrand =
  | 'Growatt'
  | 'Sessy'
  | 'Solax'
  | 'SolarEdge'
  | 'Enphase'
  | 'Deye'
  | 'Alfen'
  | 'Zaptec'
  | 'HomeWizard'
  | string

export type HouseholdStatus = 'online' | 'fault' | 'nodata'

export interface Installer {
  id: string
  name: string
  logoDataUrl?: string | null
  primaryColor?: string | null
  quoteUrl?: string | null
}

export interface LoginRequest {
  email: string
  password: string
}

export interface LoginResponse {
  token: string
  expiresAt?: string
  installer: Installer
}

export interface DeviceSummary {
  id: string
  type: DeviceType
  brand: DeviceBrand
  name?: string
  externalId?: string
  isOnline?: boolean
  lastSeen?: string | null
  lastError?: string | null
}

export type PerformanceStatus = 'unknown' | 'ok' | 'low'
export type OpportunityKind = 'battery' | 'smartcharging' | 'inspection' | 'dynamiccontract'

export interface Opportunity {
  kind: OpportunityKind
  title: string
  detail: string
  yearValueEur: number | null
}

export interface Performance {
  status: PerformanceStatus
  percentOfExpected: number | null
  lostKwh7Days: number
  lostEur7Days: number
  message: string
}

export interface ActionResult {
  ok: boolean
  message: string
}

export interface HouseholdSummary {
  id: string
  customerName: string
  city?: string
  devices: DeviceSummary[]
  status: HouseholdStatus
  todayPvKwh: number
  lastSeen: string | null
  performance?: PerformanceStatus
  opportunities?: Opportunity[]
}

export interface LiveReading {
  timestamp: string
  pvPowerW: number
  loadPowerW: number
  gridPowerW: number // positief = afname, negatief = teruglevering
  batteryPowerW: number // positief = ontladen, negatief = laden
  batterySoc: number | null
  chargerPowerW: number
}

export type AlertType = 'NoData' | 'ZeroProduction' | 'Offline'

export interface Alert {
  id: string
  deviceId: string
  deviceName?: string
  type: AlertType
  since: string
  resolvedAt: string | null
}

export interface DailySummary {
  date: string
  pvKwh: number
  selfUseKwh: number
  exportKwh: number
  importKwh: number
  chargedKwh: number
  savedEur: number
}

export interface HouseholdDetail extends HouseholdSummary {
  email?: string
  address?: string
  live: LiveReading | null
  alerts: Alert[]
  today?: DailySummary | null
  month?: DailySummary | null
  lastDays?: DailySummary[]
  performance?: Performance | null
}

export interface Branding {
  name: string
  logoDataUrl: string | null
  primaryColor: string
  quoteUrl: string
}

export interface LeadRequest {
  name: string
  company: string
  email: string
  phone: string
  customerCount: number | null
  source?: string
}

export class ApiError extends Error {
  status: number
  body: unknown
  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.status = status
    this.body = body
  }
}

const BASE = '/api'

let tokenProvider: () => string | null = () => null
let onUnauthorized: () => void = () => {}

/** De auth-store registreert hier hoe de client aan het token komt. */
export function configureClient(opts: {
  getToken: () => string | null
  onUnauthorized?: () => void
}) {
  tokenProvider = opts.getToken
  if (opts.onUnauthorized) onUnauthorized = opts.onUnauthorized
}

async function request<T>(
  path: string,
  init: RequestInit & { auth?: boolean; timeoutMs?: number } = {},
): Promise<T> {
  const { auth = true, timeoutMs = 15000, headers, ...rest } = init
  const h = new Headers(headers)
  if (!h.has('Accept')) h.set('Accept', 'application/json')
  if (rest.body && typeof rest.body === 'string' && !h.has('Content-Type')) {
    h.set('Content-Type', 'application/json')
  }
  if (auth) {
    const token = tokenProvider()
    if (token) h.set('Authorization', `Bearer ${token}`)
  }

  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), timeoutMs)

  let res: Response
  try {
    res = await fetch(`${BASE}${path}`, { ...rest, headers: h, signal: controller.signal })
  } catch (e) {
    clearTimeout(timer)
    throw new ApiError(0, 'Geen verbinding met de server.', e)
  }
  clearTimeout(timer)

  if (res.status === 401 && auth) {
    onUnauthorized()
  }

  const text = await res.text()
  const data = text ? safeJson(text) : null

  if (!res.ok) {
    throw new ApiError(res.status, errorMessage(data, res.status), data)
  }
  return data as T
}

function errorMessage(data: unknown, status: number): string {
  if (data && typeof data === 'object') {
    const d = data as Record<string, unknown>
    if (typeof d.message === 'string' && d.message) return d.message
    if (typeof d.title === 'string' && d.title) return d.title
  }
  return `Fout ${status}`
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text)
  } catch {
    return text
  }
}

// ---- Typed endpoints -------------------------------------------------------

export function login(body: LoginRequest): Promise<LoginResponse> {
  return request<LoginResponse>('/auth/login', {
    method: 'POST',
    body: JSON.stringify(body),
    auth: false,
  })
}

export function getInstallerHouseholds(): Promise<HouseholdSummary[]> {
  return request<HouseholdSummary[]>('/installer/households')
}

export function getHousehold(id: string): Promise<HouseholdDetail> {
  return request<HouseholdDetail>(`/installer/households/${encodeURIComponent(id)}`)
}

/** Actie op afstand: alle apparaten van deze klant nu opnieuw uitlezen. */
export function refreshHousehold(id: string): Promise<ActionResult> {
  return request<ActionResult>(`/installer/households/${encodeURIComponent(id)}/refresh`, { method: 'POST' })
}

/** Actie op afstand: bericht aan de klant (pushmelding in Wattzicht). */
export function messageHousehold(id: string, text: string): Promise<ActionResult> {
  return request<ActionResult>(`/installer/households/${encodeURIComponent(id)}/message`, {
    method: 'POST',
    body: JSON.stringify({ text }),
  })
}

export function getBranding(): Promise<Branding> {
  return request<Branding>('/installer/branding')
}

export function putBranding(body: Branding): Promise<Branding> {
  return request<Branding>('/installer/branding', {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}

export function postLead(body: LeadRequest): Promise<{ id?: string } | null> {
  return request<{ id?: string } | null>('/leads', {
    method: 'POST',
    body: JSON.stringify(body),
    auth: false,
  })
}
