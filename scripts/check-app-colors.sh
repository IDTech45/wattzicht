#!/usr/bin/env bash
# Controleert de kleurregels van de MAUI-app:
#
#   1. Buiten Colors.xaml staat geen enkele kleurcode in XAML of C#.
#   2. Elke sleutel die een scherm opvraagt bestaat ook echt in Colors.xaml.
#
# Die tweede is de belangrijkste. Een {StaticResource Watdanook} valt in MAUI pas op als dat scherm
# opengaat, niet bij het bouwen. Een typefout in een kleurnaam kan dus ongemerkt meeliften tot een
# klant hem tegenkomt.
#
# Gebruik: ./scripts/check-app-colors.sh   (exitcode 1 als er iets mis is)

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/src/Wattzicht.App"
COLORS="$APP/Resources/Styles/Colors.xaml"
STYLES="$APP/Resources/Styles/Styles.xaml"
APPXAML="$APP/App.xaml"

if [[ ! -f "$COLORS" ]]; then
  echo "Colors.xaml niet gevonden op $COLORS" >&2
  exit 1
fi

fouten=0

# Alleen de echte bronbestanden; obj/ en bin/ bevatten gegenereerde kopieën.
bronnen() {
  find "$APP" -type f \( -name '*.xaml' -o -name '*.cs' \) \
    -not -path '*/obj/*' -not -path '*/bin/*'
}

# ---- 1. Hardcoded kleuren --------------------------------------------------
echo "1. Kleurcodes buiten Colors.xaml"
losse_hex=$(bronnen | grep -v 'Resources/Styles/Colors.xaml' \
  | xargs grep -n '#[0-9A-Fa-f]\{6\}' 2>/dev/null || true)

if [[ -n "$losse_hex" ]]; then
  echo "   FOUT: kleurcodes buiten Colors.xaml:" >&2
  echo "$losse_hex" | sed "s|$ROOT/||" | sed 's/^/     /' >&2
  fouten=1
else
  echo "   ok, geen"
fi

# ---- 2. Onbekende sleutels ------------------------------------------------
echo "2. Opgevraagde sleutels bestaan"

# Alle sleutels uit Colors.xaml, Styles.xaml en App.xaml: die drie definiëren resources.
bekend=$(grep -o 'x:Key="[^"]*"' "$COLORS" "$STYLES" "$APPXAML" | sed 's/.*x:Key="//; s/"$//' | sort -u)

# Sleutels uit XAML: {StaticResource X} en {AppThemeBinding Light={StaticResource X} ...}.
# Colors.xaml zelf vraagt niets op; dat bestand overslaan houdt een toelichting in een commentaar
# buiten de uitkomst.
gevraagd_xaml=$(bronnen | grep '\.xaml$' | grep -v 'Resources/Styles/Colors.xaml' \
  | xargs grep -oh 'StaticResource [A-Za-z0-9_]*' 2>/dev/null \
  | sed 's/StaticResource //' | sort -u)

# Sleutels uit C#: ThemeColors.Get("X"), ThemeRes("X","XDark") en Res("X"). De hele aanroep pakken en
# daarna alle tekstwaarden eruit halen, zodat bij Themed/ThemeRes ook de dark-sleutel wordt gecheckt.
gevraagd_cs=$(bronnen | grep '\.cs$' \
  | xargs grep -ohE '(ThemeColors\.Get|ThemeColors\.Themed|ThemeRes|Res)\([^)]*\)' 2>/dev/null \
  | grep -o '"[A-Za-z0-9_]*"' | tr -d '"' | sort -u)

onbekend=""
for sleutel in $gevraagd_xaml $gevraagd_cs; do
  if ! grep -qx "$sleutel" <<< "$bekend"; then
    onbekend+="$sleutel"$'\n'
  fi
done

# Deze kent MAUI zelf, die hoeven niet in onze bestanden te staan.
INGEBOUWD='^(Transparent|White|Black)$'
onbekend=$(echo "$onbekend" | grep -v '^$' | grep -vE "$INGEBOUWD" || true)

if [[ -n "$onbekend" ]]; then
  echo "   FOUT: deze sleutels worden opgevraagd maar bestaan niet:" >&2
  echo "$onbekend" | sed 's/^/     /' >&2
  fouten=1
else
  echo "   ok, alle sleutels bestaan"
fi

if [[ $fouten -eq 0 ]]; then
  echo "Kleurcontrole geslaagd."
else
  echo "Kleurcontrole gefaald." >&2
fi
exit $fouten
