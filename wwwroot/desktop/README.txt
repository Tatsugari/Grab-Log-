HOW TO GET GrabLog-Setup.exe (real installer)
=============================================

STEP 1 — Install tools (once)
-----------------------------
1. .NET 10 SDK
   https://dotnet.microsoft.com/download/dotnet/10.0

2. Inno Setup 6 (for Setup.exe wizard)
   https://jrsoftware.org/isinfo.php
   During install, enable "Install Inno Setup Preprocessor" if asked.
   Optional: add install folder to PATH so "iscc" works from any terminal.

STEP 2 — Build
--------------
1. Unzip GrabLog.Desktop.zip
2. Double-click:  Build-Installer.bat

This will:
  - Create GrabLog.exe (desktop app)
  - If Inno Setup is installed → create GrabLog-Setup.exe

STEP 3 — Installer location
---------------------------
  installer-output\GrabLog-Setup.exe

That file is the installer you can share / put on a USB.
Users run GrabLog-Setup.exe → Next → Install → desktop shortcut.

WITHOUT Inno Setup
------------------
Build-Installer.bat still creates:
  bin\Release\net10.0-windows\win-x64\publish\GrabLog.exe
You can copy that .exe and run it (no wizard).

WebView2
--------
Usually already on Windows 10/11 with Edge.
If missing: https://developer.microsoft.com/microsoft-edge/webview2/
