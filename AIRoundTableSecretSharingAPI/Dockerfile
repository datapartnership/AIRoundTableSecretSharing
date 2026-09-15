FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM node:20-alpine AS web-ui-build
WORKDIR /src/web-ui

COPY ["web-ui/package.json", "web-ui/package-lock.json", "./"]
RUN npm ci

COPY ["web-ui/index.html", "web-ui/vite.config.js", "web-ui/.env.production", "./"]
COPY web-ui/public/ public/
COPY web-ui/src/ src/
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AIRoundTableSecretSharingAPI/AIRoundTableSecretSharingAPI.csproj", "AIRoundTableSecretSharingAPI/"]
COPY ["AIRoundTableSecretSharingCommon/AIRoundTableSecretSharingCommon.csproj", "AIRoundTableSecretSharingCommon/"]
COPY ["nuget.docker.config", "nuget.config"]

COPY AIRoundTableSecretSharingAPI/ AIRoundTableSecretSharingAPI/
COPY AIRoundTableSecretSharingCommon/ AIRoundTableSecretSharingCommon/

WORKDIR /src/AIRoundTableSecretSharingAPI
RUN dotnet publish -c Release -o /app/publish
RUN rm -rf /app/publish/wwwroot/assets
COPY --from=web-ui-build /src/web-ui/dist/ /app/publish/wwwroot/

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "AIRoundTableSecretSharingAPI.dll"]
