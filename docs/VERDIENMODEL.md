# Verdienmodel Wattzicht

Wattzicht is een product van Dogantech B.V., dezelfde eigenaar als LaadCash. Eerste installateur is
Triple Energie in Enschede; die betaalt niets en levert in ruil klanten.

Het geld komt uit vier bronnen, in volgorde van verwachte omvang:

1. **Installateurs** die per huishouden betalen, vanaf Q2 2027.
2. **Leads**: een batterij-offerte naar de installateur, later een dynamisch contract. Lead-geld,
   geen advertentiegeld.
3. **LaadCash**: elke Wattzicht-gebruiker met een laadpaal hoort LaadCash-klant te zijn.
4. **Wattzicht Plus** voor consumenten.

Over die vierde: dat is zakgeld. Benchmarks zeggen dat 2 tot 5 procent van de gratis gebruikers
betaalt. Plus bestaat omdat het de app serieus maakt en omdat installateurs het later als
"inbegrepen" aan hun klanten geven. Netjes gebouwd, niet eindeloos op optimaliseren.

**Koppelen is de trechter.** Alles wat koppelen makkelijker maakt gaat vóór alles wat geld oplevert,
en koppelen blijft altijd gratis.

## Prijzen

| Wat | Prijs | Kanaal |
| --- | --- | --- |
| Gratis | € 0 | – |
| Plus per maand | € 1,99 incl. btw | Apple / Google, `nl.wattzicht.plus.monthly` |
| Plus per jaar | € 21,99 incl. btw | Apple / Google, `nl.wattzicht.plus.yearly` |
| Proefperiode | 30 dagen Plus, één keer per woning, zonder betaalgegevens | wij zelf |
| Via Triple Energie | 12 maanden Plus gratis | bron `installer`, billing_mode `free` |
| Installateur (later) | € 1,25 per actief huishouden per maand excl. btw | per installateur instelbaar |
| Batterij-lead | € 75 | alleen registreren, facturatie met de hand |

Eén Plus per woning, onbeperkt gebruikers en apparaten. De prijzen komen uit `GET /api/plans`, nooit
uit de app; een prijs in een app-build staat vast tot de volgende store-release en dan rekent de
store iets anders af dan er op het scherm stond.

**Consumenten betalen uitsluitend via de stores.** Geen Mollie, geen Stripe, geen eigen
betaalpagina, ook niet voor het jaarabonnement. Een eigen betaalpartij komt later en alleen voor
installateurs, want dat zijn bedrijven.

Plus gaat open op **1 januari 2027**, de dag dat de saldering stopt. Tot die tijd is alles gratis:
`PLUS_ENABLED=false`. De abonnementen lopen intussen wel gewoon mee, zodat de administratie op die
dag al klopt en proefperiodes die nu starten niet met terugwerkende kracht rechtgezet hoeven worden.
De LaadCash-promotie staat **niet** achter die vlag en is nu al live.

## Wat zit er in Gratis en wat in Plus

De regel: **zien is gratis, begrijpen en besparen is Plus.**

| Functie | Gratis | Plus |
| --- | --- | --- |
| Koppelen, alle merken | ja | ja |
| Live energiestroom, vandaag | ja | ja |
| Historie | 30 dagen | onbeperkt, plus jaar-op-jaar |
| Besparing in euro's | landelijk gemiddeld tarief | eigen leverancier, terugleverkosten en dynamisch |
| Storingsmelding | omvormer offline | ook minder opwek dan verwacht, batterij laadt niet, laadpaal in fout |
| "Laad nu"-advies | – | ja |
| Zelfvoorziening, CO2, maandrapport | – | ja |
| CSV-export, widgets | – | ja |
| Meerdere woningen | 1 | onbeperkt |
| Aanbiedingen-tegel | zichtbaar | verborgen |
| LaadCash-promotie en ERE-opbrengst | ja | ja |

Zet nooit iets achter Plus dat nu gratis is zonder het hier te veranderen.

Elke Plus-controle in app en web gaat via één vraag aan de server:
`GET /api/homes/{id}/entitlement` → `{ plus, source, until, reason }`. Nooit via een vlag in de app
of een bonnetje van de store: die staan op het toestel van de klant.

## Hoe een abonnement loopt

Per woning betaalt **precies één partij**: de installateur, de bewoner zelf, of niemand. Daar staat
een unieke index op in de database, zodat het niet kan voorkomen dat er twee tegelijk betalen.

Bronnen: `trial`, `installer`, `apple`, `google`, `manual`.

### Bronwissel

