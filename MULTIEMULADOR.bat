@echo off
setlocal

rem Ruta fija de la app (funciona aunque el .bat se copie o se lance desde otro directorio)
set "APP=C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\publish\win-x64\MultiEmulator.App.exe"

if not exist "%APP%" (
  rem Fallback: mismo directorio que el .bat (si lo dejas en la carpeta del proyecto)
  set "APP=%~dp0publish\win-x64\MultiEmulator.App.exe"
)

if not exist "%APP%" (
  echo No se encuentra MultiEmulator.App.exe
  echo Esperado: C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\publish\win-x64\MultiEmulator.App.exe
  pause
  exit /b 1
)

start "" "%APP%"
endlocal
