import subprocess
import threading
import time
import os
import re
from concurrent.futures import ThreadPoolExecutor

class WingetService:
    _cache = {}
    _cache_time = 0
    _cache_ttl = 60  # seconds
    
    executor = ThreadPoolExecutor(max_workers=3)

    @staticmethod
    def _run_cli(args, include_agreements=False):
        cmd = ["winget"] + args
        if include_agreements:
            cmd.extend(["--accept-source-agreements", "--accept-package-agreements"])
        
        startupinfo = None
        if os.name == 'nt':
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW

        try:
            result = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                encoding='utf-8',
                errors='replace',
                startupinfo=startupinfo,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
            )
            return result.stdout, result.stderr, result.returncode
        except Exception as e:
            return "", str(e), -1

    @staticmethod
    def _parse_table(output):
        lines = output.split('\n')
        dash_index = -1
        for i, line in enumerate(lines):
            stripped = line.strip()
            # If the line contains only dashes and spaces
            if set(stripped).issubset({'-', ' '}) and len(stripped) > 10:
                dash_index = i
                break
                
        if dash_index == -1 or dash_index == 0:
            return []
            
        headers_line = lines[dash_index - 1]
        
        # Regex to find column starts: consecutive non-space chars (can include single spaces inside like "Match Type" if it exists)
        import re
        matches = list(re.finditer(r'\S+(?:\s\S+)*', headers_line))
        if not matches:
            return []
            
        col_starts = [m.start() for m in matches]
        col_starts.append(len(headers_line))
        headers = [m.group() for m in matches]
            
        results = []
        for line in lines[dash_index + 1:]:
            if not line.strip():
                continue
            item = {}
            for i in range(len(col_starts)-1):
                start = col_starts[i]
                end = min(col_starts[i+1], len(line))
                col_name = headers[i] if i < len(headers) else f"Col{i}"
                val = line[start:end].strip() if start < len(line) else ""
                item[col_name] = val
            if item.get("Id") or item.get("Name"):
                results.append(item)
                
        return results

    @staticmethod
    def invalidate_cache():
        WingetService._cache = {}
        WingetService._cache_time = 0

    @staticmethod
    def list_installed():
        now = time.time()
        if "list" in WingetService._cache and now - WingetService._cache_time < WingetService._cache_ttl:
            return WingetService._cache["list"]
            
        out, err, code = WingetService._run_cli(["list"])
        parsed = WingetService._parse_table(out)
        WingetService._cache["list"] = parsed
        WingetService._cache_time = now
        return parsed

    @staticmethod
    def get_upgradable():
        out, err, code = WingetService._run_cli(["upgrade"])
        return WingetService._parse_table(out)

    @staticmethod
    def search(query):
        # We don't want to accept source agreements on search usually, but it can stop scripts
        out, err, code = WingetService._run_cli(["search", query, "--accept-source-agreements"])
        return WingetService._parse_table(out)

    @staticmethod
    def _run_interactive(cmd, package_id, callback, action_name):
        def _task():
            startupinfo = None
            if os.name == 'nt':
                startupinfo = subprocess.STARTUPINFO()
                startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
                
            process = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding='utf-8',
                errors='replace',
                startupinfo=startupinfo,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
            )
            
            for line in process.stdout:
                if callback:
                    if "%" in line or "Downloading" in line:
                        m = re.search(r'(\d{1,3})%', line)
                        pct = int(m.group(1)) if m else None
                        callback(package_id, "progress", pct, line.strip())
                    else:
                        callback(package_id, action_name, None, line.strip())
            
            process.wait()
            WingetService.invalidate_cache()
            if callback:
                callback(package_id, "done" if process.returncode == 0 else "error", 100, f"Finished with code {process.returncode}")

        WingetService.executor.submit(_task)

    @staticmethod
    def install(package_id, callback=None):
        cmd = ["winget", "install", "--id", package_id, "-e", "--accept-source-agreements", "--accept-package-agreements"]
        WingetService._run_interactive(cmd, package_id, callback, "installing")

    @staticmethod
    def uninstall(package_id, callback=None):
        cmd = ["winget", "uninstall", "--id", package_id, "-e"]
        # run as elevation logic needed possibly? For now standard
        WingetService._run_interactive(cmd, package_id, callback, "uninstalling")
        
    @staticmethod
    def upgrade(package_id, callback=None):
        cmd = ["winget", "upgrade", "--id", package_id, "-e", "--accept-source-agreements"]
        WingetService._run_interactive(cmd, package_id, callback, "upgrading")
