
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY Mova.sln ./
COPY Mova.Api/Mova.Api.csproj Mova.Api/
COPY Mova.Application/Mova.Application.csproj Mova.Application/
COPY Mova.Domain/Mova.Domain.csproj Mova.Domain/
COPY Mova.Infrastructure/Mova.Infrastructure.csproj Mova.Infrastructure/
COPY Mova.Shared/Mova.Shared.csproj Mova.Shared/

# Restore dependencies
RUN dotnet restore Mova.Api/Mova.Api.csproj

# Copy the remaining source files
COPY . .

# Publish the API
RUN dotnet publish Mova.Api/Mova.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Mova.Api.dll"]
