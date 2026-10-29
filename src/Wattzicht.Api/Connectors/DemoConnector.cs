using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// Demo-connector: realistische dagcurve zonder echte verbinding. Zon volgt een halve sinus tussen zonsopkomst
/// en -ondergang (seizoensafhankelijk), verbruik heeft een ochtend- en avondpiek, de batterij laadt overdag
/// uit overschot en ontlaadt 's avonds, de laadpaal laadt 's nachts.
/// Deterministisch per apparaat en tijdstip, zodat tests reproduceerbaar zijn.
/// </summary>
public sealed class DemoConnector(TimeProvider clock) : IDeviceConnector
{
    /// <summary>Nederlandse tijdzone; de curve is gekoppeld aan lokale klok.</summary>
    private static readonly TimeZoneInfo Amsterdam = DutchTime.Zone;

    public DeviceBrand Brand => DeviceBrand.Demo;

    public Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        IReadOnlyList<DiscoveredDevice> devices = request.Type switch
        {
            DeviceType.Inverter => [new DiscoveredDevice("DEMO-INV-001", "Demo omvormer 4,2 kWp", DeviceType.Inverter)],
            DeviceType.Battery => [new DiscoveredDevice("DEMO-BAT-001", "Demo thuisbatterij 5 kWh", DeviceType.Battery)],
            DeviceType.Charger => [new DiscoveredDevice("DEMO-CHG-001", "Demo laadpaal 11 kW", DeviceType.Charger)],
            DeviceType.P1Meter => [new DiscoveredDevice("DEMO-P1-001", "Demo slimme meter", DeviceType.P1Meter)],
            _ => []
        };

        return Task.FromResult(ConnectResult.Ok(devices));
    }

    public Task<DeviceReading> ReadAsync(Device device, IReadOnlyDictionary<string, string> credentials, CancellationToken ct) =>
        Task.FromResult(Simulate(device.Type, clock.GetUtcNow(), device.ExternalId));

    /// <summary>Berekent de gesimuleerde meting op een moment. Publiek voor tests.</summary>
    public static DeviceReading Simulate(DeviceType type, DateTimeOffset utcNow, string seed = "demo")
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, Amsterdam);
        var hour = local.Hour + local.Minute / 60d + local.Second / 3600d;

        // Zon: halve sinus tussen zonsopkomst en -ondergang; piek 4,2 kWp in juni, ~1,5 kW in december.
        var dayOfYear = local.DayOfYear;
        var season = Math.Cos((dayOfYear - 172) / 365.25 * 2 * Math.PI); // +1 rond 21 juni, -1 rond 21 december
        var sunrise = 6.5 - 1.8 * season;    // ~4.7u zomer, ~8.3u winter
        var sunset = 18.5 + 2.8 * season;    // ~21.3u zomer, ~15.7u winter
        var peakW = 2800 + 1400 * season;    // 4200 zomer, 1400 winter

        var pv = 0d;
        if (hour > sunrise && hour < sunset)
        {
            var phase = (hour - sunrise) / (sunset - sunrise) * Math.PI;
            pv = peakW * Math.Sin(phase);
            // Lichte bewolking: deterministische rimpel op basis van seed en kwartier.
            pv *= 0.85 + 0.15 * Noise(seed, (int)(hour * 4));
        }

        // Verbruik: basislast 250 W, ochtendpiek rond 7:30, avondpiek rond 18:30.
        var load = 250
                   + 900 * Gauss(hour, 7.5, 0.8)
                   + 1600 * Gauss(hour, 18.5, 1.2)
                   + 150 * Noise(seed, (int)(hour * 12));

        // Laadpaal: 's nachts tussen 23:00 en 05:00 met 7,4 kW (1 fase 32 A).
        var charger = hour is >= 23 or < 5 ? 7400 : 0;

        // Batterij: laadt bij zonoverschot (max 2,2 kW), ontlaadt 's avonds/nachts tot verbruik gedekt is (max 1,7 kW).
        var surplus = pv - load;
        var battery = surplus > 200
            ? -Math.Min(2200, surplus)            // laden (negatief)
            : hour is >= 17 or < 2
                ? Math.Min(1700, Math.Max(0, -surplus)) // ontladen (positief)
                : 0;

        // SoC: laag in de ochtend, vol tegen de avond, loopt 's nachts terug.
        var soc = hour < 6 ? 35 - hour * 3
            : hour < 17 ? 17 + (hour - 6) * 7.5
            : 100 - (hour - 17) * 9;
        soc = Math.Clamp(soc, 10, 100);

        // Net: wat overblijft (positief = afname, negatief = teruglevering).
        var grid = load + charger - pv - battery;

        var ts = utcNow;

        return type switch
        {
            DeviceType.Inverter => new DeviceReading(ts, PvPowerW: Math.Round(pv), LoadPowerW: Math.Round(load), GridPowerW: Math.Round(grid)),
            DeviceType.Battery => new DeviceReading(ts, BatteryPowerW: Math.Round(battery), BatterySoc: Math.Round(soc, 1)),
            DeviceType.Charger => new DeviceReading(ts, ChargerPowerW: charger),
            DeviceType.P1Meter => new DeviceReading(ts, LoadPowerW: Math.Round(load), GridPowerW: Math.Round(grid)),
            _ => new DeviceReading(ts)
        };
    }

    /// <summary>Gauss-vorm met piek 1 op <paramref name="center"/>.</summary>
    private static double Gauss(double x, double center, double width)
    {
        var d = (x - center) / width;
        return Math.Exp(-0.5 * d * d);
    }

    /// <summary>Deterministische pseudo-ruis in [0,1) op basis van seed en bucket.</summary>
    private static double Noise(string seed, int bucket)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in seed)
            {
                h = h * 31 + c;
            }

            h = h * 31 + bucket;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0x7fffffff) / (double)0x7fffffff;
        }
    }
}
