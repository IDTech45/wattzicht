<template>
  <section class="wz-section">
    <div class="wz-wrap prose">
      <h1>Privacy</h1>
      <p class="wz-lead">
        Wattzicht leest je energiegegevens uit om ze aan jou te laten zien. Hieronder staat kort wat we bewaren, waarom,
        en wat je rechten zijn. Dit is een concepttekst; de definitieve versie volgt voor de lancering.
      </p>

      <h2>Wie is verantwoordelijk</h2>
      <p>
        Wattzicht is een product van Dogantech B.V., gevestigd in Enschede. Voor de app en deze website zijn wij de
        verwerkingsverantwoordelijke. Heeft je installateur Wattzicht voor je klaargezet, dan ziet die alleen de gegevens
        van zijn eigen klanten en hebben we daar een verwerkersovereenkomst mee.
      </p>

      <h2>Welke gegevens we verwerken</h2>
      <ul>
        <li>Je e-mailadres en wachtwoord (versleuteld) om in te loggen.</li>
        <li>De koppelgegevens van je omvormer, batterij of laadpaal. Die bewaren we versleuteld en gebruiken we alleen om je apparaten uit te lezen.</li>
        <li>Meetwaarden van je apparaten, elke vijf minuten: opwek, verbruik, netstroom, batterijstand en laadvermogen.</li>
        <li>Je energietarief, als je dat invult, om je besparing te berekenen.</li>
        <li>Bij een demo-aanvraag: je naam, bedrijf, e-mailadres en telefoonnummer.</li>
      </ul>

      <h2>Waarvoor</h2>
      <p>
        Om je te laten zien wat je opwekt, gebruikt en bespaart, om je te waarschuwen bij een storing en om je
        installateur te laten zien of je installatie werkt. We verkopen geen gegevens en we gebruiken ze niet voor
        advertenties.
      </p>

      <h2>Hoe lang</h2>
      <p>
        Meetwaarden bewaren we zolang je een account hebt, zodat je je besparing per jaar terug kunt zien. Verwijder je
        je account, dan wissen we alles binnen dertig dagen. Demo-aanvragen bewaren we maximaal een jaar.
      </p>

      <h2>Waar</h2>
      <p>Alle gegevens staan op servers in Nederland. Voor het uitlezen van je apparaten maken we verbinding met de cloud van de fabrikant (bijvoorbeeld Growatt of Sessy); daarvoor geldt ook hun privacybeleid.</p>

      <h2>Je rechten</h2>
      <p>
        Je kunt je gegevens altijd inzien, laten aanpassen, meenemen of laten verwijderen. Stuur een bericht naar
        <a href="mailto:privacy@wattzicht.nl">privacy@wattzicht.nl</a>. Ben je het niet eens met hoe we met je gegevens omgaan, dan kun je een klacht indienen bij de Autoriteit Persoonsgegevens.
      </p>

      <p class="small">Laatst bijgewerkt: oktober 2026.</p>
    </div>
  </section>
</template>

<style scoped>
.prose {
  max-width: 68ch;
  display: grid;
  gap: 16px;
}
.prose h1 {
  margin-bottom: 8px;
}
.prose h2 {
  font-size: 1.3rem;
  margin-top: 20px;
}
.prose ul {
  margin: 0;
  padding-left: 22px;
  display: grid;
  gap: 8px;
}
.prose a {
  font-weight: 600;
}
.small {
  color: var(--wz-text-soft);
  font-size: 0.9rem;
  margin-top: 16px;
}
</style>
