"""
PackageCard — Modern gradient card with hover animation.
"""
from PyQt6.QtCore import Qt, pyqtSignal, QPropertyAnimation, QEasingCurve
from PyQt6.QtWidgets import QVBoxLayout, QHBoxLayout, QGraphicsOpacityEffect
from PyQt6.QtGui import QFont, QCursor
from qfluentwidgets import CardWidget, BodyLabel, CaptionLabel
from services.theme import ThemeColors, ThemeRadius, get_package_card_stylesheet


class PackageCard(CardWidget):
    package_clicked = pyqtSignal(dict)

    def __init__(self, package_data, parent=None):
        super().__init__(parent=parent)
        self.package_data = package_data
        self._hovered = False

        self.setFixedSize(280, 140)
        self.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
        self.setStyleSheet(get_package_card_stylesheet(False))

        self.mainLayout = QVBoxLayout(self)
        self.mainLayout.setContentsMargins(20, 18, 20, 16)
        self.mainLayout.setSpacing(6)

        # Header: Icon + Title
        self.headerLayout = QHBoxLayout()
        self.headerLayout.setSpacing(10)

        icon_text = self.package_data.get("icon", "📦")
        self.icon_label = BodyLabel(icon_text, self)
        icon_font = QFont("Segoe UI Emoji", 22)
        self.icon_label.setFont(icon_font)
        self.icon_label.setStyleSheet("background: transparent;")
        self.headerLayout.addWidget(self.icon_label)

        self.title_label = BodyLabel(self.package_data.get("title", "Package"), self)
        title_font = QFont("Segoe UI", 13, QFont.Weight.DemiBold)
        self.title_label.setFont(title_font)
        self.title_label.setStyleSheet(f"color: {ThemeColors.TEXT_PRIMARY}; background: transparent;")
        self.headerLayout.addWidget(self.title_label)
        self.headerLayout.addStretch()

        self.mainLayout.addLayout(self.headerLayout)

        # Description
        self.desc_label = CaptionLabel(self.package_data.get("description", ""), self)
        self.desc_label.setWordWrap(True)
        self.desc_label.setStyleSheet(f"color: {ThemeColors.TEXT_SECONDARY}; background: transparent;")
        self.mainLayout.addWidget(self.desc_label)

        self.mainLayout.addStretch()

        # App count badge
        apps = self.package_data.get("apps", [])
        self.count_label = CaptionLabel(f"📥 {len(apps)} Apps", self)
        self.count_label.setStyleSheet(f"""
            color: {ThemeColors.TEXT_ACCENT};
            font-weight: 600;
            background: transparent;
        """)
        self.mainLayout.addWidget(self.count_label, 0, Qt.AlignmentFlag.AlignRight)

        # Opacity effect for hover
        self._opacity_effect = QGraphicsOpacityEffect(self)
        self._opacity_effect.setOpacity(0.92)
        self.setGraphicsEffect(self._opacity_effect)

    def enterEvent(self, event):
        super().enterEvent(event)
        self._hovered = True
        self.setStyleSheet(get_package_card_stylesheet(True))
        anim = QPropertyAnimation(self._opacity_effect, b"opacity")
        anim.setDuration(200)
        anim.setStartValue(self._opacity_effect.opacity())
        anim.setEndValue(1.0)
        anim.setEasingCurve(QEasingCurve.Type.OutCubic)
        anim.start()
        self._hover_anim = anim  # prevent GC

    def leaveEvent(self, event):
        super().leaveEvent(event)
        self._hovered = False
        self.setStyleSheet(get_package_card_stylesheet(False))
        anim = QPropertyAnimation(self._opacity_effect, b"opacity")
        anim.setDuration(200)
        anim.setStartValue(self._opacity_effect.opacity())
        anim.setEndValue(0.92)
        anim.setEasingCurve(QEasingCurve.Type.OutCubic)
        anim.start()
        self._hover_anim = anim

    def mouseReleaseEvent(self, e):
        super().mouseReleaseEvent(e)
        if e.button() == Qt.MouseButton.LeftButton:
            self.package_clicked.emit(self.package_data)
