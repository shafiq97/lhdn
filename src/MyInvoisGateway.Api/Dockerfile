FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/MyInvoisGateway.Api/MyInvoisGateway.Api.csproj src/MyInvoisGateway.Api/
RUN dotnet restore src/MyInvoisGateway.Api
COPY src/MyInvoisGateway.Api/ src/MyInvoisGateway.Api/
RUN dotnet publish src/MyInvoisGateway.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyInvoisGateway.Api.dll"]
