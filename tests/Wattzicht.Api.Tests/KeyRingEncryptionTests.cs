using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>De sleutelring van Data Protection moet na een herstart weer te ontsleutelen zijn.</summary>
public sealed class KeyRingEncryptionTests
{
    [Fact]
    public void Decryptor_kan_door_Data_Protection_met_IServiceProvider_gemaakt_worden()
    {
        var key = new ProtectionMasterKey("test-sleutel-voor-de-sleutelring");
        var services = new ServiceCollection().AddSingleton(key).BuildServiceProvider();

        var encrypted = new ProtectionKeyXmlEncryptor(key).Encrypt(new XElement("key", "geheim"));

        // Zo maakt Data Protection hem aan bij het lezen van de sleutelring: via Activator, met IServiceProvider.
        var decryptor = (ProtectionKeyXmlDecryptor)Activator.CreateInstance(typeof(ProtectionKeyXmlDecryptor), (IServiceProvider)services)!;

        Assert.Equal("geheim", decryptor.Decrypt(encrypted.EncryptedElement).Value);
    }
}
