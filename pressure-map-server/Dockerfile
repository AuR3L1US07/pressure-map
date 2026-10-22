FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/PressureMap.Server/PressureMap.Server.csproj src/PressureMap.Server/
RUN dotnet restore src/PressureMap.Server/PressureMap.Server.csproj
COPY src/PressureMap.Server/ src/PressureMap.Server/
RUN dotnet publish src/PressureMap.Server/PressureMap.Server.csproj -c Release --no-restore -o /out /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
RUN mkdir /data && chown app:app /data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 Cloud__DataDirectory=/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "PressureMap.Server.dll"]
