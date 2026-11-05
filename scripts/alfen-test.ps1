# Alfen lokaal uitlezen: kan het, en geeft hij echte laadsessies?
#
# Dit beantwoordt de enige vraag waar het Alfen-plan op staat of valt. Als /api/transactions
# laadsessies teruggeeft met een begin, een eind en kilowatturen, dan kunnen we een Alfen uitlezen
# die bij een andere backoffice hangt, en dat is precies de groep waar OCPP niet kan.
#
# Draai dit op een computer die op hetzelfde netwerk zit als de laadpaal.
#
#   powershell -ExecutionPolicy Bypass -File scripts\alfen-test.ps1 -Ip 192.168.1.60 -Wachtwoord "..."
#
# Het wachtwoord is dat van de gebruiker "admin" en staat op de flyer die bij de paal zat. Heb je die
# niet, dan heeft je installateur of de backoffice hem; Alfen bewaart hem zelf niet.
#
# Wat het script NIET doet: iets wijzigen aan de paal. Alleen lezen, en daarna netjes uitloggen.

param(
    [Parameter(Mandatory = $true)][string]$Ip,
    [Parameter(Mandatory = $true)][string]$Wachtwoord,
    [string]$Gebruiker = "admin"
)

# De paal gebruikt een zelfondertekend certificaat; dat hoort zo en is hier geen probleem, want we
# praten binnen het eigen netwerk tegen een adres dat we zelf hebben ingetypt.
if (-not ("TrustAll" -as [type])) {
    Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAll : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
}
"@
}
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAll
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

$basis = "https://$Ip"
$sessie = New-Object Microsoft.PowerShell.Commands.WebRequestSession

function Stap($nummer, $tekst) { Write-Host "`n[$nummer] $tekst" -ForegroundColor Cyan }

Stap 1 "Wie ben je? (/api/info, zonder inloggen)"
try {
    $info = Invoke-RestMethod -Uri "$basis/api/info" -WebSession $sessie -TimeoutSec 10
    Write-Host "    OK: $($info.Identity) model $($info.Model) firmware $($info.FWVersion)" -ForegroundColor Green
} catch {
    Write-Host "    MISLUKT: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "    Zit deze computer op hetzelfde netwerk als de paal, en klopt het IP-adres?"
    exit 1
}

Stap 2 "Inloggen (/api/login)"
try {
    $body = @{ username = $Gebruiker; password = $Wachtwoord } | ConvertTo-Json
    Invoke-RestMethod -Uri "$basis/api/login" -Method Post -Body $body `
        -ContentType "application/json" -WebSession $sessie -TimeoutSec 10 | Out-Null
    Write-Host "    OK, ingelogd als $Gebruiker" -ForegroundColor Green
} catch {
    Write-Host "    MISLUKT: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "    Verkeerd wachtwoord, of iemand anders is al ingelogd (de paal staat er maar een toe)."
    exit 1
}

try {
    Stap 3 "Laadsessies ophalen (/api/transactions) -- DIT IS DE BESLISSENDE VRAAG"
    try {
        $tx = Invoke-RestMethod -Uri "$basis/api/transactions?offset=0" -WebSession $sessie -TimeoutSec 20
        $json = $tx | ConvertTo-Json -Depth 8
        $pad = Join-Path (Get-Location) "alfen-transactions.json"
        $json | Out-File -FilePath $pad -Encoding utf8
        Write-Host "    OK, antwoord bewaard in $pad" -ForegroundColor Green
        Write-Host "    Eerste 1500 tekens:" -ForegroundColor DarkGray
        Write-Host ($json.Substring(0, [Math]::Min(1500, $json.Length)))
    } catch {
        Write-Host "    MISLUKT: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "    Zonder dit antwoord heeft de hele Alfen-route geen zin; stuur deze melding door."
    }

    Stap 4 "Meterstand (/api/prop, categorie meter1)"
    try {
        $meter = Invoke-RestMethod -Uri "$basis/api/prop?ids=meter1" -WebSession $sessie -TimeoutSec 15
        ($meter | ConvertTo-Json -Depth 6) | Out-File -FilePath (Join-Path (Get-Location) "alfen-meter.json") -Encoding utf8
        Write-Host "    OK, bewaard in alfen-meter.json" -ForegroundColor Green
    } catch {
        Write-Host "    Niet gelukt: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    Stap 5 "Blijft de backoffice verbonden? (/api/prop, categorie generic)"
    try {
        $gen = Invoke-RestMethod -Uri "$basis/api/prop?ids=generic" -WebSession $sessie -TimeoutSec 15
        ($gen | ConvertTo-Json -Depth 6) | Out-File -FilePath (Join-Path (Get-Location) "alfen-generic.json") -Encoding utf8
        Write-Host "    OK, bewaard in alfen-generic.json" -ForegroundColor Green
    } catch {
        Write-Host "    Niet gelukt: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
finally {
    # Altijd uitloggen, ook als er iets misging: de paal staat maar een sessie toe en anders kan
    # niemand anders er nog bij.
    try {
        Invoke-RestMethod -Uri "$basis/api/logout" -Method Post -WebSession $sessie -TimeoutSec 10 | Out-Null
        Write-Host "`nUitgelogd." -ForegroundColor DarkGray
    } catch { }
}

Write-Host "`nKlaar. Stuur de bestanden die hierboven genoemd zijn door, dan bouw ik de koppeling." -ForegroundColor Cyan
