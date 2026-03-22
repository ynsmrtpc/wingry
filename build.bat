@echo off
echo Building Wingy using PyInstaller...
pyinstaller --noconfirm --onefile --windowed --add-data "assets;assets" --icon="assets/icon.ico" --name "Wingy" main.py
echo Build complete. Check the \dist directory.
pause
