# Apple en Google klaarzetten voor Wattzicht Plus

Wat jij moet aanmaken, en welke waarden daarna in Dokploy moeten. Niets hiervan kan ik zelf doen:
het zijn accounts op jouw naam.

Consumenten betalen uitsluitend via Apple In-App Purchase en Google Play Billing, ook het
jaarabonnement. De stores regelen factuur, btw en opzeggen. Een eigen betaalpartij komt later en dan
alleen voor installateurs, die zijn bedrijven.

## 1. De producten

Beide stores krijgen precies deze twee producten. De id's staan ook in de code
(`StoreProducts`) en in `GET /api/plans`; ze moeten letterlijk gelijk zijn.

| Product-id | Prijs incl. btw | Periode |
| --- | --- | --- |
| `nl.wattzicht.plus.monthly` | € 1,99 | per maand |
| `nl.wattzicht.plus.yearly` | € 21,99 | per jaar |

Allebei auto-renewing en **in dezelfde subscription-groep**. Dat laatste doet ertoe: binnen één groep
kan iemand van maand naar jaar wisselen zonder dubbel te betalen, en de store rekent het verschil
zelf af. In twee aparte groepen zou hij twee lopende abonnementen krijgen.

**Proefperiode in de store: uit.** Onze proefperiode van dertig dagen regelen wij zelf, zonder dat
iemand zijn betaalgegevens hoeft te geven. Een introductieperiode in de store erbovenop zou betekenen
dat sommige mensen zestig dagen gratis hebben en dat je niet meer kunt uitleggen waarom.

### App Store Connect

1. **Mijn apps → Wattzicht → Abonnementen** → groep aanmaken, naam `Wattzicht Plus`.
2. In die groep twee abonnementen met de id's hierboven.
3. Per abonnement: weergavenaam (`Wattzicht Plus maand` / `Wattzicht Plus jaar`), beschrijving, en de
   prijs voor Nederland. Apple rekent de andere landen zelf om.
4. **Introductory offers: geen.**
5. Review-informatie: een schermafbeelding van het Plus-scherm en een testaccount.

### Play Console

1. **Monetiseren → Abonnementen** → twee abonnementen met dezelfde id's.
2. Per abonnement één basisplan: `monthly` (P1M) en `yearly` (P1Y), beide automatisch verlengend.
3. Prijs voor Nederland zetten; de rest laat je Google omrekenen.
4. **Geen gratis proefperiode en geen intro-prijs.**

## 2. Small Business Program: doen

Allebei de stores houden standaard 30% in en verlagen dat naar 15% als je je aanmeldt als kleine
onderneming. Bij € 1,99 per maand is dat het verschil tussen € 1,39 en € 1,69 netto per klant. Bij
duizend abonnees scheelt dat € 3.600 per jaar, voor een formulier van vijf minuten.

- **Apple**: App Store Small Business Program, aanmelden via App Store Connect → Overeenkomsten. Geldt
  vanaf de maand na goedkeuring, zolang je onder $1 miljoen per jaar blijft.
- **Google**: in Play Console staat het verlaagde tarief standaard aan voor de eerste $1 miljoen per
  jaar; je hoeft alleen je accounts te koppelen als je er meerdere hebt.

Doe dit vóór de eerste verkoop: met terugwerkende kracht verlagen ze niet.

## 3. Sleutels voor de server

### Apple

In App Store Connect → **Gebruikers en toegang → Integraties → App Store Connect API**, sleutel
aanmaken met rol *In-App Purchase*. Je krijgt een `.p8`-bestand dat je maar één keer kunt downloaden.

| Variabele | Waar vandaan |
| --- | --- |
| `STORE_APPLE_KEY_ID` | Key ID naast de sleutel (tien tekens) |
| `STORE_APPLE_ISSUER_ID` | Issuer ID bovenaan dezelfde pagina |
| `STORE_APPLE_BUNDLE_ID` | `nl.wattzicht.app` |
| `STORE_APPLE_PRIVATE_KEY_PEM` | De inhoud van het `.p8`-bestand, inclusief de BEGIN- en END-regels |

### Google

