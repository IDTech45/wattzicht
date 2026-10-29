using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.Models;

/// <summary>Presentatiemodel voor één apparaat in de lijst.</summary>
public sealed class DeviceItem
{
    public DeviceDto Device { get; }

    public DeviceItem(DeviceDto device) => Device = device;

    public Guid Id => Device.Id;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? BrandName : Device.Name;
    public DeviceBrand Brand => Device.Brand;
    public bool IsOnline => Device.IsOnline;

    public string Initial => BrandName.Length > 0 ? BrandName[..1].ToUpperInvariant() : "?";

    public string BrandName => Device.Brand switch
    {
        DeviceBrand.HomeWizard => "HomeWizard",
        _ => Device.Brand.ToString()
    };

    public string TypeName => Device.Type switch
    {
        DeviceType.Inverter => "Omvormer",
        DeviceType.Battery => "Thuisbatterij",
        DeviceType.Charger => "Laadpaal",
        DeviceType.P1Meter => "P1-meter",
        _ => Device.Type.ToString()
    };

    public string Subtitle => $"{BrandName} · {TypeName}";

    public string StatusText => Device.IsOnline ? "Online" : "Offline";

    public string LastSeenText
    {
        get
        {
            var text = TimeText.Ago(Device.LastSeen, "Nog geen meting");
            return text == "zojuist" ? "Zojuist gezien" : text == "Nog geen meting" ? text : $"Laatst gezien {text}";
        }
    }
}
