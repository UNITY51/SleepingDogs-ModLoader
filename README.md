# Sleeping Dogs ModLoader

Portable-менеджер модов для **Sleeping Dogs: Definitive Edition** поверх [SDmodding/FileRedirector](https://github.com/SDmodding/FileRedirector).

* Один exe, без установки, данные лежат в `Data/` рядом с программой.
* Включение/выключение модов переключателем; оригинальные файлы игры сохраняются и возвращаются.
* Свой формат `.sdmod` (zip + `mod.json`), также принимает обычные `.zip` (в том числе сжатые LZMA, как делает 7-Zip) — см. [docs/FORMAT.md](docs/FORMAT.md).
* Автопоиск игры в Steam; проверка FileRedirector и **автоматическая установка**, если его нет.
* Обнаружение конфликтов между модами, drag&drop, поиск, создание `.sdmod` из папки, экспорт.
* Интерфейс и сообщения об ошибках на английском (по умолчанию) и русском: кнопка 🌐 в правом верхнем углу, выбор запоминается.
* Кнопка «Запустить игру» (exe определяется автоматически, при неоднозначности программа спросит и запомнит).
* ПКМ по моду: удалить, экспортировать, открыть папку. Стрелка ▾ рядом с запуском: выбор exe (например, `sdhdship_debug.exe` для создателей модов).
* Архивы разбираются умно: `Characters_New`, `RedirectorData` или просто файлы без папок — всё попадёт куда нужно.
* Иконка окна берётся из установленной игры.

## Иконка

В репозитории лежит нейтральная иконка-заглушка (`src/SDModLoader.App/Assets/app.ico`). Иконку игры в репозиторий не кладём (это чужой товарный знак), но для своей сборки её легко подставить:

```
powershell -ExecutionPolicy Bypass -File make-icon.ps1 "D:\Steam\steamapps\common\SleepingDogsDefinitiveEdition\SDHDShip.exe"
publish.cmd
```
(укажите реальное имя exe игры). Иконка окна и панели задач и так подтягивается из exe игры во время работы.

## Использование

1. Скачайте `SDModLoader.exe` из Releases и запустите.
2. Игра ищется автоматически (иначе «Обзор…»). Если FileRedirector не найден — нажмите «Установить автоматически».
3. Перетащите `.sdmod`/`.zip` в окно и включите нужные моды переключателем.

> Если игра стоит в `Program Files`, запустите ModLoader от администратора.
> FileRedirector может требовать определённую версию exe игры — см. README организации [SDmodding](https://github.com/SDmodding).

## Сборка

Нужен [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) и Windows.

```
publish.cmd                                       # -> dist\SDModLoader.exe
dotnet run --project tests/SDModLoader.SelfTest   # тесты ядра (работают и на Linux/macOS)
```

## Архитектура

| Проект | Назначение |
|---|---|
| `SDModLoader.Core` (net8.0) | Формат, библиотека модов, установщик с учётом владения файлами и бэкапами, поиск игры, FileRedirector. Без UI-зависимостей. |
| `SDModLoader.App` (WPF) | GUI. |
| `SDModLoader.SelfTest` | Консольные тесты ядра без внешних пакетов. |

Лицензия: MIT.
