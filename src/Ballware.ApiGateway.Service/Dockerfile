FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5010

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ./Ballware.ApiGateway.Service ./Ballware.ApiGateway.Service

WORKDIR "/src/Ballware.ApiGateway.Service"
RUN dotnet build "Ballware.ApiGateway.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Ballware.ApiGateway.Service.csproj" -c Release -o /app/publish

FROM base AS final
USER app
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Ballware.ApiGateway.Service.dll"]