In Google Cloud (het project achter de app) een **serviceaccount** maken, een JSON-sleutel
downloaden, en dat account in Play Console → **Gebruikers en rechten** toegang geven tot de app met
het recht *Financiële gegevens bekijken* en *Bestellingen beheren*.

| Variabele | Waar vandaan |
| --- | --- |
| `STORE_GOOGLE_PACKAGE_NAME` | `nl.wattzicht.app` |
| `STORE_GOOGLE_SERVICE_ACCOUNT_FILE` | pad naar het JSON-bestand op de server |

Het JSON-bestand gaat net als de Firebase-sleutel via een Dokploy File Mount, niet in een
omgevingsvariabele en niet in de repo.

**Zonder deze waarden staat het kopen van Plus uit.** De server geeft dan geen abonnement en zegt
dat de store nog niet is ingesteld. Dat is met opzet: liever niemand Plus dan iemand Plus op een
bonnetje dat niet is nagekeken.

## 4. Meldingen van de stores

Zonder deze meldingen weet de server niet dat een abonnement vandaag verlengd of opgezegd is. Dan
staat iemand die netjes betaalt morgen op verlopen.

| Store | Waar instellen | URL |
| --- | --- | --- |
| Apple | App Store Connect → App → App-informatie → App Store Server Notifications (**versie 2**) | `https://wattzicht.nl/api/subscriptions/notifications/apple` |
| Google | Play Console → Monetiseren → Monetisatie-instellingen → Realtime-meldingen | een Pub/Sub-topic dat pusht naar `https://wattzicht.nl/api/subscriptions/notifications/google` |

Zolang er nog geen eigen domein is, zet je `wattzicht.laadcash.nl` in plaats van `wattzicht.nl`.

Bij Google gaat het via Pub/Sub: maak een topic, geef
`google-play-developer-notifications@system.gserviceaccount.com` publiceerrechten op dat topic, en
maak een push-subscription naar bovenstaande URL.

Beide adressen verwerken dezelfde melding twee keer zonder gedoe. Dat is nodig, want allebei de
stores sturen opnieuw als ze niet snel genoeg een 200 terugkrijgen.

## 5. Over RevenueCat

RevenueCat neemt de verificatie en de meldingen van beide stores over en geeft er één webhook voor
terug. Gratis tot $2.500 omzet per maand, daarna 1% van de omzet.

Mijn advies: **nu niet**. Het stuk dat zij vervangen is gebouwd en getest, en het is precies het
stuk waarover je anders geen controle meer hebt: wie Plus heeft en waarom. Daar een derde partij
tussen zetten betekent dat je bij een storing bij hen niemand kunt helpen, en dat je abonnementen
later weer moet terugverhuizen als je toch van prijsmodel verandert.

Wanneer het wél het overwegen waard wordt: zodra je meer dan een paar honderd abonnees hebt én je
tegen het randje van de store-regels aan gaat zitten (promoties, upgrades tussen groepen, meerdere
landen met eigen prijzen). Dat is administratie die zij beter doen dan wij. Tot die tijd is het 1%
van je omzet voor iets dat al werkt.

## 6. Testen vóór de eerste echte verkoop

1. **Apple**: in App Store Connect een Sandbox-tester aanmaken, in de app uitloggen uit je
   gewone Apple-account, en met die tester kopen. Zet dan `STORE_APPLE_BASE_URL` op
   `https://api.storekit-sandbox.itunes.apple.com`, anders kent de productie-API die aankoop niet.
2. **Google**: je eigen e-mailadres als licentietester toevoegen in Play Console. Aankopen worden
   dan niet afgerekend en verlengen versneld (een maand duurt vijf minuten), zodat je een verlenging
   echt kunt zien binnenkomen.
3. Controleer in beide gevallen: `GET /api/homes/{id}/entitlement` geeft `plus: true` met de juiste
   bron, en na opzeggen in de store komt er binnen een paar minuten een melding binnen en staat het
   abonnement op gestopt.

Let op: zolang `PLUS_ENABLED` uit staat geeft dat endpoint `plus: false`, ook als de aankoop klopt.
De rij staat er dan wel. Zet de vlag tijdelijk aan om de hele keten te testen.
