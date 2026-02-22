FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/agent_runner/AgentRunner/AgentRunner.csproj", "src/agent_runner/AgentRunner/"]
RUN dotnet restore "src/agent_runner/AgentRunner/AgentRunner.csproj"
COPY . .
WORKDIR "/src/src/agent_runner/AgentRunner"
RUN dotnet build "AgentRunner.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AgentRunner.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AgentRunner.dll"]
