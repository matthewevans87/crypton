FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=5003
EXPOSE 5003

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Crypton.Api.AgentRunner/Crypton.Api.AgentRunner.csproj", "src/Crypton.Api.AgentRunner/"]
RUN dotnet restore "src/Crypton.Api.AgentRunner/Crypton.Api.AgentRunner.csproj"
COPY . .
WORKDIR "/src/src/Crypton.Api.AgentRunner"
RUN dotnet build "Crypton.Api.AgentRunner.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Crypton.Api.AgentRunner.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Crypton.Api.AgentRunner.dll"]
