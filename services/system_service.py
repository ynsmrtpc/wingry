import os
import psutil
import platform
import socket
import datetime

class SystemService:
    @staticmethod
    def get_cpu_info():
        try:
            freq = f"{psutil.cpu_freq().current:.2f} MHz" if psutil.cpu_freq() else "N/A"
        except:
            freq = "N/A"
            
        return {
            "name": platform.processor(),
            "arch": platform.machine(),
            "cores_physical": psutil.cpu_count(logical=False),
            "cores_logical": psutil.cpu_count(logical=True),
            "freq": freq,
            "usage": psutil.cpu_percent(interval=None)
        }

    @staticmethod
    def get_ram_info():
        mem = psutil.virtual_memory()
        return {
            "total": f"{mem.total / (1024**3):.2f} GB",
            "used": f"{mem.used / (1024**3):.2f} GB",
            "available": f"{mem.available / (1024**3):.2f} GB",
            "usage_percent": mem.percent
        }

    @staticmethod
    def get_storage_info():
        drives = []
        for part in psutil.disk_partitions(all=False):
            if os.name == 'nt' and 'cdrom' in part.opts or part.fstype == '':
                continue
            try:
                usage = psutil.disk_usage(part.mountpoint)
                drives.append({
                    "device": part.device,
                    "mountpoint": part.mountpoint,
                    "fstype": part.fstype,
                    "total": f"{usage.total / (1024**3):.2f} GB",
                    "used": f"{usage.used / (1024**3):.2f} GB",
                    "free": f"{usage.free / (1024**3):.2f} GB",
                    "percent": usage.percent
                })
            except Exception:
                pass
        return drives

    @staticmethod
    def get_os_info():
        boot_time = datetime.datetime.fromtimestamp(psutil.boot_time())
        uptime = datetime.datetime.now() - boot_time
        
        return {
            "system": platform.system(),
            "release": platform.release(),
            "version": platform.version(),
            "boot_time": boot_time.strftime("%Y-%m-%d %H:%M:%S"),
            "uptime": str(uptime).split('.')[0]
        }
        
    @staticmethod
    def get_network_info():
        hostname = socket.gethostname()
        try:
            ip = socket.gethostbyname(hostname)
        except Exception:
            ip = "Unknown"
            
        net_io = psutil.net_io_counters()
        return {
            "hostname": hostname,
            "ip": ip,
            "bytes_sent": f"{net_io.bytes_sent / (1024**2):.2f} MB",
            "bytes_recv": f"{net_io.bytes_recv / (1024**2):.2f} MB"
        }
