import ctypes
import os
import io
import winreg
from PIL import Image, ImageDraw, ImageFont

class IconService:
    CACHE_DIR = os.path.join(os.environ.get('APPDATA', ''), 'WinGetManager', 'icons')

    @staticmethod
    def _ensure_dir():
        if not os.path.exists(IconService.CACHE_DIR):
            os.makedirs(IconService.CACHE_DIR, exist_ok=True)

    @staticmethod
    def generate_letter_avatar(name, size=64):
        """Fallback: generates a colored circle with the first letter."""
        IconService._ensure_dir()
        letter = name[0].upper() if name else "?"
        color = "#0078D4" # Windows blue default
        
        # Determine some specific colors based on hash
        colors = ["#0078D4", "#107C10", "#D83B01", "#002050", "#E81123"]
        color = colors[hash(name) % len(colors)] if name else color

        img = Image.new('RGBA', (size, size), (255, 255, 255, 0))
        draw = ImageDraw.Draw(img)
        draw.ellipse((0, 0, size, size), fill=color)

        try:
            # Try to load a generic default font
            font = ImageFont.truetype("arialbd.ttf", int(size * 0.5))
        except:
            font = ImageFont.load_default()

        # Calculate text bounding box
        bbox = draw.textbbox((0, 0), letter, font=font)
        text_w = bbox[2] - bbox[0]
        text_h = bbox[3] - bbox[1]
        
        draw.text(((size - text_w) / 2, (size - text_h) / 2 - 2), letter, fill="white", font=font)
        
        cache_path = os.path.join(IconService.CACHE_DIR, f"{name[:10]}_{size}.png")
        img.save(cache_path)
        return cache_path

    @staticmethod
    def get_icon(app_id, app_name):
        """
        Multilayer fallback strategy:
        1. winreg (placeholder)
        2. exe extraction (placeholder)
        3. favicon (placeholder)
        4. letter_avatar (implemented)
        For now, we just use the letter avatar.
        """
        # We can implement the complex logic later.
        # Fallback to letter avatar immediately for this MVP.
        return IconService.generate_letter_avatar(app_name)
