FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=5001
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/monitoring_dashboard/backend/MonitoringDashboard.Api/MonitoringDashboard.Api.csproj", "src/monitoring_dashboard/backend/MonitoringDashboard.Api/"]
RUN dotnet restore "src/monitoring_dashboard/backend/MonitoringDashboard.Api/MonitoringDashboard.Api.csproj"
COPY . .
WORKDIR "/src/src/monitoring_dashboard/backend/MonitoringDashboard.Api"
RUN dotnet build "MonitoringDashboard.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MonitoringDashboard.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MonitoringDashboard.Api.dll"]
