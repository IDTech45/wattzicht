# Wattzicht – architectuur (v0.1)

Eén app voor zonnepanelen, thuisbatterij en laadpaal van verschillende merken. White-label voor installateurs; eerste klant Triple Energie (Enschede). Zelfde opzet als LaadCash: .NET-backend in `src/`, Vue 3 + PrimeVue in `frontend/`, Docker Compose + Dokploy op de Leaseweb-VPS.

```
wattzicht/
├── Wattzicht.sln
├── global.json                      .NET 9 SDK
├── docker-compose.yml               api + frontend + postgres
├── src/
│   ├── Wattzicht.Shared/            DTO's en enums, gedeeld door API en app
│   ├── Wattzicht.Api/               ASP.NET Core minimal API + polling worker
│   │   ├── Connectors/              Growatt, Sessy, (later) Deye/Solarman, Alfen, Zaptec
│   │   ├── Data/                    EF Core + Postgres (timeseries in `Readings`)
│   │   ├── Services/                Besparingsberekening, storingsdetectie
│   │   └── Endpoints/               auth, devices, dashboard, installer
│   └── Wattzicht.App/               .NET MAUI (iOS + Android), MVVM
├── frontend/                        Vue 3 + Vite + PrimeVue: landing + installateursportal
├── scripts/                         generate-secrets.sh, dev-up.sh
└── tests/                           Wattzicht.Api.Tests (xunit)
```

## Datamodel

- **Installer** (tenant, white-label: naam, logo, kleuren, offertelink)
- **Household** → Installer; gebruikers loggen in met e-mail + wachtwoord (JWT)
- **Device** (Household, type: Inverter | Battery | Charger | P1Meter, brand, externalId, credentials versleuteld)
- **Reading** (Device, timestamp, pvPowerW, loadPowerW, gridPowerW, batteryPowerW, batterySoc, chargerPowerW, energie-tellers in Wh). Elke 5 min per apparaat. Index op (DeviceId, Timestamp).
- **Tariff** (Household: vast tarief of dynamisch, terugleververgoeding, vanaf-datum)
- **DailySummary** (Household, date, pvKwh, selfUseKwh, exportKwh, importKwh, chargedKwh, savedEur)
- **Alert** (Device, type: NoData | ZeroProduction | Offline, since, resolvedAt)

## Connector-contract

```csharp
public interface IDeviceConnector
{
    DeviceBrand Brand { get; }
    Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct);   // valideert token/credentials, haalt apparaten op
    Task<Reading> ReadAsync(Device device, CancellationToken ct);                      // één meting, genormaliseerd
}
```

- **Growatt**: Open API v1, `https://openapi.growatt.com/v1/`, token in header `token`. Klant haalt token uit ShinePhone → Ik → API Token.
- **Sessy**: lokale API `http://<ip>/api/v1/power/status` (basic auth, gebruikersnaam/wachtwoord op de dongle) en cloud-API. In v0.1 via cloud-polling door de API; lokale uitlezing via de app is fase 2.
- Nieuwe merken = één klasse in `Connectors/` + registratie in `MauiProgram`/`Program.cs`.

## Besparing (v0.1, vóór 2027 mét saldering)

- Zelfverbruik kWh × leveringstarief = bespaard
- Teruglevering kWh × terugleververgoeding (na 1-1-2027) of × leveringstarief (saldering, tot 31-12-2026)
- Laadpaal: geladen kWh × (publiek laadtarief 0,59 − eigen tarief) = bespaard t.o.v. publiek laden
- Batterij: ontladen kWh × leveringstarief − geladen-van-net kWh × tarief op dat moment

Alles per dag opgeslagen in `DailySummary`, de app toont dag/week/maand/jaar.

## Beveiliging

- Apparaat-credentials versleuteld met Data Protection API, sleutel in env `WZ_PROTECTION_KEY`
- JWT 7 dagen, refresh via `/auth/refresh`
- Installateursportal: rol `Installer`, ziet alleen eigen households
- AVG: verwerkersovereenkomst per installateur (zie `docs/AVG.md`, nog te schrijven)
