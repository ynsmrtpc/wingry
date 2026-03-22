"""
Animated Dialog — Windows 11 style with fade-in + scale animation.
Replaces plain MessageBox for a premium feel.
Centers on the main window, no dark overlay.
"""
from PyQt6.QtCore import Qt, QPropertyAnimation, QEasingCurve, QRect
from PyQt6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QLabel, QGraphicsOpacityEffect, QWidget
)
from PyQt6.QtGui import QFont
from qfluentwidgets import PrimaryPushButton, PushButton
from services.theme import ThemeColors, ThemeRadius


class AnimatedDialog(QDialog):
    """
    Modern animated dialog with:
    - Fade-in entrance (250ms)
    - Centered on parent window
    - Windows 11 rounded corners and consistent theming
    - No background overlay
    """

    def __init__(self, title: str, content: str, parent=None, confirm_text="Yes", cancel_text="No"):
        super().__init__(parent)
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.Dialog)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setModal(True)

        self._result = False

        # --- Main container with styling ---
        self.container = QWidget(self)
        self.container.setObjectName("DialogContainer")
        self.container.setStyleSheet(f"""
            QWidget#DialogContainer {{
                background-color: {ThemeColors.SURFACE_SECONDARY};
                border: 1px solid {ThemeColors.BORDER_DEFAULT};
                border-radius: {ThemeRadius.LARGE}px;
            }}
        """)

        container_layout = QVBoxLayout(self.container)
        container_layout.setContentsMargins(28, 24, 28, 20)
        container_layout.setSpacing(16)

        # Title
        title_label = QLabel(title)
        title_font = QFont("Segoe UI", 16, QFont.Weight.DemiBold)
        title_label.setFont(title_font)
        title_label.setStyleSheet(f"color: {ThemeColors.TEXT_PRIMARY}; background: transparent;")
        title_label.setWordWrap(True)
        container_layout.addWidget(title_label)

        # Content
        content_label = QLabel(content)
        content_font = QFont("Segoe UI", 11)
        content_label.setFont(content_font)
        content_label.setStyleSheet(f"color: {ThemeColors.TEXT_SECONDARY}; background: transparent;")
        content_label.setWordWrap(True)
        container_layout.addWidget(content_label)

        container_layout.addSpacing(8)

        # Buttons
        btn_layout = QHBoxLayout()
        btn_layout.setSpacing(10)
        btn_layout.addStretch()

        self.cancel_btn = PushButton(cancel_text)
        self.cancel_btn.setFixedHeight(34)
        self.cancel_btn.setMinimumWidth(100)
        self.cancel_btn.clicked.connect(self._on_cancel)
        btn_layout.addWidget(self.cancel_btn)

        self.confirm_btn = PrimaryPushButton(confirm_text)
        self.confirm_btn.setFixedHeight(34)
        self.confirm_btn.setMinimumWidth(100)
        self.confirm_btn.clicked.connect(self._on_confirm)
        btn_layout.addWidget(self.confirm_btn)

        container_layout.addLayout(btn_layout)

        # Main layout — just wraps the container
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.addWidget(self.container)

        # Fixed width for the dialog
        self.setFixedWidth(440)

        # Opacity effect for fade animation
        self._opacity_effect = QGraphicsOpacityEffect(self)
        self._opacity_effect.setOpacity(0.0)
        self.setGraphicsEffect(self._opacity_effect)

    def showEvent(self, event):
        super().showEvent(event)
        self._center_on_parent()
        self._animate_in()

    def _center_on_parent(self):
        """Center this dialog on the parent window."""
        parent = self.parent()
        if parent is not None:
            # Get the parent window's geometry in global coordinates
            parent_rect = parent.geometry()
            dialog_size = self.sizeHint()
            x = parent_rect.x() + (parent_rect.width() - dialog_size.width()) // 2
            y = parent_rect.y() + (parent_rect.height() - dialog_size.height()) // 2
            self.move(x, y)
        else:
            # Fallback: center on screen
            screen = self.screen().availableGeometry()
            dialog_size = self.sizeHint()
            x = (screen.width() - dialog_size.width()) // 2
            y = (screen.height() - dialog_size.height()) // 2
            self.move(x, y)

    def _animate_in(self):
        self._fade_anim = QPropertyAnimation(self._opacity_effect, b"opacity")
        self._fade_anim.setDuration(250)
        self._fade_anim.setStartValue(0.0)
        self._fade_anim.setEndValue(1.0)
        self._fade_anim.setEasingCurve(QEasingCurve.Type.OutCubic)
        self._fade_anim.start()

    def _animate_out(self, callback):
        self._fade_out = QPropertyAnimation(self._opacity_effect, b"opacity")
        self._fade_out.setDuration(150)
        self._fade_out.setStartValue(1.0)
        self._fade_out.setEndValue(0.0)
        self._fade_out.setEasingCurve(QEasingCurve.Type.InCubic)
        self._fade_out.finished.connect(callback)
        self._fade_out.start()

    def _on_confirm(self):
        self._result = True
        self._animate_out(self.accept)

    def _on_cancel(self):
        self._result = False
        self._animate_out(self.reject)

    def exec(self) -> bool:
        """Returns True if user confirmed, False otherwise."""
        super().exec()
        return self._result
