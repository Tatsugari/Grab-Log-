@echo off
title GRAB and LOG Server
cd /d "%~dp0"
echo Starting GRAB and LOG...
echo Open http://localhost:5000 in your browser
echo.
start http://localhost:5000
dotnet run
pause
