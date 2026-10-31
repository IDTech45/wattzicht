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

Wat Enphase daarna oplevert: het actuele vermogen van de panelen en de opwek-teller. Afname van en
teruglevering aan het net zitten in de meter-telemetrie en die hoort bij het betaalde plan
($249 per maand). Voor de besparing van een Enphase-huis is dus een P1-meter ernaast nodig, of de
lokale IQ Gateway in het thuisnetwerk.

## 8. Later: eigen domein

1. Registreer `wattzicht.nl`, DNS `A`-records `wattzicht.nl`, `www.wattzicht.nl` en `api.wattzicht.nl` → VPS-IP.
2. Dokploy → Domains: voeg de drie hosts toe aan dezelfde services (frontend: `wattzicht.nl` + `www`; api: `api.wattzicht.nl`), HTTPS aan.
3. Environment: `CORS_ORIGIN_0=https://wattzicht.nl`, `CORS_ORIGIN_1=https://www.wattzicht.nl`. Redeploy.
4. `AppConfig.cs` terug naar `https://api.wattzicht.nl`.

## Problemen

- **Build faalt op npm/dotnet**: de code is nog niet lokaal gebouwd; stuur de laatste 30 regels van de Dokploy-log naar Claude, dan komt er een fix.
- **502 op de website**: de API is nog aan het starten of de database-seed faalt; kijk in de logs van `wattzicht-api`.
- **Certificaat werkt niet**: traefik.me-domeinen krijgen soms pas na een minuut een certificaat; herlaad. Blijft het rood, zet dan tijdelijk HTTPS uit op het domein en weer aan.
