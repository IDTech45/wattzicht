# Design-tokens: Wattzicht en LaadCash

Eén bron voor beide producten. LaadCash en Wattzicht delen navy, fonts, radius, schaduwen,
statuskleuren en alle tekst- en vlakkleuren. Het enige verschil is het accent: LaadCash groen,
Wattzicht geel.

De waarden hieronder zijn letterlijk die van laadcash.nl (`frontend/src/assets/theme.css`, de
`--lc-*`-variabelen). In Wattzicht heten ze `--wz-*` op de website en dragen ze in de MAUI-app
dezelfde namen als in deze tabel (`Navy900`, `Accent500`, `EnergySolar`, …).

**Laadpaal is blauw, niet groen, omdat groen in LaadCash het merk-accent is.** Een laadpaal in
LaadCash-groen zou in die app niet van een knop of een actieve tab te onderscheiden zijn, en een
energiekleur die in het ene product iets anders betekent dan in het andere maakt elke grafiek
onleesbaar zodra je ze naast elkaar zet.

## Gedeeld (exact gelijk in beide apps)

| Token | Hex | Waarvoor |
| --- | --- | --- |
| Navy900 | `#0A2540` | Hoofdtekst, koppen, primaire knop, donkere panelen |
| Teal800 | `#0E3A4A` | Donkere panelen met cijfers erin |
| BgApp / BgCanvas | `#F6F8F7` | Achtergrond van de pagina |
| Surface | `#FFFFFF` | Kaarten |
| Surface2 | `#EEF2F0` | Secundair vlak, sectie-achtergrond |
| SurfaceTint | `#EEF6F1` | Positief gekleurd vlak |
| Border | `#DFE6E2` | Rand om kaarten |
| BorderStrong | `#C6D1CB` | Rand om invoervelden |
| Divider | `#E6EBE8` | Scheidingslijn |
| TextBody | `#36474C` | Gewone tekst |
| TextMuted | `#5C6B66` | Bijtekst |
| TextFaint | `#5F6F75` | Fijne tekst; haalt 5,2:1 op wit |
| TextOnDarkMuted | `#9DB4BC` | Bijtekst op navy |
| Placeholder | `#AEBAB5` | Tekst in een leeg invoerveld |

### Status

| Token | Dot | Tekst | Achtergrond |
| --- | --- | --- | --- |
| Success | `#0E9F6E` | `#0E7A55` | `#E6F3EA` |
| Pending | `#3B6FE0` | `#2B59C3` | `#EAF0FF` |
| Warning | `#E0A82E` | `#946200` | `#FBF1DD` |
| Error | `#E5484D` | `#C2363B` | `#FBE6E7` |

### Energiekleuren (gelijk in beide apps)

| Bron | Token | Hex |
| --- | --- | --- |
| Zon | EnergySolar | `#F5C518` |
| Batterij | EnergyBattery | `#1FA67A` |
| Laadpaal | EnergyCharger | `#3B6FE0` |
| Net (afname en teruglevering) | EnergyGrid | `#9DB4BC` |
| Huis en verbruik | EnergyHome | `#36474C` |

Deze vijf gelden overal: grafieken, tegels, stroomdiagram, merk-badges. Nergens een andere kleur
voor een energiebron, ook niet "even een keer".

## Accent (het enige verschil)

| Token | LaadCash | Wattzicht |
| --- | --- | --- |
| Accent500 | `#1FA67A` | `#F5C518` |
| Accent600 (hover, pressed) | `#178A65` | `#D9AC0F` |
| Accent300 (licht, op donker) | `#7DF0D1` | `#FFE89A` |
| FocusRing | `rgba(31,166,122,.35)` | `rgba(245,197,24,.35)` |
| TextOnAccent | `#FFFFFF` | `#0A2540` |

Let op `TextOnAccent`: op LaadCash-groen kan witte tekst, op Wattzicht-geel niet. Geel met wit haalt
geen 4,5:1, navy op geel wel (ruim 10:1). Daarom staat dat per product in de tabel en niet als
vaste waarde in de code.

Let ook op dat `Accent500` van Wattzicht en `EnergySolar` dezelfde hex hebben (`#F5C518`). Dat is
geen slordigheid maar het merk: de zon is het accent. Gebruik in code wel de juiste naam, want als
het accent ooit verschuift moeten de grafieken niet meeschuiven.

## Fonts

| Rol | Font | Gewichten |
| --- | --- | --- |
| Display en koppen | Bricolage Grotesque | 600, 700 |
| Tekst | Instrument Sans | 400, 500, 600 |
| Cijfers (kWh, euro's, procenten) | IBM Plex Mono | 400, 500 |

Alle drie van Google Fonts onder de SIL Open Font License 1.1. In de app staan ze in
`src/Wattzicht.App/Resources/Fonts/` met het licentiebestand ernaast.

Cijfers in mono is geen smaak: in een lijst met bedragen of kWh staan de cijfers dan onder elkaar,
en een getal dat van 9,99 naar 10,01 gaat laat de regel niet verspringen.

## Radius

| Element | Radius |
| --- | --- |
| Knop | 10 |
| Invoerveld | 10 |
| Kaart | 12 |
| Paneel | 14 |
| Pill (badge, tab) | 999 |

## Schaduw

| Naam | Waarde |
| --- | --- |
| Kaart | `0 1px 2px rgba(10,37,64,.06), 0 6px 18px rgba(10,37,64,.06)` |
| Soft | `0 3px 10px rgba(10,37,64,.05)` |

## Dark mode

Dezelfde logica als LaadCash: navy wordt de achtergrond in plaats van de tekstkleur.

| Rol | Light | Dark |
| --- | --- | --- |
| Pagina | `#F6F8F7` | `#0A2540` |
| Kaart | `#FFFFFF` | `#0E3A4A` |
| Tekst | `#36474C` | `#F0F0F0` |
| Bijtekst | `#5C6B66` | `#9DB4BC` |
| Rand | `#DFE6E2` | `#1C4A5C` |
| Accent | geel | gelijk |

Het accent verschuift niet in dark mode. Geel op navy haalt ruim contrast, en een accent dat per
thema van kleur wisselt maakt een merk onherkenbaar.

## Waar deze tokens staan

| Plek | Bestand |
| --- | --- |
| Wattzicht-app (MAUI) | `src/Wattzicht.App/Resources/Styles/Colors.xaml` |
| Wattzicht-website | `frontend/src/style.css` (`--wz-*`) |
| LaadCash-website | `frontend/src/assets/theme.css` in de LaadCash-repo (`--lc-*`) |

In de app staat geen enkele kleur buiten `Colors.xaml`. `scripts/check-app-colors.sh` controleert
dat, en controleert ook dat elke sleutel die een scherm opvraagt echt bestaat; een `StaticResource`
die niet bestaat valt in MAUI pas op als het scherm opent.
