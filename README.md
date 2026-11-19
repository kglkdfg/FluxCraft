# FluxCraft

Современный лаунчер Minecraft на C# / .NET 8 / WPF.

## Структура

- **FluxCraft.Domain** — доменные сущности и правила предметной области.
- **FluxCraft.Application** — сценарии приложения и интерфейсы сервисов.
- **FluxCraft.Infrastructure** — файловое хранилище, сеть и интеграции.
- **FluxCraft.App** — WPF-интерфейс и точка входа.
- **FluxCraft.Tests** — модульные и интеграционные тесты.

## Требования

- Windows 10/11
- .NET 8 SDK

## Сборка

```powershell
dotnet restore FluxCraft.sln
dotnet build FluxCraft.sln -c Release
dotnet test FluxCraft.sln -c Release
dotnet run --project src/FluxCraft.App/FluxCraft.App.csproj
```

## План разработки

1. Базовая архитектура и оболочка интерфейса.
2. Каталог официальных версий Minecraft и установка Vanilla.
3. Управление Java Runtime, библиотеками, natives и assets.
4. Запуск игры и профили/экземпляры.
5. Поддержка Fabric, Forge, Quilt и NeoForge.
6. Авторизация Microsoft и управление аккаунтами.
