using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FileLauncher
{
    internal static class Program
    {
        internal const string WindowTitle = "Запуск набора файлов";
        internal const string TrayArgument = "--tray";

        private const string MutexName = "FileLauncher.SingleInstance.7F1C2A64";
        private const string ShowEventName = "FileLauncher.ShowWindow.7F1C2A64";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [STAThread]
        private static void Main(string[] args)
        {
            try { SetProcessDPIAware(); }
            catch (Exception) { }

            Mutex mutex = null;
            bool createdNew = true;

            try
            {
                mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (Exception)
            {
                // политика может запрещать именованные объекты — тогда просто работаем без защиты
                createdNew = true;
            }

            try
            {
                if (!createdNew)
                {
                    // программа уже работает в фоне — при обычном запуске просим её показать окно
                    if (!HasArgument(args, TrayArgument))
                    {
                        SignalShowWindow();
                    }

                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                MainForm form = new MainForm(args);

                EventWaitHandle showEvent = TryCreateShowEvent();
                if (showEvent != null)
                {
                    Thread listener = new Thread(delegate()
                    {
                        while (true)
                        {
                            showEvent.WaitOne();

                            if (form.IsDisposed)
                            {
                                break;
                            }

                            try { form.BeginInvoke(new MethodInvoker(form.RestoreWindow)); }
                            catch (Exception) { break; }
                        }
                    });
                    listener.IsBackground = true;
                    listener.Start();
                }

                Application.Run(form);
            }
            finally
            {
                if (mutex != null)
                {
                    mutex.Dispose();
                }
            }
        }

        internal static bool HasArgument(string[] args, string expected)
        {
            if (args == null)
            {
                return false;
            }

            foreach (string arg in args)
            {
                if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static EventWaitHandle TryCreateShowEvent()
        {
            try
            {
                return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void SignalShowWindow()
        {
            try
            {
                using (EventWaitHandle showEvent = new EventWaitHandle(
                    false, EventResetMode.AutoReset, ShowEventName))
                {
                    showEvent.Set();
                }
            }
            catch (Exception)
            {
                // ниже попробуем найти окно по заголовку
            }

            IntPtr handle = FindWindow(null, WindowTitle);
            if (handle == IntPtr.Zero)
            {
                return;
            }

            ShowWindow(handle, 9); // SW_RESTORE
            ShowWindow(handle, 5); // SW_SHOW
            SetForegroundWindow(handle);
        }
    }

    /// <summary>Одна «сцена» — именованный набор файлов и папок.</summary>
    internal sealed class Scene
    {
        public string Name;
        public bool AutoRun;
        public readonly List<string> Paths = new List<string>();

        public Scene(string name)
        {
            Name = name;
        }
    }

    /// <summary>Панель с полупрозрачным фоном поверх картинки.</summary>
    internal sealed class GlassPanel : Panel
    {
        public GlassPanel(int backAlpha)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(backAlpha, 255, 255, 255);
        }
    }

    /// <summary>Карточка с картинкой персонажа и скруглёнными углами.</summary>
    internal sealed class HeroPanel : Panel
    {
        private const int CornerRadius = 20;

        public HeroPanel()
        {
            SetStyle(
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);

            BackColor = Color.Transparent;
            BackgroundImageLayout = ImageLayout.Zoom;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            ApplyRegion();
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null)
                {
                    old.Dispose();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), CornerRadius))
            using (Pen pen = new Pen(Color.FromArgb(80, 96, 84, 130)))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }

    /// <summary>Маленькое окно ввода названия сцены.</summary>
    internal sealed class NameDialog : Form
    {
        private readonly TextBox input = new TextBox();

        public string Value
        {
            get { return input.Text.Trim(); }
        }

        public NameDialog(string title, string caption, string initial)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(380, 122);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(248, 246, 252);

            Label label = new Label();
            label.Text = caption;
            label.AutoSize = true;
            label.Location = new Point(14, 14);
            label.ForeColor = Color.FromArgb(46, 42, 56);
            Controls.Add(label);

            input.Location = new Point(14, 40);
            input.Width = 350;
            input.Text = initial;
            Controls.Add(input);

            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(194, 80);
            ok.Size = new Size(82, 28);
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(282, 80);
            cancel.Size = new Size(82, 28);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += delegate { input.SelectAll(); };
        }
    }

    /// <summary>Небольшое окно «Автор» со ссылкой на Telegram.</summary>
    internal sealed class AboutDialog : Form
    {
        private const string AuthorText = "Автор проекта - t.me/silxncer";
        private const string AuthorUrl = "https://t.me/silxncer";

        public AboutDialog()
        {
            Text = "Автор";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(370, 158);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(248, 246, 252);
            FormBorderStyle = FormBorderStyle.FixedDialog;

            try
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                // остаётся стандартная иконка
            }

            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.BackColor = Color.FromArgb(248, 246, 252);
            Controls.Add(card);

            LinkLabel message = new LinkLabel();
            message.Text = AuthorText;
            message.AutoSize = false;
            message.TextAlign = ContentAlignment.MiddleCenter;
            message.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            message.LinkColor = Color.FromArgb(124, 92, 214);
            message.ActiveLinkColor = Color.FromArgb(108, 76, 200);
            message.VisitedLinkColor = Color.FromArgb(124, 92, 214);
            message.LinkBehavior = LinkBehavior.AlwaysUnderline;
            message.LinkArea = new LinkArea(0, AuthorText.Length);
            message.Cursor = Cursors.Hand;
            message.Location = new Point(10, 40);
            message.Size = new Size(350, 34);
            message.LinkClicked += delegate { OpenAuthorLink(); };
            card.Controls.Add(message);

            Button close = new Button();
            close.Text = "Закрыть";
            close.DialogResult = DialogResult.OK;
            close.Size = new Size(110, 32);
            close.Location = new Point((ClientSize.Width - 110) / 2, 104);
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderColor = Color.FromArgb(120, 168, 158, 190);
            close.BackColor = Color.FromArgb(255, 252, 251, 255);
            close.ForeColor = Color.FromArgb(46, 42, 56);
            close.Cursor = Cursors.Hand;
            card.Controls.Add(close);

            AcceptButton = close;
            CancelButton = close;
        }

        private static void OpenAuthorLink()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(AuthorUrl);
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception)
            {
                // если браузер не открылся — ничего страшного
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private const string AppTitle = Program.WindowTitle;
        private const string AppFolderName = "FileLauncher";
        private const string LegacyListFileName = "list.txt";
        private const string ScenesFileName = "scenes.txt";
        private const string SettingsFileName = "settings.txt";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "FileLauncher";

        private static readonly Color AccentColor = Color.FromArgb(124, 92, 214);
        private static readonly Color AccentHoverColor = Color.FromArgb(108, 76, 200);
        private static readonly Color TextColor = Color.FromArgb(46, 42, 56);
        private static readonly Color MutedColor = Color.FromArgb(104, 97, 118);
        private static readonly Color BorderColor = Color.FromArgb(120, 168, 158, 190);
        private static readonly Color FieldColor = Color.FromArgb(255, 252, 251, 255);

        private readonly bool startHidden;
        private bool exiting;
        private bool trayHintShown;
        private bool suppressAutostartEvent;
        private bool suppressSettingsEvent;
        private bool suppressSceneEvent;

        private string dataFolder;
        private string scenesFile;
        private string settingsFile;
        private string saveError;

        private readonly List<Scene> scenes = new List<Scene>();
        private int sceneIndex;

        private readonly ListBox sceneBox = new ListBox();
        private readonly ListBox fileList = new ListBox();
        private readonly NumericUpDown delayBox = new NumericUpDown();
        private readonly CheckBox minimizeCheck = new CheckBox();
        private readonly CheckBox autostartCheck = new CheckBox();
        private readonly CheckBox startSetCheck = new CheckBox();
        private readonly Label statusLabel = new Label();
        private readonly Label hintLabel = new Label();
        private readonly Button runButton = new Button();
        private NotifyIcon trayIcon;
        private ToolStripMenuItem trayRunItem;
        private ToolStripMenuItem trayAutostartItem;
        private System.Windows.Forms.Timer startupTimer;

        public MainForm(string[] args)
        {
            dataFolder = ResolveDataFolder();
            scenesFile = Path.Combine(dataFolder, ScenesFileName);
            settingsFile = Path.Combine(dataFolder, SettingsFileName);
            startHidden = Program.HasArgument(args, Program.TrayArgument);

            BuildUi();
            LoadScenes();
            LoadSettings();
            BuildTray();
            SyncAutostartUi();
            UpdateSceneTexts();
            UpdateStatus();

            if (startHidden)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;

                if (CollectAutoRunPaths().Count > 0)
                {
                    ScheduleStartupRun();
                }
            }
        }

        // -------------------------------------------------------------- модель

        private Scene CurrentScene
        {
            get
            {
                if (scenes.Count == 0)
                {
                    scenes.Add(new Scene("Набор 1"));
                    sceneIndex = 0;
                }

                if (sceneIndex < 0 || sceneIndex >= scenes.Count)
                {
                    sceneIndex = 0;
                }

                return scenes[sceneIndex];
            }
        }

        private static string ResolveDataFolder()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

                Directory.CreateDirectory(folder);

                string probe = Path.Combine(folder, ".write-test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);

                return folder;
            }
            catch (Exception)
            {
                // %APPDATA% недоступен — храним данные рядом с программой
                return Application.StartupPath;
            }
        }

        private void LoadScenes()
        {
            try
            {
                if (File.Exists(scenesFile))
                {
                    ParseScenes(File.ReadAllLines(scenesFile, Encoding.UTF8));
                }
                else
                {
                    MigrateLegacyList();
                }
            }
            catch (Exception)
            {
                // повреждённый файл сцен просто игнорируем
            }

            if (scenes.Count == 0)
            {
                scenes.Add(new Scene("Набор 1"));
            }

            sceneIndex = 0;
            SyncSceneList();
            ShowScene();
        }

        private void ParseScenes(string[] lines)
        {
            Scene current = null;

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("scene=", StringComparison.OrdinalIgnoreCase))
                {
                    string name = line.Substring(6).Trim();
                    if (name.Length == 0)
                    {
                        name = "Набор";
                    }

                    current = new Scene(name);
                    scenes.Add(current);
                    continue;
                }

                if (line.StartsWith("autorun=", StringComparison.OrdinalIgnoreCase))
                {
                    if (current != null)
                    {
                        string value = line.Substring(8).Trim();
                        current.AutoRun = value == "1" ||
                            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }

                    continue;
                }

                if (line.StartsWith("file=", StringComparison.OrdinalIgnoreCase))
                {
                    string path = line.Substring(5).Trim();
                    if (path.Length == 0)
                    {
                        continue;
                    }

                    if (current == null)
                    {
                        current = new Scene("Набор 1");
                        scenes.Add(current);
                    }

                    if (!current.Paths.Contains(path))
                    {
                        current.Paths.Add(path);
                    }
                }
            }
        }

        private void MigrateLegacyList()
        {
            string legacy = Path.Combine(dataFolder, LegacyListFileName);
            if (!File.Exists(legacy))
            {
                return;
            }

            Scene scene = new Scene("Набор 1");

            foreach (string line in File.ReadAllLines(legacy, Encoding.UTF8))
            {
                string path = line.Trim();
                if (path.Length > 0 && !scene.Paths.Contains(path))
                {
                    scene.Paths.Add(path);
                }
            }

            scenes.Add(scene);
            SaveScenes();
        }

        private void SaveScenes()
        {
            try
            {
                if (!Directory.Exists(dataFolder))
                {
                    Directory.CreateDirectory(dataFolder);
                }

                StringBuilder builder = new StringBuilder();
                foreach (Scene scene in scenes)
                {
                    builder.Append("scene=").Append(scene.Name).Append("\r\n");
                    if (scene.AutoRun)
                    {
                        builder.Append("autorun=1\r\n");
                    }

                    foreach (string path in scene.Paths)
                    {
                        builder.Append("file=").Append(path).Append("\r\n");
                    }
                }

                File.WriteAllText(scenesFile, builder.ToString(), Encoding.UTF8);
                saveError = null;
            }
            catch (Exception ex)
            {
                saveError = ex.Message;
            }
        }

        private void CaptureCurrentScene()
        {
            Scene scene = CurrentScene;
            scene.Paths.Clear();

            foreach (object item in fileList.Items)
            {
                string path = item as string;
                if (!string.IsNullOrEmpty(path))
                {
                    scene.Paths.Add(path);
                }
            }
        }

        private void ShowScene()
        {
            suppressSceneEvent = true;

            fileList.BeginUpdate();
            fileList.Items.Clear();
            foreach (string path in CurrentScene.Paths)
            {
                fileList.Items.Add(path);
            }
            fileList.EndUpdate();

            suppressSceneEvent = false;
        }

        private void SyncSceneList()
        {
            suppressSceneEvent = true;

            sceneBox.BeginUpdate();
            sceneBox.Items.Clear();
            foreach (Scene scene in scenes)
            {
                sceneBox.Items.Add(scene.Name);
            }
            sceneBox.EndUpdate();

            if (sceneIndex >= 0 && sceneIndex < sceneBox.Items.Count)
            {
                sceneBox.SelectedIndex = sceneIndex;
            }

            suppressSceneEvent = false;
        }

        private int IndexOfScene(string name)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                if (string.Equals(scenes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool SceneNameExists(string name, Scene ignore)
        {
            foreach (Scene scene in scenes)
            {
                if (scene != ignore && string.Equals(scene.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string MakeUniqueSceneName(string desired, Scene ignore)
        {
            string name = (desired ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                name = "Набор";
            }

            if (!SceneNameExists(name, ignore))
            {
                return name;
            }

            int number = 2;
            while (SceneNameExists(name + " " + number, ignore))
            {
                number++;
            }

            return name + " " + number;
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            SuspendLayout();

            Text = AppTitle;
            ClientSize = new Size(1020, 660);
            MinimumSize = new Size(940, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(246, 243, 250);
            DoubleBuffered = true;

            try
            {
                this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                // остаётся стандартная иконка
            }

            LoadBackground();

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18);
            root.ColumnCount = 3;
            root.RowCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.BackColor = Color.Transparent;
            Controls.Add(root);

            GlassPanel sidebar = new GlassPanel(210);
            sidebar.Dock = DockStyle.Fill;
            sidebar.Padding = new Padding(14);
            sidebar.Margin = new Padding(0, 0, 12, 0);
            root.Controls.Add(sidebar, 0, 0);

            TableLayoutPanel sideLayout = new TableLayoutPanel();
            sideLayout.Dock = DockStyle.Fill;
            sideLayout.ColumnCount = 1;
            sideLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sideLayout.RowCount = 4;
            sideLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sideLayout.BackColor = Color.Transparent;
            sideLayout.Margin = new Padding(0);
            sidebar.Controls.Add(sideLayout);

            Label scenesCaption = new Label();
            scenesCaption.Text = "СЦЕНЫ";
            scenesCaption.AutoSize = true;
            scenesCaption.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            scenesCaption.ForeColor = MutedColor;
            scenesCaption.Margin = new Padding(2, 0, 0, 8);
            sideLayout.Controls.Add(scenesCaption, 0, 0);

            sceneBox.Dock = DockStyle.Fill;
            sceneBox.BorderStyle = BorderStyle.None;
            sceneBox.DrawMode = DrawMode.OwnerDrawFixed;
            sceneBox.ItemHeight = 30;
            sceneBox.BackColor = Color.FromArgb(253, 251, 255);
            sceneBox.ForeColor = TextColor;
            sceneBox.IntegralHeight = false;
            sceneBox.Margin = new Padding(0, 0, 0, 10);
            sceneBox.DrawItem += DrawSceneItem;
            sceneBox.SelectedIndexChanged += OnSceneSelected;
            sceneBox.DoubleClick += OnRenameScene;
            sideLayout.Controls.Add(sceneBox, 0, 1);

            TableLayoutPanel sceneButtons = new TableLayoutPanel();
            sceneButtons.Dock = DockStyle.Fill;
            sceneButtons.AutoSize = false;
            sceneButtons.Height = 140;
            sceneButtons.ColumnCount = 1;
            sceneButtons.RowCount = 4;
            sceneButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sceneButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            sceneButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            sceneButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            sceneButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            sceneButtons.BackColor = Color.Transparent;
            sceneButtons.Margin = new Padding(0);
            sideLayout.Controls.Add(sceneButtons, 0, 2);

            Button addScene = MakeSmallButton("+ Сцена");
            addScene.Margin = new Padding(0, 0, 0, 5);
            addScene.Click += OnAddScene;
            sceneButtons.Controls.Add(addScene, 0, 0);

            Button copyScene = MakeSmallButton("Копия");
            copyScene.Margin = new Padding(0, 0, 0, 5);
            copyScene.Click += OnDuplicateScene;
            sceneButtons.Controls.Add(copyScene, 0, 1);

            Button renameScene = MakeSmallButton("Переименовать");
            renameScene.Margin = new Padding(0, 0, 0, 5);
            renameScene.Click += OnRenameScene;
            sceneButtons.Controls.Add(renameScene, 0, 2);

            Button deleteScene = MakeSmallButton("Удалить");
            deleteScene.Margin = new Padding(0);
            deleteScene.Click += OnDeleteScene;
            sceneButtons.Controls.Add(deleteScene, 0, 3);

            Button aboutButton = MakeSmallButton("Автор");
            aboutButton.Height = 28;
            aboutButton.Margin = new Padding(0, 10, 0, 0);
            aboutButton.ForeColor = MutedColor;
            aboutButton.Click += OnShowAuthor;
            sideLayout.Controls.Add(aboutButton, 0, 3);

            GlassPanel main = new GlassPanel(255);
            main.Dock = DockStyle.Fill;
            main.Padding = new Padding(14, 12, 14, 12);
            main.Margin = new Padding(0);
            root.Controls.Add(main, 1, 0);



            HeroPanel hero = new HeroPanel();
            hero.Dock = DockStyle.Fill;
            hero.Margin = new Padding(12, 0, 0, 0);
            LoadHero(hero);
            root.Controls.Add(hero, 2, 0);

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowCount = 4;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.BackColor = Color.Transparent;
            mainLayout.Margin = new Padding(0);
            main.Controls.Add(mainLayout);

            hintLabel.AutoSize = true;
            hintLabel.ForeColor = TextColor;
            hintLabel.Margin = new Padding(0, 0, 0, 8);
            mainLayout.Controls.Add(hintLabel, 0, 0);

            TableLayoutPanel middle = new TableLayoutPanel();
            middle.Dock = DockStyle.Fill;
            middle.ColumnCount = 2;
            middle.RowCount = 1;
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            middle.Margin = new Padding(0, 0, 0, 10);
            middle.BackColor = Color.Transparent;
            mainLayout.Controls.Add(middle, 0, 1);

            fileList.Dock = DockStyle.Fill;
            fileList.BorderStyle = BorderStyle.FixedSingle;
            fileList.IntegralHeight = false;
            fileList.SelectionMode = SelectionMode.MultiExtended;
            fileList.HorizontalScrollbar = true;
            fileList.AllowDrop = true;
            fileList.BackColor = Color.FromArgb(255, 253, 253);
            fileList.ForeColor = TextColor;
            fileList.Margin = new Padding(0, 0, 10, 0);
            fileList.DoubleClick += OnReveal;
            fileList.KeyDown += OnListKeyDown;
            fileList.MouseDown += OnListMouseDown;
            fileList.DragEnter += OnDragEnter;
            fileList.DragDrop += OnDragDrop;
            middle.Controls.Add(fileList, 0, 0);

            TableLayoutPanel fileButtons = new TableLayoutPanel();
            fileButtons.ColumnCount = 1;
            fileButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            fileButtons.RowCount = 6;
            fileButtons.AutoSize = true;
            fileButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            fileButtons.Dock = DockStyle.Top;
            fileButtons.BackColor = Color.Transparent;
            fileButtons.Margin = new Padding(0);
            for (int i = 0; i < 6; i++)
            {
                fileButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            middle.Controls.Add(fileButtons, 1, 0);

            Button addFiles = MakeButton("Добавить файлы…");
            addFiles.Click += OnAddFiles;
            fileButtons.Controls.Add(addFiles, 0, 0);

            Button addFolder = MakeButton("Добавить папку…");
            addFolder.Click += OnAddFolder;
            fileButtons.Controls.Add(addFolder, 0, 1);

            Button remove = MakeButton("Удалить");
            remove.Click += OnRemove;
            fileButtons.Controls.Add(remove, 0, 2);

            Button clear = MakeButton("Очистить");
            clear.Click += OnClear;
            fileButtons.Controls.Add(clear, 0, 3);

            Button up = MakeButton("Вверх");
            up.Click += delegate { MoveSelected(-1); };
            fileButtons.Controls.Add(up, 0, 4);

            Button down = MakeButton("Вниз");
            down.Click += delegate { MoveSelected(1); };
            fileButtons.Controls.Add(down, 0, 5);

            FlowLayoutPanel options = new FlowLayoutPanel();
            options.FlowDirection = FlowDirection.TopDown;
            options.AutoSize = true;
            options.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            options.WrapContents = false;
            options.Margin = new Padding(0, 0, 0, 6);
            options.BackColor = Color.Transparent;
            mainLayout.Controls.Add(options, 0, 2);

            FlowLayoutPanel optionsRow1 = new FlowLayoutPanel();
            optionsRow1.AutoSize = true;
            optionsRow1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            optionsRow1.WrapContents = false;
            optionsRow1.Margin = new Padding(0);
            optionsRow1.BackColor = Color.Transparent;
            options.Controls.Add(optionsRow1);

            Label delayLabel = new Label();
            delayLabel.AutoSize = true;
            delayLabel.Text = "Пауза между запусками:";
            delayLabel.ForeColor = TextColor;
            delayLabel.Margin = new Padding(0, 6, 6, 0);
            optionsRow1.Controls.Add(delayLabel);

            delayBox.Minimum = 0;
            delayBox.Maximum = 10000;
            delayBox.Increment = 50;
            delayBox.Value = 250;
            delayBox.Width = 72;
            delayBox.Margin = new Padding(0, 2, 6, 0);
            optionsRow1.Controls.Add(delayBox);

            Label msLabel = new Label();
            msLabel.AutoSize = true;
            msLabel.Text = "мс";
            msLabel.ForeColor = TextColor;
            msLabel.Margin = new Padding(0, 6, 24, 0);
            optionsRow1.Controls.Add(msLabel);

            minimizeCheck.AutoSize = true;
            minimizeCheck.Text = "Свернуть после запуска";
            minimizeCheck.ForeColor = TextColor;
            minimizeCheck.Margin = new Padding(0, 6, 18, 0);
            optionsRow1.Controls.Add(minimizeCheck);

            FlowLayoutPanel optionsRow2 = new FlowLayoutPanel();
            optionsRow2.AutoSize = true;
            optionsRow2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            optionsRow2.WrapContents = false;
            optionsRow2.Margin = new Padding(0, 4, 0, 0);
            optionsRow2.BackColor = Color.Transparent;
            options.Controls.Add(optionsRow2);

            autostartCheck.AutoSize = true;
            autostartCheck.Text = "Автозапуск с Windows";
            autostartCheck.ForeColor = TextColor;
            autostartCheck.Margin = new Padding(0, 6, 24, 0);
            autostartCheck.CheckedChanged += OnAutostartChanged;
            optionsRow2.Controls.Add(autostartCheck);

            startSetCheck.AutoSize = true;
            startSetCheck.Text = "Запускать эту сцену при входе";
            startSetCheck.ForeColor = TextColor;
            startSetCheck.Margin = new Padding(0, 6, 0, 0);
            startSetCheck.CheckedChanged += OnStartSetChanged;
            optionsRow2.Controls.Add(startSetCheck);

            TableLayoutPanel bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.AutoSize = true;
            bottom.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bottom.ColumnCount = 2;
            bottom.RowCount = 1;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.BackColor = Color.Transparent;
            bottom.Margin = new Padding(0);
            mainLayout.Controls.Add(bottom, 0, 3);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.AutoSize = false;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = MutedColor;
            statusLabel.Margin = new Padding(0, 0, 12, 0);
            bottom.Controls.Add(statusLabel, 0, 0);

            runButton.Text = "▶  Запустить сцену";
            runButton.Size = new Size(220, 46);
            runButton.Anchor = AnchorStyles.Right;
            runButton.FlatStyle = FlatStyle.Flat;
            runButton.FlatAppearance.BorderSize = 0;
            runButton.FlatAppearance.MouseOverBackColor = AccentHoverColor;
            runButton.FlatAppearance.MouseDownBackColor = AccentHoverColor;
            runButton.BackColor = AccentColor;
            runButton.ForeColor = Color.White;
            runButton.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            runButton.Cursor = Cursors.Hand;
            runButton.Margin = new Padding(0);
            runButton.Click += OnRunAll;
            bottom.Controls.Add(runButton, 1, 0);

            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem runOne = new ToolStripMenuItem("Запустить выбранное");
            runOne.Click += OnRunSelected;
            menu.Items.Add(runOne);

            ToolStripMenuItem reveal = new ToolStripMenuItem("Показать в проводнике");
            reveal.Click += OnReveal;
            menu.Items.Add(reveal);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem removeOne = new ToolStripMenuItem("Удалить из сцены");
            removeOne.Click += OnRemove;
            menu.Items.Add(removeOne);

            fileList.ContextMenuStrip = menu;

            FormClosing += OnFormClosing;

            ResumeLayout(true);
        }

        private void LoadBackground()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("FileLauncher.background.png"))
                {
                    if (stream == null)
                    {
                        return;
                    }

                    using (Image image = Image.FromStream(stream))
                    {
                        BackgroundImage = new Bitmap(image);
                        BackgroundImageLayout = ImageLayout.Zoom;
                    }
                }
            }
            catch (Exception)
            {
                // без фона программа тоже работает
            }
        }

        private void LoadHero(HeroPanel hero)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("FileLauncher.hero.png"))
                {
                    if (stream == null)
                    {
                        return;
                    }

                    using (Image image = Image.FromStream(stream))
                    {
                        hero.BackgroundImage = new Bitmap(image);
                    }
                }
            }
            catch (Exception)
            {
                // без картиночной панели интерфейс тоже работает
            }
        }

        private static Button MakeButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(164, 32);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 240, 236, 250);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(255, 226, 219, 245);
            button.BackColor = FieldColor;
            button.ForeColor = TextColor;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(8, 0, 0, 0);
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0, 0, 0, 8);
            return button;
        }

        private static Button MakeSmallButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Height = 28;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 240, 236, 250);
            button.BackColor = FieldColor;
            button.ForeColor = TextColor;
            button.Font = new Font("Segoe UI", 8.25F);
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0, 0, 4, 4);
            return button;
        }

        private void DrawSceneItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? Color.FromArgb(255, 233, 226, 250) : Color.FromArgb(253, 251, 255);

            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            if (selected)
            {
                using (SolidBrush accent = new SolidBrush(AccentColor))
                {
                    e.Graphics.FillRectangle(accent, new Rectangle(e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12));
                }
            }

            string text = sceneBox.Items[e.Index] as string;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            bool autoRun = e.Index < scenes.Count && scenes[e.Index].AutoRun;
            int textRightPadding = autoRun ? 30 : 16;

            Rectangle textBounds = new Rectangle(
                e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12 - textRightPadding, e.Bounds.Height);

            TextRenderer.DrawText(
                e.Graphics,
                text,
                sceneBox.Font,
                textBounds,
                TextColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (autoRun)
            {
                // метка «запускается при входе» — маленькая точка справа
                Rectangle dot = new Rectangle(e.Bounds.Right - 16, e.Bounds.Y + (e.Bounds.Height / 2) - 3, 6, 6);
                using (SolidBrush accent = new SolidBrush(AccentColor))
                {
                    e.Graphics.FillEllipse(accent, dot);
                }
            }
        }

        private void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            trayRunItem = new ToolStripMenuItem("Запустить сцену");
            trayRunItem.Font = new Font(menu.Font, FontStyle.Bold);
            trayRunItem.Click += OnRunAll;
            menu.Items.Add(trayRunItem);

            ToolStripMenuItem showItem = new ToolStripMenuItem("Открыть окно");
            showItem.Click += delegate { RestoreWindow(); };
            menu.Items.Add(showItem);

            menu.Items.Add(new ToolStripSeparator());

            trayAutostartItem = new ToolStripMenuItem("Запускать при входе в Windows");
            trayAutostartItem.Click += delegate { autostartCheck.Checked = !autostartCheck.Checked; };
            menu.Items.Add(trayAutostartItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += delegate { ExitApplication(); };
            menu.Items.Add(exitItem);

            trayIcon = new NotifyIcon();
            trayIcon.Icon = this.Icon ?? SystemIcons.Application;
            trayIcon.Text = AppTitle;
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { RestoreWindow(); };

            FormClosed += OnFormClosed;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (startHidden)
            {
                Hide();
            }
        }

        internal void RestoreWindow()
        {
            ShowInTaskbar = true;
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Show();
            Activate();
        }

        private void ExitApplication()
        {
            exiting = true;
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
            }

            Close();
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
        }

        // ------------------------------------------------------------- сцены

        private void UpdateSceneTexts()
        {
            hintLabel.Text = "Сцена «" + CurrentScene.Name + "» — что открыть (файлы и папки, можно перетащить):";

            if (trayRunItem != null)
            {
                trayRunItem.Text = "Запустить сцену «" + Shorten(CurrentScene.Name, 26) + "»";
            }
        }

        private static string Shorten(string text, int limit)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= limit)
            {
                return text;
            }

            return text.Substring(0, limit - 1) + "…";
        }

        private void OnSceneSelected(object sender, EventArgs e)
        {
            if (suppressSceneEvent)
            {
                return;
            }

            int index = sceneBox.SelectedIndex;
            if (index < 0 || index >= scenes.Count || index == sceneIndex)
            {
                return;
            }

            CaptureCurrentScene();
            sceneIndex = index;
            SaveScenes();
            ShowScene();
            SaveSettings();
            UpdateSceneTexts();
            UpdateStartSetUi();
            UpdateStatus();
        }

        private void OnAddScene(object sender, EventArgs e)
        {
            CaptureCurrentScene();

            scenes.Add(new Scene(MakeUniqueSceneName("Новый набор", null)));
            sceneIndex = scenes.Count - 1;

            SyncSceneList();
            ShowScene();
            SaveScenes();
            SaveSettings();
            UpdateSceneTexts();
            UpdateStartSetUi();
            UpdateStatus();

            sceneBox.Focus();
        }

        private void OnDuplicateScene(object sender, EventArgs e)
        {
            CaptureCurrentScene();
            Scene source = CurrentScene;

            Scene copy = new Scene(MakeUniqueSceneName(source.Name + " — копия", null));
            copy.Paths.AddRange(source.Paths);
            copy.AutoRun = source.AutoRun;

            scenes.Insert(sceneIndex + 1, copy);
            sceneIndex++;

            SyncSceneList();
            ShowScene();
            SaveScenes();
            SaveSettings();
            UpdateSceneTexts();
            UpdateStartSetUi();
            UpdateStatus();
        }

        private void OnRenameScene(object sender, EventArgs e)
        {
            Scene scene = CurrentScene;

            using (NameDialog dialog = new NameDialog("Название сцены", "Название сцены:", scene.Name))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string name = dialog.Value;
                if (name.Length == 0)
                {
                    return;
                }

                scene.Name = MakeUniqueSceneName(name, scene);
            }

            SyncSceneList();
            SaveScenes();
            SaveSettings();
            UpdateSceneTexts();
        }

        private void OnDeleteScene(object sender, EventArgs e)
        {
            if (scenes.Count <= 1)
            {
                MessageBox.Show(
                    this,
                    "Нельзя удалить единственную сцену. Создайте сначала другую.",
                    AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult answer = MessageBox.Show(
                this,
                "Удалить сцену «" + CurrentScene.Name + "» вместе со списком файлов?",
                AppTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                return;
            }

            scenes.RemoveAt(sceneIndex);
            if (sceneIndex >= scenes.Count)
            {
                sceneIndex = scenes.Count - 1;
            }

            SyncSceneList();
            ShowScene();
            SaveScenes();
            SaveSettings();
            UpdateSceneTexts();
            UpdateStartSetUi();
            UpdateStatus();
        }

        private void OnShowAuthor(object sender, EventArgs e)
        {
            using (AboutDialog dialog = new AboutDialog())
            {
                dialog.ShowDialog(this);
            }
        }

        // ------------------------------------------------------------- файлы

        private void OnAddFiles(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Выберите файлы для сцены «" + CurrentScene.Name + "»";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                dialog.Filter =
                    "Все файлы|*.*|" +
                    "Программы и ярлыки|*.exe;*.lnk;*.bat;*.cmd;*.com;*.ps1|" +
                    "Документы|*.txt;*.doc;*.docx;*.pdf;*.rtf;*.xls;*.xlsx;*.csv|" +
                    "Медиа|*.mp3;*.wav;*.mp4;*.avi;*.mkv;*.jpg;*.jpeg;*.png";
                dialog.FilterIndex = 1;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddPaths(dialog.FileNames);
                }
            }
        }

        private void OnAddFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку, которую нужно открывать вместе с остальными элементами";
                dialog.ShowNewFolderButton = true;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddPaths(new string[] { dialog.SelectedPath });
                }
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null)
            {
                AddPaths(paths);
            }
        }

        private void OnListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            int index = fileList.IndexFromPoint(e.Location);
            if (index >= 0 && !fileList.GetSelected(index))
            {
                fileList.ClearSelected();
                fileList.SetSelected(index, true);
            }
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                OnRemove(sender, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void OnRemove(object sender, EventArgs e)
        {
            if (fileList.SelectedIndices.Count == 0)
            {
                return;
            }

            List<int> indices = new List<int>();
            foreach (int index in fileList.SelectedIndices)
            {
                indices.Add(index);
            }

            indices.Sort();
            for (int i = indices.Count - 1; i >= 0; i--)
            {
                fileList.Items.RemoveAt(indices[i]);
            }

            CaptureCurrentScene();
            SaveScenes();
            UpdateStatus();
        }

        private void OnClear(object sender, EventArgs e)
        {
            if (fileList.Items.Count == 0)
            {
                return;
            }

            DialogResult answer = MessageBox.Show(
                this,
                "Очистить список файлов в сцене «" + CurrentScene.Name + "»?",
                AppTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer == DialogResult.Yes)
            {
                fileList.Items.Clear();
                CaptureCurrentScene();
                SaveScenes();
                UpdateStatus();
            }
        }

        private void OnReveal(object sender, EventArgs e)
        {
            string path = fileList.SelectedItem as string;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
                else if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", "\"" + path + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRunSelected(object sender, EventArgs e)
        {
            string path = fileList.SelectedItem as string;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                StartItem(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Не удалось запустить:" + Environment.NewLine + path + Environment.NewLine +
                        Environment.NewLine + ex.Message,
                    AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            CaptureCurrentScene();
            SaveScenes();
            SaveSettings();

            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();

                if (!trayHintShown && trayIcon != null)
                {
                    trayIcon.ShowBalloonTip(
                        4000,
                        AppTitle,
                        "Программа осталась работать в фоне: иконка в трее. Выход — правая кнопка → «Выход».",
                        ToolTipIcon.Info);
                    trayHintShown = true;
                }
            }
        }

        // ------------------------------------------------------------ запуск

        private void OnRunAll(object sender, EventArgs e)
        {
            List<string> items = new List<string>();
            foreach (object item in fileList.Items)
            {
                string path = item as string;
                if (!string.IsNullOrEmpty(path))
                {
                    items.Add(path);
                }
            }

            if (items.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Сцена «" + CurrentScene.Name + "» пуста. Добавьте файлы, которые нужно открывать.",
                    AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            RunPaths(items, "Запускаю сцену");
        }

        private void RunPaths(List<string> items, string caption)
        {
            List<string> existing = new List<string>();
            List<string> missing = new List<string>();
            foreach (string path in items)
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    existing.Add(path);
                }
                else
                {
                    missing.Add(path);
                }
            }

            if (existing.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Ни один элемент не найден: файлы перемещены или удалены.",
                    AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (missing.Count > 0)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    string.Format("Не найдено элементов: {0}.{1}{1}Запустить остальные ({2} шт.)?",
                        missing.Count, Environment.NewLine, existing.Count),
                    AppTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            int delay = (int)delayBox.Value;
            int started = 0;
            List<string> failures = new List<string>();

            System.Windows.Forms.Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            runButton.Enabled = false;
            statusLabel.Text = string.Format("{0}: 0 из {1}…", caption, existing.Count);
            statusLabel.Refresh();

            try
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    try
                    {
                        StartItem(existing[i]);
                        started++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(Path.GetFileName(existing[i]) + " — " + ex.Message);
                    }

                    statusLabel.Text = string.Format("{0}: {1} из {2}…", caption, i + 1, existing.Count);
                    statusLabel.Refresh();

                    if (delay > 0 && i < existing.Count - 1)
                    {
                        Thread.Sleep(delay);
                    }
                }
            }
            finally
            {
                runButton.Enabled = true;
                Cursor = previousCursor;
            }

            statusLabel.Text = string.Format(
                "Запущено: {0} из {1}{2}",
                started,
                existing.Count,
                failures.Count > 0 ? string.Format("   ·   ошибок: {0}", failures.Count) : "");

            if (failures.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Не удалось запустить:" + Environment.NewLine + Environment.NewLine +
                        string.Join(Environment.NewLine, failures.ToArray()),
                    AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            if (minimizeCheck.Checked)
            {
                WindowState = FormWindowState.Minimized;
            }
        }

        private static void StartItem(string path)
        {
            string directory;
            if (Directory.Exists(path))
            {
                directory = path;
            }
            else
            {
                directory = Path.GetDirectoryName(path);
            }

            if (string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase))
            {
                ProcessStartInfo script = new ProcessStartInfo("powershell.exe");
                script.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\"";
                script.WorkingDirectory = directory;
                script.UseShellExecute = true;
                Process.Start(script);
                return;
            }

            ProcessStartInfo info = new ProcessStartInfo(path);
            info.UseShellExecute = true;
            if (!string.IsNullOrEmpty(directory))
            {
                info.WorkingDirectory = directory;
            }

            Process.Start(info);
        }

        // ---------------------------------------------------------- автозапуск

        private static string AutostartCommand()
        {
            return "\"" + Application.ExecutablePath + "\" " + Program.TrayArgument;
        }

        private static string ReadAutostartValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return null;
                    }

                    return key.GetValue(RunValueName) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsAutostartEnabled()
        {
            string value = ReadAutostartValue();
            return !string.IsNullOrEmpty(value);
        }

        private static bool TrySetAutostart(bool enabled, out string error)
        {
            error = null;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        error = "раздел автозапуска реестра недоступен";
                        return false;
                    }

                    if (enabled)
                    {
                        key.SetValue(RunValueName, AutostartCommand(), RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(RunValueName, false);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void SyncAutostartUi()
        {
            bool enabled = IsAutostartEnabled();

            if (enabled && !string.Equals(ReadAutostartValue(), AutostartCommand(), StringComparison.OrdinalIgnoreCase))
            {
                string ignored;
                TrySetAutostart(true, out ignored);
            }

            suppressAutostartEvent = true;
            autostartCheck.Checked = enabled;
            suppressAutostartEvent = false;

            if (trayAutostartItem != null)
            {
                trayAutostartItem.Checked = enabled;
            }
        }

        private void OnAutostartChanged(object sender, EventArgs e)
        {
            if (suppressAutostartEvent)
            {
                return;
            }

            bool desired = autostartCheck.Checked;
            string error;

            if (!TrySetAutostart(desired, out error))
            {
                suppressAutostartEvent = true;
                autostartCheck.Checked = !desired;
                suppressAutostartEvent = false;

                if (trayAutostartItem != null)
                {
                    trayAutostartItem.Checked = !desired;
                }

                MessageBox.Show(
                    this,
                    "Не удалось изменить автозапуск: " + error,
                    AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (trayAutostartItem != null)
            {
                trayAutostartItem.Checked = desired;
            }

            statusLabel.Text = desired
                ? "Автозапуск включён: при входе в Windows программа стартует свёрнутой в трей."
                : "Автозапуск выключен.";
        }

        // ----------------------------------------------------------- настройки

        private void LoadSettings()
        {
            bool legacyStartSet = false;
            string activeScene = null;

            try
            {
                if (File.Exists(settingsFile))
                {
                    foreach (string line in File.ReadAllLines(settingsFile, Encoding.UTF8))
                    {
                        string item = line.Trim();
                        int separator = item.IndexOf('=');
                        if (separator <= 0)
                        {
                            continue;
                        }

                        string key = item.Substring(0, separator).Trim();
                        string value = item.Substring(separator + 1).Trim();

                        if (string.Equals(key, "start-set", StringComparison.OrdinalIgnoreCase))
                        {
                            legacyStartSet = value == "1" ||
                                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (string.Equals(key, "active-scene", StringComparison.OrdinalIgnoreCase))
                        {
                            activeScene = value;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // повреждённые настройки просто игнорируем
            }

            if (!string.IsNullOrEmpty(activeScene))
            {
                int index = IndexOfScene(activeScene);
                if (index >= 0 && index != sceneIndex)
                {
                    sceneIndex = index;
                    SyncSceneList();
                    ShowScene();
                }
            }

            // перенос старой общей настройки «сразу запускать набор» на сцену
            if (legacyStartSet && !AnySceneAutoRun())
            {
                CurrentScene.AutoRun = true;
                SaveScenes();
            }

            UpdateStartSetUi();
        }

        private bool AnySceneAutoRun()
        {
            foreach (Scene scene in scenes)
            {
                if (scene.AutoRun)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateStartSetUi()
        {
            suppressSettingsEvent = true;
            startSetCheck.Checked = CurrentScene.AutoRun;
            suppressSettingsEvent = false;
        }

        private void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(dataFolder))
                {
                    Directory.CreateDirectory(dataFolder);
                }

                string text = "active-scene=" + CurrentScene.Name + "\r\n";

                File.WriteAllText(settingsFile, text, Encoding.UTF8);
            }
            catch (Exception)
            {
                // не критично
            }
        }

        private void OnStartSetChanged(object sender, EventArgs e)
        {
            if (suppressSettingsEvent)
            {
                return;
            }

            if (startSetCheck.Checked && !autostartCheck.Checked)
            {
                autostartCheck.Checked = true;

                if (!autostartCheck.Checked)
                {
                    suppressSettingsEvent = true;
                    startSetCheck.Checked = false;
                    suppressSettingsEvent = false;
                    return;
                }
            }

            CurrentScene.AutoRun = startSetCheck.Checked;
            SaveScenes();

            statusLabel.Text = startSetCheck.Checked
                ? "Сцена «" + CurrentScene.Name + "» будет открываться при входе в Windows."
                : "Сцена «" + CurrentScene.Name + "» при входе открываться не будет.";
        }

        private void ScheduleStartupRun()
        {
            startupTimer = new System.Windows.Forms.Timer();
            startupTimer.Interval = 1500;
            startupTimer.Tick += OnStartupTimerTick;
            startupTimer.Start();
        }

        private List<string> CollectAutoRunPaths()
        {
            List<string> result = new List<string>();

            foreach (Scene scene in scenes)
            {
                if (!scene.AutoRun)
                {
                    continue;
                }

                foreach (string path in scene.Paths)
                {
                    if (result.Contains(path))
                    {
                        continue;
                    }

                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        result.Add(path);
                    }
                }
            }

            return result;
        }

        private void OnStartupTimerTick(object sender, EventArgs e)
        {
            if (startupTimer != null)
            {
                startupTimer.Stop();
                startupTimer.Dispose();
                startupTimer = null;
            }

            List<string> paths = CollectAutoRunPaths();
            if (paths.Count > 0)
            {
                RunPaths(paths, "Запускаю при входе");
            }
        }

        // -------------------------------------------------------------- данные

        private void AddPaths(IEnumerable<string> paths)
        {
            int added = 0;

            foreach (string raw in paths)
            {
                string path = (raw ?? string.Empty).Trim().Trim('"');
                if (path.Length == 0)
                {
                    continue;
                }

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    continue;
                }

                if (fileList.Items.Contains(path))
                {
                    continue;
                }

                fileList.Items.Add(path);
                added++;
            }

            if (added > 0)
            {
                CaptureCurrentScene();
                SaveScenes();
                UpdateStatus();
            }
        }

        private void MoveSelected(int delta)
        {
            int index = fileList.SelectedIndex;
            if (index < 0)
            {
                return;
            }

            int target = index + delta;
            if (target < 0 || target >= fileList.Items.Count)
            {
                return;
            }

            object item = fileList.Items[index];
            fileList.Items.RemoveAt(index);
            fileList.Items.Insert(target, item);
            fileList.SelectedIndex = target;

            CaptureCurrentScene();
            SaveScenes();
        }

        private void UpdateStatus()
        {
            int total = fileList.Items.Count;
            int missing = 0;

            foreach (object item in fileList.Items)
            {
                string path = item as string;
                if (path != null && !File.Exists(path) && !Directory.Exists(path))
                {
                    missing++;
                }
            }

            string text;
            if (total == 0)
            {
                text = "Сцена пуста — добавьте файлы или перетащите их в окно.";
            }
            else if (missing > 0)
            {
                text = string.Format("Элементов: {0}   ·   не найдено: {1}", total, missing);
            }
            else
            {
                text = string.Format("Элементов: {0}", total);
            }

            if (saveError != null)
            {
                text += "   ·   список не сохраняется: " + saveError;
            }

            statusLabel.Text = text;
        }
    }
}




