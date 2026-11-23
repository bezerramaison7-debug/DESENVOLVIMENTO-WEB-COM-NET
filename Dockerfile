FROM node:24.22.3-bookworm-slim AS frontend
WORKDIR /src/PortalTarefas.Frontend
COPY PortalTarefas.Frontend/package*.json ./
RUN npm ci
COPY PortalTarefas.Frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src
COPY PortalTarefas.Web/PortalTarefas.Web.csproj PortalTarefas.Web/
RUN dotnet restore PortalTarefas.Web
COPY PortalTarefas.Web/ PortalTarefas.Web/
COPY --from=frontend /src/PortalTarefas.Frontend/dist/ PortalTarefas.Web/wwwroot/
RUN dotnet publish PortalTarefas.Web -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS runtime
WORKDIR /app
COPY --from=build /app/ ./
RUN mkdir /data && chown app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Portal__RedirecionarHttps=false \
    Portal__Dependencia=http://127.0.0.1:8080/health/live
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=4s CMD ["dotnet", "PortalTarefas.Web.dll", "--probe"]
ENTRYPOINT ["dotnet", "PortalTarefas.Web.dll"]
