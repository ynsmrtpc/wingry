# Wingy 📦

**Wingy** is a modern, fast, and beautiful GUI for the Windows Package Manager (**winget**), designed specifically with the **Windows 11** design language (Fluent Design).

![Wingy UI Example](<img width="1100" height="900" alt="wingy" src="https://github.com/user-attachments/assets/61324725-7e7c-4e31-96b2-bec74930d59b" />)

## ✨ Features

- **Discover**: Browse curated application packages (Gamer, Developer, Creator) and search for thousands of packages.
- **Modern UI**: Full Windows 11 Fluent Design implementation using `PyQt-Fluent-Widgets`.
- **Batch Actions**: Select multiple applications and install/uninstall/update them simultaneously.
- **Smart Tracking**: Real-time progress cards with status indicators.
- **System Specs**: High-level overview of your PC's CPU, RAM, OS, and Network status.
- **Animations**: Soft edges, hover animations, and smooth transitions for a premium experience.

## 🛠️ Built With

- **Python 3.11+**
- **PyQt6** - Core UI framework.
- **PyQt-Fluent-Widgets** - Windows 11 themed components.
- **WinGet CLI** - Backend package operations.

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- Winget installed (standard on modern Windows)
- Python 3.11+

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/wingy.git
   ```
2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```
3. Run the application:
   ```bash
   python main.py
   ```

## 🏗️ Build
To create a standalone `.exe`:
```cmd
build.bat
```

## 📄 License
This project is for demonstration/personal tool purposes. See Wingit LICENSE for CLI terms.
