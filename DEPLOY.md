# Wattzicht – deploy op de Leaseweb-VPS (Dokploy), zonder eigen domein

Dezelfde VPS en dezelfde Dokploy als LaadCash. Wattzicht publiceert geen host-poorten (Traefik bereikt de containers intern) en heeft eigen containernamen, dus het draait naast LaadCash en Machinepas. Je hebt nog geen domein; Dokploy kan zelf een gratis adres maken (`…traefik.me`) met werkende HTTPS. Later hang je `wattzicht.nl` er gewoon bij.

Totale tijd: ongeveer 10 minuten, alles via de Dokploy-webinterface.

## 1. Secrets maken (op je pc)

```bash
git clone https://github.com/IDTech45/wattzicht
cd wattzicht
./scripts/generate-secrets.sh        # maakt .env.production
cat .env.production                  # kopieer de inhoud
```

Op Windows zonder bash: open `.env.example`, vul bij `POSTGRES_PASSWORD`, `JWT_SECRET` en `WZ_PROTECTION_KEY` lange willekeurige strings in (minimaal 32 tekens; voor `WZ_PROTECTION_KEY` 32 willekeurige bytes als base64, bijv. uit https://generate.plus/en/base64 of PowerShell `[Convert]::ToBase64String((1..32|%{Get-Random -Max 256}) -as [byte[]])`).

## 2. Project in Dokploy

1. Dokploy → **Projects → Create Project** → naam `wattzicht`.
2. Open het project → **Create Service → Compose** → naam `wattzicht`.
3. Tab **Provider**: GitHub → repo **IDTech45/wattzicht** → branch `main` → Compose path `./docker-compose.yml` → Autodeploy aan → **Save**.
   (Zie je de repo niet? Dan moet de Dokploy GitHub-app toegang krijgen tot `wattzicht`, net als destijds bij `laadcash`: GitHub → Settings → Applications → Dokploy → Repository access.)
4. Tab **Environment**: plak de inhoud van `.env.production`. Zet voor nu:
   ```
   SEED_ENABLED=true
   SWAGGER_ENABLED=true
   SEED_INSTALLER_PASSWORD=<kies iets>
   SEED_DEMO_PASSWORD=<kies iets>
   ```
   `SEED_ENABLED=true` maakt de demo-accounts aan (zie stap 5). Zet dat later op `false`.
5. **Save**.

## 3. Domeinen (zonder eigen domein)

Tab **Domains** → **Add Domain**:

| Service | Host | Container-poort | HTTPS |
| --- | --- | --- | --- |
| `frontend` | klik **Generate Domain** → je krijgt iets als `wattzicht-frontend-ab12cd.traefik.me` | `80` | aan (Let's Encrypt) |
| `api` | klik **Generate Domain** → `wattzicht-api-ab12cd.traefik.me` | `8080` | aan |

De website heeft genoeg aan het eerste domein: nginx stuurt `/api` intern door naar de API. Het tweede domein is voor de mobiele app en Swagger.

Ga terug naar **Environment** en zet `CORS_ORIGIN_0` op het frontend-domein (met `https://`). Save.

## 4. Deploy

Knop **Deploy**. De eerste build duurt 5–10 minuten (dotnet + npm). Volg het in **Logs**. Klaar als `wattzicht-api` meldt dat het luistert op 8080 en de database is geseed.

## 5. Bekijken

- Website: `https://<frontend-domein>/`
- Installateursportal: `https://<frontend-domein>/portal/login`
  - `installateur@triple-energie.nl` / het `SEED_INSTALLER_PASSWORD` dat je koos
- Swagger: `https://<api-domein>/api/swagger`
- Health: `https://<api-domein>/api/health`
- Demo-huishouden (voor de app): `demo@wattzicht.nl` / `SEED_DEMO_PASSWORD`

## 6. Mobiele app naar deze API laten wijzen

In `src/Wattzicht.App/Services/AppConfig.cs` staat de productie-URL `https://api.wattzicht.nl`. Zolang je het traefik.me-adres gebruikt: zet die tijdelijk op `https://<api-domein>` (zonder `/api`; de app voegt dat zelf niet toe, de API accepteert beide). Of bouw in Debug tegen `http://localhost:8080` met de dev-compose.

## 7. Enphase aanzetten

Enphase koppelt met OAuth, dus er is eenmalig iets nodig aan de kant van Enphase. De klant kan het
zelf niet met een sleutel regelen.

1. Ga naar [developer-v4.enphase.com](https://developer-v4.enphase.com) en open de app (of maak er
   een aan). Hetzelfde account als LaadCash mag, maar let op: het gratis plan "Watt" geeft
   **1.000 verzoeken per maand** en beide apps delen dat. De poller leest Enphase daarom hoogstens
   één keer per uur uit.
2. Zet bij **Redirect URI** de terugkeerpagina van Wattzicht, bijvoorbeeld
   `https://wattzicht.laadcash.nl/oauth/enphase`. Die moet later exact zo meegestuurd worden.
3. Vul in Dokploy → Environment de drie waarden:
   `ENPHASE_CLIENT_ID`, `ENPHASE_CLIENT_SECRET` en `ENPHASE_API_KEY`, en redeploy.

Wat Enphase oplevert: het actuele vermogen van de panelen en de opwek-teller, en ook afname van en
teruglevering aan het net via `/api/v4/systems/{id}/energy_import_lifetime` en
`/energy_export_lifetime`.

Hier stond dat die laatste twee $249 per maand kosten. Dat klopt niet. Die $249 is het Kilowatt-plan
en dat koopt VOLUME (50.000 aanroepen per maand in plaats van 1.000), geen extra soort gegevens;
site-level consumption staat gewoon in de opsomming van het gratis Watt-plan. Nagekeken 05-11-2026 op
developer-v4.enphase.com/developer-plans. Die verkeerde aanname was de enige reden om een route te
bedenken waarbij elke bewoner zelf een token moet plakken.

Wel een harde voorwaarde, en die heeft niets met het plan te maken: er moeten consumption-CT's in de
woning zitten (IQ Gateway Metered). Zonder die klemmen bestaat afname en teruglevering niet, op geen
enkel plan.

## 8. Eigen domein: wattzicht.nl en wattzicht.com (staat, 04-11-2026)

Beide domeinen staan op de nameservers van Leaseweb en wijzen sinds 4 november naar de VPS
**82.192.69.90** (IPv6 `2001:1af8:5301:138:1c00:8cff:fe00:253e`). Dit staat er nu:

| Domein | Type | Naam | Waarde |
| --- | --- | --- | --- |
| wattzicht.nl | A + AAAA | `@`, `www`, `api` | VPS |
| wattzicht.com | A + AAAA | `@`, `www` | VPS |

De `.com` heeft bewust geen `api` en geen mail: die stuurt alleen door naar de `.nl`.

**Traefik.** De routers staan in `docker-compose.yml` bij de services zelf (`wz-site` en `wz-api`),
niet in het Dokploy-scherm. Zo staat in de repo welk adres waar hoort en krijgt een verse server ze
meteen mee. Dokploy zet zijn eigen routers er bij elke deploy naast; die hebben andere namen
(`wattzicht-app-…`) en bijten elkaar niet. `wattzicht.laadcash.nl` blijft daardoor ook werken zolang
er oude app-versies rondlopen.

Let's Encrypt gaf één certificaat voor `wattzicht.nl` met de drie andere namen erbij, en een los
certificaat voor `api.wattzicht.nl`. Gecontroleerd: `https://wattzicht.nl` geeft 200, de drie andere
adressen een 301 naar `https://wattzicht.nl`, en `https://api.wattzicht.nl/api/health` zegt Healthy.

**Nog open:**

- **Mail.** `hallo@wattzicht.nl` staat in de voettekst van de site, maar het adres bestaat nog niet.
  wattzicht.nl heeft geen Web/Email Hosting-contract bij Leaseweb (laadcash.nl wel); dat moet eerst
  besteld worden. Daarna pas de MX naar `mailfilter1/2/3.leaseweb.com` (prioriteit 10), de SPF
  `v=spf1 include:mh-spf.leaseweb.com ~all` en `_dmarc` met
  `v=DMARC1; p=quarantine; rua=mailto:ibo.dogan45@gmail.com; fo=1`. MX zetten vóór de mailbox
  bestaat heeft geen zin: de mail bounst dan alsnog.
- **Enphase.** De terugkeeradressen van de `.nl` en `.com` moeten in de Enphase-developer-app staan,
  anders weigert Enphase ze op het toestemmingsscherm.

De app wijst naar `https://api.wattzicht.nl/api/` en dat adres werkt nu.

## Problemen

- **Build faalt op npm/dotnet**: de code is nog niet lokaal gebouwd; stuur de laatste 30 regels van de Dokploy-log naar Claude, dan komt er een fix.
- **502 op de website**: de API is nog aan het starten of de database-seed faalt; kijk in de logs van `wattzicht-api`.
- **Certificaat werkt niet**: traefik.me-domeinen krijgen soms pas na een minuut een certificaat; herlaad. Blijft het rood, zet dan tijdelijk HTTPS uit op het domein en weer aan.
