@echo off
title GRAB and LOG - Create Desktop Shortcut
set TARGET=%USERPROFILE%\Desktop\GRAB and LOG.url
echo [InternetShortcut]> "%TARGET%"
echo URL=http://localhost:5000>> "%TARGET%"
echo IconFile=%~dp0wwwroot\icons\icon-192.png>> "%TARGET%"
echo IconIndex=0>> "%TARGET%"
echo.
echo Desktop shortcut created: %TARGET%
echo.
echo IMPORTANT: Keep "dotnet run" running in the GrabLog-CS folder
echo for the app to work.
echo.
pause
