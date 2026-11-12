# De app in de winkel krijgen

Dit gaat over het uitbrengen zelf: de twee bouwstraten, de geheimen die ze nodig hebben, en wat er
nog ontbreekt voordat Apple en Google de app doorlaten. Over abonnementen en betalen gaat
[STORES.md](STORES.md); dat is een ander verhaal en kan later.

Alles wat hieronder onder "Jij" staat, kan alleen op jouw naam. De rest staat in de repo.

## De twee bouwstraten

| Workflow | Wat hij doet | Starten |
| --- | --- | --- |
| `release-android` | Bouwt een ondertekende .aab en zet hem in Google Play | Actions, of tag `android-v*` |
| `release-ios` | Bouwt een ondertekende .ipa en zet hem in TestFlight | Actions, of tag `ios-v*` |
| `testapk-android` | Een .apk voor je eigen telefoon, buiten de winkel om | Actions |

Het versienummer in de winkel komt uit `ApplicationDisplayVersion` in
`src/Wattzicht.App/Wattzicht.App.csproj` (nu 0.1.0). Het buildnummer zetten de workflows zelf op het
runnummer, zodat elke upload hoger is dan de vorige. Een nieuwe versie uitbrengen is dus: dat ene
nummer ophogen, pushen, workflow starten.

Beide workflows stoppen netjes als hun geheimen ontbreken. Je krijgt dan het bestand onder Artifacts
en een waarschuwing, geen mislukte run met een onleesbare fout.

> Tot 12-11-2026 kón `release-android` helemaal niet slagen: de aanroep miste `-f net9.0-android` en
> stopte met NETSDK1129, nog voordat er een bundel was. Dat is nu recht.

## Jij: Google Play

1. **App aanmaken** in Play Console, pakketnaam `nl.wattzicht.app`. Die naam ligt daarna vast en is
   nooit meer te wijzigen.
2. **Keystore maken** (eenmalig, en kwijtraken betekent: nooit meer een update op deze app):
   ```
   keytool -genkeypair -v -keystore wattzicht-upload.keystore -alias wattzicht-upload \
     -keyalg RSA -keysize 2048 -validity 10000
   ```
   Bewaar het bestand buiten de repo, bijvoorbeeld in je wachtwoordkluis.
3. **Serviceaccount** in Google Cloud, JSON-sleutel downloaden, en dat account in Play Console onder
   Gebruikers en rechten toegang geven tot deze app met het recht releases beheren.
4. **Geheimen zetten** in GitHub (Settings, Secrets and variables, Actions):
   `ANDROID_KEYSTORE_BASE64` (`base64 -w0 wattzicht-upload.keystore`), `ANDROID_KEYSTORE_PASSWORD`,
   `ANDROID_KEY_ALIAS`, `PLAY_SERVICE_ACCOUNT_JSON`.
5. Eerste upload naar het kanaal **internal**. Pas als die binnenstaat kun je de winkelpagina
   invullen; Google vraagt daar een bundel voor.

## Jij: Apple

1. **App-id** `nl.wattzicht.app` aanmaken op developer.apple.com, en in App Store Connect de app.
2. **Distributiecertificaat** (Apple Distribution) en een **App Store-provisioningprofiel** voor dat
   app-id maken en allebei downloaden.
3. **App Store Connect API-sleutel** aanmaken met de rol App Manager; je krijgt een `.p8` dat je maar
   één keer kunt downloaden.
4. **Geheimen zetten**: `IOS_DIST_CERT_P12` (base64 van je .p12), `IOS_DIST_CERT_PASSWORD`,
   `IOS_PROVISIONING_PROFILE` (base64 van het .mobileprovision), `APPLE_TEAM_ID`,
   `APPSTORE_API_KEY_ID`, `APPSTORE_API_ISSUER_ID`, `APPSTORE_API_PRIVATE_KEY` (de inhoud van de .p8).

Anders dan bij LaadCash maakt MAUI het profiel niet zelf aan. Het moet er dus echt zijn, en het
verloopt na een jaar. Zet er een herinnering op.

## Wat er nog mist voordat ze de app doorlaten

Dit zijn geen nette-to-haves. Zonder deze punten word je afgewezen, en bij Google kost dat zo een
week heen en weer.

1. **Account verwijderen. Dit is de grootste.** Google eist voor elke app waarin iemand een account
   maakt twee dingen: verwijderen vanuit de app zelf, én een openbare webpagina waar iemand het kan
   aanvragen zonder de app te installeren. Wij hebben geen van beide: er is geen endpoint, geen knop
   en geen pagina. Dit moet gebouwd worden.
2. **Privacyverklaring op een vast adres.** `wattzicht.nl/privacy` bestaat, dus dit is alleen nog het
   adres invullen in beide winkels.
3. **Data safety (Google) en App Privacy (Apple).** Een vragenlijst over wat je verzamelt. Voor ons:
   e-mailadres, adresgegevens van de woning, en meetgegevens van apparaten. Belangrijk om goed te
   zetten: de inloggegevens van lokale apparaten staan op het toestel zelf en gaan niet naar ons.
4. **Schermafbeeldingen.** Apple wil ze per schermformaat, Google minimaal twee plus een
   uitgelichte afbeelding van 1024 bij 500.
5. **Testaccount voor de beoordeling.** Allebei de winkels willen kunnen inloggen. Maak een account
   met demo-apparaten erin, zodat er wat te zien is. Let op de les van LaadCash: maak er een echte
   mailbox van, anders stuiteren de mails van de beoordelaar terug.
6. **Pushmeldingen op iPhone bestaan nog niet.** Op Android gaan ze via Firebase, op iOS is er nog
   geen APNs-sleutel en geen registratie in de app. Dat is geen afwijsgrond, maar de app belooft in
   de instellingen dus iets wat op een iPhone niet gebeurt.

## Volgorde die werkt

1. Account verwijderen bouwen (punt 1), want daar struikel je anders op.
2. Keystore en certificaten maken, geheimen zetten.
3. `release-android` naar internal, `release-ios` naar TestFlight. Zelf installeren en doorlopen.
4. Winkelpagina's invullen, schermafbeeldingen maken, vragenlijsten beantwoorden.
5. Indienen voor beoordeling.
