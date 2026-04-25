FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HackerNews.Api/HackerNews.Api.csproj HackerNews.Api/
RUN dotnet restore HackerNews.Api/HackerNews.Api.csproj
COPY . .
RUN dotnet publish HackerNews.Api/HackerNews.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "HackerNews.Api.dll"]
