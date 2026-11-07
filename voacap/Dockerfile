# VOACAP service of HamQSO (F71): voacapl (NTIA/ITS VOACAP for Linux, public domain) behind a small HTTP API.
# Build context: the repo root (Coolify: base directory "/", Dockerfile "voacap/Dockerfile").
# The .NET images of version 11 are Ubuntu 26.04, where voacapl 0.7.6 is a package.
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.2-resolute AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY voacap/src/HamQso.Voacap.Core/HamQso.Voacap.Core.csproj voacap/src/HamQso.Voacap.Core/
COPY voacap/src/HamQso.Voacap.Host/HamQso.Voacap.Host.csproj voacap/src/HamQso.Voacap.Host/
RUN dotnet restore voacap/src/HamQso.Voacap.Host/HamQso.Voacap.Host.csproj
COPY voacap/src/ voacap/src/
RUN dotnet publish voacap/src/HamQso.Voacap.Host/HamQso.Voacap.Host.csproj -c Release -o /app --no-restore

# The tests with the real voacapl: docker build --target engine-test (voacap/test-engine.sh).
FROM build AS engine-test
RUN apt-get update -qq && apt-get install -y -qq --no-install-recommends voacapl && rm -rf /var/lib/apt/lists/* \
    && cp -r /usr/share/voacapl/itshfbc /opt/itshfbc
COPY voacap/tests/ voacap/tests/
ENV Voacap__Itshfbc=/opt/itshfbc
RUN dotnet test --project voacap/tests/HamQso.Voacap.Engine.Tests

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.2 AS final
RUN apt-get update -qq && apt-get install -y -qq --no-install-recommends voacapl curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app \
    && cp -r /usr/share/voacapl/itshfbc /app/itshfbc \
    && chown -R $APP_UID /app/itshfbc \
    && echo "voacapl $(dpkg-query -W -f='${Version}' voacapl | cut -d- -f1)" > /etc/voacapl-engine
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Voacap__Itshfbc=/app/itshfbc
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/health > /dev/null || exit 1
# The engine name comes from the package installed above; VOACAP_TOKEN from Coolify.
ENTRYPOINT ["sh", "-c", "export Voacap__Engine=\"$(cat /etc/voacapl-engine)\" Voacap__Token=\"${VOACAP_TOKEN:-}\"; exec dotnet HamQso.Voacap.Host.dll"]
