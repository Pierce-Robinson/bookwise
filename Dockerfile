# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Bookwise.slnx ./
COPY src/Bookwise.Api/Bookwise.Api.csproj src/Bookwise.Api/
RUN dotnet restore src/Bookwise.Api/Bookwise.Api.csproj
COPY src/ src/
RUN dotnet publish src/Bookwise.Api/Bookwise.Api.csproj -c Release -o /app --no-restore

# Runtime stage: ASP.NET runtime only, running as the image's non-root user
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data && chown "$APP_UID" /data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Bookwise="Data Source=/data/bookwise.db"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bookwise.Api.dll"]
