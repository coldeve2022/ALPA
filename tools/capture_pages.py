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


def find_window(pid):
    """只找**本进程启动的那个**窗口。

    早先按标题子串匹配，结果抓到过编辑器窗口（它的标题里也含项目路径），
    拍出来是代码界面而不是程序界面。按 PID 匹配才是可靠的。
    """
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(hwnd, lparam):
        if not user32.IsWindowVisible(hwnd):
            return True
        wpid = wt.DWORD(0)
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(wpid))
        if wpid.value != pid:
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

    # 等窗口真正出现并画完：引擎启动要扫体检项和启动项，机器忙的时候会明显变慢。
    # 之前固定 sleep 9s 抓到过「窗口在但画面还是桌面」的空白图，所以改成轮询等待。
    hwnd = 0
    for _ in range(60):
        time.sleep(1)
        wins = find_window(proc.pid)
        if wins:
            r = wt.RECT()
            user32.GetWindowRect(wins[0], ctypes.byref(r))
            # 窗口刚创建时尺寸还没铺开（抓到过 247x34 的空白壳），等它长到正常大小
            if (r.right - r.left) > 600:
                hwnd = wins[0]
                break
    if not hwnd:
        print("  NOT_FOUND", flush=True)
        proc.terminate()
        continue
    # 置前。注意：只调 SetForegroundWindow 会被系统拒绝（前台程序有优先权），
    # 结果拍到的是压在它上面的别的窗口（实测抓到过编辑器界面）。
    # 直接置顶（HWND_TOPMOST）才可靠，抓完再取消置顶。
    HWND_TOPMOST, HWND_NOTOPMOST = -1, -2
    SWP_NOSIZE, SWP_NOMOVE, SWP_SHOWWINDOW = 0x0001, 0x0002, 0x0040
    try:
        user32.ShowWindow(hwnd, 9)          # SW_RESTORE
        user32.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW)
        user32.SetForegroundWindow(hwnd)
    except Exception:
        pass
    time.sleep(3)   # 再给界面几秒把首个采样画上去

    wins = [hwnd]

    hwnd = wins[0]
    rect = wt.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    out = os.path.join(OUTDIR, "p%d.png" % page)
    img = ImageGrab.grab(bbox=(rect.left, rect.top, rect.right, rect.bottom), all_screens=True)
    img.save(out)
    print("  saved", out, img.size, flush=True)
    try:
        user32.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                            SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW)
    except Exception:
        pass

    proc.terminate()
    try:
        proc.wait(timeout=8)
    except Exception:
        proc.kill()
    time.sleep(1.5)
print("DONE")
