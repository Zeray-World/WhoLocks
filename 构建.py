# -*- coding: utf-8 -*-
"""
构建.py —— 把 src 里的 C# 源码编译成 WhoLocks.exe
=====================================================
不需要安装 Visual Studio / .NET SDK：
直接调用 Windows 10/11 系统自带的 .NET Framework 编译器（csc.exe）。

用法：双击运行，或命令行：python 构建.py
产物：dist\\WhoLocks.exe（单文件、免安装、AnyCPU，32/64 位系统都能跑）
"""

import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src")
DIST = os.path.join(HERE, "dist")

SOURCES = ["WhoLocks.cs", "MainForm.cs", "DragDropSupport.cs", "AssemblyInfo.cs"]
OUT_NAME = "WhoLocks.exe"
# 程序图标：固定用 src\script.ico。以后想换图标，直接把新 .ico 覆盖这个名字即可，无需改代码。
ICON_NAME = "script.ico"


def setup_console():
    """让 Windows 控制台正确显示中文（GBK 控制台也能输出 UTF-8 内容）。"""
    try:
        # Python 3.7+ 支持 reconfigure；老版本忽略
        if hasattr(sys.stdout, "reconfigure"):
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
            sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    if os.name == "nt":
        try:
            import ctypes
            ctypes.windll.kernel32.SetConsoleOutputCP(65001)   # 控制台切到 UTF-8
            ctypes.windll.kernel32.SetConsoleCP(65001)
        except Exception:
            pass


def find_csc():
    """按顺序查找系统自带的 C# 编译器，兼容 32/64 位系统。"""
    windir = os.environ.get("WINDIR", r"C:\Windows")
    candidates = [
        os.path.join(windir, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe"),
        os.path.join(windir, "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe"),
    ]
    for path in candidates:
        if os.path.isfile(path):
            return path
    return None


def main():
    setup_console()
    print("=" * 62)
    print("  谁在占用 WhoLocks —— 构建脚本")
    print("=" * 62)

    csc = find_csc()
    if not csc:
        print("[失败] 没有找到系统自带的 C# 编译器 csc.exe。")
        print("       这通常说明 .NET Framework 4.x 被精简或系统损坏。")
        print("       解决办法：安装 .NET Framework 4.8（Win10/11 一般自带）。")
        return 1
    print("[1/3] 编译器：" + csc)

    # 检查源码
    missing = [f for f in SOURCES if not os.path.isfile(os.path.join(SRC, f))]
    if missing:
        print("[失败] 缺少源码文件：" + "、".join(missing))
        return 1
    print("[2/3] 源码检查通过：" + "、".join(SOURCES))

    if not os.path.isdir(DIST):
        os.makedirs(DIST)

    exe = os.path.join(DIST, OUT_NAME)
    manifest = os.path.join(SRC, "app.manifest")

    cmd = [
        csc,
        "/nologo",
        "/target:winexe",           # 窗口程序，不弹黑框
        "/platform:anycpu",         # 32/64 位系统都能跑
        "/optimize+",
        "/codepage:65001",          # 源码按 UTF-8 解析，避免中文乱码
        "/out:" + exe,
    ]
    if os.path.isfile(manifest):
        cmd.append("/win32manifest:" + manifest)

    # 程序图标：存在 src\script.ico 就嵌进去（以后直接覆盖同名文件即可换图标）
    icon = os.path.join(SRC, ICON_NAME)
    if os.path.isfile(icon):
        cmd.append("/win32icon:" + icon)
        print("[图标] 使用 " + icon)
    else:
        print("[图标] 未找到 src\\%s，将使用默认图标（放一个同名 .ico 进去即可自定义）" % ICON_NAME)

    cmd += [
        "/reference:System.dll",
        "/reference:System.Core.dll",
        "/reference:System.Drawing.dll",
        "/reference:System.Windows.Forms.dll",
    ]
    cmd += [os.path.join(SRC, f) for f in SOURCES]

    print("[3/3] 正在编译……")
    proc = subprocess.run(cmd, capture_output=True)
    out = proc.stdout.decode("utf-8", "replace") + proc.stderr.decode("utf-8", "replace")

    if proc.returncode != 0 or not os.path.isfile(exe):
        print("[失败] 编译出错：")
        print(out.strip() or "（编译器没有返回具体信息）")
        return 1

    if out.strip():
        # 只有警告时也打印出来，方便排查
        print("[警告] " + out.strip())

    size_kb = os.path.getsize(exe) / 1024.0
    print("")
    print("[成功] 已生成：" + exe)
    print("       大小：" + ("%.0f KB" % size_kb))
    print("       下一步：双击 dist\\WhoLocks.exe，或用 启动.py 以管理员身份运行。")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # 容错：构建脚本自身出错也要有中文提示
        print("[异常] " + str(exc))
        sys.exit(1)
