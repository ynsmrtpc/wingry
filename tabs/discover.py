"""
DiscoverTab — Modernized with unified theme system.
"""
import threading
import json
import os
from PyQt6.QtCore import Qt, pyqtSignal, QTimer
from PyQt6.QtWidgets import (
    QFrame, QVBoxLayout, QHBoxLayout, QHeaderView,
    QAbstractItemView, QTableWidgetItem
)
from PyQt6.QtGui import QIcon, QFont
from qfluentwidgets import (
    TitleLabel, SearchLineEdit, PrimaryPushButton, PushButton,
    TableWidget, CaptionLabel, ScrollArea, CheckBox
)
from services.winget_service import WingetService
from services.curated_apps import CURATED_APPS
from services.icon_service import IconService
from services.config_service import ConfigService
from services.theme import (
    ThemeColors, ThemeRadius, ThemeSpacing,
    get_table_stylesheet, get_bottom_bar_stylesheet,
    get_scroll_area_stylesheet, get_tab_frame_stylesheet
)
from components.progress_card import ProgressCard
from components.package_card import PackageCard
from components.animated_dialog import AnimatedDialog


class DiscoverTab(QFrame):
    progress_signal = pyqtSignal(str, str, object, str)
    search_signal = pyqtSignal(list)

    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("DiscoverTab")
        self.setStyleSheet(get_tab_frame_stylesheet("DiscoverTab"))

        self.mainLayout = QVBoxLayout(self)
        self.mainLayout.setContentsMargins(
            ThemeSpacing.XL, ThemeSpacing.XL, ThemeSpacing.XL, 0
        )
        self.mainLayout.setSpacing(ThemeSpacing.SM)

        self.selected_apps = {}
        self.installing_apps = {}
        self.current_results = []
        self.display_limit = 15

        self.progress_signal.connect(self.handle_install_progress)
        self.search_signal.connect(self.display_results)

        # ── Search Entry ──
        search_container = QHBoxLayout()
        search_container.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.search_entry = SearchLineEdit(self)
        self.search_entry.setPlaceholderText("Search packages...")
        self.search_entry.setFixedWidth(460)
        self.search_entry.setFixedHeight(40)
        self.search_entry.textChanged.connect(self.on_search_change)
        search_container.addWidget(self.search_entry)
        self.mainLayout.addLayout(search_container)

        self.search_timer = QTimer(self)
        self.search_timer.setSingleShot(True)
        self.search_timer.timeout.connect(self.perform_search)

        self.mainLayout.addSpacing(ThemeSpacing.MD)

        # ── Packages Scroll Area ──
        self.pack_label = TitleLabel("Curated Packages", self)
        self.pack_label.setStyleSheet(f"color: {ThemeColors.TEXT_PRIMARY};")
        self.mainLayout.addWidget(self.pack_label)

        self.pack_scroll = ScrollArea(self)
        self.pack_scroll.setWidgetResizable(True)
        self.pack_scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.pack_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self.pack_scroll.setFixedHeight(170)
        self.pack_scroll.setStyleSheet(get_scroll_area_stylesheet())

        self.pack_container = QFrame()
        self.pack_container.setStyleSheet("background: transparent;")
        self.pack_layout = QHBoxLayout(self.pack_container)
        self.pack_layout.setContentsMargins(0, ThemeSpacing.SM, 0, ThemeSpacing.SM)
        self.pack_layout.setSpacing(ThemeSpacing.LG)
        self.pack_layout.setAlignment(
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter
        )

        self.pack_scroll.setWidget(self.pack_container)
        self.mainLayout.addWidget(self.pack_scroll)

        self.load_packages()

        self.mainLayout.addSpacing(ThemeSpacing.MD)

        # ── Recommended Label ──
        self.rec_label = TitleLabel("Recommended Apps", self)
        self.rec_label.setStyleSheet(f"color: {ThemeColors.TEXT_PRIMARY};")
        self.mainLayout.addWidget(self.rec_label)

        # ── Table ──
        self.table = TableWidget(self)
        self.table.setColumnCount(5)
        self.table.setHorizontalHeaderLabels(
            ["", "Name", "Id", "Version", "Source / Actions"]
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

        # ── Load More ──
        self.load_more_btn = PushButton("Load More", self)
        self.load_more_btn.setFixedWidth(200)
        self.load_more_btn.setFixedHeight(36)
        self.load_more_btn.clicked.connect(self.load_more)
        
        self.mainLayout.addSpacing(20)
        self.mainLayout.addWidget(
            self.load_more_btn, 0, Qt.AlignmentFlag.AlignHCenter
        )
        self.mainLayout.addSpacing(20)
        self.load_more_btn.hide()

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

        self.install_btn = PrimaryPushButton("⬇  Install", self.bottom_bar)
        self.install_btn.setFixedHeight(36)
        self.install_btn.clicked.connect(self.start_installs)
        self.bottom_bar_layout.addWidget(self.install_btn)

        self.mainLayout.addWidget(self.bottom_bar, 0)
        self.bottom_bar.hide()

        self.load_curated_apps()

    # ── Data Loading ──

    def load_packages(self):
        try:
            pkg_path = ConfigService.get_resource_path("assets/packages.json")
            with open(pkg_path, "r", encoding="utf-8") as f:
                packages = json.load(f)

            for pkg in packages:
                card = PackageCard(pkg, self.pack_container)
                card.package_clicked.connect(self.on_package_clicked)
                self.pack_layout.addWidget(card)
        except Exception as e:
            print(f"Failed to load packages: {e}")

    def on_package_clicked(self, package_data):
        apps = package_data.get("apps", [])
        if not apps:
            return

        title = f"Install {package_data.get('title')}?"
        prompt = "The following applications will be installed:\n"
        for app in apps:
            prompt += f"  •  {app.get('Name')}\n"
        prompt += "\nDo you approve?"

        dialog = AnimatedDialog(title, prompt, self.window(),
                                confirm_text="Install All", cancel_text="Cancel")
        if dialog.exec():
            for app in apps:
                app_id = app.get("Id")
                name = app.get("Name")

                if app_id in self.installing_apps:
                    continue

                pcard = ProgressCard(name, action="installing", parent=self)
                self.progress_area.addWidget(pcard)
                self.installing_apps[app_id] = pcard

                def cb(a_id, st, pct, txt):
                    self.progress_signal.emit(a_id, st, pct, txt)

                WingetService.install(app_id, callback=cb)

    def on_search_change(self, text):
        self.search_timer.stop()
        self.search_timer.start(500)

    def perform_search(self):
        query = self.search_entry.text().strip()
        if not query:
            self.load_curated_apps()
            return

        self.table.setRowCount(0)
        self.load_more_btn.hide()
        self.rec_label.setText("Searching...")

        def _search():
            results = WingetService.search(query)
            self.search_signal.emit(results)

        threading.Thread(target=_search, daemon=True).start()

    def load_curated_apps(self):
        self.table.setRowCount(0)
        self.load_more_btn.hide()
        self.rec_label.setText("Recommended Apps")
        mock_results = []
        for app in CURATED_APPS:
            name = app.split(".")[-1]
            mock_results.append({
                "Id": app, "Name": name, "Version": "Latest", "Source": "winget"
            })
        self.display_results(mock_results)

    def display_results(self, results):
        self.current_results = results
        self.display_limit = 15

        if self.search_entry.text().strip():
            self.rec_label.setText(f"Search Results ({len(results)})")

        if not results:
            self.table.setRowCount(0)
            self.load_more_btn.hide()
            return

        self.table.setRowCount(0)
        self.render_table()

    def render_table(self):
        start_idx = self.table.rowCount()
        end_idx = min(self.display_limit, len(self.current_results))

        if end_idx <= start_idx:
            self.load_more_btn.hide()
            return

        self.table.setRowCount(end_idx)

        for i in range(start_idx, end_idx):
            app = self.current_results[i]

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
                item = QTableWidgetItem(f"v{ver} ➔ {avail_ver}")
            else:
                item = QTableWidgetItem(f"v{ver}" if ver else "")
            self.table.setItem(i, 3, item)

            source = app.get("Source", "winget")
            self.table.setItem(i, 4, QTableWidgetItem(source))

        if self.display_limit < len(self.current_results):
            self.load_more_btn.show()
        else:
            self.load_more_btn.hide()

    def load_more(self):
        self.display_limit += 15
        self.render_table()

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
            self.install_btn.setText(f"⬇  Install ({count})")
        else:
            self.bottom_bar.hide()

    def start_installs(self):
        for app_id, app_data in list(self.selected_apps.items()):
            pcard = ProgressCard(
                app_data.get("Name", app_id), action="installing", parent=self
            )
            self.progress_area.addWidget(pcard)
            self.installing_apps[app_id] = pcard

            def cb(a_id, st, pct, txt):
                self.progress_signal.emit(a_id, st, pct, txt)

            WingetService.install(app_id, callback=cb)

        self.selected_apps.clear()
        self.table.clearSelection()
        self.update_bottom_bar()
        self.load_curated_apps()

    def handle_install_progress(self, app_id, status, pct, text):
        if app_id in self.installing_apps:
            pcard = self.installing_apps[app_id]
            if status == "error":
                pcard.set_error(text)
                QTimer.singleShot(4000, pcard.deleteLater)
                del self.installing_apps[app_id]
            elif status == "done":
                pcard.set_done(text)
                QTimer.singleShot(4000, pcard.deleteLater)
                del self.installing_apps[app_id]
            else:
                pcard.update_progress(pct, text)
