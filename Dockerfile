# Multi-stage Dockerfile for LaptopCtl Backend & Web Dashboard
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["src/LaptopCtl.Shared/LaptopCtl.Shared.csproj", "LaptopCtl.Shared/"]
COPY ["src/LaptopCtl.Backend/LaptopCtl.Backend.csproj", "LaptopCtl.Backend/"]
RUN dotnet restore "LaptopCtl.Backend/LaptopCtl.Backend.csproj"

# Copy full source and build
COPY ["src/LaptopCtl.Shared/", "LaptopCtl.Shared/"]
COPY ["src/LaptopCtl.Backend/", "LaptopCtl.Backend/"]
WORKDIR "/src/LaptopCtl.Backend"
RUN dotnet build "LaptopCtl.Backend.csproj" -c Release -o /app/build

# Publish release binary
FROM build AS publish
RUN dotnet publish "LaptopCtl.Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=publish /app/publish .
COPY dashboard/ /app/dashboard/
COPY installer/ /app/installer/

ENTRYPOINT ["dotnet", "LaptopCtl.Backend.dll"]
