// ============================================================================
//  WhoLocks 原生拖放支持
//  ---------------------------------------------------------------------------
//  【为什么之前拖入会显示禁止光标 X】
//  Windows 上有两套互相冲突的拖放机制：
//    A) OLE 拖放：控件设 AllowDrop=true → 系统注册 IDropTarget，收 DragEnter/DragDrop
//    B) 原生拖放：DragAcceptFiles(hwnd,true) → 系统发 WM_DROPFILES
//  只要 AllowDrop=true，Windows 就【优先】走 OLE 路径，原生 WM_DROPFILES 根本不会再发。
//  而程序以管理员身份运行时，explorer(低权限) → 本窗口(高权限) 的 OLE 数据交换
//  会被 UIPI（用户界面特权隔离）拦截 → e.Data.GetDataPresent(FileDrop) 返回 false
//  → DragEnter 里把 e.Effect 设成 None → 鼠标显示【禁止 X】。
//
//  【本文件的解决方式】
//  彻底放弃 OLE：整个控件树 AllowDrop=false，只用原生 WM_DROPFILES。
//  并用 ChangeWindowMessageFilterEx 放行 WM_DROPFILES / WM_COPYDATA / WM_COPYGLOBALDATA，
//  这样即使以管理员身份运行，explorer 也能把路径投递进来。
//  ============================================================================
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WhoLocks
{
    /// <summary>原生拖放接收器：基于 WM_DROPFILES，不与 OLE 冲突，管理员身份下同样可用。</summary>
    internal sealed class NativeDropTarget
    {
        // ---- Win32 ----
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr hDrop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);

        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr hwnd);

        private const int WM_DROPFILES = 0x0233;
        private const int WM_COPYDATA = 0x004A;
        private const int WM_COPYGLOBALDATA = 0x0049;
        private const uint MSGFLT_ALLOW = 1;

        private readonly Form _form;
        private readonly Action<string> _onPath;
        private NativeWindow _hook;
        private bool _attached;

        public NativeDropTarget(Form form, Action<string> onPath)
        {
            _form = form;
            _onPath = onPath;
        }

        /// <summary>
        /// 挂载原生拖放。句柄未创建时会自动推迟到创建完成后执行。
        /// 可安全重复调用。
        /// </summary>
        public void Attach()
        {
            if (_form == null) return;

            if (_form.IsHandleCreated)
            {
                AttachCore(_form.Handle);
                return;
            }

            EventHandler onCreated = null;
            onCreated = delegate (object s, EventArgs e)
            {
                _form.HandleCreated -= onCreated;
                try { AttachCore(_form.Handle); } catch { }
            };
            _form.HandleCreated += onCreated;

            // 兜底：极少数创建路径不触发 HandleCreated，再用 Load/Shown 各试一次
            EventHandler retry = delegate (object s, EventArgs e)
            {
                try { if (_form.IsHandleCreated) AttachCore(_form.Handle); }
                catch { }
            };
            _form.Load += retry;
            _form.Shown += retry;
        }

        private void AttachCore(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            // ---- 第一步：注销 OLE 拖放目标 ----
            // 关键！只要 OLE IDropTarget 存在，Windows 就不会发 WM_DROPFILES，
            // 而 OLE 在管理员模式下会被 UIPI 拦截 → 表现为拖入显示禁止 X。
            // 所以这里主动撤销它，让系统走纯原生路径。
            try { RevokeDragDrop(hwnd); }
            catch { }

            // ---- 第二步：放行跨权限消息（绕开 UIPI）----
            AllowMessage(hwnd, WM_DROPFILES);
            AllowMessage(hwnd, WM_COPYDATA);
            AllowMessage(hwnd, WM_COPYGLOBALDATA);

            // ---- 第三步：注册接受拖放 ----
            DragAcceptFiles(hwnd, false);   // 先清一次，避免状态残留
            DragAcceptFiles(hwnd, true);

            // ---- 第四步：挂消息钩子接收 WM_DROPFILES ----
            if (_hook == null)
            {
                _hook = new DropHook(this);
                _hook.AssignHandle(hwnd);
            }
            _attached = true;
        }

        /// <summary>某些控件/组件可能自行注册过 OLE 拖放，全部撤销一次（窗口级兜底）。</summary>
        public void DetachOle()
        {
            if (_form == null || !_form.IsHandleCreated) return;
            try { RevokeDragDrop(_form.Handle); } catch { }
        }

        public bool IsAttached { get { return _attached; } }

        private static void AllowMessage(IntPtr hwnd, uint msg)
        {
            try { ChangeWindowMessageFilterEx(hwnd, msg, MSGFLT_ALLOW, IntPtr.Zero); }
            catch { /* 老系统无此 API，忽略 */ }
        }

        private sealed class DropHook : NativeWindow
        {
            private readonly NativeDropTarget _owner;
            public DropHook(NativeDropTarget owner) { _owner = owner; }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_DROPFILES)
                {
                    IntPtr hDrop = m.WParam;
                    try
                    {
                        uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                        if (count > 0)
                        {
                            StringBuilder sb = new StringBuilder(4096);
                            if (DragQueryFile(hDrop, 0, sb, (uint)sb.Capacity) > 0)
                            {
                                string path = sb.ToString();
                                if (_owner._onPath != null) _owner._onPath(path);
                            }
                        }
                    }
                    catch { }
                    finally { DragFinish(hDrop); }
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }

    /// <summary>
    /// 拖放路径处理的公共工具。
    ///
    /// 注意：这里【不挂 OLE 拖放事件】（不设 AllowDrop=true）。
    /// 原因见文件头注释：OLE 与原生 WM_DROPFILES 互斥，且 OLE 在管理员模式下
    /// 会被 UIPI 拦截导致显示禁止 X。全窗口统一走原生 WM_DROPFILES 即可。
    /// </summary>
    internal static class DragDropHelper
    {
        /// <summary>
        /// 确保整个控件树都【关闭】OLE 拖放。
        /// 子控件若继承或自行设置了 AllowDrop，会重新注册 OLE 目标，
        /// 导致父窗口的原生拖放被绕过 —— 必须递归清掉。
        /// </summary>
        public static void DisableOleDrop(Control root)
        {
            if (root == null) return;
            try { root.AllowDrop = false; } catch { }
            foreach (Control child in root.Controls)
                DisableOleDrop(child);
        }

        /// <summary>
        /// 校验拖入路径是否可用：文件夹原样返回；单个文件也原样返回（表示"只查这个文件"）。
        /// 注意：不能再把文件换成它所在的目录 —— 那样"拖入桌面上的 a.py"
        /// 会变成"扫描整个桌面"，这是 Bug。
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (System.IO.Directory.Exists(path)) return path;
                if (System.IO.File.Exists(path)) return path;
            }
            catch { }
            return null;
        }

        /// <summary>判断该路径是不是单个文件（用于决定扫描模式）。</summary>
        public static bool IsFile(string path)
        {
            try { return !string.IsNullOrEmpty(path) && System.IO.File.Exists(path); }
            catch { return false; }
        }
    }
}
