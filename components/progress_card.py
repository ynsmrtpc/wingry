"""
ProgressCard — Modern themed progress indicator card.
"""
from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import QVBoxLayout, QHBoxLayout
from PyQt6.QtGui import QFont
from qfluentwidgets import CardWidget, ProgressBar, BodyLabel, CaptionLabel
from services.theme import ThemeColors, ThemeRadius, get_progress_card_stylesheet


class ProgressCard(CardWidget):
    def __init__(self, app_name, action="installing", parent=None):
        super().__init__(parent=parent)
        self.app_name = app_name
        self.action = action

        self.setFixedHeight(100)
        self.setStyleSheet(get_progress_card_stylesheet())

        self.vBoxLayout = QVBoxLayout(self)
        self.vBoxLayout.setContentsMargins(18, 14, 18, 14)
        self.vBoxLayout.setSpacing(8)

        # Top row: action icon + title
        top_row = QHBoxLayout()
        top_row.setSpacing(8)

        action_icons = {"installing": "⬇️", "uninstalling": "🗑️", "updating": "🔄"}
        icon_text = action_icons.get(action, "⏳")

        self.icon_label = BodyLabel(icon_text, self)
        self.icon_label.setStyleSheet("background: transparent;")
        top_row.addWidget(self.icon_label)

        self.title = BodyLabel(f"{self.action.capitalize()}: {self.app_name}", self)
        title_font = QFont("Segoe UI", 11, QFont.Weight.DemiBold)
        self.title.setFont(title_font)
        self.title.setStyleSheet(f"color: {ThemeColors.TEXT_PRIMARY}; background: transparent;")
        top_row.addWidget(self.title)
        top_row.addStretch()

        self.vBoxLayout.addLayout(top_row)

        # Progress bar
        self.progress_bar = ProgressBar(self)
        self.progress_bar.setValue(0)
        self.progress_bar.setFixedHeight(4)
        self.vBoxLayout.addWidget(self.progress_bar)

        # Status text
        self.status_label = CaptionLabel("Waiting...", self)
        self.status_label.setStyleSheet(f"color: {ThemeColors.TEXT_TERTIARY}; background: transparent;")
        self.vBoxLayout.addWidget(self.status_label)

    def update_progress(self, percent, text):
        if percent is not None:
            self.progress_bar.setValue(percent)
            self.status_label.setText(f"{percent}% — {text}")
        else:
            self.progress_bar.setValue(100)
            display = text[:60] + "..." if len(text) > 60 else text
            self.status_label.setText(display)

    def set_error(self, message):
        self.icon_label.setText("❌")
        self.title.setText(f"Error: {self.app_name}")
        self.title.setStyleSheet(f"color: {ThemeColors.ERROR}; background: transparent;")
        self.progress_bar.error()
        self.status_label.setText(message)

    def set_done(self, message):
        self.icon_label.setText("✅")
        self.title.setText(f"Done: {self.app_name}")
        self.title.setStyleSheet(f"color: {ThemeColors.SUCCESS}; background: transparent;")
        self.progress_bar.setValue(100)
        self.status_label.setText(message)
