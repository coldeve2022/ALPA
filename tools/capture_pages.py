"""开发期批量截图：逐页启动 ALPA_v2 并抓取窗口。

用法:
  python capture_pages.py <exe路径> <输出目录> [页面列表 1,2,3] [额外参数]
会依次以 --page=N 启动程序，等界面稳定后抓图并关闭。
"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import sys
import time

from PIL import ImageGrab

user32 = ctypes.windll.user32
user32.SetProcessDPIAware()

EXE = sys.argv[1]
OUTDIR = sys.argv[2]
PAGES = [int(x) for x in (sys.argv[3].split(",") if len(sys.argv) > 3 else ["1", "2", "3", "4", "5", "6"])]
EXTRA = sys.argv[4].split() if len(sys.argv) > 4 else []
TITLE_KEY = "ALPA v2"


def find_window():
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(hwnd, lparam):
        if not user32.IsWindowVisible(hwnd):
            return True
        n = user32.GetWindowTextLengthW(hwnd)
        if n <= 0:
            return True
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(hwnd, buf, n + 1)
        if TITLE_KEY in buf.value:
            found.append(hwnd)
        return True

    user32.EnumWindows(cb, 0)
    return found


os.makedirs(OUTDIR, exist_ok=True)
for page in PAGES:
    args = [EXE, "--no-elevate", "--page=%d" % page] + EXTRA
    proc = subprocess.Popen(args, cwd=os.path.dirname(EXE))
    print("launched page", page, "pid", proc.pid, flush=True)
    time.sleep(9)

    wins = find_window()
    if not wins:
        print("  NOT_FOUND", flush=True)
        proc.terminate()
        continue

    hwnd = wins[0]
    rect = wt.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    out = os.path.join(OUTDIR, "p%d.png" % page)
    img = ImageGrab.grab(bbox=(rect.left, rect.top, rect.right, rect.bottom), all_screens=True)
    img.save(out)
    print("  saved", out, img.size, flush=True)

    proc.terminate()
    try:
        proc.wait(timeout=8)
    except Exception:
        proc.kill()
    time.sleep(1.5)
print("DONE")
