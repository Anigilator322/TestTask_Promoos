FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY global.json ./
COPY TestTask_Promoos.API/TestTask_Promoos.API.csproj TestTask_Promoos.API/
RUN dotnet restore TestTask_Promoos.API/TestTask_Promoos.API.csproj

COPY TestTask_Promoos.API/ TestTask_Promoos.API/
RUN dotnet publish TestTask_Promoos.API/TestTask_Promoos.API.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "TestTask_Promoos.API.dll"]
