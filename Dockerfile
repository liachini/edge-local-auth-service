FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["LocalAuthService.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN mkdir -p /var/lib/localauth && \
    groupadd -r localauth && \
    useradd -r -g localauth -d /var/lib/localauth localauth && \
    chown localauth:localauth /var/lib/localauth

COPY --from=build /app/publish .
USER localauth

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5063
ENV DataDirectory=/var/lib/localauth

VOLUME ["/var/lib/localauth"]
EXPOSE 5063

ENTRYPOINT ["dotnet", "LocalAuthService.dll"]
