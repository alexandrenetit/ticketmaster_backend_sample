# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/BookingService/BookingService.csproj", "src/BookingService/"]
RUN dotnet restore "src/BookingService/BookingService.csproj"
COPY . .
RUN dotnet publish "src/BookingService/BookingService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BookingService.dll"]
