# Enphase IQ Gateway lokaal uitlezen: kan het, en geeft hij ook de netkant?
#
# Drie vragen tegelijk, en de derde is de belangrijkste:
#   1. Is de gateway te bereiken en welke firmware zit erop?
#   2. Zitten er CT-klemmen in, en is er een meter van het type "net-consumption"?
#   3. Mag een token van de EIGENAAR die metergegevens ook echt lezen?
#
# Alleen als vraag 3 met ja wordt beantwoord krijgen we afname en teruglevering binnen, en dat is de
# hele reden om dit te bouwen. De zonkant hebben we al via de cloud.
#
# Draai dit op een computer die op hetzelfde wifi zit als de gateway.
#
#   Stap 1, zonder token, werkt meteen:
#     powershell -ExecutionPolicy Bypass -File scripts\enphase-test.ps1
#
#   Stap 2 en 3, met token:
#     powershell -ExecutionPolicy Bypass -File scripts\enphase-test.ps1 -Token "eyJ..."
#
# Het token haal je op bij https://entrez.enphaseenergy.com met het Enphase-account van de bewoner
# en het serienummer dat stap 1 hieronder toont. Een eigenaarsaccount geeft een token van een jaar,
# een installateursaccount twaalf uur. Welke van de twee je krijgt zegt iets, dus noteer het.
#
# Het script leest alleen en verandert niets aan de installatie.

param(
    [string]$Host = "",
    [string]$Token = ""
)

# De gateway gebruikt een zelfondertekend certificaat; dat hoort zo binnen het eigen netwerk.
if (-not ("TrustAllEnphase" -as [type])) {
    Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAllEnphase : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
}
"@
}
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllEnphase
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

function Stap($nummer, $tekst) { Write-Host "`n[$nummer] $tekst" -ForegroundColor Cyan }
function Bewaar($naam, $inhoud) {
    $pad = Join-Path (Get-Location) $naam
    $inhoud | Out-File -FilePath $pad -Encoding utf8
    Write-Host "    bewaard in $pad" -ForegroundColor DarkGray
}

# Zelf zoeken als er geen adres is meegegeven. envoy.local is de naam die Enphase zelf gebruikt; bij
# meerdere gateways in huis worden het envoy-2, envoy-3 enzovoort.
$kandidaten = if ($Host) { @($Host) } else { @("envoy.local", "envoy-2.local", "envoy") }
$basis = $null

Stap 1 "De gateway zoeken en uitlezen (/info, geen token nodig)"
foreach ($kandidaat in $kandidaten) {
    foreach ($schema in @("https", "http")) {
        try {
            $url = "${schema}://${kandidaat}"
            $info = Invoke-RestMethod -Uri "$url/info" -TimeoutSec 6
            $basis = $url
            Write-Host "    GEVONDEN op $url" -ForegroundColor Green
            $serie = $info.envoy_info.device.sn
            $firmware = $info.envoy_info.device.software
            Write-Host "    serienummer: $serie"
            Write-Host "    firmware   : $firmware"
            if ($firmware -match "^D?([0-9]+)\.") {
                if ([int]$Matches[1] -ge 7) {
                    Write-Host "    -> firmware 7 of nieuwer: een token is verplicht" -ForegroundColor Yellow
                } else {
                    Write-Host "    -> ouder dan 7: hier is geen token nodig" -ForegroundColor Green
                }
            }
            Bewaar "enphase-info.xml" ($info | ConvertTo-Xml -As String -Depth 6)
            break
        } catch { }
    }
    if ($basis) { break }
}

if (-not $basis) {
    Write-Host "    NIET GEVONDEN." -ForegroundColor Red
    Write-Host "    Zoek het IP-adres van de gateway in je router en draai opnieuw met -Host 192.168.1.x"
    exit 1
}

if (-not $Token) {
    Write-Host "`nStop hier: zonder token kan de rest niet." -ForegroundColor Yellow
    Write-Host "Haal er een op bij https://entrez.enphaseenergy.com met het serienummer hierboven,"
    Write-Host "en draai dit script opnieuw met -Token ""eyJ...""."
    exit 0
}

$kop = @{ Authorization = "Bearer $Token"; Accept = "application/json" }

Stap 2 "Welke meters zitten erin? (/ivp/meters)"
try {
    $meters = Invoke-RestMethod -Uri "$basis/ivp/meters" -Headers $kop -TimeoutSec 15
    Bewaar "enphase-meters.json" ($meters | ConvertTo-Json -Depth 8)
    foreach ($m in $meters) {
        Write-Host "    $($m.measurementType): status $($m.state), fasen $($m.phaseCount)"
    }
    if ($meters.measurementType -contains "net-consumption") {
        Write-Host "    -> er is een net-consumption-meter: afname en teruglevering zijn haalbaar" -ForegroundColor Green
    } else {
        Write-Host "    -> GEEN net-consumption-meter: deze woning geeft alleen de zonkant" -ForegroundColor Yellow
    }
} catch {
    Write-Host "    MISLUKT: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "    Een 401 hier betekent dat het token deze gegevens niet mag lezen; noteer dat, het is de beslissende uitkomst."
}

Stap 3 "De metingen zelf (/ivp/meters/readings) -- DIT IS DE BESLISSENDE VRAAG"
try {
    $lezing = Invoke-RestMethod -Uri "$basis/ivp/meters/readings" -Headers $kop -TimeoutSec 20
    Bewaar "enphase-readings.json" ($lezing | ConvertTo-Json -Depth 8)
    Write-Host "    OK" -ForegroundColor Green
    foreach ($r in $lezing) {
        Write-Host "    meter $($r.eid): vermogen $($r.activePower) W, geleverd $($r.actEnergyDlvd) Wh, ontvangen $($r.actEnergyRcvd) Wh"
    }
} catch {
    Write-Host "    MISLUKT: $($_.Exception.Message)" -ForegroundColor Red
}

Stap 4 "Alles in een keer (/production.json?details=1)"
try {
    $prod = Invoke-RestMethod -Uri "$basis/production.json?details=1" -Headers $kop -TimeoutSec 20
    Bewaar "enphase-production.json" ($prod | ConvertTo-Json -Depth 10)
    Write-Host "    OK" -ForegroundColor Green
} catch {
    Write-Host "    MISLUKT: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`nKlaar. Stuur de bewaarde bestanden door; daarmee is de omzetting te bouwen" -ForegroundColor Cyan
Write-Host "zonder dat er nog iemand bij een gateway hoeft te gaan staan." -ForegroundColor Cyan
