# F01 (docs/architecture/DEPLOYMENT.md): web and migration images from one locked build; base images pinned by digest.
FROM mcr.microsoft.com/dotnet/sdk:8.0.425-bookworm-slim@sha256:ec9c0a0dc5f60adc2065762050638dd5ed5facfc53716d4580a42d86027c8e80 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/EbayGroup5.Core/EbayGroup5.Core.csproj src/EbayGroup5.Core/packages.lock.json src/EbayGroup5.Core/
COPY src/EbayGroup5.Application/EbayGroup5.Application.csproj src/EbayGroup5.Application/packages.lock.json src/EbayGroup5.Application/
COPY src/EbayGroup5.Infrastructure/EbayGroup5.Infrastructure.csproj src/EbayGroup5.Infrastructure/packages.lock.json src/EbayGroup5.Infrastructure/
COPY src/EbayGroup5.Database/EbayGroup5.Database.csproj src/EbayGroup5.Database/packages.lock.json src/EbayGroup5.Database/
COPY src/EbayGroup5.Web/EbayGroup5.Web.csproj src/EbayGroup5.Web/packages.lock.json src/EbayGroup5.Web/
RUN dotnet restore src/EbayGroup5.Web/EbayGroup5.Web.csproj --locked-mode \
    && dotnet restore src/EbayGroup5.Database/EbayGroup5.Database.csproj --locked-mode
COPY src/ src/
# The Database project embeds the immutable source schema (baseline migration).
COPY docs/requirements/sources/clone_ebay_sqlserver_schema.sql docs/requirements/sources/
RUN dotnet publish src/EbayGroup5.Web/EbayGroup5.Web.csproj -c Release --no-restore -o /out/web /p:UseAppHost=false \
    && dotnet publish src/EbayGroup5.Database/EbayGroup5.Database.csproj -c Release --no-restore -o /out/migrate /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0.31-bookworm-slim@sha256:a3cd573ac05cf88ca496e3309cb7f7c44aa16664352d1ee41eda93154f50cdbf AS migrate
WORKDIR /app
COPY --from=build /out/migrate .
USER app
ENTRYPOINT ["dotnet", "EbayGroup5.Database.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0.31-bookworm-slim@sha256:a3cd573ac05cf88ca496e3309cb7f7c44aa16664352d1ee41eda93154f50cdbf AS web
WORKDIR /app
# Data Protection keys live in a volume mounted here; the directory must exist and belong to the non-root user.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet
COPY --from=build /out/web .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "EbayGroup5.Web.dll"]
