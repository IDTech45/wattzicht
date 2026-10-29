# Marktonderzoek: één energie-app voor zonnepanelen, batterij en laadpaal

Oct 29, 2026 · @Ibrahim

## Het idee

Eén app voor iOS en Android die zonnepanelen, thuisbatterij en laadpaal van verschillende merken in één scherm toont: opwek, verbruik, laadstatus en bespaarde euro's. Aanleiding: Triple Energy ziet dat klanten nu drie of vier fabrikant-apps naast elkaar moeten openen en beschikt al over de API-toegang van de merken die zij installeren.

De kernvraag van dit onderzoek is tweeledig: bestaat zoiets al, en is er een verdienmodel dat de bouw- en onderhoudskosten rechtvaardigt?

## Wat er al bestaat

Het idee bestaat al in drie vormen, maar geen enkele is zowel merkonafhankelijk, consumentvriendelijk én los van een energiecontract. Dat gat is de kans; de concurrentie is wel sterk en kapitaalkrachtig.

| Oplossing | Type | Merkonafhankelijk? | Wat de klant nodig heeft | Prijs |
| --- | --- | --- | --- | --- |
| [Zonneplan app](https://www.zonneplan.nl/zonneplan-app) | Energieleverancier + eigen hardware | Deels: eigen omvormer, batterij en laadpaal; laadpaal werkt wel met alle zonnepanelen | Zonneplan-contract en -producten | Gratis bij contract |
| [Tibber app](https://solarmagazine.nl/nieuws-zonne-energie/i44753/tibber-elektrisch-rijden-is-slim-zonnepanelen-blijven-interessant) | Energieleverancier met "Power-ups" | Ja voor laadpalen, auto's en steeds meer batterijen via koppelingen | Tibber dynamisch contract + Pulse P1-meter | Contract; slimme batterijsturing ca. €5,99/mnd |
| [Frank Energie app](https://apps.apple.com/se/app/frank-energie/id1546000285?l=en-GB) | Energieleverancier | Beperkt: slim laden en batterijsturing voor ondersteunde merken | Frank-contract | Gratis bij contract |
| [Home Assistant](https://www.home-assistant.io/docs/energy) | Open-source smarthome | Ja, honderden merken via integraties | Eigen server (Raspberry Pi), technische kennis | Gratis software, cloud-abonnement optioneel |
| [Homey Pro](https://homey.app/en-lt/news/introducing-the-homey-energy-dongle) | Smarthome-hub | Ja, via apps in de Homey-store | Homey Pro (ca. €400) + Energy Dongle €39 | Eenmalig hardware |
| [HomeWizard](https://www.homewizard.com/energy-plus/) | P1-meter + app | Alleen eigen meetproducten; geen sturing van laadpaal of batterij van derden | HomeWizard-hardware | App gratis, Energy+ €0,99/mnd of €11,95/jr |
| Fabrikant-apps (SolarEdge, Enphase, Alfen, Zaptec, Sessy, AlphaESS) | Per merk | Nee | Hardware van dat merk | Gratis |

De energieleveranciers bouwen hun eigen app actief uit tot het "ene scherm": Tibber lanceerde in januari 2026 een eigen batterij die samen met zonnepanelen en auto via de Tibber-app wordt beheerd, en Zonneplan combineert in zijn app zonnepanelen, batterij, laadpaal en dynamisch contract. Beide binden de klant echter aan hun eigen energiecontract. Home Assistant en Homey doen technisch precies wat Triple Energy beschrijft, maar vragen een hobbyist: eigen hardware, instellen van integraties, geen installateur-ondersteuning.

Wat ontbreekt: een kant-en-klare consumenten-app die de installateur bij oplevering voor de klant inricht, met alle merken die de installateur plaatst, zonder dat de klant van energieleverancier hoeft te wisselen of zelf een server moet draaien.

**Aanvulling na second opinion (29 okt 2026):** het gat is kleiner dan hierboven staat. [Eniris / SmartgridOne](https://eniris.com/our-products/distributors) (Gent) biedt installateurs al een white-label cloudplatform en app met 250+ merken (SolarEdge, SMA, Huawei, Victron, Growatt, AlphaESS, GoodWe) en 150.000 installaties; prijzen op aanvraag, de consumentenapp heeft in de App Store nog nauwelijks reviews. Kleinere NL-spelers die al merkonafhankelijk sturen: [GridSense](https://apps.apple.com/bf/app/gridsense/id6670372565) (SolarEdge, GoodWe, FoxESS, Afore) en [Powerselect](https://apps.apple.com/nl/app/powerselect/id6754448698) (GivEnergy, Sonnen). Eerste vraag aan Triple Energy wordt daarmee: waarom niet gewoon Eniris afnemen?

**Vraag op fora:** het probleem leeft. Een [Bouwinfo-draad](https://www.bouwinfo.be/bouwforum/threads/app-voor-zonnepanelen-en-batterijen-van-verschillende-merken.416616/) (april 2026) stelt letterlijk deze vraag; de antwoorden zijn HomeWizard of Home Assistant. De Homey-draad [SlimLaden Thuis & Auto](https://community.homey.app/t/app-pro-slimladen-thuis-auto/140156) telt ruim 7.000 berichten van gebruikers die SolarEdge, Marstek, Zonneplan en laadpalen aan elkaar knopen. Dat zijn echter hobbyisten die gratis verwachten; betalingsbereidheid blijkt nergens uit de fora.

**Tweede aanvulling: Zerofy.** [Zerofy](https://www.em-power.eu/exhibitorlist/zerofy) (Tallinn/Zürich, opgericht 2022) is de meest directe concurrent: een cloud-HEMS-app zonder extra hardware, white-label voor installateurs en energiebedrijven, 40–70+ merken en 500+ apparaten (o.a. Enphase, SMA, Fronius, Deye, Solarman, Anker), plus "Mission Control", een installateursdashboard over de hele klantenbasis. Volgens ChatGPT (niet door mij geverifieerd op de Zerofy-site): consument gratis of €8,99/mnd; white-label €7.500 opstart, €4 per gebruiker per maand met een minimum van €1.000/mnd; bouwen op hun API €3 per gebruiker per maand. Als die minimumbedragen kloppen, is Zerofy te duur voor een installateur met minder dan ca. 250 app-gebruikers. Dat segment, kleine en middelgrote installateurs zoals Triple Energy, is dan de enige echte niche voor een eigen, goedkopere monitoring-app.

## Marktomvang Nederland

De doelgroep met meerdere systemen groeit snel: zonnepanelen zijn gemeengoed, thuisladen nadert het miljoen aansluitingen en thuisbatterijen verdubbelen ongeveer per jaar. Juist de combinatie-huishoudens (zon + laadpaal + batterij) zijn de kopers van dit soort app.

| Segment | Aantal in Nederland | Peildatum | Bron |
| --- | --- | --- | --- |
| Huishoudens met zonnepanelen | bijna 3 miljoen (34–35% van de woningen) | begin 2026 | [Zonnepaneelcentrum / Netbeheer NL](https://zonnepaneelcentrum.nl/zonnepanelen-statistieken/) |
| Thuislaadpunten | 863.374 (+185.011 in 2025) | eind 2025 | [Solar Magazine](https://solarmagazine.nl/nieuws-zonne-energie/i43188/de-harde-cijfers-private-laadpunten-als-groeidiamant-thuisladen-en-werkladen-exploderen) |
| Geregistreerde thuisbatterijen | 79.231; werkelijk aantal vermoedelijk ca. 2x hoger | 30 sep 2026 | [Zonneplan](https://www.zonneplan.nl/thuisbatterij/aantal-thuisbatterijen-nederland), [jeroen.nl](https://jeroen.nl/blog/hoeveel-thuisbatterijen-zonnepanelen-in-nederland) |
| Huishoudens met dynamisch contract | 573.000 (ca. 7%) | 1 jan 2026 | [Onzestroom / Solar Magazine](https://onzestroom.nl/blogs/algemeen/thuisbatterij-saldering-2027/) |

De batterijmarkt is de motor. Van februari tot september 2026 steeg het aantal geregistreerde thuisbatterijen van 31.170 naar 79.231; alleen al in september kwamen er 8.535 bij. Het einde van de salderingsregeling op 1 januari 2027 jaagt dit aan: eigen verbruik wordt dan geld waard, en precies dat wil een klant in één scherm kunnen zien.

Realistische adresseerbare markt voor een merkonafhankelijke app: de huishoudens met minstens twee van de drie systemen. Dat zijn er nu grofweg 150.000 tot 300.000 (batterij-eigenaren hebben vrijwel altijd zonnepanelen; een flink deel heeft ook een laadpaal), groeiend met circa 100.000 per jaar. Dit is een schatting op basis van bovenstaande cijfers, geen gemeten getal.

## Technische haalbaarheid

De koppelingen hoeven niet zelf gebouwd te worden: aggregator-API's leveren tientallen merken via één integratie. Dat verlaagt de bouwkosten fors, maar maakt de app wel afhankelijk van een externe partij.

- [Enode](https://developers.enode.com/api/reference#batteries) (Oslo, Y Combinator) biedt één API voor omvormers, thuisbatterijen, laadpalen, EV's en warmtepompen: meer dan 1.000 apparaten van 80+ merken (stand eind 2025). Klanten zijn o.a. E.ON en OVO Energy. Prijzen zijn niet openbaar (enterprise-abonnement); reken op een vast maandbedrag plus een bedrag per gekoppeld apparaat.
- Telematica is een vergelijkbaar alternatief met 500+ integraties.
- Eigen API-toegang van Triple Energy per merk (SolarEdge, Enphase, Alfen, Zaptec, Sessy, AlphaESS enz.) kan als aanvulling of als goedkopere start voor de merken die zij zelf installeren. Nadeel: elk merk heeft eigen authenticatie, limieten en wijzigingen die onderhoud vragen.
- Verbruiksdata uit de slimme meter (P1) is nodig om besparing correct te berekenen; dat kan via een P1-dongle (HomeWizard, Tibber Pulse) of via de meetdata van de omvormer als die een netmeter heeft.

De app zelf is technisch standaard: één codebase voor iOS en Android (Flutter of React Native), een backend die de API-data ophaalt en opslaat, en een rekenmodule die opwek, eigen verbruik, laadsessies en teruglevering omzet naar euro's op basis van het tarief van de klant. Het lastigste onderdeel is niet de techniek maar de besparingsberekening geloofwaardig krijgen bij dynamische contracten en het einde van saldering in 2027.

## Verdienmodellen en wat het kan opleveren

Een losse consumentenapp met abonnement verdient zichzelf moeilijk terug; de markt accepteert lage prijzen (HomeWizard vraagt €0,99 per maand, Tibber €5,99 voor batterijsturing). De kansrijkste route is B2B2C: de installateur betaalt, de klant krijgt de app gratis bij oplevering.

| Model | Wie betaalt | Richtprijs | Opbrengst bij 5.000 gebruikers/jaar | Opbrengst bij 25.000 gebruikers/jaar |
| --- | --- | --- | --- | --- |
| Consumentenabonnement | Huishouden | €1–3 per maand | €60.000–180.000 | €300.000–900.000 |
| White-label voor installateurs | Installateur, per opgeleverde klant | €2–4 per klant per maand of €50–100 eenmalig | €120.000–240.000 | €600.000–1.200.000 |
| Upsell vanuit de app | Huishouden via installateur | Leadvergoeding €100–300 per verkochte batterij of laadpaal | Afhankelijk van conversie; bij 5% conversie €25.000–75.000 | €125.000–375.000 |
| Flexibiliteit / grid rewards | Netbeheerder of aggregator | Vergoeding per kWh of per beschikbare kW; vraagt sturing, niet alleen inzicht | Later stadium | Later stadium |

De cijfers in de tabel zijn rekenvoorbeelden op basis van de genoemde marktprijzen, geen prognoses. De realistische eerste stap is het white-label model: Triple Energy heeft de klanten, de API-codes en het moment van oplevering waarop de app wordt geïnstalleerd. Andere installateurs hebben hetzelfde probleem en zijn de logische tweede afzetmarkt; in Nederland werken honderden installatiebedrijven in zon, opslag en laden.

Het upsell-model is de verborgen waarde: een klant die in de app ziet dat 40% van zijn zonnestroom het net op gaat, is na 2027 een warme lead voor een batterij. Voor een installateur is dat meer waard dan de app-omzet zelf.

Flexibiliteitsinkomsten (batterij of laadpaal inzetten voor het net, zoals Tibber's Grid Rewards) zijn in potentie het grootst, maar vereisen aansturing van apparaten, contracten met aggregators en een vergunningstraject. Dat is een fase-2-onderwerp.

## Kosten en risico's

Een eerste versie (inzicht, geen sturing) kost naar schatting €80.000–150.000 aan ontwikkeling en daarna €3.000–6.000 per maand aan hosting, API-kosten en onderhoud. Dit zijn ervaringsgetallen voor een app met backend en vijf tot tien merkkoppelingen in Nederland, geen offerte.

| Kostenpost | Eerste jaar (schatting) |
| --- | --- |
| Ontwikkeling app (iOS + Android, één codebase) en backend | €80.000–150.000 |
| Aggregator-API (Enode of vergelijkbaar) | Niet openbaar; reken op €1.000–3.000 per maand plus per apparaat |
| Hosting, monitoring, app-store-accounts | €500–1.500 per maand |
| Onderhoud en merkwijzigingen | 15–25% van de bouwkosten per jaar |

De risico's wegen zwaarder dan de kosten:

- Energieleveranciers bouwen hetzelfde gratis in hun eigen app; Tibber en Zonneplan zijn al ver. Zij kunnen het gratis aanbieden omdat zij aan het energiecontract verdienen.
- Fabrikanten kunnen API-toegang beperken of betaald maken; de app is dan deels blind. Een aggregator als Enode vangt dat op, maar verschuift de afhankelijkheid.
- Zonder sturing (alleen inzicht) is de app na een paar weken minder interessant: de klant kijkt, maar hoeft niets te doen. Sturing (slim laden, batterij op prijs) is wat Tibber en Frank onderscheidt en vraagt veel meer bouw- en testwerk.
- De besparingsberekening moet kloppen met de jaarnota van de leverancier; afwijkingen leiden direct tot klachten bij de installateur.
- Privacy: energiedata is persoonsgegeven; AVG-verwerkersovereenkomsten met installateurs en toestemming per klant zijn nodig.

## Conclusie en vervolgstappen

Het idee is niet nieuw, maar de positie van Triple Energy wel: als installateur zit hij op het moment van oplevering, heeft hij de API-toegang en kent hij de merkencombinaties van zijn klanten. Een losse consumentenapp tegen Tibber en Zonneplan is kansloos; een white-label installateursapp die Triple Energy als eerste klant gebruikt en daarna aan collega-installateurs wordt verkocht, is een reëel businessmodel met een upsell-motor richting 2027.

Aanbevolen aanpak, klein beginnen:

1. Valideer de vraag bij Triple Energy zelf: hoeveel klanten hebben twee of meer merken, en wat zouden zij als installateur per klant per maand willen betalen? Eén gesprek van een uur levert dit op.
2. Bouw een klikbaar prototype (geen code) van het ene scherm: opwek, batterij, laadpaal, bespaard bedrag deze maand. Leg dit voor aan 10 klanten van Triple Energy.
3. Vraag bij Enode en Telematica een prijsopgave en een lijst welke merken van Triple Energy worden ondersteund.
4. Bouw een minimale eerste versie voor alleen de merken die Triple Energy installeert, met inzicht en besparing, zonder sturing. Doel: 100 klanten van Triple Energy live binnen vier maanden na start.
5. Beslis na die 100 klanten over het vervolg: sturing toevoegen, of eerst drie andere installateurs aanhaken.

Open vragen die het antwoord kunnen veranderen:

- Wil Triple Energy mede-investeren of alleen eerste klant zijn?
- Welke merken heeft hij exact, en bieden die een officiële API of alleen een scraper?
- Is er een energieleverancier die liever een white-label installateursapp koopt dan er zelf een bouwt? Dat zou een exit- of partnerroute zijn.

## Bronnen

- [Zonneplan: aantal thuisbatterijen in Nederland, oktober 2026](https://www.zonneplan.nl/thuisbatterij/aantal-thuisbatterijen-nederland)
- [jeroen.nl: thuisbatterijen en zonnepanelen per maand](https://jeroen.nl/blog/hoeveel-thuisbatterijen-zonnepanelen-in-nederland)
- [Solar Magazine: private laadpunten, maart 2026](https://solarmagazine.nl/nieuws-zonne-energie/i43188/de-harde-cijfers-private-laadpunten-als-groeidiamant-thuisladen-en-werkladen-exploderen)
- [Zonnepaneelcentrum: zonnepanelen statistieken 2026](https://zonnepaneelcentrum.nl/zonnepanelen-statistieken/)
- [Onzestroom: thuisbatterij en saldering 2027](https://onzestroom.nl/blogs/algemeen/thuisbatterij-saldering-2027/)
- [Zonneplan app](https://www.zonneplan.nl/zonneplan-app)
- [Solar Magazine: Tibber Smart Battery System, januari 2026](https://solarmagazine.nl/nieuws-zonne-energie/i42809/tibber-lanceert-nieuwe-thuisbatterij-tibber-smart-battery-system)
- [Slimster: Homevolt en Tibber-abonnement](https://slimster.nl/thuisbatterij/merken/homevolt-thuisbatterij/)
- [Tibber support: Power-ups](https://support.tibber.com/nl/articles/11170986-power-ups-voor-een-slimmer-huis)
- [Home Assistant: Energy dashboard](https://www.home-assistant.io/docs/energy)
- [Homey: Energy Dongle en Energy tab](https://homey.app/en-lt/news/introducing-the-homey-energy-dongle)
- [HomeWizard Energy+](https://www.homewizard.com/energy-plus/)
- [Enode developer docs](https://developers.enode.com/api/reference#batteries)
- [Dealroom: Enode bedrijfsprofiel](https://app.dealroom.co/companies/enode)
