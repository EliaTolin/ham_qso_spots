# Server of the spots (F24). Built by Coolify from this repo.
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.2-resolute AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props HamQso.Spots.slnx ./
COPY src/HamQso.Spots.Domain/HamQso.Spots.Domain.csproj src/HamQso.Spots.Domain/
COPY src/HamQso.Spots.Application/HamQso.Spots.Application.csproj src/HamQso.Spots.Application/
COPY src/HamQso.Spots.Infrastructure/HamQso.Spots.Infrastructure.csproj src/HamQso.Spots.Infrastructure/
COPY src/HamQso.Spots.Host/HamQso.Spots.Host.csproj src/HamQso.Spots.Host/
RUN dotnet restore src/HamQso.Spots.Host/HamQso.Spots.Host.csproj
COPY src/ src/
RUN dotnet publish src/HamQso.Spots.Host/HamQso.Spots.Host.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.2-alpine3.24
WORKDIR /app
COPY --from=build /app .
# The ports come from the Server section (8080 public, 8081 internal), not from the image default.
ENV ASPNETCORE_HTTP_PORTS=
USER $APP_UID
# 8080: /health (public). 8081: /mqtt/* for Mosquitto (Docker network only).
EXPOSE 8080 8081
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "HamQso.Spots.Host.dll"]
