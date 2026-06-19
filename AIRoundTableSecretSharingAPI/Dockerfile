FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AIRoundTableSecretSharingAPI/AIRoundTableSecretSharingAPI.csproj", "AIRoundTableSecretSharingAPI/"]
COPY ["AIRoundTableSecretSharingCommon/AIRoundTableSecretSharingCommon.csproj", "AIRoundTableSecretSharingCommon/"]
COPY ["nuget.config", "."]

COPY AIRoundTableSecretSharingAPI/ AIRoundTableSecretSharingAPI/
COPY AIRoundTableSecretSharingCommon/ AIRoundTableSecretSharingCommon/

WORKDIR /src/AIRoundTableSecretSharingAPI
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "AIRoundTableSecretSharingAPI.dll"]
