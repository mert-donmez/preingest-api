#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
RUN apt-get update
RUN apt-get install -y libgdiplus
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Noord.Hollands.Archief.Preingest.WebApi/Noord.Hollands.Archief.Preingest.WebApi.csproj", "Noord.Hollands.Archief.Preingest.WebApi/"]
RUN dotnet restore "Noord.Hollands.Archief.Preingest.WebApi/Noord.Hollands.Archief.Preingest.WebApi.csproj"
COPY . .
WORKDIR "/src/Noord.Hollands.Archief.Preingest.WebApi"
RUN dotnet build "Noord.Hollands.Archief.Preingest.WebApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Noord.Hollands.Archief.Preingest.WebApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Noord.Hollands.Archief.Preingest.WebApi.dll"]