"""
SystemTab — Modernized with themed info cards and accent progress bars.
"""
from PyQt6.QtCore import Qt, QTimer
from PyQt6.QtWidgets import QFrame, QVBoxLayout, QHBoxLayout, QGridLayout
from PyQt6.QtGui import QFont
from qfluentwidgets import (
    ScrollArea, TitleLabel, BodyLabel, PushButton,
    CardWidget, ProgressBar, CaptionLabel
)
from services.system_service import SystemService
from services.theme import (
    ThemeColors, ThemeRadius, ThemeSpacing,
    get_system_card_stylesheet, get_scroll_area_stylesheet,
    get_tab_frame_stylesheet
)


class SystemTab(ScrollArea):
    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("SystemTab")
        self.setWidgetResizable(True)
        self.setStyleSheet(get_scroll_area_stylesheet())

        self.view = QFrame(self)
        self.view.setStyleSheet("QFrame {background: transparent;}")
        self.setWidget(self.view)

        self.vBoxLayout = QVBoxLayout(self.view)
        self.vBoxLayout.setContentsMargins(
            ThemeSpacing.XL, ThemeSpacing.XL, ThemeSpacing.XL, ThemeSpacing.XL
        )
        self.vBoxLayout.setSpacing(ThemeSpacing.LG)

        # ── Header ──
        self.header_layout = QHBoxLayout()
        self.title = TitleLabel("System Information", self.view)
        self.title.setStyleSheet(f"color: {ThemeColors.TEXT_PRIMARY};")
        self.copy_btn = PushButton("📋  Copy Specs", self.view)
        self.copy_btn.setFixedHeight(36)
        self.copy_btn.clicked.connect(self.copy_specs)
        self.header_layout.addWidget(self.title)
        self.header_layout.addStretch(1)
        self.header_layout.addWidget(self.copy_btn)
        self.vBoxLayout.addLayout(self.header_layout)

        self.vBoxLayout.addSpacing(ThemeSpacing.SM)

        # ── Grid of Cards ──
        self.gridLayout = QGridLayout()
        self.gridLayout.setSpacing(ThemeSpacing.LG)

        self.cards = {}
        self.cards["cpu"] = self.create_card(0, 0, "🖥  CPU", ThemeColors.ACCENT)
        self.cards["ram"] = self.create_card(0, 1, "🧠  RAM", "#8764B8")
        self.cards["os"] = self.create_card(1, 0, "💻  Operating System", "#107C10")
        self.cards["network"] = self.create_card(1, 1, "🌐  Network", "#D83B01")
        self.cards["storage"] = self.create_card(2, 0, "💾  Storage", "#4C4A48", col_span=2)

        self.vBoxLayout.addLayout(self.gridLayout)
        self.vBoxLayout.addStretch(1)

        # ── Live Update Timer ──
        self.timer = QTimer(self)
        self.timer.timeout.connect(self.update_loop)
        self.timer.start(2000)

        self.update_loop()

    def create_card(self, row, col, title, accent_color, col_span=1):
        card = CardWidget(self.view)
        card.setStyleSheet(get_system_card_stylesheet())

        layout = QVBoxLayout(card)
        layout.setContentsMargins(20, 18, 20, 18)
        layout.setSpacing(ThemeSpacing.SM)

        # Title with accent line
        title_row = QHBoxLayout()
        accent_line = QFrame()
        accent_line.setFixedSize(4, 20)
        accent_line.setStyleSheet(
            f"background-color: {accent_color}; border-radius: 2px;"
        )
        title_row.addWidget(accent_line)

        title_lbl = BodyLabel(title, card)
        title_font = QFont("Segoe UI", 12, QFont.Weight.DemiBold)
        title_lbl.setFont(title_font)
        title_lbl.setStyleSheet(f"color: {ThemeColors.TEXT_PRIMARY}; background: transparent;")
        title_row.addWidget(title_lbl)
        title_row.addStretch()
        layout.addLayout(title_row)

        layout.addSpacing(6)

        # Content
        content = CaptionLabel("Loading...", card)
        content.setStyleSheet(f"color: {ThemeColors.TEXT_SECONDARY}; background: transparent; line-height: 1.4;")
        content.setWordWrap(True)
        layout.addWidget(content)

        # Progress bar with accent color
        bar = ProgressBar(card)
        bar.setValue(0)
        bar.setFixedHeight(4)
        layout.addWidget(bar)

        self.gridLayout.addWidget(card, row, col, 1, col_span)

        return {"frame": card, "content": content, "bar": bar, "accent": accent_color}

    def update_loop(self):
        try:
            cpu = SystemService.get_cpu_info()
            ram = SystemService.get_ram_info()
            net = SystemService.get_network_info()
            os_info = SystemService.get_os_info()
            disks = SystemService.get_storage_info()

            self.refresh_ui(cpu, ram, net, os_info, disks)
        except Exception:
            pass

    def refresh_ui(self, cpu, ram, net, os_info, disks):
        self.cards["cpu"]["content"].setText(
            f"{cpu['name']}\n"
            f"Cores: {cpu['cores_physical']} Physical / {cpu['cores_logical']} Logical\n"
            f"Speed: {cpu['freq']}\n"
            f"Usage: {cpu['usage']}%"
        )
        self.cards["cpu"]["bar"].setValue(int(cpu["usage"]))

        self.cards["ram"]["content"].setText(
            f"Total: {ram['total']}\n"
            f"Used: {ram['used']}\n"
            f"Available: {ram['available']}\n"
            f"Usage: {ram['usage_percent']}%"
        )
        self.cards["ram"]["bar"].setValue(int(ram["usage_percent"]))

        self.cards["os"]["content"].setText(
            f"{os_info['system']} {os_info['release']} (Build: {os_info['version']})\n"
            f"Uptime: {os_info['uptime']}\n"
            f"Boot: {os_info['boot_time']}"
        )
        self.cards["os"]["bar"].hide()

        self.cards["network"]["content"].setText(
            f"Hostname: {net['hostname']}\n"
            f"IPv4: {net['ip']}\n"
            f"Sent: {net['bytes_sent']}\n"
            f"Received: {net['bytes_recv']}"
        )
        self.cards["network"]["bar"].hide()

        disk_text = ""
        for d in disks:
            disk_text += (
                f"{d['device']} ({d['fstype']}) — "
                f"Total: {d['total']}, Free: {d['free']} [{d['percent']}%]\n"
            )
        self.cards["storage"]["content"].setText(disk_text.strip())
        self.cards["storage"]["bar"].hide()

    def copy_specs(self):
        from PyQt6.QtWidgets import QApplication
        specs = (
            self.cards["cpu"]["content"].text() + "\n\n"
            + self.cards["ram"]["content"].text() + "\n\n"
            + self.cards["os"]["content"].text()
        )
        QApplication.clipboard().setText(specs)
