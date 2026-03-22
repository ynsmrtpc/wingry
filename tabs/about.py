"""
AboutTab — Modernized with hero gradient, card layout, and consistent theming.
"""
import webbrowser
from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import QFrame, QVBoxLayout, QHBoxLayout
from PyQt6.QtGui import QFont
from qfluentwidgets import (
    TitleLabel, BodyLabel, PushButton, PrimaryPushButton,
    CaptionLabel, CardWidget
)
from services.theme import (
    ThemeColors, ThemeRadius, ThemeSpacing,
    get_about_hero_stylesheet, get_tab_frame_stylesheet
)


class AboutTab(QFrame):
    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("AboutTab")
        self.setStyleSheet(get_tab_frame_stylesheet("AboutTab"))

        self.outerLayout = QVBoxLayout(self)
        self.outerLayout.setContentsMargins(
            ThemeSpacing.XL, ThemeSpacing.XL, ThemeSpacing.XL, ThemeSpacing.XL
        )
        self.outerLayout.setAlignment(Qt.AlignmentFlag.AlignCenter)

        # ── Hero Card ──
        self.hero_frame = QFrame(self)
        self.hero_frame.setObjectName("HeroFrame")
        self.hero_frame.setStyleSheet(get_about_hero_stylesheet())
        self.hero_frame.setFixedWidth(520)

        hero_layout = QVBoxLayout(self.hero_frame)
        hero_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        hero_layout.setSpacing(ThemeSpacing.LG)
        hero_layout.setContentsMargins(
            ThemeSpacing.XXL, 40, ThemeSpacing.XXL, 36
        )

        # Logo
        logo = TitleLabel("📦", self.hero_frame)
        logo_font = QFont("Segoe UI Emoji", 52)
        logo.setFont(logo_font)
        logo.setAlignment(Qt.AlignmentFlag.AlignCenter)
        logo.setStyleSheet("background: transparent;")
        hero_layout.addWidget(logo)

        # App name
        title = TitleLabel("WinGet Manager", self.hero_frame)
        title_font = QFont("Segoe UI", 24, QFont.Weight.Bold)
        title.setFont(title_font)
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        title.setStyleSheet(f"color: {ThemeColors.TEXT_PRIMARY}; background: transparent;")
        hero_layout.addWidget(title)

        # Tagline
        tagline = BodyLabel("The modern Windows package manager", self.hero_frame)
        tagline.setAlignment(Qt.AlignmentFlag.AlignCenter)
        tagline.setStyleSheet(f"color: {ThemeColors.TEXT_SECONDARY}; background: transparent;")
        hero_layout.addWidget(tagline)

        # Version badge
        version_container = QHBoxLayout()
        version_container.setAlignment(Qt.AlignmentFlag.AlignCenter)
        version_badge = CaptionLabel("v1.0.0", self.hero_frame)
        version_badge.setStyleSheet(f"""
            color: {ThemeColors.TEXT_ACCENT};
            background-color: {ThemeColors.ACCENT_SUBTLE};
            padding: 4px 14px;
            border-radius: {ThemeRadius.ROUND}px;
            font-weight: 600;
        """)
        version_container.addWidget(version_badge)
        hero_layout.addLayout(version_container)

        hero_layout.addSpacing(ThemeSpacing.SM)

        # Description
        desc = BodyLabel(
            "WinGet Manager is a UI wrapper for the\n"
            "Windows Package Manager (winget).\n"
            "Easily discover, install, update, and\n"
            "remove applications on your PC.",
            self.hero_frame
        )
        desc.setAlignment(Qt.AlignmentFlag.AlignCenter)
        desc.setStyleSheet(f"color: {ThemeColors.TEXT_SECONDARY}; background: transparent; line-height: 1.5;")
        hero_layout.addWidget(desc)

        hero_layout.addSpacing(ThemeSpacing.MD)

        # Action buttons
        btn_layout = QHBoxLayout()
        btn_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        btn_layout.setSpacing(ThemeSpacing.MD)

        btn_gh = PrimaryPushButton("  GitHub  ", self.hero_frame)
        btn_gh.setFixedHeight(36)
        btn_gh.clicked.connect(lambda: webbrowser.open("https://github.com/"))
        btn_layout.addWidget(btn_gh)

        btn_bug = PushButton("  Report a Bug  ", self.hero_frame)
        btn_bug.setFixedHeight(36)
        btn_bug.clicked.connect(lambda: webbrowser.open("https://github.com/"))
        btn_layout.addWidget(btn_bug)

        hero_layout.addLayout(btn_layout)

        self.outerLayout.addWidget(self.hero_frame, 0, Qt.AlignmentFlag.AlignCenter)

        self.outerLayout.addSpacing(ThemeSpacing.XL)

        # ── Footer Credits ──
        credits = CaptionLabel("Built with Python + PyQt-Fluent-Widgets", self)
        credits.setAlignment(Qt.AlignmentFlag.AlignCenter)
        credits.setStyleSheet(f"color: {ThemeColors.TEXT_TERTIARY};")
        self.outerLayout.addWidget(credits)

        footer = CaptionLabel("Made with ❤️ for Windows 11", self)
        footer.setAlignment(Qt.AlignmentFlag.AlignCenter)
        footer.setStyleSheet(f"color: {ThemeColors.TEXT_TERTIARY};")
        self.outerLayout.addWidget(footer)
