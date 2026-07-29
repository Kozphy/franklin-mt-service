FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["src/Franklin.MtService/Franklin.MtService.csproj", "src/Franklin.MtService/"]
RUN dotnet restore "src/Franklin.MtService/Franklin.MtService.csproj"

COPY . .
RUN dotnet publish "src/Franklin.MtService/Franklin.MtService.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
COPY --from=build --chown=appuser:appgroup /app/publish .
USER appuser
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD wget -q -O - http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Franklin.MtService.dll"]
