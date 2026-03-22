import sys
from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import QApplication

def main():
    if hasattr(Qt, 'AA_EnableHighDpiScaling'):
        QApplication.setAttribute(Qt.ApplicationAttribute.AA_EnableHighDpiScaling, True)
    if hasattr(Qt, 'AA_UseHighDpiPixmaps'):
        QApplication.setAttribute(Qt.ApplicationAttribute.AA_UseHighDpiPixmaps, True)

    app = QApplication(sys.argv)
    
    from app import WinGetManagerApp
    window = WinGetManagerApp()
    window.show()
    
    sys.exit(app.exec())

if __name__ == "__main__":
    main()
