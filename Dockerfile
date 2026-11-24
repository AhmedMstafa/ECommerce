# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for restore layer caching.
COPY ECommerce.slnx NuGet.Config ./
COPY src/ECommerce.Domain/ECommerce.Domain.csproj src/ECommerce.Domain/
COPY src/ECommerce.UseCases/ECommerce.UseCases.csproj src/ECommerce.UseCases/
COPY src/ECommerce.Infrastructure/ECommerce.Infrastructure.csproj src/ECommerce.Infrastructure/
COPY src/ECommerce.API/ECommerce.API.csproj src/ECommerce.API/

RUN dotnet restore src/ECommerce.API/ECommerce.API.csproj

# Copy only the source required to publish the API.
COPY src/ src/

RUN dotnet publish src/ECommerce.API/ECommerce.API.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

# Copy published output only; no SDK, source, or tests in final image.
COPY --from=build --chown=app:app /app/publish .

# Use the non-root user provided by the ASP.NET runtime image.
USER app

ENTRYPOINT ["dotnet", "ECommerce.API.dll"]