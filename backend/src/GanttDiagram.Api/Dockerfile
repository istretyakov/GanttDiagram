# Контекст сборки — папка backend: docker build -f src/GanttDiagram.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config Directory.Build.props ./
COPY src/GanttDiagram.Domain/GanttDiagram.Domain.csproj src/GanttDiagram.Domain/
COPY src/GanttDiagram.Application/GanttDiagram.Application.csproj src/GanttDiagram.Application/
COPY src/GanttDiagram.Infrastructure/GanttDiagram.Infrastructure.csproj src/GanttDiagram.Infrastructure/
COPY src/GanttDiagram.Api/GanttDiagram.Api.csproj src/GanttDiagram.Api/
RUN dotnet restore src/GanttDiagram.Api/GanttDiagram.Api.csproj
COPY src/ src/
RUN dotnet publish src/GanttDiagram.Api/GanttDiagram.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Корневые сертификаты внутреннего ЦС (Jira по HTTPS): положите *.crt в backend/certs перед сборкой.
COPY certs/ /usr/local/share/ca-certificates/
# libgssapi-krb5-2 нужна Npgsql (иначе предупреждение при старте).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && update-ca-certificates \
    && mkdir -p /app/keys && chown app:app /app/keys

COPY --from=build /app .
USER app
# Образ aspnet по умолчанию слушает порт 8080 (ASPNETCORE_HTTP_PORTS).
ENV DataProtection__KeysPath=/app/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "GanttDiagram.Api.dll"]
