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

## Het meeste staat er al, bij LaadCash

Dit is de kortste weg: een Apple-account en een Play-account zijn per bedrijf, niet per app. Wat
LaadCash gebruikt, geldt dus ook hier. De geheimen in deze repo heten daarom precies hetzelfde als
daar, zodat je ze kunt overnemen in plaats van opnieuw aanmaken.

| Geheim | Nieuw maken? |
| --- | --- |
| `APPLE_TEAM_ID` | Nee, zelfde team |
| `APPSTORE_API_KEY_ID`, `APPSTORE_API_ISSUER_ID`, `APPSTORE_API_PRIVATE_KEY` | Nee, een API-sleutel geldt voor het hele account |
| `IOS_DIST_CERT_P12`, `IOS_DIST_CERT_PASSWORD` | Nee, een distributiecertificaat is per team en niet per app |
| `IOS_PROVISIONING_PROFILE` | **Ja.** Een profiel hoort bij één app-id, dus `nl.wattzicht.app` heeft zijn eigen |
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_STORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` | Nee, dezelfde upload-sleutel mag een tweede app ondertekenen |
| `PLAY_SERVICE_ACCOUNT_JSON` | Nee, wel even rechten geven op de nieuwe app |

Let op: GitHub geeft de waarde van een geheim nooit meer terug, ook niet aan jezelf. Overnemen gaat
dus vanaf de bestanden die je zelf bewaard hebt, niet vanuit de LaadCash-repo. Ben je het `.p8` of
het `.p12` kwijt, maak dan een nieuwe aan; dat kan allebei zonder Mac.

Over die gedeelde upload-sleutel: Play ondertekent in de winkel met een eigen sleutel per app, dus
dit koppelt de twee apps niet aan elkaar. Het enige nadeel is dat één gelekte sleutel beide apps
raakt. Voor twee apps van hetzelfde bedrijf is dat een prima ruil.

## Jij: wat er dan nog overblijft

**Google Play — klaar.** De app staat in Play Console op `nl.wattzicht.app`, het serviceaccount
heeft rechten en de vijf Android-geheimen staan in deze repo. `release-android` zet sinds 12-11-2026
een bundel in de internal testing-track; dat is met één klik te starten onder Actions.

**Apple — hier ligt het werk.** Het App-ID `nl.wattzicht.app` bestaat al (team ZP9APJFDZS, met
Sign in with Apple en pushmeldingen erop). Wat er nog moet:

1. De app aanmaken in App Store Connect onder datzelfde bundel-id.
2. Voor dat app-id één App Store-provisioningprofiel maken en downloaden. Let op: het moet het recht
   **Sign in with Apple** bevatten, anders weigert iOS het aanmeldscherm. Het verloopt na een jaar;
   zet er een herinnering op.
3. De zes Apple-geheimen overnemen uit wat LaadCash gebruikt, plus `IOS_PROVISIONING_PROFILE` van
   het nieuwe profiel. Zolang die geheimen leeg zijn stopt `release-ios` meteen met een melding; er
   gaat dan niets stuk en er worden geen macOS-minuten verbrand.

Anders dan bij LaadCash maakt MAUI het profiel niet zelf aan. Bij Capacitor regelt xcodebuild dat met
`-allowProvisioningUpdates`; hier moet het profiel er echt zijn.

## Wat er nog mist voordat ze de app doorlaten

Dit zijn geen nette-to-haves. Zonder deze punten word je afgewezen, en bij Google kost dat zo een
week heen en weer.

1. **Account verwijderen — gebouwd (13-11-2026).** Onderaan Meer staat "Account verwijderen" met
   twee bevestigingen, en `wattzicht.nl/account-verwijderen` is de openbare pagina voor wie de app
   al weg heeft. Achter de knop gaat alles per tabel echt weg; vier tests leggen vast dat elke tabel
   leeg is, dat de buren eraf blijven en dat tweemaal verwijderen niet klapt.
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
   de instellingen dus iets wat op een iPhone niet gebeurt. Het recht staat al in
   `Platforms/iOS/Entitlements.plist`, dus het profiel hoeft er later niet opnieuw voor gemaakt te
   worden.

## Volgorde die werkt

1. ~~Account verwijderen bouwen~~ — staat er sinds 13-11-2026.
2. De Apple-geheimen overnemen uit wat LaadCash al gebruikt, en het ene nieuwe provisioningprofiel
   maken (met Sign in with Apple erop).
3. `release-ios` starten; `release-android` draait al naar internal. Zelf installeren en doorlopen.
4. Winkelpagina's invullen, schermafbeeldingen maken, vragenlijsten beantwoorden.
5. Indienen voor beoordeling.

## Inloggen met Google en Apple

**Google staat klaar.** In het Cloud-project `wattzicht` (nummer 664049832837) is op 12-11-2026 het
toestemmingsscherm ingericht op naam Wattzicht, met wattzicht.nl als domein en
wattzicht.nl/privacy als privacyverklaring, en daarna op **In production** gezet. Dat laatste is
nodig: zolang het op Testing staat kan alleen een handjevol testgebruikers inloggen.

Er zijn drie client-id's, en alle drie staan ze in `appsettings.json` onder `ExternalAuth`. Dat mag:
een client-id is geen geheim, het zit zichtbaar in elke app-build. Het client secret van de
web-client gebruiken we niet en staat dus ook nergens.

| Soort | Waarvoor |
| --- | --- |
| Web | De ontvanger van het token. Android vraagt zijn id-token met dit id als `serverClientId`. |
| Android | Hoort bij pakket `nl.wattzicht.app`. Twee stuks: één op de **app-ondertekeningssleutel** van Play (03:5C:...:5F) en één op de **uploadsleutel** (7D:61:...:79) |
| iOS | Hoort bij bundel `nl.wattzicht.app` |

**De val waar ik zelf in liep:** Play toont op de pagina App-ondertekening twee vingerafdrukken, en
alleen die van de uploadsleutel staat als tekst in de pagina. De andere zit achter een kopieerknop.
Maar de app die mensen installeren is ondertekend met de **app-ondertekeningssleutel**, want Play
tekent elke bundel opnieuw. Registreer je alleen de uploadsleutel, dan weigert Google de aanmelding
met fout 10 (DEVELOPER_ERROR): "deze versie van de app wordt niet geaccepteerd". Dat gebeurde hier op
12-11-2026, en aan de app is dan niets te zien of te repareren.

Draait er ooit een build buiten Play om (een losse apk), dan tekent die met weer een andere sleutel
en moet ook die SHA-1 erbij. Anders weigert Google de aanmelding op dat toestel.

Na het toevoegen van een client kan het volgens Google vijf minuten tot een paar uur duren voordat
het overal geldt. Werkt het niet meteen, wacht dan even voordat je verder zoekt.

**Apple staat aan de serverkant klaar.** Het App-ID `nl.wattzicht.app` is op 12-11-2026 aangemaakt
onder team ZP9APJFDZS (DoganTech B.V.), met Sign in with Apple en pushmeldingen erop, en het
bundel-id staat in `ExternalAuth:AppleClientIds`. Datzelfde App-ID is ook wat de iOS-build nodig
heeft; zonder dat kan er niets naar TestFlight.

**En de knop in de app staat er sinds 13-11-2026 ook.** `Platforms/iOS/AppleLoginIos.cs` vraagt
via AuthenticationServices een ondertekend token op en stuurt dat naar onze server, die het bij
Apple controleert. Het recht `com.apple.developer.applesignin` staat in
`Platforms/iOS/Entitlements.plist` en moet ook op het provisioningprofiel staan; zonder dat doet de
knop op het toestel niets en is daar in de app niets van te zien.

Nog niet op een echte iPhone geprobeerd: dat kan pas als het profiel er is en er een TestFlight-build
staat. De code compileert wel voor iOS, dus hij gaat niet stuk op de build.

Let op het verborgen e-mailadres: wie "Verberg mijn e-mailadres" kiest krijgt een privaterelay-adres.
Post komt daar alleen aan als onze afzender bij Apple geregistreerd staat. Bij LaadCash is dat punt
al eens tegengekomen; voor Wattzicht staat het nog open.
