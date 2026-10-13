
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Mova.sln ./
COPY Mova.Api/Mova.Api.csproj Mova.Api/
COPY Mova.Application/Mova.Application.csproj Mova.Application/
COPY Mova.Domain/Mova.Domain.csproj Mova.Domain/
COPY Mova.Infrastructure/Mova.Infrastructure.csproj Mova.Infrastructure/
COPY Mova.Shared/Mova.Shared.csproj Mova.Shared/

# Restore API and referenced projects
RUN dotnet restore Mova.Api/Mova.Api.csproj

# Install EF Core CLI.
# Ideally, match this version to your EF Core package version.
RUN dotnet tool install --global dotnet-ef --version 8.0.0
ENV PATH="${PATH}:/root/.dotnet/tools"

# Copy the remaining source, including Infrastructure migrations
COPY . .

# Publish API
RUN dotnet publish Mova.Api/Mova.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Generate a migration executable
RUN dotnet ef migrations bundle \
    --project Mova.Infrastructure \
    --startup-project Mova.Api \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained \
    --output /app/migrate


# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
COPY --from=build /app/migrate /app/migrate
COPY docker-entrypoint.sh /app/docker-entrypoint.sh

RUN chmod +x /app/migrate /app/docker-entrypoint.sh

ENTRYPOINT ["/app/docker-entrypoint.sh"]
