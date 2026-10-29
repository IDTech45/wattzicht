using System.Text.Json;
using Wattzicht.Api.Connectors;

namespace Wattzicht.Api.Tests;

/// <summary>Normalisatie van het Sessy power/status-antwoord.</summary>
public sealed class SessyConnectorTests
{
    [Fact]
    public void Parse_normaliseert_vermogens_en_soc()
    {
        const string json = """
        {
          "status": "ok",
          "sessy": { "state_of_charge": 0.42, "power": -1500, "power_setpoint": -1500, "system_state": "SYSTEM_STATE_RUNNING_SAFE" },
          "renewable_energy_phase1": { "voltage_rms": 230, "current_rms": 4, "power_w": 900 },
          "renewable_energy_phase2": { "voltage_rms": 230, "current_rms": 4, "power_w": 900 },
          "renewable_energy_phase3": { "voltage_rms": 230, "current_rms": 4, "power_w": 900 },
          "grid_phase1": { "power_w": -300 },
          "grid_phase2": { "power_w": -300 },
          "grid_phase3": { "power_w": -300 }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var reading = SessyConnector.Parse(doc.RootElement);

        Assert.Equal(2700, reading.PvPowerW);
        Assert.Equal(-1500, reading.BatteryPowerW);
        Assert.NotNull(reading.BatterySoc);
        Assert.Equal(42d, reading.BatterySoc.Value, 6);
        Assert.Equal(-900, reading.GridPowerW);
        // verbruik = 2700 − 1500 − 900 = 300
        Assert.Equal(300, reading.LoadPowerW);
    }

    [Fact]
    public void Parse_overleeft_ontbrekende_velden()
    {
        using var doc = JsonDocument.Parse("""{ "sessy": { "power": 0 } }""");
        var reading = SessyConnector.Parse(doc.RootElement);

        Assert.Equal(0, reading.PvPowerW);
        Assert.Equal(0, reading.BatteryPowerW);
        Assert.Null(reading.BatterySoc);
    }
}
