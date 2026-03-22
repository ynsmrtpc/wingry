import sys
from PyQt6.QtCore import Qt, QSize
from PyQt6.QtGui import QIcon
from PyQt6.QtWidgets import QApplication
from qfluentwidgets import FluentWindow, NavigationItemPosition, setTheme, Theme, FluentIcon as FIF

from services.config_service import ConfigService
from tabs.discover import DiscoverTab
from tabs.installed import InstalledTab
from tabs.system import SystemTab
from tabs.about import AboutTab

class WinGetManagerApp(FluentWindow):
    def __init__(self):
        super().__init__()
        
        self.setWindowTitle("Wingy")
        self.setWindowIcon(QIcon(ConfigService.get_resource_path("assets/icon.ico")))
        self.resize(1100, 900)
        self.setMinimumSize(800, 600)
        
        theme = ConfigService.get("theme", "dark")
        if theme == "dark":
            setTheme(Theme.DARK)
        else:
            setTheme(Theme.LIGHT)
            
        # Create real sub-interfaces
        self.discover_interface = DiscoverTab(self)
        self.installed_interface = InstalledTab(self)
        self.system_interface = SystemTab(self)
        self.about_interface = AboutTab(self)
        
        self.initNavigation()
        self._center()

    def _center(self):
        screen = self.screen().availableGeometry()
        w, h = self.width(), self.height()
        x = (screen.width() - w) // 2
        y = (screen.height() - h) // 2
        self.move(x, y)

    def initNavigation(self):
        self.addSubInterface(self.discover_interface, FIF.HOME, "Discover")
        self.addSubInterface(self.installed_interface, FIF.APPLICATION, "Installed")
        self.addSubInterface(self.system_interface, FIF.DEVELOPER_TOOLS, "System")
        
        self.addSubInterface(self.about_interface, FIF.INFO, "About", position=NavigationItemPosition.BOTTOM)
