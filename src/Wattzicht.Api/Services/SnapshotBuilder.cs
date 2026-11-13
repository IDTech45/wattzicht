using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Telt de laatste metingen van alle apparaten van een huishouden op tot één <see cref="LiveSnapshotDto"/>.
/// Elke connector levert per apparaattype alleen de velden die bij dat apparaat horen, zodat optellen klopt.
/// </summary>
public static class SnapshotBuilder
{
    public static LiveSnapshotDto Empty { get; } = new(0, 0, 0, 0, null, 0, null);

    /// <summary>
    /// De momentopname, met daarin welke soorten apparaat deze woning heeft.
    ///
    /// Dat laatste moet erbij, want een optelling van nul watt zegt niets over bezit. Zonder deze
    /// lijst stond er bij iedereen een batterijtegel op "Rust" en een laadpaaltegel op "Niet aan het
    /// laden", ook bij een woning met alleen zonnepanelen. Dat leest als twee apparaten die stil
    /// staan in plaats van twee apparaten die er niet zijn.
    /// </summary>
    /// <param name="soorten">De apparaatsoorten van deze woning; weglaten mag, dan raden we aan de
    /// metingen af en kan een batterij die niets doet gemist worden.</param>
    public static LiveSnapshotDto Build(
        IReadOnlyCollection<Reading> latestPerDevice, IEnumerable<DeviceType>? soorten = null)
    {
        if (latestPerDevice.Count == 0)
        {
            return Empty;
        }

        double pv = 0, load = 0, grid = 0, battery = 0, charger = 0;
        double socSum = 0;
        var socCount = 0;
        DateTimeOffset? updatedAt = null;

        foreach (var r in latestPerDevice)
        {
            pv += r.PvPowerW;
            load += r.LoadPowerW;
            grid += r.GridPowerW;
            battery += r.BatteryPowerW;
            charger += r.ChargerPowerW;

            if (r.BatterySoc is { } soc)
            {
                socSum += soc;
                socCount++;
            }

            if (updatedAt is null || r.Timestamp > updatedAt)
            {
                updatedAt = r.Timestamp;
            }
        }

        var lijst = soorten?.ToList();

        return new LiveSnapshotDto(
            PvPowerW: Math.Round(pv),
            LoadPowerW: Math.Round(load),
            GridPowerW: Math.Round(grid),
            BatteryPowerW: Math.Round(battery),
            BatterySoc: socCount > 0 ? Math.Round(socSum / socCount, 1) : null,
            ChargerPowerW: Math.Round(charger),
            UpdatedAt: updatedAt,
            HasBattery: lijst is not null ? lijst.Contains(DeviceType.Battery) : socCount > 0 || battery != 0,
            HasCharger: lijst is not null ? lijst.Contains(DeviceType.Charger) : charger != 0);
    }
}
