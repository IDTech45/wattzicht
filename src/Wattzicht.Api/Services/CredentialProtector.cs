using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Wattzicht.Api.Services;

/// <summary>
/// Versleutelt en ontsleutelt apparaat-credentials (tokens, wachtwoorden van dongles) met de Data Protection API.
/// De sleutelring staat in Postgres en is zelf versleuteld met <c>WZ_PROTECTION_KEY</c>
/// (zie <see cref="ProtectionKeyXmlEncryptor"/>).
/// </summary>
public sealed class CredentialProtector
{
    private const string Purpose = "Wattzicht.DeviceCredentials.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;

    public CredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>Serialiseert de dictionary naar JSON en versleutelt die.</summary>
    public string Protect(IReadOnlyDictionary<string, string> credentials)
    {
        var json = JsonSerializer.Serialize(credentials, JsonOptions);
        return _protector.Protect(json);
    }

    /// <summary>Ontsleutelt naar een dictionary; gooit <see cref="CryptographicException"/> bij een verkeerde sleutel.</summary>
    public Dictionary<string, string> Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var json = _protector.Unprotect(protectedValue);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                   ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Versleutelt de Data Protection-sleutelring met AES-256-GCM op basis van <c>WZ_PROTECTION_KEY</c>.
/// Zo kunnen de sleutels veilig in de database staan en overleven ze een nieuwe container.
/// </summary>
public sealed class ProtectionKeyXmlEncryptor(ProtectionMasterKey masterKey) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var ciphertext = new byte[plaintext.Length];

        using (var aes = new AesGcm(masterKey.Key, tag.Length))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var element = new XElement("wattzichtEncryptedKey",
            new XAttribute("version", 1),
            new XElement("nonce", Convert.ToBase64String(nonce)),
            new XElement("tag", Convert.ToBase64String(tag)),
            new XElement("ciphertext", Convert.ToBase64String(ciphertext)));

        return new EncryptedXmlInfo(element, typeof(ProtectionKeyXmlDecryptor));
    }
}

/// <summary>Tegenhanger van <see cref="ProtectionKeyXmlEncryptor"/>; wordt door Data Protection via DI geïnstantieerd.</summary>
public sealed class ProtectionKeyXmlDecryptor : IXmlDecryptor
{
    private readonly ProtectionMasterKey masterKey;

    public ProtectionKeyXmlDecryptor(ProtectionMasterKey masterKey) => this.masterKey = masterKey;

    /// <summary>
    /// Data Protection maakt de decryptor zelf aan bij het lezen van de sleutelring, met óf een lege
    /// constructor óf één met <see cref="IServiceProvider"/>. Zonder deze constructor faalt dat met
    /// "No parameterless constructor" en kunnen opgeslagen apparaat-credentials niet meer ontsleuteld worden.
    /// </summary>
    public ProtectionKeyXmlDecryptor(IServiceProvider services)
        : this(services.GetRequiredService<ProtectionMasterKey>())
    {
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var nonce = Convert.FromBase64String(encryptedElement.Element("nonce")!.Value);
        var tag = Convert.FromBase64String(encryptedElement.Element("tag")!.Value);
        var ciphertext = Convert.FromBase64String(encryptedElement.Element("ciphertext")!.Value);
        var plaintext = new byte[ciphertext.Length];

        using (var aes = new AesGcm(masterKey.Key, tag.Length))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return XElement.Parse(Encoding.UTF8.GetString(plaintext));
    }
}

/// <summary>De 32-byte hoofdsleutel, afgeleid van <c>WZ_PROTECTION_KEY</c> (base64 of willekeurige tekst).</summary>
public sealed class ProtectionMasterKey
{
    public byte[] Key { get; }

    public ProtectionMasterKey(string configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue) || configuredValue.Length < 32)
        {
            throw new InvalidOperationException("WZ_PROTECTION_KEY ontbreekt of is korter dan 32 tekens.");
        }

        // Probeer base64 (zoals generate-secrets.sh maakt); anders afleiden met SHA-256.
        try
        {
            var raw = Convert.FromBase64String(configuredValue);
            Key = raw.Length == 32 ? raw : SHA256.HashData(raw);
        }
        catch (FormatException)
        {
            Key = SHA256.HashData(Encoding.UTF8.GetBytes(configuredValue));
        }
    }
}
