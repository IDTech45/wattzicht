# Wattzicht testen

Alles wat de app kan, zonder dat er een omvormer, batterij, laadpaal of slimme meter in huis hangt.
Dit is de volgorde die werkt; elke stap duurt seconden.

## 1. Een woning waarin iets te zien is

Maak een account (of log in), en tik op het beginscherm op **Proefwoning neerzetten**. Je krijgt vier
apparaten en de afgelopen zestig dagen ingevuld: zon, thuisbatterij, laadpaal en slimme meter.

Wat je daarna hoort te zien:

| Scherm | Wat er moet staan |
|---|---|
| Begin | Energiestromen nu: zon, huis, net, batterij met laadstand, laadpaal. 's Nachts laadt de auto en loopt de netstand op |
| Besparen · dag | Veertien dagen, elk met een eigen bedrag |
| Besparen · week | Opgewekt, zelf gebruikt, teruggeleverd en geladen; het weekbedrag klopt met de staven |
| Besparen · maand en jaar | Twee maanden geschiedenis, dus maand gevuld en jaar deels |
| Apparaten | Vier apparaten, alle vier "zojuist gezien", met de kaart "Dit is een proefwoning" erboven |
| Sturen | Uurprijzen van vandaag; tips blijven leeg zolang je geen dynamisch contract hebt |
| Meer | De tegel van LaadCash (verschijnt omdat er een laadpaal hangt) |

De curve is elke dag dezelfde: dezelfde datum geeft altijd hetzelfde beeld. Een zomerdag levert meer op
dan een winterdag, en de auto laadt drie nachten per week drie uur.

Weghalen kan bij **Apparaten → Proefwoning weghalen**. Heb je ook een echt apparaat gekoppeld, dan
blijven dat apparaat en zijn metingen staan.

## 2. Een echt merk koppelen

Apparaten → Koppelen. Wat er per merk nodig is:

| Merk | Wat je invult | Waar het vandaan komt |
|---|---|---|
| Growatt, SolaX, Enphase (cloud), Easee, Zaptec | je eigen inlog bij het merk | het portaal of de app van dat merk |
| SolarEdge | een API-sleutel | monitoring.solaredge.com → Admin → Site Access → API Access |
| Enphase (gateway), Alfen, Peblar, Sessy, P1-meter | het IP-adres in huis | je router; dit werkt alleen thuis op je eigen wifi |

Buitenshuis slaat de app de apparaten in huis over: die zijn over mobiel internet onbereikbaar en het
kostte alleen maar wachttijd.

## 3. De installateurskant

1. Meld een installateur aan op wattzicht.nl (knop voor installateurs).
2. Keur hem goed in het beheerpaneel. **Dit kan nu niet** zolang `Seed__AdminPassword` op productie op
   een wachtwoord staat dat in onze eigen broncode voorkomt; de server weigert dat dan. Zet er een
   eigen wachtwoord neer in Dokploy en herstart, dan kun je inloggen als `admin@wattzicht.nl`.
3. Log in de app in met het installateursaccount. Je komt in zijn eigen tabbalk: **Overzicht** met zijn
   koppelcode, de tellers en wat aandacht nodig heeft, en **Woningen** met zijn lijst.
4. Deel de code met de knop **Delen**.
5. Log in als bewoner, ga naar **Meer → Je installateur**, vul de code in en tik op Koppelen.
6. De woning verschijnt in het overzicht van de installateur. Tik erop voor meldingen, apparaten en
   opbrengst.

Loskoppelen kan bij de bewoner met één tik; de woning hoort daarna bij niemand en verdwijnt uit elk
portal.

## 4. LaadCash

De koppeling met LaadCash staat aan en het gedeelde geheim klopt aan beide kanten. Testen gaat zo:

1. Zorg dat de woning een laadpaal heeft (de proefwoning heeft er een).
2. Ga naar **Meer → LaadCash** en volg de link. Daarin zit het woning-id.
3. Meld je bij LaadCash aan met die link. LaadCash meldt de koppeling terug, de uitnodiging in
   Wattzicht stopt, en de geladen kilowatturen komen vanaf dan elk kwartier mee.

## 5. Lokaal draaien

```
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
```

Dit vraagt om `POSTGRES_PASSWORD`, `JWT_SECRET` en `WZ_PROTECTION_KEY` in een `.env`. De API staat dan
op `http://localhost:8080` (Swagger op `/swagger`), de website op `http://localhost:8081`.

De app in Debug praat met `10.0.2.2:8080`, het adres van je eigen machine vanuit een Android-emulator.
Installeren op een draaiende emulator:

```
dotnet build src/Wattzicht.App/Wattzicht.App.csproj -f net9.0-android -c Debug -t:Install
```

Let op: `adb shell pm clear` wist bij een debugbuild ook de snel-deploy-bestanden, en dan start de app
niet meer. Opnieuw installeren lost dat op.
