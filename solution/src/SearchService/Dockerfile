# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/SearchService/SearchService.csproj", "src/SearchService/"]
RUN dotnet restore "src/SearchService/SearchService.csproj"
COPY . .
RUN dotnet publish "src/SearchService/SearchService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SearchService.dll"]
