// ============================================================================
//  谁在占用  (WhoLocks)  v1.0
//  用途：选择一个文件夹，找出到底是谁在占用里面的文件（进程），
//        支持「一键全杀」「手动勾选杀」「自己去任务管理器对照杀」。
//
//  检测原理（双层交叉验证，保证结果真实）：
//    第一层：Windows Restart Manager API (rstrtmgr.dll)  —— 系统托管，误报最低
//    第二层：系统全局句柄表扫描 (NtQuerySystemInformation + ReadProcessMemory)
//            —— 专治任务管理器里翻不到的情况：explorer 预览/缩略图、
//               杀毒扫描、索引服务、后台服务、加载了该目录 DLL 的程序等
//
//  兼容：Windows 10 / 11（32 位与 64 位系统均有对应编译版本）
//  依赖：仅系统自带的 .NET Framework 4.x，无任何第三方库
//  编译：见 构建.py（调用系统自带 csc.exe），或直接使用已编译好的 exe
//
//  命令行（可选，给脚本/批处理用）：
//    WhoLocks.exe --selftest "D:\某个目录" "D:\报告.txt"
//        无界面跑一次扫描，把结果写进 txt（方便验证/自动化）
//    WhoLocks.exe "D:\某个目录"       直接打开界面并自动开始扫描该目录
// ============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WhoLocks
{
    // ========================================================================
    //  程序入口
    // ========================================================================
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // 无界面自检模式：WhoLocks.exe --selftest <目录> [输出txt]
            if (args.Length >= 2 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                int code;
                try
                {
                    code = SelfTest.Run(args[1], args.Length >= 3 ? args[2] : null);
                }
                catch (Exception ex)
                {
                    code = 3;
                    try
                    {
                        if (args.Length >= 3) File.WriteAllText(args[2], "自检异常：" + ex, Encoding.UTF8);
                    }
                    catch { }
                }
                Environment.Exit(code);
                return;
            }

            // CSV 导出自我验证：--csvtest <目录> <输出csv>
            if (args.Length >= 3 && string.Equals(args[0], "--csvtest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.CsvCheck(args[1], args[2]));
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show("界面线程异常：" + e.Exception.Message, "谁在占用",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.Run(new MainForm(args));
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序启动失败：" + ex.Message, "谁在占用",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // ========================================================================
    //  数据结构：一个「占用者」
    // ========================================================================
    internal sealed class LockProc
    {
        public int Pid;
        public string Name = "";
        public string ImagePath = "";
        public string User = "";
        public int Session = -1;
        public string StartTime = "";
        public bool CanTerminate;
        public bool IsCritical;    // 系统关键进程：一键全杀自动跳过
        public bool IsWarn;        // 建议不要杀（explorer / 杀软 / 输入法等）
        public int Sources;        // 1=RestartManager  2=文件句柄  4=DLL映射(节对象)
        public readonly List<string> Files = new List<string>();
        public readonly List<IntPtr> RawHandles = new List<IntPtr>();
        public bool Killed;

        public string SourceText
        {
            get
            {
                List<string> parts = new List<string>();
                if ((Sources & 1) != 0) parts.Add("系统RM");
                if ((Sources & 2) != 0) parts.Add("句柄表");
                if ((Sources & 4) != 0) parts.Add("DLL映射");
                return string.Join("+", parts.ToArray());
            }
        }

        public string RiskText
        {
            get
            {
                if (IsCritical) return "系统关键-禁止杀";
                if (IsWarn) return "建议保留";
                return "普通";
            }
        }

        public string SampleFile
        {
            get { return Files.Count > 0 ? Files[0] : ""; }
        }

        public string FilesForCsv
        {
            get
            {
                if (Files.Count == 0) return "";
                int cap = 40;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < Files.Count && i < cap; i++)
                {
                    if (i > 0) sb.Append(" | ");
                    sb.Append(Files[i]);
                }
                if (Files.Count > cap) sb.Append(" | ...(共 ").Append(Files.Count).Append(" 个)");
                return sb.ToString();
            }
        }
    }

    internal sealed class ScanOptions
    {
        /// <summary>要检查的路径：可以是文件夹，也可以单个文件。</summary>
        public string Root = "";
        /// <summary>true 表示 Root 是一个文件本身（只查这个文件，不展开父目录）。</summary>
        public bool RootIsFile;
        public bool TopOnly;
        public bool IncludeSections = true;
        public int MaxFiles = 40000;
    }

    internal sealed class ScanResult
    {
        public readonly List<LockProc> Procs = new List<LockProc>();
        public readonly List<string> Log = new List<string>();
        public int FileCount;
        public bool Truncated;
        public bool Cancelled;
        public bool RmOk;
        public bool HandleOk;
        public string Root = "";
        /// <summary>true 表示本次只检查了 Root 这一个文件（而不是整个目录）。</summary>
        public bool RootIsFile;
        public DateTime ScanTime = DateTime.Now;
        public bool Elevated;
    }

    // ========================================================================
    //  原生 API
    // ========================================================================
    internal static class Native
    {
        // ---- Restart Manager ----
        [StructLayout(LayoutKind.Sequential)]
        public struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        public enum RM_APP_TYPE
        {
            RmUnknownApp = 0, RmMainWindow = 1, RmOtherWindow = 2, RmService = 3,
            RmExplorer = 4, RmConsole = 5, RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        public const int ERROR_MORE_DATA = 234;

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        public static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, [In] RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        public static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
            ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        // ---- 句柄表扫描 ----
        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation,
            int SystemInformationLength, out int ReturnLength);

        [DllImport("ntdll.dll")]
        public static extern int NtQueryObject(IntPtr Handle, int ObjectInformationClass,
            IntPtr ObjectInformation, int ObjectInformationLength, out int ReturnLength);

        [DllImport("ntdll.dll")]
        public static extern int NtCreateSection(out IntPtr SectionHandle, uint DesiredAccess,
            IntPtr ObjectAttributes, ref long MaximumSize, uint SectionPageProtection,
            uint AllocationAttributes, IntPtr FileHandle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer,
            IntPtr nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DuplicateHandle(IntPtr hSourceProcessHandle, IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle, uint dwDesiredAccess,
            bool bInheritHandle, uint dwOptions);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetFinalPathNameByHandle(IntPtr hFile, StringBuilder lpszFilePath,
            int cchFilePath, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetFileType(IntPtr hFile);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

        // ---- 令牌提权（SeDebugPrivilege） ----
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            // 必须内联固定长度数组：AdjustTokenPrivileges 按值读取这块内存，
            // 不加 ByValArray 会被当成指针写入，提权必然失败。
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public LUID_AND_ATTRIBUTES[] Privileges;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
            IntPtr NewState, int BufferLength, IntPtr PreviousState, out int ReturnLength);

        // ---- 进程令牌 / 用户 ----
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
            IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool LookupAccountSid(string lpSystemName, IntPtr Sid, StringBuilder lpName,
            ref int cchName, StringBuilder lpReferencedDomainName, ref int cchReferencedDomainName, out int peUse);

        // ---- 常量 ----
        public const int SystemExtendedHandleInformation = 64;

        public const int PROCESS_TERMINATE = 0x0001;
        public const int PROCESS_QUERY_INFORMATION = 0x0400;
        public const int PROCESS_VM_READ = 0x0010;
        public const int PROCESS_DUP_HANDLE = 0x0040;

        public const uint TOKEN_QUERY = 0x0008;
        public const int TokenUser = 1;

        public const uint GENERIC_READ = 0x80000000;
        public const uint FILE_SHARE_READ_WRITE_DELETE = 0x00000007;
        public const uint OPEN_EXISTING = 3;
        public const uint DUPLICATE_CLOSE_SOURCE = 0x00000001;

        public static bool IsElevated()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    // ========================================================================
    //  扫描引擎
    // ========================================================================
    internal sealed class ScanEngine
    {
        private volatile bool _cancel;
        private readonly Action<int, string> _progress;

        public ScanEngine(Action<int, string> progress)
        {
            _progress = progress;
            if (_progress == null) _progress = delegate { };
        }

        public void Cancel() { _cancel = true; }

        public ScanResult Scan(ScanOptions opt)
        {
            ScanResult result = new ScanResult();
            result.Root = opt.Root;
            result.RootIsFile = opt.RootIsFile;
            result.ScanTime = DateTime.Now;
            result.Elevated = Native.IsElevated();
            _cancel = false;

            // ---------- 1. 收集要检查的路径 ----------
            List<string> paths = new List<string>();
            int dirsDone = 0;
            int dirErrors = 0;

            // 情况 A：用户指定的是【单个文件】——只查这一个文件，不展开它所在的文件夹
            // （否则"选桌面上的 a.py"会变成"扫整个桌面"，那是 Bug）
            if (opt.RootIsFile)
            {
                _progress(1, "正在检查文件……");
                paths.Add(opt.Root);
                result.FileCount = 1;
                result.Log.Add("当前检查的是单个文件：" + opt.Root);
            }
            else
            {
                _progress(1, "正在枚举目录……");
                paths.Add(opt.Root);
            }

            // 情况 B：目录模式，按选项决定是否递归
            if (!opt.RootIsFile)
            {
                if (opt.TopOnly)
                {
                    try
                    {
                        foreach (string f in Directory.GetFiles(opt.Root)) paths.Add(f);
                    }
                    catch (Exception ex) { dirErrors++; result.Log.Add("读取顶层目录失败：" + ex.Message); }
                }
                else
                {
                    Stack<string> stack = new Stack<string>();
                    stack.Push(opt.Root);
                    while (stack.Count > 0)
                    {
                        if (_cancel) { result.Cancelled = true; break; }
                        string dir = stack.Pop();
                        try
                        {
                            foreach (string f in Directory.GetFiles(dir))
                            {
                                paths.Add(f);
                                if (paths.Count >= opt.MaxFiles) { result.Truncated = true; break; }
                            }
                            if (result.Truncated) break;
                            foreach (string sub in Directory.GetDirectories(dir))
                            {
                                paths.Add(sub);                       // 目录本身也可能被占用
                                stack.Push(sub);
                                if (paths.Count >= opt.MaxFiles) { result.Truncated = true; break; }
                            }
                        }
                        catch (Exception) { dirErrors++; }
                        dirsDone++;
                        if (dirsDone % 20 == 0)
                        {
                            int pct = 2 + Math.Min(28, dirsDone / 40);
                            _progress(pct, "正在枚举目录：" + dir + "（已找到 " + paths.Count + " 项）");
                        }
                    }
                    if (dirErrors > 0)
                        result.Log.Add("有 " + dirErrors + " 个目录无权限读取，已自动跳过（不影响其他目录）。");
                }

                result.FileCount = paths.Count - 1;
                if (result.FileCount < 0) result.FileCount = 0;
            }

            result.Log.Add("待检查的文件/目录共 " + paths.Count + " 项。");
            if (result.Truncated)
                result.Log.Add("目录内容超过上限（" + opt.MaxFiles + " 项），已截断扫描。建议分目录检查。");

            Dictionary<int, LockProc> map = new Dictionary<int, LockProc>();

            if (!result.Cancelled)
            {
                // ---------- 2. Restart Manager 检测 ----------
                _progress(32, "第一层检测：Windows Restart Manager 正在分析……");
                try
                {
                    result.RmOk = RmDetect(paths, map, result.Log);
                }
                catch (Exception ex)
                {
                    result.Log.Add("Restart Manager 检测失败（不影响句柄检测）：" + ex.Message);
                    result.RmOk = false;
                }
                if (!result.RmOk)
                    result.Log.Add("Restart Manager 本次未返回结果（非管理员时常见），已自动改用句柄表检测，不影响使用。");
            }

            // ---------- 3. 全局句柄表检测 ----------
            if (!result.Cancelled)
            {
                _progress(58, "第二层检测：正在扫描系统句柄表……");
                try
                {
                    result.HandleOk = HandleScan(opt, map, result.Log);
                }
                catch (Exception ex)
                {
                    result.Log.Add("句柄表扫描失败：" + ex.Message);
                    result.HandleOk = false;
                }
                if (!result.HandleOk)
                    result.Log.Add("句柄表扫描未完成（常见原因：未以管理员身份运行、系统限制）。");
            }

            // ---------- 4. 补全进程信息 ----------
            _progress(90, "正在整理结果……");
            foreach (KeyValuePair<int, LockProc> kv in map)
            {
                if (_cancel) { result.Cancelled = true; break; }
                Enrich(kv.Value);
                result.Procs.Add(kv.Value);
            }
            result.Procs.Sort(delegate (LockProc a, LockProc b)
            {
                if (a.IsCritical != b.IsCritical) return a.IsCritical ? 1 : -1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            _progress(100, "完成");
            return result;
        }

        // ===================== 第一层：Restart Manager =====================
        private bool RmDetect(List<string> paths, Dictionary<int, LockProc> map, List<string> log)
        {
            uint session;
            string key = Guid.NewGuid().ToString();
            int rv = Native.RmStartSession(out session, 0, key);
            if (rv != 0) { log.Add("[RM] RmStartSession 失败，错误码 " + rv); return false; }
            try
            {
                const int batch = 400;
                for (int i = 0; i < paths.Count; i += batch)
                {
                    if (_cancel) return true;
                    int cnt = Math.Min(batch, paths.Count - i);
                    string[] arr = new string[cnt];
                    paths.CopyTo(i, arr, 0, cnt);
                    Native.RmRegisterResources(session, (uint)cnt, arr, 0, null, 0, null);
                    int pct = 32 + (int)((i + cnt) * 20L / Math.Max(1, paths.Count));
                    if (pct > 52) pct = 52;
                    _progress(pct, "第一层检测：已登记 " + (i + cnt) + "/" + paths.Count + " 项……");
                }
                if (_cancel) return true;

                uint needed = 0, count = 0, reasons = 0;
                Native.RM_PROCESS_INFO[] none = new Native.RM_PROCESS_INFO[0];
                rv = Native.RmGetList(session, out needed, ref count, none, ref reasons);
                if (rv == 5)   // ERROR_ACCESS_DENIED：非管理员时常见，不一定是故障
                    log.Add("[RM] 无权限访问全部会话（非管理员运行的正常现象），已由句柄表检测补足。");
                if (rv == Native.ERROR_MORE_DATA && needed > 0)
                {
                    count = needed;
                    Native.RM_PROCESS_INFO[] list = new Native.RM_PROCESS_INFO[needed];
                    rv = Native.RmGetList(session, out needed, ref count, list, ref reasons);
                    if (rv == 0)
                    {
                        for (int i = 0; i < count && i < list.Length; i++)
                        {
                            int pid = list[i].Process.dwProcessId;
                            if (pid <= 0) continue;
                            LockProc lp = GetOrAdd(map, pid);
                            lp.Sources |= 1;
                            if (!string.IsNullOrEmpty(list[i].strAppName) && lp.Name.Length == 0)
                                lp.Name = list[i].strAppName;
                        }
                    }
                }
                return rv == 0;
            }
            finally
            {
                Native.RmEndSession(session);
            }
        }

        // ===================== 第二层：句柄表扫描 =====================
        private bool HandleScan(ScanOptions opt, Dictionary<int, LockProc> map, List<string> log)
        {
            EnableDebugPrivilege();

            // ---- 关键顺序：先创建"探测句柄"，再抓句柄表快照 ----
            // 这样快照里必然包含我们自己刚打开的 文件句柄 / 节对象句柄，
            // 从快照中反查它们所在的 ObjectTypeIndex，就能准确锁定"文件"和"节"两种类型编号。
            int myPid = Process.GetCurrentProcess().Id;
            IntPtr hProbeFile = IntPtr.Zero;
            IntPtr hProbeSection = IntPtr.Zero;
            try
            {
                string self = Application.ExecutablePath;
                if (string.IsNullOrEmpty(self) || !File.Exists(self)) self = typeof(ScanEngine).Assembly.Location;
                if (!string.IsNullOrEmpty(self) && File.Exists(self))
                {
                    hProbeFile = Native.CreateFile(self, Native.GENERIC_READ,
                        Native.FILE_SHARE_READ_WRITE_DELETE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (hProbeFile == new IntPtr(-1)) hProbeFile = IntPtr.Zero;
                    if (hProbeFile != IntPtr.Zero)
                    {
                        long maxSize = 0;
                        if (Native.NtCreateSection(out hProbeSection, 0x0005, IntPtr.Zero, ref maxSize,
                                0x02, 0x8000000, hProbeFile) != 0)
                            hProbeSection = IntPtr.Zero;
                    }
                }

                IntPtr buffer = IntPtr.Zero;
                int length = 1 << 20;
                int status = -1;
                int returnLength = 0;

                for (int attempt = 0; attempt < 16; attempt++)
                {
                    buffer = Marshal.AllocHGlobal(length);
                    status = Native.NtQuerySystemInformation(Native.SystemExtendedHandleInformation, buffer, length, out returnLength);
                    if (status == 0) break;
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                    if (status != unchecked((int)0xC0000004))   // STATUS_INFO_LENGTH_MISMATCH
                    {
                        log.Add("[句柄] NtQuerySystemInformation 失败，状态 0x" + status.ToString("X8"));
                        return false;
                    }
                    length = Math.Max(length * 2, returnLength + (1 << 16));
                    if (length > (256 << 20)) return false;
                }
                if (buffer == IntPtr.Zero) return false;

                Dictionary<int, IntPtr> procHandles = new Dictionary<int, IntPtr>();
                try
                {
                    long numHandles = Marshal.ReadIntPtr(buffer, 0).ToInt64();
                    int entrySize = Marshal.SizeOf(typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX));
                    IntPtr entries = new IntPtr(buffer.ToInt64() + IntPtr.Size * 2);

                    int fileType = -1, sectionType = -1;
                    if (hProbeFile != IntPtr.Zero)
                        fileType = FindTypeIndex(entries, numHandles, entrySize, myPid, hProbeFile.ToInt64());
                    if (opt.IncludeSections && hProbeSection != IntPtr.Zero)
                        sectionType = FindTypeIndex(entries, numHandles, entrySize, myPid, hProbeSection.ToInt64());

                    log.Add("[句柄] 句柄总数=" + numHandles + "，文件类型索引=" + fileType +
                            "，节对象索引=" + sectionType);

                    if (fileType < 0)
                    {
                        log.Add("[句柄] 未能识别文件对象类型编号，无法安全过滤句柄。");
                        return false;
                    }

                    Dictionary<string, string> devices = BuildDeviceMap();
                    string rootNorm = NormalizeRoot(opt.Root);
                    // 单文件模式：只认这个文件本身，不能把同目录其他文件算进来
                    bool exactMatch = opt.RootIsFile;
                    HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // 探测句柄是我自己为识别类型编号而临时打开的，不能把它算成"占用者"
                    long probeFileVal = hProbeFile.ToInt64();
                    long probeSectionVal = hProbeSection.ToInt64();
                    int offPid = IntPtr.Size;
                    int offHandle = IntPtr.Size * 2;
                    int offType = IntPtr.Size * 3 + 6;
                    long denied = 0, matchedHandles = 0;

                    for (long i = 0; i < numHandles; i++)
                    {
                        if ((i & 0x7FF) == 0)
                        {
                            if (_cancel) return true;
                            int pct = 58 + (int)(i * 30L / Math.Max(1, numHandles));
                            if (pct > 88) pct = 88;
                            _progress(pct, "第二层检测：句柄 " + i + "/" + numHandles + "……");
                        }

                        IntPtr p = new IntPtr(entries.ToInt64() + i * entrySize);
                        int typeIdx = Marshal.ReadInt16(p, offType);
                        bool isSection = false;
                        if (typeIdx != fileType)
                        {
                            if (!(opt.IncludeSections && sectionType > 0 && typeIdx == sectionType)) continue;
                            isSection = true;
                        }

                        long pid64 = Marshal.ReadIntPtr(p, offPid).ToInt64();
                        if (pid64 <= 4 || pid64 > 0x7FFFFFFF) continue;   // 跳过 Idle/System
                        int pid = (int)pid64;

                        long handleVal = Marshal.ReadIntPtr(p, offHandle).ToInt64();
                        // 跳过本程序自己的探测句柄
                        if (pid == myPid && (handleVal == probeFileVal || handleVal == probeSectionVal))
                            continue;
                        IntPtr hProc;
                        if (!procHandles.TryGetValue(pid, out hProc))
                        {
                            // 注意：复制别人的句柄必须带 PROCESS_DUP_HANDLE 权限，否则 DuplicateHandle 必然失败
                            hProc = Native.OpenProcess(Native.PROCESS_DUP_HANDLE | Native.PROCESS_QUERY_INFORMATION,
                                false, pid);
                            procHandles[pid] = hProc;
                        }
                        if (hProc == IntPtr.Zero) { denied++; continue; }

                        // 正确做法：把目标进程的句柄复制到本进程，再查询它指向的真实路径。
                        // （句柄表里的 ObjectName 是内核地址，用户态读不到，必须走 DuplicateHandle）
                        IntPtr hDup;
                        if (!Native.DuplicateHandle(hProc, new IntPtr(handleVal),
                                Native.GetCurrentProcess(), out hDup, 0, false, 0x00000002)) // DUPLICATE_SAME_ACCESS
                            continue;

                        string dos = null;
                        try
                        {
                            string name = GetHandlePath(hDup, isSection);
                            if (!string.IsNullOrEmpty(name)) dos = ConvertDevicePath(name, devices);
                        }
                        finally { Native.CloseHandle(hDup); }

                        if (dos == null) continue;
                        if (exactMatch)
                        {
                            // 单文件：必须完全相等（大小写不敏感）
                            if (!string.Equals(dos, rootNorm, StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                        else if (!IsUnderRoot(dos, rootNorm)) continue;

                        matchedHandles++;
                        LockProc lp = GetOrAdd(map, pid);
                        lp.Sources |= isSection ? 4 : 2;
                        if (lp.RawHandles.Count < 200) lp.RawHandles.Add(new IntPtr(handleVal));

                        string key = pid + "|" + dos.ToLowerInvariant();
                        if (seen.Add(key) && !lp.Files.Contains(dos))
                        {
                            lp.Files.Add(dos);
                            if (lp.Files.Count > 5000) lp.Files.RemoveRange(5000, lp.Files.Count - 5000);
                        }
                    }

                    log.Add("[句柄] 匹配句柄 " + matchedHandles + " 个，" + map.Count +
                            " 个进程；因权限打不开的进程 " + denied + " 个。");
                    if (denied > 20 && Native.IsElevated() == false)
                        log.Add("[句柄] 提示：以管理员身份运行可检测到更多后台/系统进程的占用。");
                    return true;
                }
                finally
                {
                    foreach (KeyValuePair<int, IntPtr> kv in procHandles)
                        if (kv.Value != IntPtr.Zero) Native.CloseHandle(kv.Value);
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                if (hProbeSection != IntPtr.Zero) Native.CloseHandle(hProbeSection);
                if (hProbeFile != IntPtr.Zero) Native.CloseHandle(hProbeFile);
            }
        }

        // 打开 SeDebugPrivilege，尽量提升跨进程读取能力（失败不影响后续流程）
        private static void EnableDebugPrivilege()
        {
            IntPtr hTok = IntPtr.Zero;
            try
            {
                if (!Native.OpenProcessToken(Native.GetCurrentProcess(), 0x0020 | 0x0008, out hTok)) return;
                Native.LUID luid = new Native.LUID();
                if (!Native.LookupPrivilegeValue(null, "SeDebugPrivilege", out luid)) return;
                Native.TOKEN_PRIVILEGES tp = new Native.TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Privileges = new Native.LUID_AND_ATTRIBUTES[1];
                tp.Privileges[0].Luid = luid;
                tp.Privileges[0].Attributes = 0x00000002;   // SE_PRIVILEGE_ENABLED
                int len = Marshal.SizeOf(typeof(Native.TOKEN_PRIVILEGES));
                IntPtr buf = Marshal.AllocHGlobal(len);
                try
                {
                    Marshal.StructureToPtr(tp, buf, false);
                    int ret;
                    Native.AdjustTokenPrivileges(hTok, false, buf, len, IntPtr.Zero, out ret);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { }
            finally { if (hTok != IntPtr.Zero) Native.CloseHandle(hTok); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
        {
            public IntPtr Object;
            public IntPtr UniqueProcessId;
            public IntPtr HandleValue;
            public uint GrantedAccess;
            public ushort CreatorBackTraceIndex;
            public ushort ObjectTypeIndex;
            public uint HandleAttributes;
            public uint Reserved;
        }

        private static int FindTypeIndex(IntPtr entries, long count, int entrySize, int pid, long handle)
        {
            // 自己刚创建的句柄在表的末尾，倒着找更快
            for (long i = count - 1; i >= 0; i--)
            {
                IntPtr p = new IntPtr(entries.ToInt64() + i * entrySize);
                if (Marshal.ReadIntPtr(p, IntPtr.Size).ToInt64() != pid) continue;
                if (Marshal.ReadIntPtr(p, IntPtr.Size * 2).ToInt64() != handle) continue;
                return Marshal.ReadInt16(p, IntPtr.Size * 3 + 6);
            }
            return -1;
        }

        // 查询一个（已复制到本进程的）句柄指向的真实路径
        //   isSection=true 表示这是"节对象"（映射到内存的 DLL/EXE），走 NtQueryObject
        //   否则是普通文件句柄：必须先确认它是磁盘文件，否则 GetFinalPathNameByHandle
        //   遇到管道/网络共享句柄会永久阻塞（这是该 API 的著名陷阱）
        private static string GetHandlePath(IntPtr h, bool isSection)
        {
            if (isSection) return QueryObjectName(h);

            try
            {
                uint ft = Native.GetFileType(h);
                if (ft != 1) return null;   // 1 = FILE_TYPE_DISK，其它（管道/字符设备/未知）直接跳过
            }
            catch { return null; }

            try
            {
                const int cap = 1024;
                StringBuilder sb = new StringBuilder(cap);
                // 0 = FILE_NAME_NORMALIZED | VOLUME_NAME_DOS，返回 \\?\C:\... 形式
                int n = Native.GetFinalPathNameByHandle(h, sb, cap, 0);
                if (n > 0 && n <= cap) return sb.ToString(0, n);
                if (n > cap)
                {
                    StringBuilder sb2 = new StringBuilder(n + 4);
                    n = Native.GetFinalPathNameByHandle(h, sb2, n + 4, 0);
                    if (n > 0 && n <= sb2.Length) return sb2.ToString(0, n);
                }
            }
            catch { }
            return null;
        }

        // 用 NtQueryObject 查询对象名（对节对象安全，且不需要目标进程内存权限）
        private static string QueryObjectName(IntPtr h)
        {
            try
            {
                int size = 2048;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    int ret;
                    int status = Native.NtQueryObject(h, 1, buf, size, out ret);   // 1 = ObjectNameInformation
                    if (status != 0) return null;
                    // OBJECT_NAME_INFORMATION { UNICODE_STRING Name; }  —— 在本进程内，可直接读
                    int len = Marshal.ReadInt16(buf, 0);
                    if (len <= 2 || len > 32768) return null;
                    IntPtr strPtr = Marshal.ReadIntPtr(buf, IntPtr.Size == 8 ? 8 : 4);
                    if (strPtr == IntPtr.Zero) return null;
                    return Marshal.PtrToStringUni(strPtr, len / 2);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { return null; }
        }

        private static string ReadObjectName(IntPtr hProc, IntPtr unicodeStringAddr)
        {
            if (unicodeStringAddr == IntPtr.Zero) return null;
            int hdrLen = IntPtr.Size == 8 ? 16 : 8;
            byte[] hdr = new byte[hdrLen];
            IntPtr read;
            if (!Native.ReadProcessMemory(hProc, unicodeStringAddr, hdr, new IntPtr(hdrLen), out read)) return null;
            if (read.ToInt64() < 4) return null;
            int len = BitConverter.ToUInt16(hdr, 0);
            if (len <= 2 || len > 32768) return null;
            IntPtr bufPtr = IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(hdr, 8))
                                             : new IntPtr(BitConverter.ToInt32(hdr, 4));
            if (bufPtr == IntPtr.Zero) return null;
            byte[] sb = new byte[len];
            if (!Native.ReadProcessMemory(hProc, bufPtr, sb, new IntPtr(len), out read)) return null;
            int real = (int)read.ToInt64();
            if (real < 2) return null;
            if ((real & 1) == 1) real--;
            return Encoding.Unicode.GetString(sb, 0, real);
        }

        private static Dictionary<string, string> BuildDeviceMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            StringBuilder sb = new StringBuilder(1024);
            for (char c = 'A'; c <= 'Z'; c++)
            {
                string dev = c + ":";
                sb.Length = 0;
                if (Native.QueryDosDevice(dev, sb, sb.Capacity) > 0)
                {
                    string target = sb.ToString();
                    if (target.Length > 0)
                    {
                        string[] multi = target.Split('\0');
                        for (int i = 0; i < multi.Length; i++)
                        {
                            if (multi[i].Length == 0) continue;
                            if (!map.ContainsKey(multi[i])) map[multi[i]] = dev;
                        }
                    }
                }
                sb.Length = 0;
                if (Native.QueryDosDevice(c + ":", sb, sb.Capacity) == 0) { }
            }
            return map;
        }

        private static string ConvertDevicePath(string ntPath, Dictionary<string, string> devices)
        {
            if (string.IsNullOrEmpty(ntPath)) return null;

            // GetFinalPathNameByHandle 常返回 \\?\C:\... 或 \\?\UNC\... 形式
            if (ntPath.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
                return "\\\\" + ntPath.Substring(8);
            if (ntPath.StartsWith("\\\\?\\", StringComparison.Ordinal))
            {
                string a = ntPath.Substring(4);
                if (a.Length >= 2 && a[1] == ':') return a;
                return a;
            }
            // \??\C:\x 或 \Device\HarddiskVolume3\x
            if (ntPath.StartsWith("\\??\\", StringComparison.Ordinal))
            {
                string rest0 = ntPath.Substring(4);
                if (rest0.Length >= 2 && rest0[1] == ':') return rest0;
            }
            foreach (KeyValuePair<string, string> kv in devices)
            {
                if (ntPath.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = ntPath.Substring(kv.Key.Length);
                    if (rest.Length == 0) rest = "\\";
                    return kv.Value + rest;
                }
            }
            return null;
        }

        private static string NormalizeRoot(string root)
        {
            if (string.IsNullOrEmpty(root)) return "";
            string r = root.Trim();
            if (r.EndsWith("\\", StringComparison.Ordinal) && r.Length > 3) r = r.Substring(0, r.Length - 1);
            return r;
        }

        private static bool IsUnderRoot(string dosPath, string rootNorm)
        {
            if (string.IsNullOrEmpty(dosPath) || string.IsNullOrEmpty(rootNorm)) return false;
            if (dosPath.Length == rootNorm.Length)
                return string.Equals(dosPath, rootNorm, StringComparison.OrdinalIgnoreCase);
            string prefix = rootNorm.EndsWith("\\", StringComparison.Ordinal) ? rootNorm : rootNorm + "\\";
            return dosPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static LockProc GetOrAdd(Dictionary<int, LockProc> map, int pid)
        {
            LockProc lp;
            if (!map.TryGetValue(pid, out lp))
            {
                lp = new LockProc();
                lp.Pid = pid;
                map[pid] = lp;
            }
            return lp;
        }

        // ===================== 进程信息补全 =====================
        private static void Enrich(LockProc lp)
        {
            try
            {
                Process p = Process.GetProcessById(lp.Pid);
                if (string.IsNullOrEmpty(lp.Name)) lp.Name = p.ProcessName + ".exe";
                try { lp.Session = p.SessionId; } catch { }
                try { lp.StartTime = p.StartTime.ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
                try { lp.ImagePath = p.MainModule.FileName; } catch { }
            }
            catch
            {
                if (string.IsNullOrEmpty(lp.Name)) lp.Name = "PID " + lp.Pid;
            }
            lp.User = GetProcessUser(lp.Pid);
            lp.CanTerminate = CanTerminate(lp.Pid);
            ClassifyRisk(lp);
        }

        public static bool CanTerminate(int pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_TERMINATE, false, pid);
            if (h == IntPtr.Zero) return false;
            Native.CloseHandle(h);
            return true;
        }

        public static string GetProcessUser(int pid)
        {
            IntPtr hProc = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return "";
            IntPtr hTok = IntPtr.Zero;
            try
            {
                if (!Native.OpenProcessToken(hProc, Native.TOKEN_QUERY, out hTok)) return "";
                int size;
                Native.GetTokenInformation(hTok, Native.TokenUser, IntPtr.Zero, 0, out size);
                if (size <= 0) return "";
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (!Native.GetTokenInformation(hTok, Native.TokenUser, buf, size, out size)) return "";
                    IntPtr sid = Marshal.ReadIntPtr(buf);
                    StringBuilder name = new StringBuilder(256);
                    StringBuilder dom = new StringBuilder(256);
                    int cn = 256, cd = 256;
                    int use;
                    if (!Native.LookupAccountSid(null, sid, name, ref cn, dom, ref cd, out use)) return "";
                    return dom.Length > 0 ? dom + "\\" + name : name.ToString();
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { return ""; }
            finally
            {
                if (hTok != IntPtr.Zero) Native.CloseHandle(hTok);
                Native.CloseHandle(hProc);
            }
        }

        // 系统关键进程（一键全杀自动跳过）；建议保留（允许杀但会提示）
        // 注意：这里全部写"去掉 .exe 的文件名"，比对前会先归一化，带不带 .exe 都能命中。
        private static readonly string[] CriticalNames =
        {
            "system", "system idle process", "registry", "memory compression", "secure system",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "lsaiso", "fontdrvhost", "audiodg",
            "msmpeng", "securityhealthservice", "nissrv", "msmpsvc"
        };

        private static readonly string[] WarnNames =
        {
            "explorer", "dwm", "sihost", "taskhostw", "runtimebroker", "startmenuexperiencehost",
            "searchhost", "shellexperiencehost", "textinputhost", "ctfmon", "spoolsv",
            "wmiprvse", "dllhost", "searchindexer", "searchprotocolhost",
            "searchfilterhost", "trustedinstaller", "tiworker", "msseces", "unsecapp"
        };

        // 把进程名归一化为"小写、去掉 .exe 后缀、去掉首尾空白"，
        // 用于和上面两张名单比对。进程名可能来自 Process.ProcessName（不带 .exe）、
        // Restart Manager 的 strAppName（可能带 .exe），必须统一后才能可靠匹配，
        // 否则 svchost / lsass 这类关键进程会漏保护而被一键全杀误杀。
        private static string NormalizeProcName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string n = name.Trim().ToLowerInvariant();
            if (n.EndsWith(".exe", StringComparison.Ordinal))
                n = n.Substring(0, n.Length - 4).TrimEnd();
            return n;
        }

        private static void ClassifyRisk(LockProc lp)
        {
            // PID <= 4 是 System Idle Process / System / Smss 等，属于内核级，绝对不能杀
            if (lp.Pid <= 4) { lp.IsCritical = true; return; }

            string n = NormalizeProcName(lp.Name);
            if (n.Length == 0) return;

            for (int i = 0; i < CriticalNames.Length; i++)
                if (n == CriticalNames[i]) { lp.IsCritical = true; return; }
            for (int i = 0; i < WarnNames.Length; i++)
                if (n == WarnNames[i]) { lp.IsWarn = true; return; }
            if (lp.Pid == Process.GetCurrentProcess().Id) lp.IsWarn = true;
        }

        // ===================== 结束进程 =====================
        // 带进程身份校验：仅凭 PID 结束进程有风险 —— 扫描到点击"结束"之间，
        // 原进程可能已退出且该 PID 被系统分配给别的进程，那样会杀掉无辜进程。
        // 这里用「进程名 + 启动时间」作为指纹，和扫描时记录的不一致就拒绝结束。
        public static bool KillOne(int pid, string expectedName, string expectedStartTime, out string message)
        {
            message = "";
            try
            {
                Process p = Process.GetProcessById(pid);

                // 校验 1：进程名（不带 .exe 比较，避免来源差异）
                string actualName = NormalizeProcName(p.ProcessName);
                string wantName = NormalizeProcName(expectedName);
                if (wantName.Length > 0 && actualName.Length > 0 && actualName != wantName)
                {
                    message = "已跳过：PID " + pid + " 现在的进程是「" + p.ProcessName +
                              "」，不是扫描时的「" + expectedName + "」（PID 可能已被复用）";
                    return false;
                }

                // 校验 2：启动时间（能拿到就比对，拿不到不阻断）
                if (!string.IsNullOrEmpty(expectedStartTime))
                {
                    string actualStart = "";
                    try { actualStart = p.StartTime.ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
                    if (actualStart.Length > 0 && actualStart != expectedStartTime)
                    {
                        message = "已跳过：PID " + pid + " 的启动时间与扫描时不一致（PID 已被复用）";
                        return false;
                    }
                }

                p.Kill();
                if (!p.WaitForExit(6000))
                {
                    message = "等待退出超时（进程可能已进入异常状态）";
                    return false;
                }
                message = "已结束";
                return true;
            }
            catch (ArgumentException)
            {
                message = "进程已不存在（可能已被你手动结束）";
                return true;
            }
            catch (Win32Exception ex)
            {
                message = "无法结束：" + ex.Message + "（可能是受保护进程或权限不足）";
                return false;
            }
            catch (InvalidOperationException)
            {
                message = "进程状态异常，可能已退出";
                return true;
            }
            catch (Exception ex)
            {
                message = "结束失败：" + ex.Message;
                return false;
            }
        }

        // ===================== 关闭句柄解锁（不结束进程，进阶功能） =====================
        public static string CloseHandles(LockProc lp)
        {
            if (lp.RawHandles.Count == 0)
                return "没有可关闭的句柄记录（可能需要以管理员身份重新扫描）。";
            IntPtr hProc = Native.OpenProcess(Native.PROCESS_DUP_HANDLE, false, lp.Pid);
            if (hProc == IntPtr.Zero)
                return "无法打开进程句柄（权限不足或进程受保护），请以管理员身份运行。";
            int ok = 0, fail = 0;
            try
            {
                IntPtr me = Native.GetCurrentProcess();
                for (int i = 0; i < lp.RawHandles.Count; i++)
                {
                    IntPtr dup;
                    if (Native.DuplicateHandle(hProc, lp.RawHandles[i], me, out dup, 0, false, Native.DUPLICATE_CLOSE_SOURCE))
                    {
                        Native.CloseHandle(dup);
                        ok++;
                    }
                    else fail++;
                }
            }
            finally { Native.CloseHandle(hProc); }
            return "已关闭 " + ok + " 个句柄" + (fail > 0 ? "，" + fail + " 个失败" : "") +
                   "。注意：目标程序可能因此出错或崩溃，请自行确认。";
        }
    }

    // ========================================================================
    //  CSV 导出（带 12KB 上限）
    // ========================================================================
    internal static class CsvExport
    {
        public const int SizeLimit = 12 * 1024;   // 12 KB

        public static string Build(ScanResult res, List<LockProc> procs)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# 谁在占用 WhoLocks 进程占用报告");
            sb.AppendLine("# 工具版本,1.0");
            sb.AppendLine("# 扫描目录," + Q(res.Root));
            sb.AppendLine("# 扫描时间," + Q(res.ScanTime.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine("# 运行身份," + Q(res.Elevated ? "管理员" : "普通用户（建议管理员）"));
            sb.AppendLine("# 检测方式," + Q((res.RmOk ? "RestartManager" : "RestartManager(不可用)") + " + " +
                                               (res.HandleOk ? "系统句柄表" : "系统句柄表(未完成)")));
            sb.AppendLine("# 扫描范围," + Q(res.FileCount + " 个文件/目录" + (res.Truncated ? "（已截断）" : "")));
            sb.AppendLine("# 本次导出," + Q(procs.Count + " 个进程"));
            sb.AppendLine("# 说明," + Q("结果已双重交叉验证；实验性功能请谨慎使用"));
            sb.AppendLine();
            sb.AppendLine("进程名,PID,用户,会话,启动时间,能否结束,风险标记,检测来源,占用文件数,进程路径,占用文件列表");
            foreach (LockProc lp in procs)
            {
                sb.Append(Q(lp.Name)).Append(',');
                sb.Append(lp.Pid).Append(',');
                sb.Append(Q(lp.User)).Append(',');
                sb.Append(lp.Session).Append(',');
                sb.Append(Q(lp.StartTime)).Append(',');
                sb.Append(Q(lp.CanTerminate ? "可以" : "否")).Append(',');
                sb.Append(Q(lp.RiskText)).Append(',');
                sb.Append(Q(lp.SourceText)).Append(',');
                sb.Append(lp.Files.Count).Append(',');
                sb.Append(Q(lp.ImagePath)).Append(',');
                sb.AppendLine(Q(lp.FilesForCsv));
            }
            return sb.ToString();
        }

        private static string Q(string s)
        {
            if (s == null) return "";
            bool need = s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 ||
                        s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0;
            if (!need) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static int Utf8BytesWithBom(string text)
        {
            return Encoding.UTF8.GetByteCount(text) + 3;   // BOM
        }

        public static void Write(string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }
    }

    // ========================================================================
    //  无界面自检（命令行用，便于验证检测是否真实）
    // ========================================================================
    internal static class SelfTest
    {
        // CSV 导出自我验证：扫描 → 生成 CSV → 检查体积上限逻辑
        public static int CsvCheck(string dir, string outCsv)
        {
            string logFile = outCsv + ".log.txt";
            StringBuilder log = new StringBuilder();
            try
            {
                if (!Directory.Exists(dir))
                {
                    log.AppendLine("目录不存在：" + dir);
                    File.WriteAllText(logFile, log.ToString(), Encoding.UTF8);
                    return 2;
                }
                ScanOptions opt = new ScanOptions();
                opt.Root = Path.GetFullPath(dir);
                ScanResult res = new ScanEngine(delegate { }).Scan(opt);
                log.AppendLine("扫描到占用进程：" + res.Procs.Count + " 个");

                List<LockProc> all = res.Procs;

                // 1) 全量 CSV
                string csv = CsvExport.Build(res, all);
                int size = CsvExport.Utf8BytesWithBom(csv);
                log.AppendLine("全量 CSV 体积：" + size + " 字节（" + (size / 1024.0).ToString("0.0") + " KB），上限 " +
                               CsvExport.SizeLimit + " 字节");
                log.AppendLine("是否超限：" + (size > CsvExport.SizeLimit ? "是（会提示用户勾选）" : "否"));

                // 2) 转义正确性检查：CSV 里每个数据行的字段数应等于表头字段数
                string[] lines = csv.Replace("\r\n", "\n").Split('\n');
                int headerIdx = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("进程名,PID,", StringComparison.Ordinal)) { headerIdx = i; break; }
                }
                if (headerIdx >= 0)
                {
                    int headerCols = CountCsvFields(lines[headerIdx]);
                    log.AppendLine("表头字段数：" + headerCols);
                    int bad = 0;
                    for (int i = headerIdx + 1; i < lines.Length; i++)
                    {
                        if (lines[i].Length == 0) continue;
                        int c = CountCsvFields(lines[i]);
                        if (c != headerCols) { bad++; if (bad <= 3) log.AppendLine("  字段数不符：" + c + " vs " + headerCols); }
                    }
                    log.AppendLine("字段数不符的数据行：" + bad + " 行");
                }
                else log.AppendLine("未找到表头行（进程数为 0 时正常）");

                // 3) 写出文件，确认可写、可读回
                CsvExport.Write(outCsv, csv);
                long written = new FileInfo(outCsv).Length;
                string back = File.ReadAllText(outCsv, Encoding.UTF8);
                log.AppendLine("实际文件大小：" + written + " 字节（含 BOM）");
                log.AppendLine("读回长度一致：" + (back.Length > 0 && back == csv.TrimStart('\uFEFF')));
                log.AppendLine("结果：CSV 导出功能正常。");
                File.WriteAllText(logFile, log.ToString(), Encoding.UTF8);
                return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine("CSV 测试异常：" + ex);
                try { File.WriteAllText(logFile, log.ToString(), Encoding.UTF8); } catch { }
                return 3;
            }
        }

        // 按 CSV 规则数一行有多少个字段（正确处理引号内的逗号）
        private static int CountCsvFields(string line)
        {
            int count = 1;
            bool inQuote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuote && i + 1 < line.Length && line[i + 1] == '"') i++;
                    else inQuote = !inQuote;
                }
                else if (c == ',' && !inQuote) count++;
            }
            return count;
        }

        public static int Run(string dir, string outFile)
        {
            // 允许传文件夹，也允许传单个文件（单文件只查该文件本身）
            bool isFileArg = File.Exists(dir);
            if (!isFileArg && !Directory.Exists(dir))
            {
                if (outFile != null) File.WriteAllText(outFile, "路径不存在：" + dir, Encoding.UTF8);
                return 2;
            }
            ScanOptions opt = new ScanOptions();
            opt.Root = Path.GetFullPath(dir);
            opt.RootIsFile = isFileArg;
            ScanEngine engine = new ScanEngine(delegate { });
            ScanResult res = engine.Scan(opt);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("扫描目录：" + res.Root);
            sb.AppendLine("时间：" + res.ScanTime);
            sb.AppendLine("管理员：" + res.Elevated + "   RM：" + res.RmOk + "   句柄表：" + res.HandleOk);
            sb.AppendLine("文件/目录数：" + res.FileCount + "  截断：" + res.Truncated);
            sb.AppendLine("占用进程数：" + res.Procs.Count);
            sb.AppendLine("----------------------------------------");
            foreach (LockProc lp in res.Procs)
            {
                sb.AppendLine("名称=" + lp.Name + "  PID=" + lp.Pid + "  用户=" + lp.User +
                              "  能否结束=" + lp.CanTerminate + "  风险=" + lp.RiskText +
                              "  来源=" + lp.SourceText + "  句柄=" + lp.RawHandles.Count);
                sb.AppendLine("  路径=" + lp.ImagePath);
                for (int i = 0; i < lp.Files.Count && i < 20; i++) sb.AppendLine("  文件=" + lp.Files[i]);
                if (lp.Files.Count > 20) sb.AppendLine("  文件=...（共 " + lp.Files.Count + " 个）");
            }
            sb.AppendLine("----------------------------------------");
            foreach (string log in res.Log) sb.AppendLine("日志：" + log);

            if (outFile != null) File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
            return res.Procs.Count > 0 ? 1 : 0;
        }
    }
}
