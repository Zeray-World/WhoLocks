# WhoLocks · 谁在占用

> **Find out what's actually holding your files.**
> Windows says "The file is in use" but Task Manager shows nobody? Use this.

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-blue)
![Runtime](https://img.shields.io/badge/.NET%20Framework-4.x-lightgrey)
![Admin](https://img.shields.io/badge/Permission-Admin%20recommended-orange)
![License](https://img.shields.io/badge/License-MIT-green)

[中文](README.md) · **English**

---

## What problem does this solve

You try to delete, move, or rename a folder and Windows says "The file is in use" — but you search Task Manager and can't find who.

Resource Monitor can only search by handle name. It's essentially useless against thumbnail caches, antivirus scans, indexing services, or directories held open by DLL mappings.

This tool cross-checks **two independent detection layers** to flush those hidden holders out:

1. **Windows Restart Manager** (`rstrtmgr.dll`) — Microsoft's official API, lowest false-positive rate
2. **Whole-system handle table scan** (`NtQuerySystemInformation` + `ReadProcessMemory`) — walks the kernel handle table directly and pairs each process with the files it holds open

Processes found by both layers are labeled "系统RM+句柄表" (RM + handle table), the highest confidence level.

---

## Features

| Feature | Description |
|---|---|
| **Pick or drag a folder** | Recursively scans the folder and every subfolder |
| **Real holder detection** | Dual-layer cross-check; finds hidden holders other tools miss |
| **Kill all** | Ends every detected process (system-critical ones are skipped automatically) |
| **Kill selected** | Tick the checkbox column and end only those |
| **Do it yourself** | Copy PID / process name / path and handle it in Task Manager manually |
| **Export CSV** | With a 12 KB size guard; includes process info and the list of locked files |
| **Close handles to unlock** | Advanced: release the file handles without killing the process |

---

## Quick start

### Requirements

| Item | Requirement |
|---|---|
| OS | Windows 10 / 11 (32-bit and 64-bit both supported) |
| Runtime | .NET Framework 4.x (bundled with Win10/11 — **nothing to install**) |
| Permission | **Administrator recommended** (why, below) |
| Python | Optional. Only needed if you use `启动.py` |

**No network access, no registry writes, no services — fully portable.**

### Three ways to start (pick one)

1. **Double-click `启动.py`** — requests elevation automatically; approve the UAC prompt
2. **Right-click `dist\WhoLocks.exe` → "Run as administrator"** — use this if Python isn't installed
3. **Launch normally, then click the "⚡ 立即提权" button in the top-right** — it restarts elevated and automatically continues scanning the folder you already selected

When you're already elevated, that button turns into a green "✓ 已是管理员" (already administrator).

### Three steps to use it

1. Launch the program (any of the three ways above)
2. **Drag the folder you want to inspect into the window** (anywhere works), or click the select button
   - Scanning **starts automatically** after dropping or selecting — there's no separate "start" button
3. When it finishes, the results list **expands automatically**

In the results list:

- Holders found → process name / PID / user / locked files / status
- Nothing found → it shows "**没有检测到占用进程**" (no holding processes detected)

### The two options

| Option | Default | Meaning |
|---|---|---|
| **包含子文件夹** (include subfolders) | ✅ On | Also scans every subfolder — least likely to miss anything. Turn it off to scan only the top level (useful for very large trees when you just want a quick look) |
| **深度检测（更准，稍慢）** (deep scan — more accurate, slightly slower) | ✅ On | Besides processes with open files, it also finds programs that have **loaded DLLs/EXEs from that directory into memory**. Those are the hardest to spot in Task Manager and a common cause of "can't copy but can't find who". Costs only a second or two — keep it on |

The "?" button in the UI shows the same explanations.

### What to do next

- Want them all gone → click "**一键全杀**" (kill all)
- Want to pick → tick the "☑" column, then click "**结束勾选**" (kill selected)
- Want to do it manually → select a row → right-click → "复制 PID" (copy PID) → match it in Task Manager's Details tab
- Need a record / to send to someone → click "**导出CSV**" (export CSV)

---

## Why it finds holders Task Manager can't

The naive approach only asks "which program has this folder open", but the real holder is often hiding elsewhere. This tool cross-checks two layers.

**Layer 1 · Windows Restart Manager**

Microsoft's recommended detection method with the lowest false-positive rate.

**Layer 2 · Whole-system handle table scan**

Walks the kernel handle table directly and pairs each process with the files it holds open. This layer specifically covers what Task Manager misses:

- Explorer thumbnail and preview caches
- Antivirus currently scanning the file
- Everything and search indexing services
- Background services and processes leaking handles
- Programs that loaded DLLs from that directory (DLL mapping detection)

---

## Important: administrator rights

**Running as administrator is strongly recommended.** Why:

- A normal user can only see processes within their own privilege scope
- Files held by other users, system services, or protected processes are invisible without elevation
- Measured: without elevation roughly 2400+ processes cannot be opened, so some holders will be missed

---

## About dropping folders

When you drop a folder, the program uses **only the native Windows drop mechanism (WM_DROPFILES)** and deliberately disables .NET's OLE drag-and-drop.

The reason — and this is the root cause of the "can't drop into an elevated window, cursor shows a forbidden sign" problem many tools have:

- Windows has two drop mechanisms: **OLE drag-and-drop** and **native WM_DROPFILES**
- As soon as a control enables `AllowDrop`, the system prefers OLE and the native path never fires
- And **when running elevated**, dragging from Explorer (normal privileges) into a high-privilege window is blocked by UIPI privilege isolation for OLE data
- Result: the program never receives the file data → decides "not a file" → cursor becomes a **forbidden sign**

This program's approach: **no OLE at all, only native `WM_DROPFILES`**, with `ChangeWindowMessageFilterEx` to allow the message through. Dropping works under both normal and elevated privileges.

Both **folders** and **single files** can be dropped:

- Drop a **folder** → inspect the whole folder (including subfolders, depending on the option)
- Drop a **single file** → inspect **only that file**, not the folder it lives in
  (so dropping `a.py` from your desktop checks `a.py` alone, not your entire desktop)

---

## Safety design (mis-kill protection)

The program **has built-in protection** so a single click can't wreck your system:

| Protection level | Example processes | Behavior |
|---|---|---|
| **System-critical · kill forbidden** | `csrss` `wininit` `lsass` `services` `smss` `winlogon` `svchost` `lsaiso` `fontdrvhost` `System` etc. | **Kill-all skips them automatically**, and right-click kill is refused. Recognized with or without `.exe` |
| **Keep advisedly · caution** | `explorer` `dwm` antivirus `searchindexer` IME-related etc. | Allowed to kill, but flagged yellow with a "caution" note |
| Normal | Programs you started yourself | Ended normally |

Also:

- **Every kill operation asks for confirmation** and lists exactly what will be killed first
- **Process identity is verified before ending**: process name + start time is compared against the record taken at scan time. If that PID has since been reassigned to a different process, it's skipped with a notice — so no mis-kills
- **Unsaved data in the killed program will be lost** — that's OS behavior and unavoidable
- The program **provides no file deletion** at all, to avoid accidental deletion

---

## CSV export (12 KB limit)

Exports larger than **12 KB** are blocked.

- With the locked-file list: about **45 processes**
- Compact mode (no file list): about **71 processes**
- When over the limit the program suggests selecting fewer rows, or using compact mode

The CSV is **UTF-8 with BOM**, so double-clicking it in Excel shows no mojibake.

Columns:

```
进程名, PID, 用户, 会话, 启动时间, 能否结束,
风险标记, 检测来源, 占用文件数, 进程路径, 占用文件列表
```

(process name, PID, user, session, start time, whether it can be ended,
risk flag, detection source, locked-file count, process path, locked-file list)

The file header also records the scanned directory, scan time, run identity, and detection method.

---

## FAQ

**Q: It found 0 processes, but I still can't copy.**

In order of likelihood:

1. The holder is a **kernel driver** (antivirus file-system filter, virtual disk driver) — the handle table only shows `System`, with no way to pinpoint it. This is a limitation shared by every tool of this kind
2. You're not elevated → click "⚡ 立即提权" and rescan
3. The lock may come from **a network share / OneDrive sync**
4. The lock may already be released — try copying again

**Q: Killing reports "failed / access denied".**

- The process is protected (antivirus self-protection, system process)
- You're not an administrator
- You need to use "close handles to unlock" instead (right-click → advanced)

**Q: Still can't copy after ending the process.**

Rescan. If the same process shows up again, it's a **service** that restarts itself when killed (antivirus, indexing, etc.). Disable it in its own settings rather than killing it repeatedly.

**Q: What does "close handles to unlock" do?**

It doesn't kill the process — it force-closes the file handles the process holds. Useful when you don't want to interrupt a program but just want it to release a file. **It carries risk**: the target program may error out or crash, which is why it's tucked away and requires confirmation.

**Q: Antivirus flags it.**

What the program does (enumerating handles, ending processes) is the same class of operation Task Manager performs, and some AV engines misclassify it. All the source is in `src\` — you can audit it yourself.

---

## Modifying and rebuilding

1. Edit the `.cs` files in `src`
2. Double-click `构建.py` (requires the system `csc.exe`, present on all Win10/11 installs)
3. The resulting exe is written to `dist\WhoLocks.exe`

No Visual Studio or .NET SDK installation is required.

### Changing the icon

The icon is fixed at **`src\script.ico`**.

To change it: **rename your new `.ico` to `script.ico` and overwrite it**, then re-run `构建.py`. **No code changes needed.**

- A **multi-size ico** (16/24/32/48/64/128/256) is recommended so taskbar, desktop, and Explorer views all stay sharp
- The icon is embedded at build time, so the exe carries it even when copied elsewhere

### Command-line self-test (for diagnostics, no window)

```cmd
dist\WhoLocks.exe --selftest "D:\some\folder" "D:\report.txt"
dist\WhoLocks.exe --selftest "D:\some\file.py" "D:\report.txt"
```

The first argument can be either a folder or a single file; the corresponding mode is used and results are written to the report file.

---

## Project layout

```
谁在占用/
├── 启动.py                 One-click launcher (auto-elevates)
├── 构建.py                 Rebuild script
├── 使用说明.md              Detailed manual (Chinese)
├── README.md               Chinese README
├── README.en.md            This file
├── dist/
│   └── WhoLocks.exe        Main program (single portable file, ~92 KB)
└── src/
    ├── WhoLocks.cs         Detection engine + CSV + data structures
    ├── MainForm.cs         UI
    ├── DragDropSupport.cs  Native WM_DROPFILES drop support
    ├── AssemblyInfo.cs     Version info
    ├── app.manifest        Compatibility / DPI declarations
    └── script.ico          Application icon (replace this file to change it)
```

---

## Compatibility

- **OS**: Windows 10 / Windows 11 (32-bit and 64-bit)
- **Runtime**: .NET Framework 4.x — bundled with Win10/11, **nothing to install**
- **No network access, no registry writes, no services** — fully portable

To use it on another machine, just copy the whole folder.

---

## UI reference (for non-Chinese readers)

The entire UI is in Chinese. Here's what the key controls mean:

| Chinese | English |
|---|---|
| 选择 | Select (choose a folder) |
| 重扫 | Rescan |
| 一键全杀 | Kill all |
| 结束勾选 | Kill selected |
| 导出CSV | Export CSV |
| ⚡ 立即提权 | Elevate now (restart as administrator) |
| ✓ 已是管理员 | Already administrator |
| 包含子文件夹 | Include subfolders |
| 深度检测（更准，稍慢） | Deep scan (more accurate, slightly slower) |
| 没有检测到占用进程 | No holding processes detected |
| 关闭句柄解锁 | Close handles to unlock |
| 复制 PID | Copy PID |
| 进程名 | Process name |
| 占用文件 | Locked file |
| 风险标记 | Risk flag |
| 检测来源 | Detection source |
| 系统RM+句柄表 | Found by both Restart Manager and the handle table |
| 谨慎 | Caution |

There is no English localization and none is planned for the UI — the source is plain C# WinForms in `src\`, so forking and translating the strings is straightforward.

---

## Colors

The UI uses **deep sea blue + soft cream white**:

| Color | HEX | RGB |
|---|---|---|
| Deep sea blue | `#0018A8` | 0, 24, 168 |
| Soft cream white | `#F5F1E9` | 245, 241, 233 |

To change the palette, edit the `Theme` class at the top of `src/MainForm.cs`.

---

## License

[MIT License](LICENSE)

For the full manual (every feature, safety mechanisms, troubleshooting) see [使用说明.md](使用说明.md) (Chinese).
