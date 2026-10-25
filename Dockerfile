# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SafeShare/SafeShare.csproj src/SafeShare/
RUN dotnet restore src/SafeShare/SafeShare.csproj

COPY src/ src/
RUN dotnet publish src/SafeShare/SafeShare.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# SQLite database and data-protection keys live here; mount a volume to keep pending secrets across container re-creation.
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    SafeShare__DataDirectory=/data

COPY --from=build /app .

USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["dotnet", "SafeShare.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "SafeShare.dll"]
