"""
WinGet Manager — Unified Theme System
Windows 11 Design Language compliant centralized theme.
All colors, radii, spacing, and QSS generators live here.
"""

class ThemeColors:
    """Windows 11 inspired color palette."""

    # --- Accent ---
    ACCENT = "#0078D4"
    ACCENT_HOVER = "#1A86D9"
    ACCENT_PRESSED = "#006CBE"
    ACCENT_LIGHT = "rgba(0, 120, 212, 0.15)"
    ACCENT_SUBTLE = "rgba(0, 120, 212, 0.08)"

    # --- Surfaces (dark mode) ---
    SURFACE_PRIMARY = "#202020"
    SURFACE_SECONDARY = "#2D2D2D"
    SURFACE_TERTIARY = "#383838"
    SURFACE_CARD = "#2A2A2A"
    SURFACE_CARD_HOVER = "#323232"

    # --- Borders ---
    BORDER_SUBTLE = "rgba(255, 255, 255, 0.06)"
    BORDER_DEFAULT = "rgba(255, 255, 255, 0.08)"
    BORDER_ACCENT = "rgba(0, 120, 212, 0.4)"

    # --- Text ---
    TEXT_PRIMARY = "#FFFFFF"
    TEXT_SECONDARY = "rgba(255, 255, 255, 0.68)"
    TEXT_TERTIARY = "rgba(255, 255, 255, 0.45)"
    TEXT_ON_ACCENT = "#FFFFFF"
    TEXT_ACCENT = "#60CDFF"

    # --- Semantic ---
    SUCCESS = "#6CCB5F"
    WARNING = "#FCE100"
    ERROR = "#FF4545"
    INFO = "#60CDFF"

    # --- Gradient ---
    GRADIENT_CARD_START = "#1B3A5C"
    GRADIENT_CARD_END = "#2D1B4E"
    GRADIENT_HERO_START = "#0F2847"
    GRADIENT_HERO_END = "#1A0F30"

    # --- Overlay / Glassmorphism ---
    OVERLAY_DIM = "rgba(0, 0, 0, 0.5)"
    GLASS_BG = "rgba(32, 32, 32, 0.85)"
    GLASS_BORDER = "rgba(255, 255, 255, 0.08)"


class ThemeRadius:
    """Border radius values (px)."""
    SMALL = 6
    MEDIUM = 8
    LARGE = 12
    XLARGE = 16
    ROUND = 50


class ThemeSpacing:
    """Spacing values (px)."""
    XS = 4
    SM = 8
    MD = 12
    LG = 16
    XL = 24
    XXL = 32


class ThemeShadow:
    """Box shadow definitions for QSS."""
    CARD = "0px 2px 8px rgba(0, 0, 0, 0.25)"
    CARD_HOVER = "0px 4px 16px rgba(0, 0, 0, 0.35)"
    DIALOG = "0px 8px 32px rgba(0, 0, 0, 0.5)"


# ─── QSS Stylesheet Generators ────────────────────────────────────────────

def get_table_stylesheet() -> str:
    """Consistent table style for Discover & Installed tabs."""
    return f"""
        QTableWidget {{
            background-color: {ThemeColors.SURFACE_CARD};
            border: 1px solid {ThemeColors.BORDER_SUBTLE};
            border-radius: {ThemeRadius.MEDIUM}px;
            gridline-color: transparent;
            outline: none;
            color: {ThemeColors.TEXT_PRIMARY};
            selection-background-color: {ThemeColors.ACCENT_LIGHT};
            selection-color: {ThemeColors.TEXT_PRIMARY};
        }}
        QTableWidget::item {{
            padding: 6px 8px;
            border-bottom: 1px solid {ThemeColors.BORDER_SUBTLE};
        }}
        QTableWidget::item:hover {{
            background-color: {ThemeColors.SURFACE_CARD_HOVER};
        }}
        QHeaderView::section {{
            background-color: {ThemeColors.SURFACE_TERTIARY};
            color: {ThemeColors.TEXT_SECONDARY};
            padding: 8px 8px;
            border: none;
            border-bottom: 1px solid {ThemeColors.BORDER_DEFAULT};
            font-weight: 600;
            font-size: 12px;
        }}
        QHeaderView::section:first {{
            border-top-left-radius: {ThemeRadius.MEDIUM}px;
        }}
        QHeaderView::section:last {{
            border-top-right-radius: {ThemeRadius.MEDIUM}px;
        }}
        QScrollBar:vertical {{
            background: transparent;
            width: 6px;
            margin: 0;
        }}
        QScrollBar::handle:vertical {{
            background: rgba(255, 255, 255, 0.15);
            min-height: 30px;
            border-radius: 3px;
        }}
        QScrollBar::handle:vertical:hover {{
            background: rgba(255, 255, 255, 0.25);
        }}
        QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{
            height: 0;
        }}
        QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical {{
            background: none;
        }}
    """


