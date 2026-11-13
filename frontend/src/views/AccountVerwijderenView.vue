<template>
  <section class="wz-section">
    <div class="wz-wrap prose">
      <h1>Account verwijderen</h1>
      <p class="wz-lead">
        Je kunt je Wattzicht-account op elk moment weghalen. Het gaat dan ook echt weg: we zetten geen vlaggetje
        "verwijderd" op gegevens die blijven staan.
      </p>

      <h2>In de app, in twee tikken</h2>
      <p>
        Open Wattzicht, ga naar <strong>Meer</strong> en tik onderaan op <strong>Account verwijderen</strong>. De app
        vraagt twee keer om bevestiging, en daarna is het gebeurd. Dit is de snelste manier en je hebt er niemand voor
        nodig.
      </p>

      <h2>Zonder de app</h2>
      <p>
        Lukt dat niet, bijvoorbeeld omdat je de app al hebt verwijderd, stuur dan een bericht naar
        <a href="mailto:info@laadcash.nl?subject=Wattzicht%3A%20account%20verwijderen">info@laadcash.nl</a> vanaf
        het e-mailadres waarmee je je hebt aangemeld. Dan doen wij het voor je. We vragen niets anders dan dat adres,
        want daarmee weten we al welk account het is. Dat is de mailbox van DoganTech, het bedrijf achter Wattzicht;
        een eigen Wattzicht-adres volgt.
      </p>
      <p>We handelen zo'n verzoek binnen dertig dagen af, en in de praktijk een stuk sneller.</p>

      <h2>Wat er verdwijnt</h2>
      <ul>
        <li>Je account en je inloggegevens.</li>
        <li>Je huishouden, met je plaats en je energietarief.</li>
        <li>Alle gekoppelde apparaten, inclusief de versleutelde inloggegevens daarvan.</li>
        <li>Al je meetwaarden en dagoverzichten: opwek, verbruik, netstroom, batterij en laadsessies.</li>
        <li>Je meldingen, je pushmeldingen en je abonnementsgegevens.</li>
      </ul>

      <h2>Wat er blijft</h2>
      <p>
        Het bedrijf van je installateur blijft bestaan, want dat is niet van jou en die heeft andere klanten. Verder
        kunnen gegevens nog kort in onze back-ups zitten; die rouleren vanzelf en worden niet teruggezet om jouw account
        te herstellen.
      </p>
      <p>
        Heb je een abonnement via de App Store of Google Play, zeg dat dan ook daar op. Wij kunnen een abonnement bij
        Apple of Google niet voor je beëindigen; dat gaat alleen via je eigen account in die winkel.
      </p>

      <h2>Je andere rechten</h2>
      <p>
        Inzien, corrigeren of je gegevens meenemen kan ook. Hoe dat werkt staat in onze
        <RouterLink to="/privacy">privacyverklaring</RouterLink>.
      </p>
    </div>
  </section>
</template>

<script setup lang="ts">
import { RouterLink } from 'vue-router'
</script>
