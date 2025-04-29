# Используем официальный .NET SDK для сборки
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Копируем проект и восстанавливаем зависимости
COPY . ./
RUN dotnet restore
RUN dotnet publish -c Release -o out

# Используем минимальный .NET рантайм
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

# Указываем порт и переменную окружения
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

ENTRYPOINT ["dotnet", "Api.dll"]