def get_bottom_bar_stylesheet() -> str:
    """Glassmorphism bottom action bar."""
    return f"""
        QFrame#BottomBar {{
            background-color: {ThemeColors.GLASS_BG};
            border-top: 1px solid {ThemeColors.GLASS_BORDER};
            border-radius: 0px;
        }}
    """


def get_search_stylesheet() -> str:
    """Modern search bar."""
    return f"""
        SearchLineEdit {{
            background-color: {ThemeColors.SURFACE_CARD};
            border: 1px solid {ThemeColors.BORDER_DEFAULT};
            border-radius: {ThemeRadius.MEDIUM}px;
            padding: 6px 12px;
            color: {ThemeColors.TEXT_PRIMARY};
        }}
        SearchLineEdit:focus {{
            border: 1px solid {ThemeColors.ACCENT};
        }}
    """


def get_package_card_stylesheet(is_hovered: bool = False) -> str:
    """Gradient package card."""
    bg = f"qlineargradient(x1:0, y1:0, x2:1, y2:1, stop:0 {ThemeColors.GRADIENT_CARD_START}, stop:1 {ThemeColors.GRADIENT_CARD_END})"
    border_color = ThemeColors.BORDER_ACCENT if is_hovered else ThemeColors.BORDER_DEFAULT
    return f"""
        CardWidget {{
            background: {bg};
            border: 1px solid {border_color};
            border-radius: {ThemeRadius.LARGE}px;
        }}
    """


def get_progress_card_stylesheet() -> str:
    """Modern progress card."""
    return f"""
        CardWidget {{
            background-color: {ThemeColors.SURFACE_CARD};
            border: 1px solid {ThemeColors.BORDER_SUBTLE};
            border-radius: {ThemeRadius.MEDIUM}px;
        }}
    """


def get_system_card_stylesheet() -> str:
    """System info card."""
    return f"""
        CardWidget {{
            background-color: {ThemeColors.SURFACE_CARD};
            border: 1px solid {ThemeColors.BORDER_SUBTLE};
            border-radius: {ThemeRadius.LARGE}px;
        }}
    """


def get_about_hero_stylesheet() -> str:
    """About page hero area."""
    bg = f"qlineargradient(x1:0, y1:0, x2:1, y2:1, stop:0 {ThemeColors.GRADIENT_HERO_START}, stop:1 {ThemeColors.GRADIENT_HERO_END})"
    return f"""
        QFrame#HeroFrame {{
            background: {bg};
            border-radius: {ThemeRadius.XLARGE}px;
            border: 1px solid {ThemeColors.BORDER_SUBTLE};
        }}
    """


def get_scroll_area_stylesheet() -> str:
    """Transparent scroll area with themed scrollbar."""
    return f"""
        QScrollArea {{
            background: transparent;
            border: none;
        }}
        QScrollBar:horizontal {{
            background: transparent;
            height: 6px;
            margin: 0;
        }}
        QScrollBar::handle:horizontal {{
            background: rgba(255, 255, 255, 0.15);
            min-width: 30px;
            border-radius: 3px;
        }}
        QScrollBar::handle:horizontal:hover {{
            background: rgba(255, 255, 255, 0.25);
        }}
        QScrollBar::add-line:horizontal, QScrollBar::sub-line:horizontal {{
            width: 0;
        }}
        QScrollBar::add-page:horizontal, QScrollBar::sub-page:horizontal {{
            background: none;
        }}
        QScrollBar:vertical {{
            background: transparent;
            width: 6px;
            margin: 0;
        }}
        QScrollBar::handle:vertical {{
            background: rgba(255, 255, 255, 0.15);
            min-height: 30px;
            border-radius: 3px;
        }}
        QScrollBar::handle:vertical:hover {{
            background: rgba(255, 255, 255, 0.25);
        }}
        QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{
            height: 0;
        }}
        QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical {{
            background: none;
        }}
    """


def get_tab_frame_stylesheet(object_name: str) -> str:
    """Consistent transparent frame for tab roots."""
    return f"QFrame#{object_name} {{background: transparent;}}"


def get_button_accent_stylesheet() -> str:
    """Accent button override."""
    return f"""
        PrimaryPushButton {{
            background-color: {ThemeColors.ACCENT};
            border-radius: {ThemeRadius.SMALL}px;
            padding: 6px 16px;
            color: {ThemeColors.TEXT_ON_ACCENT};
            font-weight: 600;
        }}
        PrimaryPushButton:hover {{
            background-color: {ThemeColors.ACCENT_HOVER};
        }}
        PrimaryPushButton:pressed {{
            background-color: {ThemeColors.ACCENT_PRESSED};
        }}
    """


def get_button_outlined_stylesheet() -> str:
    """Outlined secondary button."""
    return f"""
        PushButton {{
            background-color: transparent;
            border: 1px solid {ThemeColors.BORDER_DEFAULT};
            border-radius: {ThemeRadius.SMALL}px;
            padding: 6px 16px;
            color: {ThemeColors.TEXT_PRIMARY};
        }}
        PushButton:hover {{
            background-color: {ThemeColors.SURFACE_TERTIARY};
            border-color: {ThemeColors.TEXT_SECONDARY};
        }}
    """
