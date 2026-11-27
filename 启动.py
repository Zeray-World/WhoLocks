# -*- coding: utf-8 -*-
"""
启动.py —— 【谁在占用 WhoLocks】一键启动器
==========================================
作用：以管理员身份启动 WhoLocks.exe（不做这一步，很多"隐藏的占用者"扫不出来）。

使用：双击本文件即可。
前提：电脑上装有 Python 3（任意版本，Win10/11 常见自带的是 3.8+）。
      如果没有 Python，可以直接右键 dist\\WhoLocks.exe →「以管理员身份运行」，
      效果完全一样，这个启动器只是帮你省掉"右键"这一步。
"""

import ctypes
import os
import subprocess
import sys

EXE_NAME = "WhoLocks.exe"


def show(msg, title="谁在占用 · 启动器", error=False):
    """弹窗提示（没有 tkinter 时退回控制台输出，保证任何环境都能看到信息）。"""
    print(msg)
    try:
        import tkinter as tk  # 标准库，正常 Python 都自带
        from tkinter import messagebox
        root = tk.Tk()
        root.withdraw()
        root.attributes("-topmost", True)
        if error:
            messagebox.showerror(title, msg)
        else:
            messagebox.showinfo(title, msg)
        root.destroy()
    except Exception:
        # 没有 tkinter 就退回系统弹窗，尽量让用户看到提示
        try:
            flags = 0x10 if error else 0x40
            ctypes.windll.user32.MessageBoxW(0, msg, title, flags)
        except Exception:
            pass


def find_exe():
    """在当前目录和常见子目录里找 exe，路径自适应，不写死。"""
    here = os.path.dirname(os.path.abspath(__file__))
    candidates = [
        os.path.join(here, "dist", EXE_NAME),
        os.path.join(here, EXE_NAME),
        os.path.join(here, "bin", EXE_NAME),
    ]
    for path in candidates:
        if os.path.isfile(path):
            return path
    return None


def is_admin():
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def main():
    if os.name != "nt":
        show("本程序仅支持 Windows 10 及以上系统。", error=True)
        return 1

    # 检查系统版本（Win10 是 10.0.10240 起）
    try:
        ver = sys.getwindowsversion()
        if ver.major < 10:
            show("检测到系统版本过低（%d.%d）。\n本程序需要 Windows 10 或更高版本。"
                 % (ver.major, ver.minor), error=True)
            return 1
    except Exception:
        pass  # 老版本 Python 没有该接口时忽略，继续启动

    exe = find_exe()
    if not exe:
        show("没有找到 %s。\n\n请先运行同目录下的「构建.py」生成程序，\n"
             "或者确认 dist 文件夹和本文件放在同一目录。" % EXE_NAME, error=True)
        return 1

    admin = is_admin()
    print("程序路径：" + exe)
    print("管理员权限：" + ("是" if admin else "否，将尝试提权"))

    try:
        if admin:
            # 已经是管理员，直接启动
            subprocess.Popen([exe], cwd=os.path.dirname(exe))
        else:
            # 通过 ShellExecute 的 runas 提权（系统会弹一次 UAC 确认框）
            ctypes.windll.shell32.ShellExecuteW(
                None, "runas", exe, None, os.path.dirname(exe), 1
            )
        return 0
    except Exception as exc:
        show("启动失败：%s\n\n可以手动右键 dist\\%s →「以管理员身份运行」。"
             % (exc, EXE_NAME), error=True)
        return 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        show("启动器异常：%s" % exc, error=True)
        sys.exit(1)
