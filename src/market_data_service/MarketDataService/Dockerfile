FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/market_data_service/MarketDataService/MarketDataService.csproj", "src/market_data_service/MarketDataService/"]
RUN dotnet restore "src/market_data_service/MarketDataService/MarketDataService.csproj"
COPY . .
WORKDIR "/src/src/market_data_service/MarketDataService"
RUN dotnet build "MarketDataService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MarketDataService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MarketDataService.dll"]
