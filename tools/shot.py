"""抓取 ALPA_v2 主窗口截图（开发期人工核对界面用）。

用法: python shot.py <输出png> [标题关键字] [等待秒数] [页面序号1-6]
页面序号通过给窗口投递 Ctrl+N 实现（与程序内快捷键一致）。
"""
import ctypes
import ctypes.wintypes as wt
import sys
import time

from PIL import ImageGrab

user32 = ctypes.windll.user32
user32.SetProcessDPIAware()

OUT = sys.argv[1] if len(sys.argv) > 1 else "shot.png"
TITLE_KEY = sys.argv[2] if len(sys.argv) > 2 else "ALPA v2"
WAIT = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
PAGE = int(sys.argv[4]) if len(sys.argv) > 4 else 0

WM_KEYDOWN, WM_KEYUP = 0x0100, 0x0101
VK_CONTROL = 0x11


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
            rect = wt.RECT()
            user32.GetWindowRect(hwnd, ctypes.byref(rect))
            found.append((hwnd, buf.value, rect))
        return True

    user32.EnumWindows(cb, 0)
    return found


def send_ctrl_digit(hwnd, digit):
    """PostMessage 走的是窗口消息队列，Application 的消息循环会照常做 PreProcessMessage，
    所以等价于真的按了 Ctrl+数字。"""
    vk = 0x30 + digit
    user32.PostMessageW(hwnd, WM_KEYDOWN, VK_CONTROL, 0)
    user32.PostMessageW(hwnd, WM_KEYDOWN, vk, 0)
    user32.PostMessageW(hwnd, WM_KEYUP, vk, 0)
    user32.PostMessageW(hwnd, WM_KEYUP, VK_CONTROL, 0)


wins = find_window()
if not wins:
    print("NOT_FOUND")
    sys.exit(2)

hwnd, title, rect = wins[0]
print("FOUND", hwnd, repr(title), (rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top))

if WAIT:
    time.sleep(WAIT)

user32.ShowWindow(hwnd, 9)  # SW_RESTORE
user32.SetForegroundWindow(hwnd)
user32.BringWindowToTop(hwnd)
time.sleep(0.4)

if PAGE:
    send_ctrl_digit(hwnd, PAGE)
    time.sleep(1.0)

user32.GetWindowRect(hwnd, ctypes.byref(rect))
img = ImageGrab.grab(bbox=(rect.left, rect.top, rect.right, rect.bottom), all_screens=True)
img.save(OUT)
print("SAVED", OUT, img.size, "page", PAGE or 1)