Er is een vierde stand naast active, cancelled en expired: **pending**. Die bestaat om één situatie
op te lossen. Iemand heeft een jaar bij Apple afgerekend en zijn installateur neemt het over. Zijn
Apple-periode meteen beëindigen zou betekenen dat hij betaalt voor maanden die hij niet krijgt, en
terugbetalen kunnen wij niet: dat geld staat bij Apple. Dus loopt de betaalde periode uit en staat
de nieuwe klaar als `pending`; zodra de oude afloopt telt de nieuwe. Hij krijgt wel het advies om
zijn abonnement in de store op te zeggen, want dat kan alleen hij.

Zat hij in zijn proefperiode of had hij Plus via zijn installateur, dan stopt dat meteen: daar is
niet voor betaald, dus er gaat niets verloren.

### Proefperiode

Dertig dagen bij de eerste koppeling van een woning, één keer. Opnieuw koppelen geeft geen tweede
proef. Zeven dagen voor het einde één pushmelding, nooit meer dan één.

### Dagelijkse controle

Verlopen periodes gaan op `expired`, klaarstaande op `active`, en proefperiodes die bijna om zijn
krijgen hun melding.

## Installateurs

Een installateur staat op `free` of `per_household`, met een prijs per actief huishouden (standaard
€ 1,25 excl. btw). Komt een bewoner binnen met zijn code, dan regelt de installateur zijn Plus:
twaalf maanden bij `free`, doorlopend bij `per_household`. Die code mag alleen mét het vinkje dat de
installateur mag meekijken; zonder toestemming mag hij die gegevens niet zien en is er geen grond
om hem eraan te koppelen.

`GET /api/installer/billing` toont het maandbedrag. **Alleen tonen, niet factureren.** De telling
kijkt naar abonnementen waarvan deze installateur de bron is, dus een klant die zelf in de store
betaalt valt eruit.

## LaadCash-promotie

Niet achter de Plus-vlag: dit geldt voor iedereen met een laadpaal. Het is ons eigen product, dus
het mag nadrukkelijker dan een gewone aanbieding, maar niet irritant.

Het bedrag is geladen kWh over het afgelopen jaar maal € 0,118, afgerond op tientallen.

| Kanaal | Wanneer |
| --- | --- |
| Popup | bij het koppelen van de laadpaal, één keer |
| Tegel | permanent, zolang er geen LaadCash-koppeling is |
| Push | 7 dagen na koppelen, daarna elke 30 dagen, hoogstens 4 keer, tussen 9:00 en 20:00 |
| E-mail | 3 dagen na koppelen, en een jaar-mail op 1 december |

Vier grenzen, en die zijn er niet voor de vorm:

- **Nooit een bedrag zonder uitgelezen laadsessie.** Een gegokt getal dat later niet klopt kost je
  het vertrouwen voor de hele app. Daarom ook minstens dertig meetdagen: uit één vakantieweek met
  veel laden rolt anders een bedrag van duizenden euro's.
- **Onder € 50 per jaar niets.** Daar val je niemand voor lastig.
- **Nooit 's nachts, hoogstens vier keer.**
- **Na "niet meer tonen" nooit meer.** Anders zet hij alle meldingen uit en ben je hem ook kwijt
  voor een storingsmelding.

Zodra LaadCash terugmeldt dat iemand zich heeft aangemeld stopt alle promotie en toont de tegel de
echte opbrengst. Wat LaadCash daarvoor moet doen staat in `docs/LAADCASH-KOPPELING.md`.

## Aanbiedingen

Maximaal drie kaarten, alleen voor Gratis, en alleen als de eigen data het rechtvaardigt. **Geen
advertentie-SDK's, nooit.**

Nu één kaart: meer dan 40 procent van de opwek teruggeleverd en geen batterij gekoppeld → offerte
bij de eigen installateur. Klikken maakt een lead. Het dynamische contract komt er pas bij als de
tariefvergelijking in Plus af is; iemand laten overstappen zonder te kunnen laten zien wat het hem
oplevert is precies het soort aanbieding waar je later spijt van krijgt.

## Besparing vanaf 2027

Tot en met 31 december 2026 wordt gesaldeerd: elke teruggeleverde kilowattuur telt voor de volle
leveringsprijs. Daarna verschilt het per leverancier wat je terugkrijgt, en rekenen de meeste er ook
nog terugleverkosten bovenop.

`supplier_tariffs` houdt dat bij, met de hand gevuld en met een ingangsdatum per rij zodat een oude
berekening blijft kloppen. Gratis rekent met het landelijke gemiddelde (rij `standaard`), Plus met
de leverancier van deze bewoner. Dat verschil is geen kleingeld: hetzelfde dak van 2000 kWh levert
bij de ene leverancier € 140 op en bij de andere € 340 aan kosten.

## Wat we niet doen

- Geen advertentie-SDK's.
- Geen Mollie, Stripe of eigen betaalpagina voor consumenten.
- Geen prijzen in de app.
- Geen entitlement zonder dat de store het bevestigd heeft.
- Geen installateurslogo's; Wattzicht blijft het merk, de installateur staat als naam bij "jouw
  installateur".
