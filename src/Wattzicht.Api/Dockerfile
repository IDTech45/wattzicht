# Wattzicht API – multi-stage build.
# Bouwen vanuit de repo-root:  docker build -f src/Wattzicht.Api/Dockerfile -t wattzicht-api .

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Eerst alleen projectbestanden kopiëren zodat de restore-laag gecachet blijft.
COPY global.json ./
COPY src/Wattzicht.Shared/Wattzicht.Shared.csproj src/Wattzicht.Shared/
COPY src/Wattzicht.Api/Wattzicht.Api.csproj src/Wattzicht.Api/
RUN dotnet restore src/Wattzicht.Api/Wattzicht.Api.csproj

COPY src/Wattzicht.Shared/ src/Wattzicht.Shared/
COPY src/Wattzicht.Api/ src/Wattzicht.Api/
RUN dotnet publish src/Wattzicht.Api/Wattzicht.Api.csproj -c Release -o /app/publish --no-restore

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# tzdata voor Europe/Amsterdam (dagsamenvattingen en de 10–16u-regel werken op Nederlandse tijd).
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Europe/Amsterdam

COPY --from=build /app/publish .

# Niet als root draaien.
RUN useradd --system --uid 10001 --no-create-home wattzicht \
    && chown -R wattzicht:wattzicht /app
USER wattzicht

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Wattzicht.Api.dll"]
