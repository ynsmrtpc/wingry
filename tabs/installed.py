"""
InstalledTab — Modernized with unified theme system.
"""
import threading
from PyQt6.QtCore import Qt, pyqtSignal, QTimer
from PyQt6.QtWidgets import (
    QFrame, QVBoxLayout, QHBoxLayout, QHeaderView,
    QAbstractItemView, QTableWidgetItem
)
from PyQt6.QtGui import QIcon, QFont
from qfluentwidgets import (
    TitleLabel, SearchLineEdit, PrimaryPushButton, PushButton,
    TableWidget, CaptionLabel, CheckBox
)
from services.winget_service import WingetService
from services.icon_service import IconService
from services.theme import (
    ThemeColors, ThemeSpacing,
    get_table_stylesheet, get_bottom_bar_stylesheet,
    get_tab_frame_stylesheet
)
from components.progress_card import ProgressCard
from components.animated_dialog import AnimatedDialog


class InstalledTab(QFrame):
    progress_signal = pyqtSignal(str, str, object, str)
    fetch_signal = pyqtSignal(list)

    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("InstalledTab")
        self.setStyleSheet(get_tab_frame_stylesheet("InstalledTab"))

        self.mainLayout = QVBoxLayout(self)
        self.mainLayout.setContentsMargins(
            ThemeSpacing.XL, ThemeSpacing.XL, ThemeSpacing.XL, 0
        )
        self.mainLayout.setSpacing(ThemeSpacing.SM)

        self.all_installed = []
        self.all_upgradable = []
        self.selected_apps = {}
        self.processing_apps = {}

        self.progress_signal.connect(self.handle_progress)
        self.fetch_signal.connect(self.render_list)

        # ── Top Bar ──
        self.top_frame = QHBoxLayout()
        self.top_frame.setSpacing(ThemeSpacing.SM)

        self.search_entry = SearchLineEdit(self)
        self.search_entry.setPlaceholderText("Filter installed apps...")
        self.search_entry.setFixedWidth(320)
        self.search_entry.setFixedHeight(40)
        self.search_entry.textChanged.connect(self.filter_results)
        self.top_frame.addWidget(self.search_entry)

        self.refresh_btn = PushButton("🔄  Refresh", self)
        self.refresh_btn.setFixedHeight(36)
        self.refresh_btn.clicked.connect(self.load_data)
        self.top_frame.addWidget(self.refresh_btn)
        self.top_frame.addStretch(1)

        self.mainLayout.addLayout(self.top_frame)
        self.mainLayout.addSpacing(ThemeSpacing.MD)

        # ── Table ──
        self.table = TableWidget(self)
        self.table.setColumnCount(5)
        self.table.setHorizontalHeaderLabels(
            ["", "Name", "Id", "Version", "Available Update"]
        )
        self.table.verticalHeader().hide()
        self.table.horizontalHeader().setSectionResizeMode(
            0, QHeaderView.ResizeMode.Fixed
        )
        self.table.setColumnWidth(0, 50)
        self.table.horizontalHeader().setSectionResizeMode(
            1, QHeaderView.ResizeMode.Stretch
        )
        self.table.horizontalHeader().setSectionResizeMode(
            2, QHeaderView.ResizeMode.Stretch
        )
        self.table.horizontalHeader().setSectionResizeMode(
            3, QHeaderView.ResizeMode.ResizeToContents
        )
        self.table.horizontalHeader().setSectionResizeMode(
            4, QHeaderView.ResizeMode.ResizeToContents
        )

        self.table.setSelectionBehavior(
            QAbstractItemView.SelectionBehavior.SelectRows
        )
        self.table.setSelectionMode(
            QAbstractItemView.SelectionMode.NoSelection
        )
        self.table.setEditTriggers(
            QAbstractItemView.EditTrigger.NoEditTriggers
        )
        self.table.setShowGrid(False)
        self.table.setStyleSheet(get_table_stylesheet())

        self.mainLayout.addWidget(self.table, 1)

        # ── Progress Area ──
        self.progress_area = QVBoxLayout()
        self.progress_area.setSpacing(ThemeSpacing.SM)
        self.mainLayout.addLayout(self.progress_area)

        # ── Bottom Bar (Glassmorphism) ──
        self.bottom_bar = QFrame(self)
        self.bottom_bar.setObjectName("BottomBar")
        self.bottom_bar.setStyleSheet(get_bottom_bar_stylesheet())
        self.bottom_bar_layout = QHBoxLayout(self.bottom_bar)
        self.bottom_bar_layout.setContentsMargins(
            ThemeSpacing.XL, ThemeSpacing.LG, ThemeSpacing.XL, ThemeSpacing.LG
        )

        self.selection_label = TitleLabel("0 selected", self.bottom_bar)
        self.selection_label.setStyleSheet(
            f"color: {ThemeColors.TEXT_PRIMARY}; font-size: 14px;"
        )
        self.bottom_bar_layout.addWidget(self.selection_label)
        self.bottom_bar_layout.addStretch(1)

        self.update_btn = PrimaryPushButton("🔄  Update", self.bottom_bar)
        self.update_btn.setFixedHeight(36)
        self.update_btn.clicked.connect(self.start_updates)
        self.bottom_bar_layout.addWidget(self.update_btn)

        self.uninstall_btn = PushButton("🗑  Uninstall", self.bottom_bar)
        self.uninstall_btn.setFixedHeight(36)
        self.uninstall_btn.clicked.connect(self.confirm_uninstall)
        self.bottom_bar_layout.addWidget(self.uninstall_btn)

        self.mainLayout.addWidget(self.bottom_bar, 0)
        self.bottom_bar.hide()

        self.load_data()

    # ── Data Loading ──

    def load_data(self):
        self.table.setRowCount(0)
        self.table.setRowCount(1)
        loading_item = QTableWidgetItem("Loading... Please wait.")
        loading_item.setFlags(Qt.ItemFlag.NoItemFlags)
        self.table.setItem(0, 1, loading_item)

        def _fetch():
            WingetService.invalidate_cache()
            self.all_installed = WingetService.list_installed()
            self.all_upgradable = WingetService.get_upgradable()

            upgrades_map = {
                app.get("Id", ""): app
                for app in self.all_upgradable
                if app.get("Id")
            }
            for app in self.all_installed:
                app_id = app.get("Id", "")
                if app_id in upgrades_map:
                    app["AvailableVersion"] = upgrades_map[app_id].get("Available", "")

            self.fetch_signal.emit(self.all_installed)

        threading.Thread(target=_fetch, daemon=True).start()

    def filter_results(self, text):
        query = text.lower()
        if not query:
            self.render_list(self.all_installed)
            return

        filtered = [
            app for app in self.all_installed
            if query in app.get("Name", "").lower()
            or query in app.get("Id", "").lower()
        ]
        self.render_list(filtered)

    def render_list(self, app_list):
        self.table.setRowCount(0)

        if not app_list:
            return

        limit = min(150, len(app_list))
        self.table.setRowCount(limit)

        for i in range(limit):
            app = app_list[i]

            name = app.get("Name", "Unknown")
            app_id = app.get("Id", "")
            ver = app.get("Version", "")

            # Checkbox
            cb_container = QFrame()
            cb_container.setStyleSheet("background: transparent;")
            cb_layout = QHBoxLayout(cb_container)
            cb_layout.setContentsMargins(0, 0, 0, 0)
            cb_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
            cb = CheckBox()
            if app_id in self.selected_apps:
                cb.setChecked(True)
            cb.stateChanged.connect(
                lambda state, a=app: self.on_checkbox_toggled(state, a)
            )
            cb_layout.addWidget(cb)

            self.table.setCellWidget(i, 0, cb_container)

            icon_path = IconService.get_icon(app_id, name)
            name_item = QTableWidgetItem(QIcon(icon_path), f"  {name}")
            self.table.setItem(i, 1, name_item)
            self.table.setItem(i, 2, QTableWidgetItem(app_id))

            avail_ver = app.get("AvailableVersion")
            if avail_ver:
                ver_item = QTableWidgetItem(f"v{ver}")
                update_item = QTableWidgetItem(f"⬆ {avail_ver}")
            else:
                ver_item = QTableWidgetItem(f"v{ver}" if ver else "")
                update_item = QTableWidgetItem("—")

            self.table.setItem(i, 3, ver_item)
            self.table.setItem(i, 4, update_item)

    # ── Selection ──

    def on_checkbox_toggled(self, state, app_data):
        app_id = app_data.get("Id")
        if not app_id:
            return

        if state == 2:
            self.selected_apps[app_id] = app_data
        else:
            if app_id in self.selected_apps:
                del self.selected_apps[app_id]

        self.update_bottom_bar()

    def update_bottom_bar(self):
        count = len(self.selected_apps)
        if count > 0:
            self.bottom_bar.show()
            self.selection_label.setText(f"{count} app(s) selected")

            can_update = any(
                app.get("AvailableVersion") for app in self.selected_apps.values()
            )
            self.update_btn.setEnabled(can_update)
        else:
            self.bottom_bar.hide()

    def confirm_uninstall(self):
        count = len(self.selected_apps)
        if count == 0:
            return

        dialog = AnimatedDialog(
            "Confirm Uninstall",
            f"Are you sure you want to uninstall {count} app(s)?\nThis action cannot be undone.",
            self.window(),
            confirm_text="Uninstall",
            cancel_text="Cancel"
        )
        if dialog.exec():
            self.start_uninstalls()

    def start_uninstalls(self):
        for app_id, app_data in list(self.selected_apps.items()):
            pcard = ProgressCard(
                app_data.get("Name", app_id), action="uninstalling", parent=self
            )
            self.progress_area.addWidget(pcard)
            self.processing_apps[app_id] = pcard

            def cb(a_id, st, pct, txt):
                self.progress_signal.emit(a_id, st, pct, txt)

            WingetService.uninstall(app_id, callback=cb)

        self.selected_apps.clear()
        self.table.clearSelection()
        self.update_bottom_bar()

    def start_updates(self):
        for app_id, app_data in list(self.selected_apps.items()):
            if not app_data.get("AvailableVersion"):
                continue
            pcard = ProgressCard(
                app_data.get("Name", app_id), action="updating", parent=self
            )
            self.progress_area.addWidget(pcard)
            self.processing_apps[app_id] = pcard

            def cb(a_id, st, pct, txt):
                self.progress_signal.emit(a_id, st, pct, txt)

            WingetService.upgrade(app_id, callback=cb)

        self.selected_apps.clear()
        self.table.clearSelection()
        self.update_bottom_bar()

    def handle_progress(self, app_id, status, pct, text):
        if app_id in self.processing_apps:
            pcard = self.processing_apps[app_id]
            if status == "error":
                pcard.set_error(text)
                QTimer.singleShot(4000, pcard.deleteLater)
                del self.processing_apps[app_id]
            elif status == "done":
                pcard.set_done(text)
                QTimer.singleShot(4000, pcard.deleteLater)
                del self.processing_apps[app_id]
                QTimer.singleShot(4000, self.load_data)
            else:
                pcard.update_progress(pct, text)
