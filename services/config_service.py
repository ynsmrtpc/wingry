import sys
import os
import json

CONFIG_DIR = os.path.join(os.environ.get('APPDATA', ''), 'Wingy')
CONFIG_FILE = os.path.join(CONFIG_DIR, 'config.json')

DEFAULT_CONFIG = {
    "theme": "dark",
    "favorites": []
}

class ConfigService:
    @staticmethod
    def get_resource_path(relative_path):
        """Get the absolute path to a resource, works for dev and PyInstaller."""
        try:
            # PyInstaller creates a temp folder and stores path in _MEIPASS
            base_path = sys._MEIPASS
        except Exception:
            base_path = os.path.abspath(".")
        return os.path.join(base_path, relative_path)

    @staticmethod
    def _ensure_dir():
        if not os.path.exists(CONFIG_DIR):
            os.makedirs(CONFIG_DIR, exist_ok=True)

    @staticmethod
    def load_config():
        ConfigService._ensure_dir()
        if not os.path.exists(CONFIG_FILE):
            ConfigService.save_config(DEFAULT_CONFIG)
            return DEFAULT_CONFIG.copy()
        
        try:
            with open(CONFIG_FILE, 'r', encoding='utf-8') as f:
                config = json.load(f)
                return {**DEFAULT_CONFIG, **config}
        except Exception:
            return DEFAULT_CONFIG.copy()

    @staticmethod
    def save_config(config_data):
        ConfigService._ensure_dir()
        try:
            with open(CONFIG_FILE, 'w', encoding='utf-8') as f:
                json.dump(config_data, f, indent=4)
        except Exception as e:
            print(f"Error saving config: {e}")

    @staticmethod
    def get(key, default=None):
        return ConfigService.load_config().get(key, default)

    @staticmethod
    def set(key, value):
        config = ConfigService.load_config()
        config[key] = value
        ConfigService.save_config(config)
