FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=5003
EXPOSE 5003

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
