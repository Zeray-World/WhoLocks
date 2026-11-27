// ============================================================================
//  WhoLocks 界面部分
//  配色：深海蓝 #0018A8  柔奶白 #F5F1E9
//  设计：紧凑小长方形，选完目录 / 拖入后向下展开结果
// ============================================================================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WhoLocks
{
    // ===================== 配色 =====================
    // 参考「API接口封装」那种干净规整的桌面工具风格：
    // 白底 + 极浅灰页面 + 1px 淡边框卡片 + 深蓝点缀，留白充足，字号层次分明。
    internal static class Theme
    {
        public static readonly Color Blue = Color.FromArgb(0, 24, 168);        // #0018A8 深海蓝（品牌色）
        public static readonly Color BlueLight = Color.FromArgb(38, 74, 205);
        public static readonly Color BlueDark = Color.FromArgb(0, 18, 124);
        public static readonly Color BlueSoft = Color.FromArgb(238, 242, 253);
        public static readonly Color BlueTag = Color.FromArgb(233, 238, 252);

        public static readonly Color Page = Color.FromArgb(245, 246, 248);     // 页面底色（浅灰白）
        public static readonly Color Card = Color.White;                       // 卡片白
        public static readonly Color Border = Color.FromArgb(228, 230, 235);   // 卡片 1px 边框
        public static readonly Color BorderStrong = Color.FromArgb(212, 215, 222);
        public static readonly Color RowAlt = Color.FromArgb(250, 251, 252);   // 表格交替行

        public static readonly Color TextDark = Color.FromArgb(32, 35, 42);
        public static readonly Color TextMid = Color.FromArgb(88, 92, 102);
        public static readonly Color TextGray = Color.FromArgb(138, 142, 152);
        public static readonly Color TextFaint = Color.FromArgb(176, 180, 190);
        public static readonly Color White = Color.White;

        public static readonly Color Warn = Color.FromArgb(180, 108, 20);
        public static readonly Color WarnSoft = Color.FromArgb(255, 248, 235);
        public static readonly Color Danger = Color.FromArgb(200, 48, 48);
        public static readonly Color DangerSoft = Color.FromArgb(255, 242, 242);
        public static readonly Color Ok = Color.FromArgb(24, 140, 88);
        public static readonly Color OkSoft = Color.FromArgb(238, 250, 244);

        public static readonly Font FontTitle = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
        public static readonly Font FontH2 = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        public static readonly Font FontBody = new Font("Microsoft YaHei UI", 9F);
        public static readonly Font FontSmall = new Font("Microsoft YaHei UI", 8.5F);
        public static readonly Font FontTiny = new Font("Microsoft YaHei UI", 8F);
        public static readonly Font FontInput = new Font("Microsoft YaHei UI", 10F);
    }

    // ===================== 圆角按钮 =====================
    internal sealed class FlatButton : Button
    {
        public int Radius = 6;
        public bool Ghost;
        public Color HoverBack = Theme.BlueDark;
        public Color HoverBorder = Theme.BlueDark;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Theme.FontBody;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.Selectable, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool hot = ClientRectangle.Contains(PointToClient(Cursor.Position));

            Color back, fore, border;
            if (Ghost)
            {
                back = !Enabled ? Color.FromArgb(242, 240, 235)
                       : (hot ? Theme.BlueSoft : Theme.Card);
                fore = !Enabled ? Theme.TextFaint : Theme.Blue;
                border = !Enabled ? Theme.Border : (hot ? Theme.Blue : Theme.BorderStrong);
            }
            else
            {
                back = !Enabled ? Color.FromArgb(200, 202, 210) : (hot ? HoverBack : BackColor);
                fore = !Enabled ? Color.FromArgb(130, 132, 140) : Color.White;
                border = back;
            }

            using (GraphicsPath path = Round(r, Radius))
            {
                using (SolidBrush b = new SolidBrush(back)) g.FillPath(b, path);
                if (Ghost)
                    using (Pen p = new Pen(border, 1.2f)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, Font, r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0 || r.Width <= 0 || r.Height <= 0) { path.AddRectangle(r); return path; }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // ===================== 圆角卡片面板 =====================
    internal sealed class CardPanel : Panel
    {
        public int Radius = 10;
        public Color Fill = Theme.Card;
        public Color BorderColor = Theme.Border;
        public bool DrawBorder = true;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 先铺父级底色，避免圆角外露出透明黑
            if (Parent != null)
            {
                using (SolidBrush pb = new SolidBrush(Parent.BackColor))
                    e.Graphics.FillRectangle(pb, ClientRectangle);
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = FlatButton.Round(r, Radius))
            {
                using (SolidBrush b = new SolidBrush(Fill)) e.Graphics.FillPath(b, path);
                if (DrawBorder)
                    using (Pen p = new Pen(BorderColor, 1.2f)) e.Graphics.DrawPath(p, path);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        // ---------- 尺寸 ----------
        // 折叠高度 >= header(62) + body(186) + statusBar(26) = 274
        private const int W = 640;
        private const int H_COLLAPSED = 278;
        private const int H_EXPANDED = 560;

        // ---------- 控件 ----------
        private Panel _header;
        private Label _lblClose, _lblMin;
        private Label _lblTitle, _lblSub;
        private FlatButton _btnElevate;

        private TextBox _txtPath;
        private Label _lblPathHint;
        private FlatButton _btnBrowse, _btnScan, _btnCancel;
        private CheckBox _chkRecursive, _chkDeep;
        private Label _lblHelp;

        private Panel _resultPanel;
        private Label _lblResultHead, _lblResultSub;
        private FlatButton _btnKillAll, _btnKillChecked, _btnCopy, _btnExport, _btnRefresh;
        private DataGridView _grid;
        private Panel _emptyPanel;
        private Label _lblEmpty, _lblEmptySub;

        private Panel _barBg, _barFill;
        private Label _lblStatus;
        private NativeDropTarget _dropTarget;

        // ---------- 状态 ----------
        private ScanEngine _engine;
        private ScanResult _last;
        private bool _scanning;
        private string _pendingScanPath;   // 扫描中又拖入新目录时先记下，扫描结束后自动重扫
        private bool _dragging;
        private Point _dragStart;
        private DateTime _scanStart;
        private readonly List<string> _logLines = new List<string>();
        private System.Windows.Forms.Timer _expandTimer;
        private int _expandTarget;
        private bool _hasResultsShown;

        public MainForm(string[] args)
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Page;
            Size = new Size(W, H_COLLAPSED);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.FontBody;
            DoubleBuffered = true;
            KeyPreview = true;
            Text = "谁在占用";
            Icon = MakeIcon();

            BuildUi();
            SetupDragDrop();

            if (args != null && args.Length > 0 && Directory.Exists(args[0]))
                SetPathAndScan(args[0]);

            Log("就绪。把文件夹拖进窗口，或点「选择文件夹」。");
        }

        /// <summary>
        /// 加载程序图标。
        /// 优先用 exe 内嵌图标（构建时经 /win32icon:src\script.ico 嵌入，
        /// 换图标只需用同名文件覆盖 src\script.ico 再重新构建，无需改代码）；
        /// 其次读磁盘上的 script.ico；最后退回动态绘制，保证任何情况下都有图标。
        /// </summary>
        private static Icon MakeIcon()
        {
            // 1) exe 内嵌图标（最可靠，exe 单独拷走也带着图标）
            try
            {
                Icon embedded = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (embedded != null) return embedded;
            }
            catch { }

            // 2) 磁盘上的 script.ico（开发调试时方便）
            try
            {
                string[] candidates = new string[]
                {
                    Path.Combine(Application.StartupPath, "script.ico"),
                    Path.Combine(Application.StartupPath, "src", "script.ico"),
                    Path.Combine(Application.StartupPath, "..", "src", "script.ico"),
                };
                for (int i = 0; i < candidates.Length; i++)
                {
                    string p = Path.GetFullPath(candidates[i]);
                    if (File.Exists(p)) return new Icon(p);
                }
            }
            catch { }

            // 3) 兜底：动态绘制
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        using (SolidBrush b = new SolidBrush(Theme.Blue))
                            g.FillEllipse(b, 1, 1, 30, 30);
                        using (Pen p = new Pen(Color.White, 2.6f))
                        {
                            g.DrawEllipse(p, 9, 8, 13, 13);
                            g.DrawLine(p, 19, 18, 26, 25);
                        }
                    }
                    return Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch { return null; }
        }

        // ====================================================================
        //  拖放：统一走原生 WM_DROPFILES（不与 OLE 混用）
        //  ------------------------------------------------------------------
        //  之前两种机制同时开导致拖入显示禁止 X：
        //    AllowDrop=true 注册了 OLE IDropTarget → 系统优先走 OLE
        //    → 管理员模式下 explorer 数据被 UIPI 拦住
        //    → DragEnter 判定"没有文件" → e.Effect=None → 光标变 X
        //  现在：整个控件树 AllowDrop=false，只用原生 WM_DROPFILES，
        //  并用 ChangeWindowMessageFilterEx 放行消息，管理员身份下也能拖。
        // ====================================================================
        private void SetupDragDrop()
        {
            // 1) 关闭所有控件的 OLE 拖放，避免抢走原生 WM_DROPFILES
            DragDropHelper.DisableOleDrop(this);

            // 2) 启用原生拖放接收（会自动等窗口句柄创建完成）
            _dropTarget = new NativeDropTarget(this, OnDroppedPath);
            _dropTarget.Attach();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 句柄重建时（如修改窗体样式）重新注销 OLE 目标并确认原生拖放可用
            if (_dropTarget != null)
            {
                try
                {
                    DragDropHelper.DisableOleDrop(this);
                    _dropTarget.DetachOle();
                }
                catch { }
            }
        }

        private void OnDroppedPath(string path)
        {
            string p = DragDropHelper.NormalizePath(path);
            if (p == null)
            {
                Warn("拖入的内容不是有效的文件夹或文件：\n" + path);
                return;
            }
            // 拖入文件夹 → 扫描该文件夹；拖入单个文件 → 只检查这个文件
            // （不展开它所在的目录，否则拖一个桌面文件会扫遍整个桌面）
            SetPathAndScan(p);
        }

        // ====================================================================
        //  界面构建
        // ====================================================================
        private void BuildUi()
        {
            BuildResultPanel();   // Fill
            BuildStatusBar();     // Bottom
            BuildBody();          // Top
            BuildHeader();        // Top（最后加 → 贴最顶）
        }

        // ---- 顶部标题条（白底 + 深蓝标题，参考图那种干净风格） ----
        private void BuildHeader()
        {
            _header = new Panel();
            _header.Dock = DockStyle.Top;
            _header.Height = 62;
            _header.BackColor = Theme.Card;
            _header.Paint += HeaderPaint;
            Controls.Add(_header);
            _header.MouseDown += HeaderDown;
            _header.MouseMove += HeaderMove;
            _header.MouseUp += delegate { _dragging = false; };

            _lblTitle = new Label();
            _lblTitle.Text = "谁在占用";
            _lblTitle.ForeColor = Theme.Blue;
            _lblTitle.Font = Theme.FontTitle;
            _lblTitle.AutoSize = false;
            _lblTitle.Size = new Size(200, 26);
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.BackColor = Color.Transparent;
            _lblTitle.Location = new Point(22, 12);
            AttachHeaderDrag(_lblTitle);
            _header.Controls.Add(_lblTitle);

            _lblSub = new Label();
            _lblSub.Text = "揪出到底是谁占用了你的文件夹";
            _lblSub.ForeColor = Theme.TextGray;
            _lblSub.Font = Theme.FontTiny;
            _lblSub.AutoSize = false;
            _lblSub.Size = new Size(300, 18);
            _lblSub.TextAlign = ContentAlignment.MiddleLeft;
            _lblSub.BackColor = Color.Transparent;
            _lblSub.Location = new Point(23, 33);
            AttachHeaderDrag(_lblSub);
            _header.Controls.Add(_lblSub);

            // 提权按钮（常驻，醒目标识当前权限状态）
            _btnElevate = new FlatButton();
            _btnElevate.Size = new Size(104, 26);
            _btnElevate.Location = new Point(W - 224, 18);
            _btnElevate.Radius = 4;
            _btnElevate.Font = Theme.FontSmall;
            _btnElevate.Click += delegate { ElevateNow(); };
            _header.Controls.Add(_btnElevate);
            UpdateElevateButton();

            _lblMin = MakeHeaderBtn("—", W - 92);
            _lblMin.Click += delegate { WindowState = FormWindowState.Minimized; };

            _lblClose = MakeHeaderBtn("✕", W - 58);
            _lblClose.Click += delegate { Close(); };
            _lblClose.MouseEnter += delegate { _lblClose.ForeColor = Theme.Danger; };
            _lblClose.MouseLeave += delegate { _lblClose.ForeColor = Theme.TextGray; };
        }

        private void HeaderPaint(object sender, PaintEventArgs e)
        {
            if (_header == null || _header.Width <= 0) return;
            // 顶部一条 3px 深蓝装饰线（参考图的品牌色点缀）
            using (SolidBrush b = new SolidBrush(Theme.Blue))
                e.Graphics.FillRectangle(b, 0, 0, _header.Width, 3);
            // 底部 1px 分隔线
            using (Pen p = new Pen(Theme.Border, 1))
                e.Graphics.DrawLine(p, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        }

        private void AttachHeaderDrag(Control c)
        {
            c.MouseDown += HeaderDown;
            c.MouseMove += HeaderMove;
            c.MouseUp += delegate { _dragging = false; };
        }

        private Label MakeHeaderBtn(string text, int x)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Theme.TextGray;
            l.Font = new Font("Microsoft YaHei UI", 10F);
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Size = new Size(30, 26);
            l.Location = new Point(x, 18);
            l.BackColor = Color.Transparent;
            l.Cursor = Cursors.Hand;
            _header.Controls.Add(l);
            return l;
        }

        private void HeaderDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragStart = new Point(e.X, e.Y);
        }

        private void HeaderMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point screen = ((Control)sender).PointToScreen(e.Location);
            Location = new Point(screen.X - _dragStart.X, screen.Y - _dragStart.Y);
        }

        // ---- 主体 ----
        private void BuildBody()
        {
            Panel body = new Panel();
            body.Dock = DockStyle.Top;
            body.Height = 186;
            body.BackColor = Theme.Page;
            Controls.Add(body);

            // ===== 卡片一：路径选择 =====
            CardPanel card1 = new CardPanel();
            card1.Radius = 8;
            card1.Fill = Theme.Card;
            card1.BorderColor = Theme.Border;
            card1.Location = new Point(18, 16);
            card1.Size = new Size(W - 36, 92);
            body.Controls.Add(card1);

            Label lblCardTitle = new Label();
            lblCardTitle.Text = "要检查的文件夹";
            lblCardTitle.ForeColor = Theme.TextMid;
            lblCardTitle.Font = Theme.FontSmall;
            lblCardTitle.AutoSize = true;
            lblCardTitle.BackColor = Color.Transparent;
            lblCardTitle.Location = new Point(16, 12);
            card1.Controls.Add(lblCardTitle);

            // 路径输入框（带 1px 边框的浅色容器）
            Panel inputWrap = new Panel();
            inputWrap.Location = new Point(16, 34);
            inputWrap.Size = new Size(W - 36 - 32 - 76, 32);
            inputWrap.BackColor = Theme.White;
            inputWrap.Paint += delegate (object s, PaintEventArgs e)
            {
                DrawRoundBorder(e.Graphics, inputWrap.ClientRectangle, Theme.BorderStrong, 5);
            };
            card1.Controls.Add(inputWrap);

            _txtPath = new TextBox();
            _txtPath.BorderStyle = BorderStyle.None;
            _txtPath.BackColor = Theme.White;
            _txtPath.ForeColor = Theme.TextDark;
            _txtPath.Font = Theme.FontBody;
            _txtPath.Location = new Point(9, 8);
            _txtPath.Width = inputWrap.Width - 18;
            _txtPath.TextChanged += delegate { _lblPathHint.Visible = _txtPath.Text.Length == 0; };
            _txtPath.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (!_scanning) BeginScan(); }
            };
            inputWrap.Controls.Add(_txtPath);

            _lblPathHint = new Label();
            _lblPathHint.Text = "把文件夹拖到窗口任意位置，或点右侧「选择」";
            _lblPathHint.ForeColor = Theme.TextFaint;
            _lblPathHint.Font = Theme.FontSmall;
            _lblPathHint.AutoSize = true;
            _lblPathHint.BackColor = Color.Transparent;
            _lblPathHint.Location = new Point(10, 9);
            _lblPathHint.Cursor = Cursors.IBeam;
            _lblPathHint.Click += delegate { BrowseFolder(); };
            inputWrap.Controls.Add(_lblPathHint);
            _lblPathHint.BringToFront();

            _btnBrowse = new FlatButton();
            _btnBrowse.Ghost = true;
            _btnBrowse.Text = "选择";
            _btnBrowse.Size = new Size(68, 32);
            _btnBrowse.Radius = 5;
            _btnBrowse.Location = new Point(card1.Width - 84, 34);
            _btnBrowse.Click += delegate { BrowseFolder(); };
            card1.Controls.Add(_btnBrowse);

            // 提示行
            Label lblDrop = new Label();
            lblDrop.Text = "支持拖入文件夹（扫整个文件夹）或单个文件（只查这个文件）";
            lblDrop.ForeColor = Theme.TextFaint;
            lblDrop.Font = Theme.FontTiny;
            lblDrop.AutoSize = true;
            lblDrop.BackColor = Color.Transparent;
            lblDrop.Location = new Point(17, 70);
            card1.Controls.Add(lblDrop);

            // ===== 扫描按钮 =====
            _btnScan = new FlatButton();
            _btnScan.Text = "开始扫描";
            _btnScan.Size = new Size(W - 36, 36);
            _btnScan.Radius = 6;
            _btnScan.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _btnScan.BackColor = Theme.Blue;
            _btnScan.HoverBack = Theme.BlueLight;
            _btnScan.Location = new Point(18, 116);
            _btnScan.Click += delegate { if (!_scanning) BeginScan(); };
            body.Controls.Add(_btnScan);

            _btnCancel = new FlatButton();
            _btnCancel.Text = "取消扫描";
            _btnCancel.Ghost = true;
            _btnCancel.Size = new Size(W - 36, 36);
            _btnCancel.Radius = 6;
            _btnCancel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _btnCancel.Location = new Point(18, 116);
            _btnCancel.Visible = false;
            _btnCancel.Click += delegate { CancelScan(); };
            body.Controls.Add(_btnCancel);

            // ===== 选项行 =====
            _chkRecursive = new CheckBox();
            _chkRecursive.Text = "包含子文件夹";
            _chkRecursive.AutoSize = true;
            _chkRecursive.Checked = true;
            _chkRecursive.ForeColor = Theme.TextMid;
            _chkRecursive.Font = Theme.FontSmall;
            _chkRecursive.Location = new Point(21, 160);
            body.Controls.Add(_chkRecursive);

            _chkDeep = new CheckBox();
            _chkDeep.Text = "深度检测（更准）";
            _chkDeep.AutoSize = true;
            _chkDeep.Checked = true;
            _chkDeep.ForeColor = Theme.TextMid;
            _chkDeep.Font = Theme.FontSmall;
            _chkDeep.Location = new Point(148, 160);
            body.Controls.Add(_chkDeep);

            _lblHelp = new Label();
            _lblHelp.Text = "?";
            _lblHelp.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            _lblHelp.ForeColor = Theme.TextGray;
            _lblHelp.BackColor = Color.Transparent;
            _lblHelp.AutoSize = false;
            _lblHelp.Size = new Size(16, 16);
            _lblHelp.TextAlign = ContentAlignment.MiddleCenter;
            _lblHelp.Location = new Point(280, 160);
            _lblHelp.Cursor = Cursors.Help;
            _lblHelp.Click += delegate { ShowHelp(); };
            body.Controls.Add(_lblHelp);

            // 状态文字（右侧对齐）
            _lblStatus = new Label();
            _lblStatus.Text = "就绪";
            _lblStatus.ForeColor = Theme.TextGray;
            _lblStatus.Font = Theme.FontTiny;
            _lblStatus.AutoSize = false;
            _lblStatus.TextAlign = ContentAlignment.MiddleRight;
            _lblStatus.Size = new Size(220, 18);
            _lblStatus.Location = new Point(W - 36 - 220, 158);
            body.Controls.Add(_lblStatus);

            _barBg = new Panel();
            _barBg.Location = new Point(18, 180);
            _barBg.Size = new Size(W - 36, 2);
            _barBg.BackColor = Theme.Border;
            body.Controls.Add(_barBg);

            _barFill = new Panel();
            _barFill.Location = new Point(0, 0);
            _barFill.Size = new Size(0, 2);
            _barFill.BackColor = Theme.Blue;
            _barBg.Controls.Add(_barFill);
        }

        // 画一个 1px 圆角矩形边框
        private static void DrawRoundBorder(Graphics g, Rectangle r, Color color, int radius)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rr = new Rectangle(0, 0, r.Width - 1, r.Height - 1);
            using (GraphicsPath p = FlatButton.Round(rr, radius))
            using (Pen pen = new Pen(color, 1f))
                g.DrawPath(pen, p);
        }

        // ---- 结果区 ----
        private void BuildResultPanel()
        {
            _resultPanel = new Panel();
            _resultPanel.Dock = DockStyle.Fill;
            _resultPanel.BackColor = Theme.Page;
            _resultPanel.Visible = false;
            Controls.Add(_resultPanel);

            // Dock 顺序在方法末尾统一修正（见 SetChildIndex 部分），
            // 这里只需创建控件，不用关心添加早晚。
            Panel line = new Panel();
            line.Dock = DockStyle.Top;
            line.Height = 1;
            line.BackColor = Theme.Border;

            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 52;
            bar.BackColor = Theme.Page;

            _lblResultHead = new Label();
            _lblResultHead.Text = "检测结果";
            _lblResultHead.ForeColor = Theme.Blue;
            _lblResultHead.Font = Theme.FontH2;
            _lblResultHead.AutoSize = true;
            _lblResultHead.Location = new Point(20, 9);
            bar.Controls.Add(_lblResultHead);

            _lblResultSub = new Label();
            _lblResultSub.Text = "";
            _lblResultSub.ForeColor = Theme.TextGray;
            _lblResultSub.Font = Theme.FontTiny;
            _lblResultSub.AutoSize = true;
            _lblResultSub.Location = new Point(21, 29);
            bar.Controls.Add(_lblResultSub);

            // 按钮从右往左排，保证不会被窗口右边缘截断
            int rightEdge = W - 20;
            _btnRefresh = new FlatButton();
            _btnRefresh.Text = "重扫";
            _btnRefresh.Size = new Size(48, 28);
            _btnRefresh.Location = new Point(rightEdge - 48, 11);
            _btnRefresh.Font = Theme.FontSmall;
            _btnRefresh.Radius = 5;
            _btnRefresh.Ghost = true;
            _btnRefresh.BackColor = Theme.Card;
            _btnRefresh.HoverBack = Theme.BlueSoft;
            _btnRefresh.Click += delegate { if (!_scanning) BeginScan(); };
            bar.Controls.Add(_btnRefresh);

            _btnExport = new FlatButton();
            _btnExport.Text = "导出CSV";
            _btnExport.Size = new Size(68, 28);
            _btnExport.Location = new Point(_btnRefresh.Left - 6 - 68, 11);
            _btnExport.Font = Theme.FontSmall;
            _btnExport.Radius = 5;
            _btnExport.Ghost = true;
            _btnExport.BackColor = Theme.Card;
            _btnExport.HoverBack = Theme.BlueSoft;
            _btnExport.Click += delegate { ExportCsv(false); };
            bar.Controls.Add(_btnExport);

            _btnCopy = new FlatButton();
            _btnCopy.Text = "复制信息";
            _btnCopy.Size = new Size(68, 28);
            _btnCopy.Location = new Point(_btnExport.Left - 6 - 68, 11);
            _btnCopy.Font = Theme.FontSmall;
            _btnCopy.Radius = 5;
            _btnCopy.Ghost = true;
            _btnCopy.BackColor = Theme.Card;
            _btnCopy.HoverBack = Theme.BlueSoft;
            _btnCopy.Click += delegate { CopyForTaskManager(); };
            bar.Controls.Add(_btnCopy);

            _btnKillChecked = new FlatButton();
            _btnKillChecked.Text = "结束勾选";
            _btnKillChecked.Size = new Size(70, 28);
            _btnKillChecked.Location = new Point(_btnCopy.Left - 6 - 70, 11);
            _btnKillChecked.Font = Theme.FontSmall;
            _btnKillChecked.Radius = 5;
            _btnKillChecked.BackColor = Theme.Blue;
            _btnKillChecked.HoverBack = Theme.BlueLight;
            _btnKillChecked.Click += delegate { KillChecked(); };
            bar.Controls.Add(_btnKillChecked);

            _btnKillAll = new FlatButton();
            _btnKillAll.Text = "一键全杀";
            _btnKillAll.Size = new Size(72, 28);
            _btnKillAll.Location = new Point(_btnKillChecked.Left - 6 - 72, 11);
            _btnKillAll.Font = Theme.FontSmall;
            _btnKillAll.Radius = 5;
            _btnKillAll.BackColor = Theme.Danger;
            _btnKillAll.HoverBack = Color.FromArgb(168, 36, 36);
            _btnKillAll.Click += delegate { KillAll(); };
            bar.Controls.Add(_btnKillAll);

            // 表格
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersWidth = 26;
            _grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _grid.RowHeadersDefaultCellStyle.BackColor = Theme.Page;
            _grid.RowHeadersDefaultCellStyle.SelectionBackColor = Theme.BlueSoft;
            _grid.BackgroundColor = Theme.Card;
            _grid.BorderStyle = BorderStyle.None;
            _grid.GridColor = Color.FromArgb(240, 236, 228);
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _grid.ColumnHeadersHeight = 32;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.BlueSoft;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Blue;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.BlueSoft;
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Blue;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
            _grid.DefaultCellStyle.BackColor = Theme.Card;
            _grid.DefaultCellStyle.ForeColor = Theme.TextDark;
            _grid.DefaultCellStyle.SelectionBackColor = Theme.BlueSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Theme.Blue;
            _grid.DefaultCellStyle.Font = Theme.FontSmall;
            _grid.DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 248, 243);
            _grid.RowTemplate.Height = 30;
            _grid.Columns.AddRange(new DataGridViewColumn[]
            {
                ChkCol("chk", "☑", 32),
                TxtCol("name", "进程名", 138),
                TxtCol("pid", "PID", 54),
                TxtCol("user", "用户", 104),
                TxtCol("files", "占用文件", 190),
                TxtCol("state", "状态", 74)
            });
            _grid.Columns["chk"].Frozen = true;
            _grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn)
                    _grid.Rows[e.RowIndex].Cells[0].Value =
                        !Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells[0].Value ?? false);
            };
            BuildContextMenu();

            // 空状态
            _emptyPanel = new Panel();
            _emptyPanel.Dock = DockStyle.None;     // 不参与 Dock 布局，避免盖住表格
            _emptyPanel.BackColor = Theme.Card;
            _emptyPanel.Visible = false;

            Label ico = new Label();
            ico.Text = "✓";
            ico.Font = new Font("Microsoft YaHei UI", 30F, FontStyle.Bold);
            ico.ForeColor = Color.FromArgb(196, 218, 200);
            ico.AutoSize = false;
            ico.Dock = DockStyle.Top;
            ico.Height = 74;
            ico.TextAlign = ContentAlignment.BottomCenter;
            _emptyPanel.Controls.Add(ico);

            _lblEmpty = new Label();
            _lblEmpty.Text = "没有检测到占用进程";
            _lblEmpty.ForeColor = Theme.Ok;
            _lblEmpty.Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold);
            _lblEmpty.AutoSize = false;
            _lblEmpty.Dock = DockStyle.Top;
            _lblEmpty.Height = 34;
            _lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
            _emptyPanel.Controls.Add(_lblEmpty);

            _lblEmptySub = new Label();
            _lblEmptySub.ForeColor = Theme.TextGray;
            _lblEmptySub.Font = Theme.FontSmall;
            _lblEmptySub.AutoSize = false;
            _lblEmptySub.Dock = DockStyle.Fill;
            _lblEmptySub.TextAlign = ContentAlignment.TopCenter;
            _lblEmptySub.Padding = new Padding(36, 6, 36, 0);
            _lblEmptySub.Text = "可能已经自己释放了，可以再试一次复制操作确认。\n" +
                                "也可能是内核驱动级占用（杀软驱动等），这类无法精确定位。";
            _emptyPanel.Controls.Add(_lblEmptySub);

            // ---- 控件加入顺序决定 Dock 结果（关键）----
            // WinForms 按 Controls 集合【逆序】处理 Dock：最后加入的最先占位。
            // 因此 Fill 控件必须【最先】加入（最后处理，才能拿到剩余空间），
            // Top 控件必须【最后】加入。写反就会出现"表格占满全区域、表头被操作条盖住"。
            _resultPanel.Controls.Add(_grid);       // Fill：表格（最后处理，占剩余）
            _resultPanel.Controls.Add(_emptyPanel); // Fill：空状态（与表格同区域，靠 Visible 切换）
            _resultPanel.Controls.Add(line);        // Top：分隔线
            _resultPanel.Controls.Add(bar);         // Top：操作条（最后加入 → 最贴顶）
            _emptyPanel.BringToFront();
        }

        private DataGridViewCheckBoxColumn ChkCol(string name, string header, int w)
        {
            DataGridViewCheckBoxColumn c = new DataGridViewCheckBoxColumn();
            c.Name = name; c.HeaderText = header; c.Width = w;
            c.Resizable = DataGridViewTriState.False;
            c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            return c;
        }

        private DataGridViewTextBoxColumn TxtCol(string name, string header, int w)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name; c.HeaderText = header; c.Width = w;
            c.ReadOnly = true; c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return c;
        }

        private FlatButton SmallBtn(Panel parent, string text, ref int x, int w, bool solid)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.Size = new Size(w, 28);
            b.Location = new Point(x, 11);
            b.Font = Theme.FontSmall;
            b.Radius = 6;
            b.Ghost = !solid;
            b.BackColor = solid ? Theme.Blue : Theme.Card;
            b.HoverBack = solid ? Theme.BlueLight : Theme.BlueSoft;
            parent.Controls.Add(b);
            x += w + 6;
            return b;
        }

        private void BuildStatusBar()
        {
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 26;
            bottom.BackColor = Theme.Page;
            bottom.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border, 1))
                    e.Graphics.DrawLine(p, 0, 0, bottom.Width, 0);
            };
            Controls.Add(bottom);

            Label tip = new Label();
            tip.Dock = DockStyle.Fill;
            tip.TextAlign = ContentAlignment.MiddleLeft;
            tip.Padding = new Padding(20, 0, 0, 0);
            tip.ForeColor = Theme.TextFaint;
            tip.Font = Theme.FontTiny;
            tip.Text = "Restart Manager + 全系统句柄表 双重检测   ·   勾选 ☑ 指定要处理的进程   ·   右键行可查看更多操作";
            bottom.Controls.Add(tip);
        }

        private void BuildContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Theme.Card;
            menu.Font = Theme.FontBody;
            menu.RenderMode = ToolStripRenderMode.System;
            menu.Items.Add("结束这个进程", null, delegate { KillSelectedRows(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("复制 PID（自己去任务管理器杀）", null, delegate { CopySelected("pid"); });
            menu.Items.Add("复制进程完整路径", null, delegate { CopySelected("path"); });
            menu.Items.Add("复制它占用的文件列表", null, delegate { CopySelected("files"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("定位到程序所在文件夹", null, delegate { OpenSelectedFileLocation(); });
            menu.Items.Add("打开任务管理器", null, delegate { OpenTaskManager(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("关闭句柄解锁（不结束进程，进阶）", null, delegate { CloseHandlesSelected(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("勾选全部", null, delegate { CheckAll(true); });
            menu.Items.Add("取消全部勾选", null, delegate { CheckAll(false); });
            _grid.ContextMenuStrip = menu;
        }

        // ====================================================================
        //  提权
        // ====================================================================
        private void UpdateElevateButton()
        {
            if (_btnElevate == null) return;
            if (Native.IsElevated())
            {
                _btnElevate.Text = "✓ 已是管理员";
                _btnElevate.Ghost = true;
                _btnElevate.BackColor = Theme.OkSoft;
                _btnElevate.HoverBack = Theme.OkSoft;
                _btnElevate.Cursor = Cursors.Default;
            }
            else
            {
                _btnElevate.Text = "⚡ 立即提权";
                _btnElevate.Ghost = true;
                _btnElevate.BackColor = Theme.BlueSoft;
                _btnElevate.HoverBack = Theme.White;
                _btnElevate.Cursor = Cursors.Hand;
            }
        }

        private void ElevateNow()
        {
            if (Native.IsElevated())
            {
                Info("当前已经是管理员权限，检测范围已经是最完整的。");
                return;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                string p = (_txtPath.Text ?? "").Trim().Trim('"');
                if (p.Length > 0 && Directory.Exists(p))
                    psi.Arguments = "\"" + p + "\"";
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Close();
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)   // 用户取消了 UAC
                    Info("你取消了提权。\n\n不提权也能用，但跨用户的进程和部分系统进程无法查出。");
                else
                    Warn("提权失败：" + ex.Message + "\n\n可以右键 exe →「以管理员身份运行」。");
            }
            catch (Exception ex)
            {
                Warn("提权失败：" + ex.Message);
            }
        }

        // ====================================================================
        //  展开 / 收起
        // ====================================================================
        private void ExpandWindow(bool expand)
        {
            _expandTarget = expand ? H_EXPANDED : H_COLLAPSED;
            if (_expandTimer == null)
            {
                _expandTimer = new System.Windows.Forms.Timer();
                _expandTimer.Interval = 12;
                _expandTimer.Tick += delegate
                {
                    int diff = _expandTarget - Height;
                    if (Math.Abs(diff) <= 6)
                    {
                        Height = _expandTarget;
                        _expandTimer.Stop();
                        return;
                    }
                    Height += diff / 3;
                };
            }
            _expandTimer.Start();
        }

        private void ShowEmptyState(bool empty)
        {
            if (_emptyPanel == null) return;
            if (empty)
            {
                // 手动铺满结果区剩余空间（该面板不参与 Dock，避免盖住表格表头）
                _emptyPanel.Bounds = _grid.Bounds;
                _emptyPanel.Visible = true;
                _emptyPanel.BringToFront();
            }
            else
            {
                _emptyPanel.Visible = false;
            }
            _grid.Visible = !empty;
            _btnKillAll.Enabled = !empty;
            _btnKillChecked.Enabled = !empty;
            _btnCopy.Enabled = !empty;
            _btnExport.Enabled = !empty;
        }

        // ====================================================================
        //  扫描
        // ====================================================================
        private void BrowseFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择要检查占用的文件夹";
                dlg.ShowNewFolderButton = false;
                if (Directory.Exists(_txtPath.Text)) dlg.SelectedPath = _txtPath.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) SetPathAndScan(dlg.SelectedPath);
            }
        }

        /// <summary>设定路径并立刻开始扫描（拖入 / 选择 / 命令行都用这条路径）。</summary>
        private void SetPathAndScan(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _txtPath.Text = path;
            _lblPathHint.Visible = false;

            // 正在扫描时又拖入新目录：先取消当前扫描，等它结束后再自动重扫，
            // 否则新路径只是显示出来、结果还是旧目录的，用户会以为没反应。
            if (_scanning)
            {
                _pendingScanPath = path;
                CancelScan();
                _lblStatus.Text = "已切换目录，正在切换扫描……";
                return;
            }
            BeginScan();
        }

        private void BeginScan()
        {
            string path = (_txtPath.Text ?? "").Trim().Trim('"');
            if (path.Length == 0)
            {
                Info("请先选择一个文件夹或文件。\n\n可以把文件夹/文件直接拖进窗口，或点「选择」。");
                return;
            }

            bool isFile = File.Exists(path);
            bool isDir = Directory.Exists(path);
            if (!isFile && !isDir)
            {
                Warn("路径不存在：\n" + path);
                return;
            }
            if (_scanning) return;

            ScanOptions opt = new ScanOptions();
            opt.Root = Path.GetFullPath(path);
            opt.RootIsFile = isFile;                   // 单文件只查这个文件本身
            opt.TopOnly = !_chkRecursive.Checked;      // 勾选"包含子文件夹"则不只看顶层
            opt.IncludeSections = _chkDeep.Checked;
            _txtPath.Text = opt.Root;

            _grid.Rows.Clear();
            _last = null;
            _logLines.Clear();
            _scanning = true;
            _scanStart = DateTime.Now;
            _barFill.Width = 0;
            _btnScan.Visible = false;
            _btnCancel.Visible = true;
            _btnCancel.Enabled = true;
            _btnKillAll.Enabled = false;
            _btnKillChecked.Enabled = false;
            _btnCopy.Enabled = false;
            _btnExport.Enabled = false;
            _lblStatus.Text = "正在扫描……";
            _lblStatus.ForeColor = Theme.Blue;
            Log("开始扫描：" + opt.Root);

            _engine = new ScanEngine(OnProgress);
            Thread t = new Thread(delegate ()
            {
                ScanResult res;
                try { res = _engine.Scan(opt); }
                catch (Exception ex)
                {
                    try { BeginInvoke(new Action(delegate { FinishScan(null, "扫描出错：" + ex.Message); })); }
                    catch { }
                    return;
                }
                try { BeginInvoke(new Action(delegate { FinishScan(res, null); })); }
                catch { }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void OnProgress(int pct, string text)
        {
            try
            {
                if (IsDisposed) return;
                BeginInvoke(new Action(delegate
                {
                    if (IsDisposed) return;
                    if (pct < 0) pct = 0;
                    if (pct > 100) pct = 100;
                    _barFill.Width = (int)(_barBg.Width * pct / 100.0);
                    _lblStatus.Text = text;
                    _lblStatus.ForeColor = Theme.Blue;
                }));
            }
            catch { }
        }

        private void CancelScan()
        {
            if (_engine != null) _engine.Cancel();
            _lblStatus.Text = "正在取消……";
            _btnCancel.Enabled = false;
        }

        private void FinishScan(ScanResult res, string error)
        {
            _scanning = false;
            _btnScan.Visible = true;
            _btnCancel.Visible = false;

            // 扫描期间用户又拖入了别的目录 → 丢弃本次结果，立刻对新目录重扫
            if (_pendingScanPath != null)
            {
                string next = _pendingScanPath;
                _pendingScanPath = null;
                _txtPath.Text = next;
                BeginScan();
                return;
            }

            if (error != null)
            {
                _barFill.Width = 0;
                _lblStatus.Text = "扫描失败";
                _lblStatus.ForeColor = Theme.Danger;
                Log(error);
                Warn(error);
                return;
            }
            if (res == null) return;

            _last = res;
            _barFill.Width = _barBg.Width;
            foreach (string line in res.Log) Log(line);
            foreach (LockProc lp in res.Procs) AddRow(lp);

            double sec = (DateTime.Now - _scanStart).TotalSeconds;
            bool found = res.Procs.Count > 0;

            if (found)
            {
                _lblResultHead.Text = "找到 " + res.Procs.Count + " 个占用进程";
                _lblResultSub.Text = (res.RootIsFile ? "检查单个文件" : "扫描 " + res.FileCount + " 项") +
                                     " · 用时 " + sec.ToString("0.0") + " 秒 · " +
                                     (res.Elevated ? "管理员权限" : "普通权限");
                _lblStatus.Text = "找到 " + res.Procs.Count + " 个占用进程";
                _lblStatus.ForeColor = Theme.Blue;
            }
            else
            {
                _lblResultHead.Text = "没有检测到占用进程";
                _lblResultSub.Text = (res.RootIsFile ? "检查单个文件" : "扫描 " + res.FileCount + " 项") +
                                     " · 用时 " + sec.ToString("0.0") + " 秒 · " +
                                     (res.Elevated ? "管理员权限" : "普通权限");
                _lblStatus.Text = res.Cancelled ? "已取消" : "没有检测到占用进程";
                _lblStatus.ForeColor = Theme.TextGray;
            }

            ShowEmptyState(!found);
            if (!_hasResultsShown)
            {
                _hasResultsShown = true;
                _resultPanel.Visible = true;
                ExpandWindow(true);
            }
            Log("扫描完成：" + res.Procs.Count + " 个进程，用时 " + sec.ToString("0.0") + " 秒。");
        }

        private void AddRow(LockProc lp)
        {
            int i = _grid.Rows.Add();
            DataGridViewRow r = _grid.Rows[i];
            r.Cells["chk"].Value = false;
            r.Cells["name"].Value = lp.Name;
            r.Cells["pid"].Value = lp.Pid;
            r.Cells["user"].Value = ShortUser(lp.User);
            r.Cells["files"].Value = lp.Files.Count > 1
                ? Path.GetFileName(lp.SampleFile) + "  (+" + (lp.Files.Count - 1) + ")"
                : (lp.Files.Count == 1 ? Path.GetFileName(lp.SampleFile) : "");
            r.Tag = lp;
            r.Cells["files"].ToolTipText = lp.FilesForCsv;
            r.Cells["name"].ToolTipText = lp.Name + "\n" + lp.ImagePath;
            r.Cells["user"].ToolTipText = lp.User;

            if (lp.IsCritical)
            {
                r.Cells["state"].Value = "禁止杀";
                r.Cells["state"].Style.ForeColor = Theme.Danger;
                r.Cells["state"].Style.BackColor = Theme.DangerSoft;
                r.Cells["state"].ToolTipText = "系统关键进程，一键全杀会自动跳过";
                r.Cells["name"].Style.ForeColor = Theme.Danger;
            }
            else if (lp.IsWarn)
            {
                r.Cells["state"].Value = "谨慎";
                r.Cells["state"].Style.ForeColor = Theme.Warn;
                r.Cells["state"].Style.BackColor = Theme.WarnSoft;
                r.Cells["state"].ToolTipText = "结束它可能影响桌面 / 杀毒 / 输入法";
                r.Cells["name"].Style.ForeColor = Theme.Warn;
            }
            else if (!lp.CanTerminate)
            {
                r.Cells["state"].Value = "无权限";
                r.Cells["state"].Style.ForeColor = Theme.TextGray;
                r.Cells["state"].ToolTipText = "权限不足：点右上角「立即提权」后重试";
            }
            else
            {
                r.Cells["state"].Value = "可结束";
                r.Cells["state"].Style.ForeColor = Theme.Ok;
            }
        }

        private static string ShortUser(string user)
        {
            if (string.IsNullOrEmpty(user)) return "";
            int i = user.LastIndexOf('\\');
            return i >= 0 ? user.Substring(i + 1) : user;
        }

        // ====================================================================
        //  结束进程
        // ====================================================================
        private List<LockProc> RowsToProcs(bool onlyChecked)
        {
            List<LockProc> list = new List<LockProc>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                LockProc lp = r.Tag as LockProc;
                if (lp == null || lp.Killed) continue;
                if (onlyChecked && !Convert.ToBoolean(r.Cells["chk"].Value ?? false)) continue;
                list.Add(lp);
            }
            return list;
        }

        private List<LockProc> SelectedProcs()
        {
            List<LockProc> list = new List<LockProc>();
            foreach (DataGridViewRow r in _grid.SelectedRows)
            {
                LockProc lp = r.Tag as LockProc;
                if (lp != null) list.Add(lp);
            }
            return list;
        }

        private void KillAll() { SplitKillable(RowsToProcs(false), true); }

        private void KillChecked()
        {
            List<LockProc> list = RowsToProcs(true);
            if (list.Count == 0)
            {
                Info("你还没有勾选任何进程。\n\n勾选表格最左边的「☑」列，或右键 →「勾选全部」。");
                return;
            }
            SplitKillable(list, false);
        }

        private void KillSelectedRows()
        {
            List<LockProc> list = SelectedProcs();
            if (list.Count == 0) { Info("请先选中要结束的进程行。"); return; }
            SplitKillable(list, false);
        }

        private void SplitKillable(List<LockProc> list, bool isKillAll)
        {
            if (list.Count == 0) { Info("当前没有可结束的进程。"); return; }

            List<LockProc> killable = new List<LockProc>();
            List<LockProc> skipped = new List<LockProc>();
            foreach (LockProc lp in list)
            {
                if (lp.IsCritical) skipped.Add(lp);
                else killable.Add(lp);
            }
            if (killable.Count == 0)
            {
                Info("选中的都是系统关键进程，已拒绝操作（防止系统崩溃）。");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("即将结束 " + killable.Count + " 个进程：");
            sb.AppendLine();
            int show = 0;
            foreach (LockProc lp in killable)
            {
                if (show++ >= 18) { sb.AppendLine("  …… 还有 " + (killable.Count - 18) + " 个"); break; }
                sb.AppendLine("  " + lp.Name + "   (PID " + lp.Pid + ")   占用 " + lp.Files.Count + " 个文件");
            }
            if (skipped.Count > 0)
                sb.AppendLine("\n已自动跳过 " + skipped.Count + " 个系统关键进程。");
            sb.AppendLine("\n⚠ 被结束的程序未保存的数据会丢失，确定继续吗？");

            if (MessageBox.Show(this, sb.ToString(), "确认结束进程",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK) return;

            ExecuteKill(killable);
        }

        private void ExecuteKill(List<LockProc> list)
        {
            int ok = 0, fail = 0;
            StringBuilder errs = new StringBuilder();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (LockProc lp in list)
                {
                    string msg;
                    bool success = ScanEngine.KillOne(lp.Pid, lp.Name, lp.StartTime, out msg);
                    lp.Killed = success;
                    UpdateRowState(lp, success, msg);
                    if (success) ok++;
                    else
                    {
                        fail++;
                        if (errs.Length < 3000) errs.AppendLine("  " + lp.Name + " (PID " + lp.Pid + ")：" + msg);
                    }
                    Log("结束 " + lp.Name + " (PID " + lp.Pid + ")：" + msg);
                    Application.DoEvents();
                }
            }
            finally { Cursor = Cursors.Default; }

            _lblStatus.Text = "已结束 " + ok + " 个进程" + (fail > 0 ? "，失败 " + fail + " 个" : "");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("结束成功：" + ok + " 个");
            if (fail > 0)
            {
                sb.AppendLine("结束失败：" + fail + " 个，原因：");
                sb.AppendLine(errs.ToString());
                sb.AppendLine("常见原因：受保护进程（杀毒 / 系统）、权限不足、进程已退出。");
            }
            MessageBox.Show(this, sb.ToString(), "结束结果", MessageBoxButtons.OK,
                fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void UpdateRowState(LockProc lp, bool success, string msg)
        {
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (!ReferenceEquals(r.Tag, lp)) continue;
                if (success)
                {
                    r.Cells["state"].Value = "已结束";
                    r.Cells["state"].Style.ForeColor = Theme.TextFaint;
                    r.Cells["state"].Style.BackColor = Color.Transparent;
                    r.DefaultCellStyle.BackColor = Color.FromArgb(243, 248, 243);
                    r.DefaultCellStyle.ForeColor = Theme.TextFaint;
                }
                else
                {
                    r.Cells["state"].Value = "失败";
                    r.Cells["state"].Style.ForeColor = Theme.Danger;
                    r.Cells["state"].Style.BackColor = Theme.DangerSoft;
                    r.Cells["state"].ToolTipText = msg;
                }
                return;
            }
        }

        private void CloseHandlesSelected()
        {
            List<LockProc> list = SelectedProcs();
            if (list.Count == 0) { Info("请先选中一行。"); return; }
            if (list.Count > 1) { Info("关闭句柄一次只能处理一个进程，请只选中一行。"); return; }
            LockProc lp = list[0];
            if (lp.IsCritical) { Info("这是系统关键进程，禁止操作。"); return; }
            if (MessageBox.Show(this,
                "将强制关闭「" + lp.Name + "」(PID " + lp.Pid + ") 占用你选定目录的 " + lp.Files.Count +
                " 个句柄，但【不结束进程】。\n\n⚠ 进阶操作：目标程序可能报错、崩溃或丢数据。\n确定继续吗？",
                "关闭句柄解锁", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            string msg;
            try { msg = ScanEngine.CloseHandles(lp); }
            finally { Cursor = Cursors.Default; }
            Log("关闭句柄 " + lp.Name + "：" + msg);
            Info(msg + "\n\n建议重新扫描一次确认占用是否解除。");
        }

        // ====================================================================
        //  复制 / 自己去杀
        // ====================================================================
        private void CopyForTaskManager()
        {
            List<LockProc> list = SelectedProcs();
            if (list.Count == 0) list = RowsToProcs(true);
            if (list.Count == 0) list = RowsToProcs(false);
            if (list.Count == 0) { Info("没有可复制的进程信息。"); return; }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("【谁在占用 · 扫描结果】");
            sb.AppendLine("目录：" + (_last != null ? _last.Root : _txtPath.Text));
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("进程数：" + list.Count);
            sb.AppendLine();
            foreach (LockProc lp in list)
            {
                sb.AppendLine("进程名：" + lp.Name);
                sb.AppendLine("PID：" + lp.Pid);
                sb.AppendLine("用户：" + lp.User + "   会话：" + lp.Session);
                sb.AppendLine("路径：" + lp.ImagePath);
                sb.AppendLine("状态：" + lp.RiskText + " / " + (lp.CanTerminate ? "可结束" : "无权限"));
                sb.AppendLine("占用文件数：" + lp.Files.Count);
                for (int i = 0; i < lp.Files.Count && i < 10; i++) sb.AppendLine("  · " + lp.Files[i]);
                if (lp.Files.Count > 10) sb.AppendLine("  · …（共 " + lp.Files.Count + " 个）");
                sb.AppendLine();
            }
            sb.AppendLine("照着上面的 PID 去任务管理器「详细信息」页找到，右键结束任务即可。");

            try
            {
                Clipboard.SetText(sb.ToString());
                Log("已复制 " + list.Count + " 个进程信息。");
                Info("已复制 " + list.Count + " 个进程的信息到剪贴板。\n\n" +
                     "接下来：打开任务管理器 →「详细信息」→ 按 PID 找到对应进程 → 右键结束任务。");
            }
            catch (Exception ex) { Warn("复制失败：" + ex.Message); }
        }

        private void CopySelected(string what)
        {
            List<LockProc> list = SelectedProcs();
            if (list.Count == 0) { Info("请先选中要复制的行。"); return; }
            StringBuilder sb = new StringBuilder();
            foreach (LockProc lp in list)
            {
                switch (what)
                {
                    case "pid": sb.AppendLine(lp.Pid.ToString()); break;
                    case "path": sb.AppendLine(lp.ImagePath); break;
                    case "files": foreach (string f in lp.Files) sb.AppendLine(f); break;
                }
            }
            try { Clipboard.SetText(sb.ToString()); Log("已复制到剪贴板。"); }
            catch (Exception ex) { Warn("复制失败：" + ex.Message); }
        }

        private void OpenTaskManager()
        {
            try { Process.Start("taskmgr.exe"); }
            catch (Exception ex) { Warn("打开任务管理器失败：" + ex.Message); }
        }

        private void OpenSelectedFileLocation()
        {
            List<LockProc> list = SelectedProcs();
            if (list.Count == 0) { Info("请先选中一行。"); return; }
            string p = list[0].ImagePath;
            if (string.IsNullOrEmpty(p) || !File.Exists(p))
            {
                Info("拿不到这个进程的程序路径（权限不足或系统进程）。\nPID：" + list[0].Pid);
                return;
            }
            try { Process.Start("explorer.exe", "/select,\"" + p + "\""); }
            catch (Exception ex) { Warn("定位失败：" + ex.Message); }
        }

        private void CheckAll(bool value)
        {
            foreach (DataGridViewRow r in _grid.Rows)
            {
                LockProc lp = r.Tag as LockProc;
                if (lp != null && lp.Killed) continue;
                r.Cells["chk"].Value = value;
            }
        }

        // ====================================================================
        //  CSV 导出（12KB 上限）
        // ====================================================================
        private void ExportCsv(bool preferChecked)
        {
            List<LockProc> list = RowsToProcs(true);
            bool fromChecked = true;
            if (list.Count == 0) { list = RowsToProcs(false); fromChecked = false; }
            if (list.Count == 0) { Info("没有可导出的进程。"); return; }

            ScanResult res = _last;
            if (res == null)
            {
                res = new ScanResult();
                res.Root = _txtPath.Text;
                res.Elevated = Native.IsElevated();
            }

            string csv = CsvExport.Build(res, list);
            int size = CsvExport.Utf8BytesWithBom(csv);

            if (size > CsvExport.SizeLimit)
            {
                string head = fromChecked ? "勾选的 " + list.Count + " 个进程" : "全部 " + list.Count + " 个进程";
                string msg = head + "导出后约 " + (size / 1024.0).ToString("0.0") + " KB，超过 12 KB 上限。\n\n" +
                             "太大了，请在列表里【勾选几个】再导出。\n\n" +
                             "也可以选「精简模式」（去掉文件列表，体积小很多）。\n是否用精简模式导出？";
                DialogResult dr = MessageBox.Show(this, msg, "CSV 超过 12 KB",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (dr == DialogResult.Cancel) return;
                if (dr == DialogResult.Yes)
                {
                    csv = BuildCompactCsv(res, list);
                    size = CsvExport.Utf8BytesWithBom(csv);
                    if (size > CsvExport.SizeLimit)
                    {
                        MessageBox.Show(this,
                            "精简模式仍然超过 12 KB（" + (size / 1024.0).ToString("0.0") + " KB）。\n\n" +
                            "请勾选更少的进程（例如 20~40 个）再导出。",
                            "还是太大了", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else return;
            }

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "保存 CSV 报告";
                dlg.Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
                string dir = (_last != null && Directory.Exists(_last.Root))
                    ? _last.Root
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                dlg.InitialDirectory = dir;
                dlg.FileName = "谁在占用报告_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    CsvExport.Write(dlg.FileName, csv);
                    Log("已导出：" + dlg.FileName + "（" + (size / 1024.0).ToString("0.0") + " KB）");
                    _lblStatus.Text = "已导出 " + list.Count + " 个进程";
                    if (MessageBox.Show(this,
                        "已导出 " + list.Count + " 个进程（" + (size / 1024.0).ToString("0.0") + " KB）。\n\n" +
                        dlg.FileName + "\n\n要打开它吗？",
                        "导出成功", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        try { Process.Start(dlg.FileName); } catch { }
                    }
                }
                catch (Exception ex) { Warn("保存失败：" + ex.Message); }
            }
        }

        private static string BuildCompactCsv(ScanResult res, List<LockProc> procs)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# 谁在占用 WhoLocks 报告（精简版）");
            sb.AppendLine("# 扫描目录," + Q(res.Root));
            sb.AppendLine("# 扫描时间," + Q(res.ScanTime.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine("# 运行身份," + Q(res.Elevated ? "管理员" : "普通用户"));
            sb.AppendLine("# 本次导出," + Q(procs.Count + " 个进程"));
            sb.AppendLine();
            sb.AppendLine("进程名,PID,用户,会话,启动时间,能否结束,风险标记,检测来源,占用文件数,程序路径");
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
                sb.AppendLine(Q(lp.ImagePath));
            }
            return sb.ToString();
        }

        private static string Q(string s)
        {
            if (s == null) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0 && s.IndexOf('\r') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // ====================================================================
        //  杂项
        // ====================================================================
        private void ShowHelp()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("两个选项是什么意思？");
            sb.AppendLine();
            sb.AppendLine("【包含子文件夹】");
            sb.AppendLine("  勾选（默认）：连文件夹里的所有子文件夹一起查，最不容易漏。");
            sb.AppendLine("  不勾选：只查你选中的这一层，不管里面的子文件夹。");
            sb.AppendLine("  什么时候不勾：文件夹特别大（几十万文件），只想快速看一层的时候。");
            sb.AppendLine();
            sb.AppendLine("【深度检测（更准，稍慢）】");
            sb.AppendLine("  勾选（默认）：除了“打开文件”的进程，还会查那些把目录里的");
            sb.AppendLine("    DLL / EXE 加载到内存中的程序。这类占用在任务管理器里最难看");
            sb.AppendLine("    出来，也是“复制不了但找不到人”的常见元凶。");
            sb.AppendLine("  不勾选：只查真正打开文件的进程，速度快一点，但可能漏掉");
            sb.AppendLine("    那种“加载了 DLL 所以文件被占用”的情况。");
            sb.AppendLine();
            sb.AppendLine("一句话：拿不准就两个都保持勾选，只是稍微慢一两秒。");
            MessageBox.Show(this, sb.ToString(), "选项说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Log(string line)
        {
            _logLines.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (_logLines.Count > 300) _logLines.RemoveAt(0);
        }

        private void Info(string msg)
        {
            MessageBox.Show(this, msg, "谁在占用", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Warn(string msg)
        {
            MessageBox.Show(this, msg, "谁在占用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Color.FromArgb(0, 16, 100), 1))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_scanning)
            {
                if (MessageBox.Show(this, "扫描还没结束，确定退出吗？", "谁在占用",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                {
                    e.Cancel = true;
                    return;
                }
                if (_engine != null) _engine.Cancel();
            }
            base.OnFormClosing(e);
        }
    }
}
