import { defineStore } from 'pinia'
import { ref } from 'vue'
import { postLead, type LeadRequest } from '@/api/client'

export type LeadState = 'idle' | 'sending' | 'sent' | 'failed'

const PENDING_KEY = 'wz.pendingLeads'

export const useLeadsStore = defineStore('leads', () => {
  const state = ref<LeadState>('idle')
  const message = ref<string | null>(null)
  const lastLead = ref<LeadRequest | null>(null)

  async function submit(lead: LeadRequest): Promise<boolean> {
    state.value = 'sending'
    message.value = null
    lastLead.value = lead
    try {
      await postLead({ ...lead, source: 'website' })
      state.value = 'sent'
      message.value = `Bedankt, ${lead.name.split(' ')[0]}. We bellen je binnen één werkdag om een moment te plannen.`
      return true
    } catch {
      // Fallback: bewaar lokaal en toon een alternatieve route, zodat de aanvraag niet verloren gaat.
      try {
        const existing = JSON.parse(localStorage.getItem(PENDING_KEY) ?? '[]') as LeadRequest[]
        existing.push({ ...lead, source: 'website-offline' })
        localStorage.setItem(PENDING_KEY, JSON.stringify(existing))
      } catch {
        /* opslag niet beschikbaar */
      }
      state.value = 'failed'
      message.value =
        'Versturen lukt nu niet. Mail je gegevens naar hallo@wattzicht.nl, dan plannen we direct een demo.'
      return false
    }
  }

  function reset() {
    state.value = 'idle'
    message.value = null
  }

  return { state, message, lastLead, submit, reset }
})
