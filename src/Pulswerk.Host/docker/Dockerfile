FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .

# Build Pulswerk
RUN dotnet publish src/Pulswerk.Host/Pulswerk.Host.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Pulswerk.dll"]
EXPOSE 47808/udp
EXPOSE 5000
