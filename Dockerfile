# Adapted from https://github.com/dotnet/dotnet-docker/blob/main/samples/aspnetapp/Dockerfile.chiseled

# Build stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy project files and restore as distinct layers
COPY --link Alloy.Api/*.csproj ./Alloy.Api/
COPY --link Alloy.Api.Data/*.csproj ./Alloy.Api.Data/
COPY --link Alloy.Api.Migrations.PostgreSQL/*.csproj ./Alloy.Api.Migrations.PostgreSQL/
WORKDIR /source/Alloy.Api
RUN dotnet restore -a $TARGETARCH

# Copy source code and publish app
WORKDIR /source
COPY --link . .
WORKDIR /source/Alloy.Api
RUN dotnet publish -a $TARGETARCH --no-restore -o /app

# Debug Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS debug
ENV DOTNET_HOSTBUILDER__RELOADCONFIGCHANGE=false
EXPOSE 8080
WORKDIR /app
COPY --link --from=build /app .
USER $APP_UID
ENTRYPOINT ["./Alloy.Api"]

# Production stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble-chiseled AS prod
ARG commit
ENV COMMIT=$commit
ENV DOTNET_HOSTBUILDER__RELOADCONFIGCHANGE=false
EXPOSE 8080
WORKDIR /app
COPY --link --from=build /app .
ENTRYPOINT ["./Alloy.Api"]
