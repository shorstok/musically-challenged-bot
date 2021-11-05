FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["musicallychallenged/musicallychallenged.csproj", "musicallychallenged/"]
RUN dotnet restore "musicallychallenged/musicallychallenged.csproj"
COPY . .
WORKDIR "/src/musicallychallenged"
RUN dotnet build "musicallychallenged.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "musicallychallenged.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "musicallychallenged.dll"]
