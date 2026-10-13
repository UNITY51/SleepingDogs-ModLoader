@echo off
rem Собирает portable-версию: dist\SDModLoader.exe (один файл, .NET не нужен)
dotnet publish src\SDModLoader.App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
  -o dist
