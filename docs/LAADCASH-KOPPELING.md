# Taak voor de LaadCash-repo: aanmeldingen vanuit Wattzicht

Wattzicht is klaar. Dit is wat er aan de LaadCash-kant nog moet gebeuren, zodat een Wattzicht-
gebruiker met één klik LaadCash-klant wordt en de promotie daarna stopt.

Ik heb de LaadCash-repo niet aangeraakt; dit is de opdracht die je daar kunt uitvoeren.

## Wat Wattzicht doet

Iedereen met een gekoppelde laadpaal en nog geen LaadCash krijgt een popup, een tegel en hoogstens
vier pushmeldingen. De knop stuurt hem naar:

```
https://laadcash.nl/register?src=wattzicht&home=<woning-id>&email=<e-mailadres>
```

`home` is een GUID, `email` is url-gecodeerd. Beide mogen ontbreken; `src=wattzicht` staat er altijd.

## Wat LaadCash moet doen

### 1. De aanmeldpagina moet de parameters gebruiken

- `email` invullen in het aanmeldformulier, zodat hij het niet hoeft over te typen. Als dat adres al
  een LaadCash-account heeft: inloggen aanbieden in plaats van aanmelden, en daarna doorgaan met stap 3.
- `src=wattzicht` opslaan bij de klant als herkomst. Dat loopt al via de bestaande herkomstmeting;
  het is alleen een bron die er nog niet bij staat.
- `home` onveranderd meedragen tot het einde van de aanmelding. Dit is het nummer waarmee Wattzicht
  de woning herkent; zonder dat nummer kunnen we de promotie niet uitzetten.

Bewaar `home` bij de klant (bijvoorbeeld `WattzichtHomeId`), zodat stap 3 ook werkt als iemand de
aanmelding pas de volgende dag afmaakt.

### 2. Niets veranderen aan het inboeken

Een klant die via Wattzicht binnenkomt is een gewone LaadCash-klant: zelfde machtiging, zelfde
commissie, zelfde uitbetaling. Er is geen aparte behandeling en geen aparte prijs.

### 3. Terugmelden zodra de aanmelding rond is

Zodra de klant de aanmelding heeft afgerond (machtiging getekend of op zijn minst een account met
een gekoppelde laadpaal), één aanroep naar Wattzicht:

```
POST https://wattzicht.nl/api/laadcash/linked
X-Laadcash-Secret: <gedeeld geheim>
Content-Type: application/json

{ "homeId": "<de home-parameter uit de link>", "linkedAt": "2026-11-02T10:15:00Z" }
```

`linkedAt` mag weg; dan gebruikt Wattzicht het moment van de aanroep. Het antwoord is altijd 204,
ook voor een woning die Wattzicht niet kent: anders kan iemand met dit adres uitvissen welke
woning-id's bestaan.

Zolang er geen eigen domein is, is het adres `https://wattzicht.laadcash.nl/api/laadcash/linked`.

**Het geheim** zet je aan beide kanten in de omgeving: in Wattzicht als `LAADCASH_SHARED_SECRET`, in
LaadCash onder een eigen naam. Zonder dat geheim staat het endpoint in Wattzicht dicht. Dat is met
opzet: zonder slot kan iedereen de promotie voor een willekeurige woning uitzetten, en dan verlies
je de aanmelding die er misschien nog aan zat te komen.

### 4. Opbrengst teruggeven (later)

Zodra de koppeling er is, laat Wattzicht op de tegel staan wat LaadCash dit jaar heeft opgeleverd.
Daar is een manier voor nodig om dat bedrag op te vragen, bijvoorbeeld:

```
GET /api/v1/partners/wattzicht/earnings?homeId=<guid>
→ { "yearEur": 182.40, "ereCount": 1540, "lastPayoutAt": "2026-10-01" }
```

Dit is geen haast: zolang dit er niet is, toont Wattzicht alleen dat de woning gekoppeld is. Maar
het is wel het verschil tussen "je bent aangemeld" en "dit heeft het je opgeleverd", en dat tweede
is precies wat iemand zijn buurman laat vertellen.

## Volgorde

1. Geheim verzinnen en aan beide kanten in de omgeving zetten.
2. Aanmeldpagina: `email` vooraf invullen, `src` en `home` bewaren.
3. Terugmelden na afronding.
4. Later: opbrengst per woning.

Stap 1 tot en met 3 zijn samen een halve dag werk en maken het verschil tussen een promotie die
stopt als iemand zich aanmeldt, en een promotie die hem daarna nog drie keer lastigvalt met iets
wat hij al gedaan heeft.
