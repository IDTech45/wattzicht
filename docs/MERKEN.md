# Merkencatalogus: welke merken kan Wattzicht koppelen?

Stand: 30 oktober 2026. Onderzoek per merk naar cloud-API, lokale API, benodigde invoer van de klant, Home Assistant-integratie (HA), ondersteuning bij de aggregators Enode en Zerofy, en een inschatting van de koppel-moeilijkheid. De machineleesbare versie staat in `src/Wattzicht.Shared/brands.json`.

**Moeilijkheid:**
- **Makkelijk**: klant vult zelf token, IP of eigen inloggegevens in; geen tussenkomst van derden.
- **Middel**: developer-/partneraccount aanvragen, installateur moet iets aanzetten, of alleen een onofficiële (reverse-engineered) koppeling.
- **Moeilijk**: partner-only, betaald per request, of geen bruikbare API.

**Aggregators (gecontroleerd op 30-10-2026):**
- [Enode](https://enode.com/connect) noemt 24 omvormermerken, 11 batterijmerken en 18 laadpaalmerken. In hun merkenlijst staan o.a. APsystems, Deye, Enphase, FoxESS, Fronius, GivEnergy, Growatt, Hoymiles, Huawei, SMA, Sofar, SolarEdge, Solax, Solis, Solplanet, Sungrow, Sunsynk, BYD, Tesla, Easee, go-e, myenergi, Peblar, Wallbox, Zaptec, Garo, KEBA, Charge Amps. Enode is enterprise-pricing (niet openbaar); klant koppelt via Enode Link met eigen fabrikant-login.
- [Zerofy](https://zerofy-docs.web.app/docs/integrations/pv-deye.html) ondersteunt o.a. Anker, EcoFlow, Enphase, FoxESS, Fronius, GoodWe, Growatt, Hoymiles, Huawei FusionSolar, SAJ, SMA, SolarEdge, Solarman, Solis, Sungrow, TSUN; meters HomeWizard-achtige via Shelly, Tibber Pulse, Solarman P1; laadpalen Easee, go-e, Fronius Wattpilot, Huawei SCharger, Wallbox, Zaptec en generiek OCPP.
- Belangrijk: Sessy, Marstek, HomeWizard, Alfen, Peblar, Zonneplan en Sigenergy zijn typisch Nederlandse/Europese merken die bij **geen van beide** aggregators volledig zitten (Peblar wel bij Enode). Daar moet Wattzicht zelf bouwen; dat is tegelijk de onderscheidende waarde.

---

## 1. Omvormers / zon

| Merk | Cloud-API | Lokale API | Klant vult in | HA-integratie | Enode / Zerofy | Documentatie | Moeilijkheid |
|---|---|---|---|---|---|---|---|
| **Growatt** | Ja, Open API v1; token maakt klant zelf aan (ShinePhone > Ik > API-token). Rate-limits streng. | Modbus RTU/TCP via dongle (ShineWiFi-X met Modbus-firmware) | Token | core `growatt_server` (onofficieel), HACS `growatt_modbus` | Enode ja / Zerofy ja | [HA growatt_server](https://www.home-assistant.io/integrations/growatt_server/), [Enode×Growatt](https://enode.com/blog/partnerships/growatt-and-enode-partner-to-unlock-smarter-energy-management-for-households-worldwide) | **Makkelijk** (gebouwd) |
| **SolarEdge** | Ja, Monitoring API; API-key maakt de site-eigenaar zelf aan onder Admin > Site-toegang, **mits** de installateur hem eigenaarsrechten gaf. Anders via installateur. | Modbus TCP poort 1502 (SunSpec), aanzetten in SetApp | API-key + Site-ID (of IP) | core `solaredge` (cloud), HACS `solaredge-modbus-multi` | Enode ja / Zerofy ja | [HA solaredge](https://www.home-assistant.io/integrations/solaredge), [forumdraad rechten](https://community.home-assistant.io/t/solaredge-api-access-integration/893804) | **Makkelijk** (onzeker: eigenaarsrechten per installatie) |
| **Fronius** | Solar.web API alleen voor partners | Ja, Solar API v1 (JSON, geen login); bij Gen24 eerst aanzetten in webinterface | IP | core `fronius` | Enode ja / Zerofy ja | [HA fronius](https://www.home-assistant.io/integrations/fronius/) | **Makkelijk** (alleen lokaal; vereist app in thuisnetwerk of lokale bridge) |
| **GoodWe (SEMS)** | SEMS Portal heeft geen officiële consumenten-API; onofficiële koppeling met SEMS-login werkt | Ja, UDP poort 8899 op de dongle (ook Modbus TCP bij nieuwere) | IP (lokaal) of SEMS e-mail+wachtwoord | core `goodwe` (lokaal), HACS `sems` (cloud) | Enode nee (onzeker) / Zerofy ja | [HA goodwe](https://home-assistant.io/integrations/goodwe) | **Makkelijk** (lokaal) / Middel (cloud onofficieel) |
| **Deye / Solarman** | Solarman Smart API: appId+secret aanvragen per e-mail bij Solarman (customerservice@solarmanpv.com); daarna logt klant in met eigen account | Ja, Solarman V5-protocol op de wifi-logger (poort 8899) | IP + serienummer logger | HACS `solarman` (davidrapan), zeer actief | Enode ja (Deye) / Zerofy ja (Deye én Solarman) | [HA-draad Solarman API](https://community.home-assistant.io/t/collect-inverter-data-from-solarman-api/328760), [ha-solarman](https://github.com/davidrapan/ha-solarman) | **Makkelijk** (lokaal) / Middel (cloud, eenmalig appId aanvragen) |
| **SolaX** | Ja, twee smaken: oude token (SolaXCloud > Service > Derde partij) en nieuw Developer Portal (Client ID/Secret, gratis) | Modbus TCP via Pocket LAN/WiFi dongle; oudere lokale JSON-API | Token + registratienummer dongle, of IP | core `solax` (lokaal), HACS `solax_modbus`, HACS SolaX Cloud | Enode ja / Zerofy nee | [HA-draad SolaX developer API](https://community.home-assistant.io/t/solax-power-developer-portal-api-for-home-assistant/1023369) | **Makkelijk** |
| **FoxESS** | Ja, Open API; klant genereert zelf API-key op developer-eu.foxesscloud.com | Modbus TCP/RTU via dongle | API-key (serienummer wordt opgehaald) | HACS `foxess-ha` (cloud), HACS `foxess_modbus` | Enode ja / Zerofy ja | [foxess-ha](https://github.com/macxq/foxess-ha) | **Makkelijk** |
| **AlphaESS** | Ja, officiële Open API; klant registreert zelf op open.alphaess.com en koppelt systeem met serienummer + CheckCode | Modbus TCP op sommige modellen (SMILE), beperkt gedocumenteerd | AppID + AppSecret | HACS `homeassistant-alphaESS` | Enode nee (onzeker) / Zerofy nee | [homeassistant-alphaESS](https://www.github.com/CharlesGillanders/homeassistant-alphaESS) | **Makkelijk** (registratiestap is wel wat werk; ≥10 s tussen calls) |
| **APsystems** | EMA-cloud: geen openbare API (onofficiële scrapers) | Ja: EZ1 lokale REST (aanzetten in app), ECU-R/ECU-C lokaal via poort 8899 | IP | core `apsystems` (EZ1), HACS `apsystems_ecur` | Enode ja / Zerofy nee | [HA apsystems](https://www.home-assistant.io/integrations/apsystems/) | **Makkelijk** (lokaal) |
| **Victron (VRM)** | Ja, VRM API; klant maakt zelf access-token aan in VRM | Ja, Modbus TCP en MQTT op GX-apparaat | VRM-token (of IP GX) | HACS `hass-victron` (Modbus) | Enode nee / Zerofy nee | [VRM API docs](https://vrm-api-docs.victronenergy.com/) | **Makkelijk** |
| **Enphase** | Ja, Enlighten API v4 via OAuth; vereist developer-plan: Watt gratis (1.000 hits/mnd, veel te weinig), Kilowatt $249/mnd, Megawatt $999/mnd | Ja, Envoy/IQ Gateway lokale API; firmware 7+ vereist token dat met Enlighten-login wordt opgehaald | IP Envoy + Enlighten e-mail/wachtwoord (lokaal) of OAuth-consent (cloud) | core `enphase_envoy` (lokaal, Platinum) | Enode ja / Zerofy ja | [Enphase developer plans](https://developer-v4.enphase.com/developer-plans), [HA enphase_envoy](https://www.home-assistant.io/integrations/enphase_envoy/) | **Middel** (lokaal makkelijk, cloud betaald) |
| **SMA** | Sunny Portal / ennexOS: geen publieke API; partnerprogramma (Enode heeft die) | Ja: Webconnect (oudere) en ennexOS (nieuwere) lokaal met wachtwoord; Speedwire | IP + gebruiker/wachtwoord | core `sma` | Enode ja / Zerofy ja | [HA sma](https://www.home-assistant.io/integrations/sma/), [pysma-plus](https://pypi.org/project/pysma-plus) | **Middel** (alleen lokaal, of via aggregator) |
| **Huawei (FusionSolar)** | Ja, Northbound API, maar het account moet door de **installateur** worden aangemaakt. Alternatief: Kiosk-URL (geen login; alleen vermogen en totalen, ~30 min vertraging) | Modbus TCP via SDongle; installateur moet dit aanzetten | Kiosk-URL, of Northbound-login, of IP dongle | HACS `FusionSolar` (kiosk/Northbound), HACS `huawei_solar` (Modbus) | Enode ja / Zerofy ja | [homeassistant-fusionsolar](https://github.com/tijsverkoyen/homeassistant-fusionsolar) | **Middel** (voor Triple Energie als installateur zelf prima te doen) |
| **Sungrow (iSolarCloud)** | Ja, iSolarCloud OpenAPI: developer-account met appkey + secret; klant logt daarna in met eigen iSolarCloud-account | Ja, Modbus TCP op WiNet-S dongle (poort 502) | IP WiNet (lokaal) of iSolarCloud-login | HACS `SungrowInverter` (Modbus), HACS iSolarCloud | Enode ja / Zerofy ja | [HA-draad iSolarCloud](https://community.home-assistant.io/t/sungrow-isolarcloud-integration-custom-hacs-sensors-optional-charge-discharge-control/1017858) | **Middel** |
| **Hoymiles (S-Miles)** | Geen officiële consumenten-API; onofficiële koppeling met S-Miles-login | Modbus TCP op DTU-Pro; HMS-W-serie lokaal via OpenDTU/AhoyDTU | S-Miles e-mail+wachtwoord of IP DTU | HACS `hoymiles_cloud` (onofficieel), HACS `ha-hoymiles-modbus-tcp` | Enode ja / Zerofy ja | [homeassistant-hoymiles-cloud](https://github.com/Philra94/homeassistant-hoymiles-cloud) | **Middel** (onofficieel) |
| **Zonneplan Evo** | Ja, Zonneplan-API met e-mail + eenmalige code (OTP); geen developer-account nodig, maar wel onofficieel voor Evo/Nexus-data | Nee (Evo spreekt alleen met Zonneplan-cloud) | E-mailadres (OTP) | core `zonneplan` (2026.10, alleen prijzen/verbruik), HACS `zonneplan_one` (Evo, Nexus, batterij, laadpaal) | Enode nee / Zerofy nee | [HA zonneplan](https://www.home-assistant.io/integrations/zonneplan/), [zonneplan_one](https://github.com/fsaris/home-assistant-zonneplan-one) | **Middel** (API onofficieel; klant heeft Zonneplan-contract) |
| **Sigenergy** | SigenCloud heeft een partner-API (onzeker; niet publiek gedocumenteerd) | Ja, Modbus TCP (poort 502) op SigenStor; **installateur** moet dit aanzetten in de Sigenergy-app; firmware SPC109+ | IP + device-ID's | HACS `Sigenergy-Local-Modbus` (omvormer, batterij, laadpaal in één) | Enode nee / Zerofy nee | [Sigenergy-Local-Modbus](https://github.com/TypQxQ/Sigenergy-Local-Modbus) | **Middel** |

## 2. Thuisbatterijen

| Merk | Cloud-API | Lokale API | Klant vult in | HA-integratie | Enode / Zerofy | Documentatie | Moeilijkheid |
|---|---|---|---|---|---|---|---|
| **Sessy** | Ja (cloud-API, inloggen met Sessy-account) | Ja, REST op de dongle (basic auth, poort 80) | IP + gebruikersnaam/wachtwoord van de sticker | HACS `sessy` | Enode nee / Zerofy nee | [api_docs_dongle.txt](https://www.sessy.nl/wp-content/uploads/2023/06/api_docs_dongle.txt) | **Makkelijk** (gebouwd) |
| **Marstek** | Marstek heeft een "Device Open API"-PDF gepubliceerd (lokaal); cloud alleen via onofficiële app-API | Ja: Venus E v3 / Venus D met "Local API" (UDP 30000, JSON) aan te zetten in de app; oudere Venus via RS485-naar-wifi-adapter met Modbus | IP (en bij Modbus: adapter + unit-ID) | HACS `marstek_local_api` (v1.x, jan 2026), HACS `marstek_venus_modbus` | Enode nee / Zerofy nee | [marstek_venus_modbus](https://github.com/ViperRNMC/marstek_venus_modbus), [HA-draad Local API](https://community.home-assistant.io/t/marstek-local-api-v1-0-0-stable-release/942264) | **Makkelijk** (nieuwe modellen) / Middel (oude modellen: extra hardware) |
| **HomeWizard Plug-in Battery** | HomeWizard-cloud-API in bèta (onzeker, nog niet publiek beschikbaar) | Ja, lokale API v2: koppelen door op de knop van de batterij te drukken | IP (+ knop indrukken) | core `homewizard_energy` | Enode nee / Zerofy nee | [HA homewizard_energy](https://www.home-assistant.io/integrations/homewizard_energy/), [API-docs](https://api-documentation.homewizard.com/docs/introduction) | **Makkelijk** (alleen lokaal) |
| **AlphaESS** | Zie omvormer: officiële Open API | Beperkt | AppID + AppSecret | HACS `homeassistant-alphaESS` | nee / nee | [homeassistant-alphaESS](https://www.github.com/CharlesGillanders/homeassistant-alphaESS) | **Makkelijk** |
| **Tesla Powerwall** | Ja, Fleet API, maar sinds 2025 betaald per request ($10 korting/mnd voor kleine ontwikkelaars) | Ja, Gateway lokale API (klantwachtwoord) | IP Gateway + wachtwoord | core `powerwall` (lokaal), core `tesla_fleet` (cloud) | Enode ja / Zerofy nee | [Tesla billing](https://developer.tesla.com/docs/fleet-api/billing-and-limits), [HA powerwall](https://www.home-assistant.io/integrations/powerwall/) | **Makkelijk** (lokaal) / Moeilijk (cloud) |
| **GivEnergy** | Ja, API-token maakt klant zelf aan op givenergy.cloud | Ja, Modbus TCP via dongle | API-token | HACS `ge_cloud`, HACS `givenergy-local` | Enode ja / Zerofy nee | [giv_tcp](https://github.com/GivEnergy/giv_tcp), [givenergy-local](https://github.com/cdpuk/givenergy-local) | **Makkelijk** (weinig in NL) |
| **sonnen** | sonnen-API voor partners | Ja, lokale API v2 met token uit de webinterface | IP + token | HACS `sonnenbatterie` | nee / nee | [ha_sonnenbatterie](https://github.com/weltmeyer/ha_sonnenbatterie) | **Makkelijk** (weinig in NL) |
| **Zendure** | Onofficiële cloud-API (met eigen account; advies: tweede account) | Ja bij nieuwere modellen (SolarFlow 800, Hyper 2000): zenSDK/REST en lokale MQTT | E-mail+wachtwoord of IP | HACS `Zendure-HA`, HACS `Zendure-HA-zenSDK` | Enode nee / Zerofy nee | [Zendure HA-wiki](https://wiki.zendure.com/en/zendure-software/zendure-home-assistant), [HA-draad REST](https://community.home-assistant.io/t/zendure-restapi-local-zero-export-control-for-solarflow-batteries/1029076) | **Middel** |
| **Anker SOLIX** | Onofficiële cloud-API (tweede account aanbevolen) | Ja, officieel: Modbus TCP op Solarbank 3 / Max AC / XE, aanzetten in de Anker-app | IP | HACS `ha-anker-solix-official` (door Anker zelf), HACS `hacs-anker-solix` (cloud) | Enode nee / Zerofy ja | [ha-anker-solix-official](https://github.com/anker-charging/ha-anker-solix-official) | **Middel** (oudere Solarbank alleen onofficieel) |
| **Enphase IQ Battery** | Zie Enphase (betaald developer-plan) | Via IQ Gateway lokaal | IP + Enlighten-login | core `enphase_envoy` | Enode ja / Zerofy ja | [HA enphase_envoy](https://www.home-assistant.io/integrations/enphase_envoy/) | **Middel** |
| **Huawei LUNA2000** | Via FusionSolar Northbound (installateur) | Via Modbus TCP dongle | Zie Huawei | HACS `FusionSolar`, HACS `huawei_solar` | Enode ja / Zerofy ja | [homeassistant-fusionsolar](https://github.com/tijsverkoyen/homeassistant-fusionsolar) | **Middel** |
| **Sigenergy SigenStor** | Zie Sigenergy | Modbus TCP (installateur zet aan) | IP | HACS `Sigenergy-Local-Modbus` | nee / nee | [Sigenergy-Local-Modbus](https://github.com/TypQxQ/Sigenergy-Local-Modbus) | **Middel** |
| **BYD Battery-Box** | Nee; BYD Be Connect is alleen een service-tool | Nee (BMU heeft een onofficiële lokale uitlezing) | n.v.t.: data komt via de omvormer | via omvormer; HACS `bydbatterybox` (beperkt) | Enode ja (via omvormer, onzeker) | n.v.t. | **Middel** (= moeilijkheid van de omvormer) |
| **Pylontech** | Nee | RS232/RS485-console, niet voor consumenten | n.v.t.: via omvormer | via omvormer (Deye, SolaX, Victron, GoodWe, Growatt) | n.v.t. | n.v.t. | **Middel** (= omvormer) |
| **Dyness** | Nee | Nee | n.v.t.: via omvormer | via omvormer | n.v.t. | n.v.t. | **Middel** (= omvormer) |

Toelichting "via omvormer": BYD, Pylontech en Dyness zijn DC-gekoppelde batterijpakketten zonder eigen netwerkinterface voor consumenten. Wattzicht moet ze in de UI tonen als "batterij" maar de data komt uit de Deye/SolaX/GoodWe/Growatt/SMA/Fronius-koppeling. In `brands.json` staan ze met `api: none` zodat de app de gebruiker kan doorverwijzen naar de omvormerkoppeling.

## 3. Laadpalen

| Merk | Cloud-API | Lokale API | Klant vult in | HA-integratie | Enode / Zerofy | Documentatie | Moeilijkheid |
|---|---|---|---|---|---|---|---|
| **Zaptec** | Ja, officiële API; eigen Zaptec-account volstaat (rollen bepalen toegang); wachtwoord-grant wordt uitgefaseerd richting OAuth | Nee | Zaptec e-mail + wachtwoord | HACS `zaptec` | Enode ja / Zerofy ja | [Zaptec API-auth](https://docs.zaptec.com/docs/api_authentication), [Zaptec help](https://help.zaptec.com/hc/en-001/articles/17623179508369-Integrate-with-Zaptec-using-the-API) | **Makkelijk** |
| **Easee** | Ja, officiële API (developer.easee.com), eigen account volstaat | Nee (lokale API staat op de wensenlijst van Easee) | Easee e-mail/telefoon + wachtwoord | HACS `easee_hass` | Enode ja / Zerofy ja | [easee_hass](https://github.com/nordicopen/easee_hass), [Easee local API feedback](https://feedback.easee.com/b/features/local-apis) | **Makkelijk** |
| **Wallbox** | Ja (myWallbox API, semi-officieel), eigen account | Nee (Modbus alleen op Copper/Commander business) | myWallbox e-mail + wachtwoord | core `wallbox` | Enode ja / Zerofy ja | [HA wallbox](https://www.home-assistant.io/integrations/wallbox/) | **Makkelijk** |
| **Peblar** | Peblar-cloud alleen via app | Ja, lokale REST API met token en Modbus TCP; klant zet dit zelf aan in de webinterface | IP + API-token | core `peblar` (Platinum, door Franck Nijhof) | Enode ja / Zerofy nee | [Peblar developer info](https://support.peblar.com/hc/en-gb/articles/35386010277138-Developer-information-Modbus-API-Rest-API), [HA-draad](https://community.home-assistant.io/t/peblar-home-ev-charger-with-local-rest-api/775064) | **Makkelijk** |
| **Volt Time** | Ja, Volt Time/Plugchoice Cloud API; klant maakt zelf API-token aan op app.plugchoice.com | Nee (lokale OCPP gepland) | Plugchoice API-token | Geen HA; Homey-app officieel | nee / nee | [Homey Volt Time Chargers](https://homey.app/nl-nl/app/com.volttime.chargers/) | **Makkelijk** (API zelf niet publiek gedocumenteerd: onzeker) |
| **myenergi (zappi)** | Ja, API met hub-serienummer + API-key uit myaccount | Nee | Hub-serienummer + API-key | HACS `myenergi` | Enode ja / Zerofy nee | [ha-myenergi](https://github.com/CJNE/ha-myenergi) | **Makkelijk** |
| **Tesla Wall Connector** | Via Fleet API (betaald) | Ja, Gen 3 lokale API zonder login | IP | core `tesla_wall_connector` | Enode ja (Tesla) / Zerofy nee | [HA tesla_wall_connector](https://www.home-assistant.io/integrations/tesla_wall_connector/) | **Makkelijk** |
| **go-e** | Ja, go-e cloud API (serienummer + token) | Ja, lokale HTTP API v2, aanzetten in app | IP | HACS `goecharger_api2` | Enode ja / Zerofy ja | [go-eCharger-API-v2](https://github.com/goecharger/go-eCharger-API-v2) | **Makkelijk** |
| **Shelly Pro EV** | Shelly Cloud | Ja, Shelly RPC lokaal | IP | core `shelly` | nee / Zerofy ja (Shelly meters) | [Shelly API-docs](https://shelly-api-docs.shelly.cloud/) | **Makkelijk** |
| **Alfen** | Nee (geen consumenten-cloud; Alfen gebruikt OCPP naar backoffices) | Ja: lokale HTTPS-API (admin-wachtwoord) en Modbus TCP (vereist betaalde licentie "Active Load Balancing") | IP + admin-wachtwoord | HACS `alfen_wallbox` (HTTPS), HACS `alfen_modbus` | Enode nee / Zerofy nee (alleen via OCPP) | [alfen_wallbox](https://github.com/leeyuentuen/alfen_wallbox), [evcc Alfen Eve](https://docs.evcc.io/en/chargers/alfen-eve) | **Middel** (meest geplaatste paal in NL, maar klant heeft admin-wachtwoord vaak niet; installateur wel) |
| **Webasto** | Nee | Ja, Modbus TCP op Next/Unite (aanzetten in webinterface) | IP | HACS `webasto-next-modbus` | nee / nee | [evcc wiki](https://github.com/evcc-io/evcc/wiki) | **Middel** |
| **Smappee EV Wall** | Ja, OAuth-API; client-id/secret per e-mail aanvragen (gratis privé, betaald commercieel) | Deels (monitors wel, EV Wall onzeker) | Client ID + secret + Smappee-login | core `smappee` | nee / nee | [HA smappee](https://www.home-assistant.io/integrations/smappee) | **Middel** |
| **Enphase IQ EV Charger** | Via Enlighten (betaald plan) | Via IQ Gateway (deels) | Zie Enphase | core `enphase_envoy` (deels) | Enode ja / Zerofy ja | [HA enphase_envoy](https://www.home-assistant.io/integrations/enphase_envoy/) | **Middel** |
| **Sigenergy AC Charger** | Zie Sigenergy | Modbus TCP, device-ID 1 | IP | HACS `Sigenergy-Local-Modbus` | nee / nee | [Sigenergy-Local-Modbus](https://github.com/TypQxQ/Sigenergy-Local-Modbus) | **Middel** |
| **Ratio Electric** | Alleen onofficieel (reverse-engineered app-API, `aioratio`); geen documentatie van Ratio | Nee | Ratio-app e-mail + wachtwoord | Geen HA; Homey-app onofficieel | nee / nee | [Homey Ratio Electric Charger](https://community.homey.app/t/app-pro-ratio-electric-charger/159271) | **Moeilijk** (zonder afspraak met Ratio) |
| **EVBox** | Everon-platform is afgebouwd; partner-API alleen voor laadnetwerken | Nee; alleen OCPP naar een eigen backend | n.v.t. | Geen; alleen via generieke `ocpp` | nee / Zerofy via OCPP | [HA OCPP](https://home-assistant-ocpp.readthedocs.io/) | **Moeilijk** |

Opmerking OCPP: Alfen, EVBox, Ratio, Volt Time, Peblar, Webasto en Zaptec spreken allemaal OCPP 1.6J. Eén eigen OCPP-backend in Wattzicht (zoals Zerofy doet) zou in één klap alle "moeilijke" laadpalen uitlezen, maar de paal kan dan niet meer tegelijk met een laadpas-backoffice verbonden zijn. Dat is een fase-2-optie voor thuisladers zonder zakelijke verrekening.

## 4. P1 / slimme meter

| Merk | Cloud-API | Lokale API | Klant vult in | HA-integratie | Enode / Zerofy | Documentatie | Moeilijkheid |
|---|---|---|---|---|---|---|---|
| **HomeWizard P1** | Cloud-API in bèta (onzeker) | Ja, lokale API v1/v2 (aanzetten in app) | IP | core `homewizard_energy` | nee / nee | [HomeWizard API](https://api-documentation.homewizard.com/docs/introduction) | **Makkelijk** |
| **Tibber Pulse** | Ja, GraphQL-API met persoonlijk token; realtime via websocket (vereist Tibber-account met Pulse; werkt zonder Tibber-contract: onzeker) | Nee (Pulse praat alleen met Tibber-cloud; lokale bridge-firmware is hobbyisten-werk) | Access-token | core `tibber` | nee / Zerofy ja | [Tibber developer](https://developer.tibber.com/docs/overview) | **Makkelijk** |
| **Shelly (Pro 3EM / EM)** | Shelly Cloud | Ja, RPC lokaal | IP | core `shelly` | nee / Zerofy ja | [Shelly API-docs](https://shelly-api-docs.shelly.cloud/) | **Makkelijk** |
| **SlimmeLezer(+)** | Nee | Ja, ESPHome native API / webserver | IP | core `esphome` | nee / nee | [SlimmeLezer+](https://www.zuidwijk.com/product/slimmelezer-plus/) | **Makkelijk** |

---

## Architectuurconsequentie: lokaal versus cloud

Opvallend veel "makkelijke" koppelingen zijn **lokaal** (Fronius, GoodWe, Deye, HomeWizard, Marstek, Peblar, Alfen, SMA, Sessy). Wattzicht draait de connectors nu in de backend (`Wattzicht.Api`) die de apparaten niet in het thuisnetwerk kan bereiken. Drie opties, in volgorde van voorkeur:

1. **App als lokale poller**: de MAUI-app leest lokale apparaten uit als de telefoon thuis op wifi zit en pusht samples naar de API. Simpel, geen extra hardware, maar geen data als niemand thuis is.
2. **Cloud waar mogelijk, lokaal als aanvulling**: eerst de merken met een cloud-API (Growatt, Sessy, SolarEdge, FoxESS, SolaX, AlphaESS, Zaptec, Easee, Wallbox, Tibber, Victron) die de backend 24/7 kan pollen.
3. **Lokale bridge (fase 2)**: een kleine Docker-/Raspberry-agent of een Home Assistant-add-on bij de klant. Dat is in feite wat HomeWizard en Zonneplan met eigen hardware doen.

## Aanbevolen bouwvolgorde (top 10)

Score = geschat marktaandeel NL (1–5) × gemak (makkelijk 3, middel 2, moeilijk 1). Growatt en Sessy zijn al gebouwd en staan buiten de ranglijst.

| # | Merk | Categorie | NL-aandeel | Gemak | Waarom nu |
|---|---|---|---|---|---|
| 1 | **HomeWizard P1 (+ Plug-in Battery)** | P1 / batterij | 5 | Makkelijk | Verreweg de meest voorkomende P1-meter in NL; zonder P1-data klopt geen enkele besparingsberekening. Zelfde integratie dekt ook de Plug-in Battery. Lokaal, dus meteen de lokale-poller-route bewijzen. |
| 2 | **SolarEdge** | omvormer | 5 | Makkelijk | Grootste premium-merk in NL, nette cloud-API met API-key; backend kan 24/7 pollen. Let op eigenaarsrechten per site. |
| 3 | **Marstek** | batterij | 5 | Makkelijk | Bestverkochte plug-in/thuisbatterij van 2025–2026 in NL; nieuwe Local API maakt uitlezen eenvoudig. Niet bij Enode/Zerofy: onderscheidend. |
| 4 | **Zaptec** | laadpaal | 4 | Makkelijk | Veel geplaatst door installateurs (ook Triple Energie), officiële cloud-API met eigen account. |
| 5 | **Easee** | laadpaal | 4 | Makkelijk | Idem; populair bij particulieren, officiële cloud-API. Samen met Zaptec dekt dit de helft van de nieuwe thuispalen. |
| 6 | **Deye / Solarman** | omvormer (+ Pylontech/Dyness) | 4 | Makkelijk (lokaal) | Dominante hybride omvormer in het batterijsegment; lokale logger-uitlezing dekt tevens de DC-batterijen Pylontech en Dyness. Zit al in `DeviceBrand`. |
| 7 | **Alfen** | laadpaal | 5 | Middel | Meest geïnstalleerde laadpaal van NL. Lokale HTTPS-API met admin-wachtwoord; de installateur heeft dat wachtwoord, wat het white-label-model precies past. |
| 8 | **Enphase (Envoy lokaal)** | omvormer + batterij | 5 | Middel | Zeer veel micro-omvormers in NL. Cloud-API is te duur ($249+/mnd), lokaal via IQ Gateway is gratis en dekt ook IQ Battery. |
| 9 | **GoodWe** | omvormer | 4 | Makkelijk (lokaal) | Veel geplaatst in het middensegment; lokale UDP-koppeling in HA-core is volwassen. |
| 10 | **Huawei FusionSolar** | omvormer + LUNA | 4 | Middel | Groot marktaandeel in hybride systemen; voor een installateur-gedreven app is het Northbound-account geen drempel (installateur maakt het zelf aan). Kiosk-URL als eenvoudige fallback. |

Vlak daarachter: **FoxESS**, **SolaX** en **AlphaESS** (alle drie makkelijk, token-gebaseerd, maar kleiner aandeel), **Zonneplan Evo** (groot aandeel bij Zonneplan-klanten, maar die hebben al de Zonneplan-app), **Peblar** (makkelijk, groeiend) en **Tibber Pulse** (makkelijk, maar klanten met Tibber hebben al een energie-app).

Bewust laag op de lijst: **Ratio** en **EVBox** (geen bruikbare API; alleen via eigen OCPP-backend), **Tesla Powerwall cloud** (betaald per request; lokaal wel makkelijk maar klein aandeel in NL), **Sungrow/SMA cloud** (partner-only; lokaal wel haalbaar).

## Niet geverifieerd / onzeker

- Of de HomeWizard cloud-API al buiten bèta is.
- Of Tibber's realtime-API werkt zonder Tibber-energiecontract.
- Exacte Enode-categorie-indeling (de merkenlijst op enode.com/connect is ongesorteerd; platform.enode.com/brands kon niet worden opgehaald).
- Sigenergy partner-cloud-API en Volt Time's publieke API-documentatie.
- Of SolarEdge-huiseigenaren standaard API-rechten hebben; in de praktijk hangt dit af van hoe de installateur de site heeft aangemaakt.
- Marktaandelen zijn inschattingen op basis van installateursaanbod, forumvolume en HA-integratiepopulariteit; er bestaan geen openbare NL-verkoopcijfers per merk.
