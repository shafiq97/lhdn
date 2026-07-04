FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/MockLhdn/MockLhdn.csproj src/MockLhdn/
RUN dotnet restore src/MockLhdn
COPY src/MockLhdn/ src/MockLhdn/
RUN dotnet publish src/MockLhdn -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
ENTRYPOINT ["dotnet", "MockLhdn.dll"]
