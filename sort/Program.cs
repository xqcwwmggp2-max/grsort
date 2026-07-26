using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Media;

namespace DownloadsSorter
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SorterForm());
        }
    }

    public class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; } = 15;
        public RoundedPanel() => DoubleBuffered = true;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = CreateRoundRectPath(ClientRectangle, CornerRadius);
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Region = new Region(CreateRoundRectPath(ClientRectangle, CornerRadius));
        }

        private GraphicsPath CreateRoundRectPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
            path.AddArc(rect.Width - radius, rect.Y, radius, radius, 270, 90);
            path.AddArc(rect.Width - radius, rect.Height - radius, radius, radius, 0, 90);
            path.AddArc(rect.X, rect.Height - radius, radius, radius, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class RoundedButton : Button
    {
        public int CornerRadius { get; set; } = 15;
        private bool _isHovered, _isPressed;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _isHovered = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _isHovered = false; _isPressed = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _isPressed = true; base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { _isPressed = false; base.OnMouseUp(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color baseColor = BackColor;
            if (_isPressed) baseColor = ControlPaint.Dark(baseColor, 0.15f);
            else if (_isHovered) baseColor = ControlPaint.Light(baseColor, 0.15f);

            using (var path = CreateRoundRectPath(ClientRectangle, CornerRadius))
            using (var brush = new SolidBrush(baseColor))
                e.Graphics.FillPath(brush, path);

            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private GraphicsPath CreateRoundRectPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
            path.AddArc(rect.Width - radius, rect.Y, radius, radius, 270, 90);
            path.AddArc(rect.Width - radius, rect.Height - radius, radius, radius, 0, 90);
            path.AddArc(rect.X, rect.Height - radius, radius, radius, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class MoveRecord
    {
        public string OriginalPath { get; set; }
        public string NewPath { get; set; }
    }

    public class SorterForm : Form
    {
        private string _currentPath;
        private readonly Dictionary<string, HashSet<string>> _categoryMap;
        private Dictionary<string, CategoryProgress> _categoryProgressBars;
        private bool _isRunning;

        private RoundedButton btnSort;
        private RoundedButton btnUndo;
        private RoundedButton btnChangeFolder;
        private RoundedButton btnFAQ;
        private RoundedButton btnMinimize;
        private RoundedButton btnClose;
        private Label lblStatus;
        private RoundedPanel faqPopup;
        private Timer fadeTimer;

        private int _fadeAlpha = 0;
        private bool _isFadingIn = false;
        private bool _isFadingOut = false;

        private const int WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        private readonly List<MoveRecord> _moveHistory = new List<MoveRecord>();
        private readonly HashSet<string> _createdFolders = new HashSet<string>();

        private readonly Dictionary<string, Color> _categoryColors;

        public SorterForm()
        {
            Text = "GR Sort Pro";
            Size = new Size(750, 900);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            BackColor = Color.FromArgb(20, 20, 45);

            _currentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            _categoryMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Images", new HashSet<string> { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".svg", ".webp", ".ico" } },
                { "Videos", new HashSet<string> { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v" } },
                { "Documents", new HashSet<string> { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".csv", ".odt" } },
                { "Audio", new HashSet<string> { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" } },
                { "Archives", new HashSet<string> { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" } },
                { "Executables", new HashSet<string> { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".lnk" } },
                { "Code", new HashSet<string> { ".cs", ".js", ".py", ".html", ".css", ".json", ".xml", ".cpp", ".java", ".ts", ".php" } }
            };

            _categoryColors = new Dictionary<string, Color>
            {
                { "Images", Color.FromArgb(100, 180, 255) },
                { "Videos", Color.FromArgb(180, 100, 255) },
                { "Documents", Color.FromArgb(100, 150, 255) },
                { "Audio", Color.FromArgb(200, 100, 255) },
                { "Archives", Color.FromArgb(100, 200, 255) },
                { "Executables", Color.FromArgb(220, 100, 200) },
                { "Code", Color.FromArgb(100, 220, 200) },
                { "Other", Color.FromArgb(180, 180, 180) }
            };

            _categoryProgressBars = new Dictionary<string, CategoryProgress>();

            fadeTimer = new Timer { Interval = 16 };
            fadeTimer.Tick += FadeTimer_Tick;

            InitializeUI();
            InitializeFAQPopup();
            ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            var path = new GraphicsPath();
            int r = 20;
            path.AddArc(0, 0, r, r, 180, 90);
            path.AddArc(Width - r, 0, r, r, 270, 90);
            path.AddArc(Width - r, Height - r, r, r, 0, 90);
            path.AddArc(0, Height - r, r, r, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ApplyRoundedRegion(); }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && m.Result == (IntPtr)HTCLIENT)
            {
                m.Result = (IntPtr)HTCAPTION;
            }
        }

        private void InitializeUI()
        {
            btnFAQ = new RoundedButton
            {
                Text = "?",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 100),
                Location = new Point(615, 15),
                Size = new Size(40, 40),
                CornerRadius = 20
            };
            btnFAQ.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) ShowFAQWithAnimation(); };
            btnFAQ.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) HideFAQWithAnimation(); };
            btnFAQ.MouseLeave += (s, e) => HideFAQWithAnimation();
            Controls.Add(btnFAQ);

            btnMinimize = new RoundedButton
            {
                Text = "−",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(50, 50, 90),
                Location = new Point(660, 15),
                Size = new Size(40, 40),
                CornerRadius = 12
            };
            btnMinimize.FlatAppearance.MouseOverBackColor = Color.FromArgb(100, 100, 150);
            btnMinimize.Click += (s, e) => this.WindowState = FormWindowState.Minimized;
            Controls.Add(btnMinimize);

            btnClose = new RoundedButton
            {
                Text = "✕",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(50, 50, 90),
                Location = new Point(705, 15),
                Size = new Size(40, 40),
                CornerRadius = 12
            };
            btnClose.MouseEnter += (s, e) => { btnClose.BackColor = Color.FromArgb(220, 60, 60); btnClose.Invalidate(); };
            btnClose.MouseLeave += (s, e) => { btnClose.BackColor = Color.FromArgb(50, 50, 90); btnClose.Invalidate(); };
            btnClose.Click += (s, e) => this.Close();
            Controls.Add(btnClose);

            var lblTitle = new Label
            {
                Text = "Сортировщик загрузок",
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(30, 20),
                Size = new Size(670, 45),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblSubtitle = new Label
            {
                Text = "Решение файлового беспорядка за мгновение",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(150, 160, 190),
                Location = new Point(30, 65),
                Size = new Size(670, 25),
                TextAlign = ContentAlignment.MiddleCenter
            };

            int yPos = 110;
            foreach (var category in _categoryMap.Keys.Concat(new[] { "Other" }))
            {
                var categoryPanel = CreateCategoryPanel(category, yPos);
                Controls.Add(categoryPanel);
                _categoryProgressBars[category] = new CategoryProgress
                {
                    ProgressBar = categoryPanel.Controls["progressBar"] as ProgressBar,
                    CountLabel = categoryPanel.Controls["lblCount"] as Label
                };
                yPos += 75;
            }

            var controlPanel = new RoundedPanel
            {
                Location = new Point(30, 710),
                Size = new Size(670, 140),
                BackColor = Color.FromArgb(30, 30, 60),
                CornerRadius = 15
            };
            Controls.Add(controlPanel);

            btnChangeFolder = new RoundedButton
            {
                Text = "📂 Выбрать папку",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(50, 50, 80),
                Location = new Point(20, 20),
                Size = new Size(180, 50),
                CornerRadius = 12
            };
            btnChangeFolder.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 110);
            btnChangeFolder.Click += (s, e) => SelectFolderDialog();
            controlPanel.Controls.Add(btnChangeFolder);

            btnSort = new RoundedButton
            {
                Text = "НАЧАТЬ СОРТИРОВКУ",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(88, 101, 242),
                Location = new Point(220, 20),
                Size = new Size(250, 50),
                CornerRadius = 12
            };
            btnSort.FlatAppearance.MouseOverBackColor = Color.FromArgb(120, 130, 255);
            btnSort.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 70, 200);
            btnSort.Click += BtnSort_Click;
            controlPanel.Controls.Add(btnSort);

            btnUndo = new RoundedButton
            {
                Text = "↩️ ОТМЕНИТЬ",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 200),
                BackColor = Color.FromArgb(40, 40, 60),
                Location = new Point(490, 20),
                Size = new Size(160, 50),
                CornerRadius = 12,
                Visible = false
            };
            btnUndo.FlatAppearance.MouseOverBackColor = Color.FromArgb(150, 50, 50);
            btnUndo.Click += BtnUndo_Click;
            controlPanel.Controls.Add(btnUndo);

            var lblPath = new Label
            {
                Text = "Путь: " + _currentPath,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(150, 160, 190),
                Location = new Point(20, 85),
                Size = new Size(630, 20),
                AutoSize = false,
                TextAlign = ContentAlignment.TopCenter
            };
            controlPanel.Controls.Add(lblPath);

            lblStatus = new Label
            {
                Text = "Готов к работе",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 255, 150),
                Location = new Point(20, 110),
                Size = new Size(630, 25),
                TextAlign = ContentAlignment.MiddleCenter
            };
            controlPanel.Controls.Add(lblStatus);

            Controls.Add(lblTitle);
            Controls.Add(lblSubtitle);
        }

        private void InitializeFAQPopup()
        {
            faqPopup = new RoundedPanel
            {
                Size = new Size(320, 350),
                BackColor = Color.FromArgb(0, 35, 35, 75),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false,
                Location = new Point(380, 70),
                Padding = new Padding(15),
                CornerRadius = 15
            };

            var lblFAQTitle = new Label
            {
                Text = "ℹ️ О программе",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(15, 15),
                Size = new Size(270, 30),
                AutoSize = false
            };
            faqPopup.Controls.Add(lblFAQTitle);

            var sep1 = new Panel { Location = new Point(15, 50), Size = new Size(270, 1), BackColor = Color.FromArgb(100, 100, 150) };
            faqPopup.Controls.Add(sep1);

            var lblInfo = new Label
            {
                Text = "📦 Сортировщик загрузок Pro\n🔖 Версия: 0.0.1\n Дата: 19.05.2026\n🔧 Платформа: Windows (.NET)",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(180, 180, 220),
                Location = new Point(15, 65),
                Size = new Size(270, 70),
                AutoSize = false
            };
            faqPopup.Controls.Add(lblInfo);

            var sep2 = new Panel { Location = new Point(15, 145), Size = new Size(270, 1), BackColor = Color.FromArgb(100, 100, 150) };
            faqPopup.Controls.Add(sep2);

            var lblDev = new Label
            {
                Text = "👨‍💻 Разработчик: \nGromov Developer",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(180, 180, 220),
                Location = new Point(15, 160),
                Size = new Size(270, 40),
                AutoSize = false
            };
            faqPopup.Controls.Add(lblDev);

            var sep3 = new Panel { Location = new Point(15, 210), Size = new Size(270, 1), BackColor = Color.FromArgb(100, 100, 150) };
            faqPopup.Controls.Add(sep3);

            var lblFAQ = new Label
            {
                Text = "❓ FAQ:\n• Куда сортируются файлы?\n  → В подпапки внутри выбранной папки\n• Можно отменить?\n  → Да, кнопка «Отменить» вернёт всё назад\n• Удаляет ли файлы?\n  → Нет, только перемещает",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(150, 160, 190),
                Location = new Point(15, 225),
                Size = new Size(270, 90),
                AutoSize = false
            };
            faqPopup.Controls.Add(lblFAQ);

            Controls.Add(faqPopup);
            faqPopup.BringToFront();
        }

        private void ShowFAQWithAnimation()
        {
            if (_isFadingOut) return;
            faqPopup.Location = new Point(btnFAQ.Right - faqPopup.Width, btnFAQ.Bottom + 5);
            faqPopup.Visible = true;
            faqPopup.BringToFront();
            _isFadingIn = true; _isFadingOut = false; _fadeAlpha = 0;
            UpdatePopupOpacity();
            fadeTimer.Start();
            btnFAQ.BackColor = Color.FromArgb(100, 100, 180);
        }

        private void HideFAQWithAnimation()
        {
            if (!faqPopup.Visible) return;
            _isFadingIn = false; _isFadingOut = true;
            fadeTimer.Start();
            btnFAQ.BackColor = Color.FromArgb(60, 60, 100);
        }

        private void FadeTimer_Tick(object sender, EventArgs e)
        {
            if (_isFadingIn)
            {
                _fadeAlpha += 20;
                if (_fadeAlpha >= 255) { _fadeAlpha = 255; _isFadingIn = false; fadeTimer.Stop(); }
                UpdatePopupOpacity();
            }
            else if (_isFadingOut)
            {
                _fadeAlpha -= 20;
                if (_fadeAlpha <= 0) { _fadeAlpha = 0; _isFadingOut = false; faqPopup.Visible = false; fadeTimer.Stop(); return; }
                UpdatePopupOpacity();
            }
        }

        private void UpdatePopupOpacity()
        {
            faqPopup.BackColor = Color.FromArgb(_fadeAlpha, 35, 35, 75);
            faqPopup.Invalidate();
        }

        private void SelectFolderDialog()
        {
            HideFAQWithAnimation();
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = _currentPath;
                fbd.Description = "Выберите папку для сортировки:";
                if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    _currentPath = fbd.SelectedPath;
                    lblStatus.Text = "Путь изменён: " + _currentPath;
                    lblStatus.ForeColor = Color.FromArgb(150, 160, 190);
                    btnUndo.Visible = false;
                    _moveHistory.Clear();
                    _createdFolders.Clear();
                }
            }
        }

        private RoundedPanel CreateCategoryPanel(string categoryName, int yPos)
        {
            var panel = new RoundedPanel
            {
                Location = new Point(30, yPos),
                Size = new Size(670, 65),
                BackColor = Color.FromArgb(35, 35, 70),
                CornerRadius = 12
            };

            var iconLabel = new Label
            {
                Text = GetCategoryIcon(categoryName),
                Font = new Font("Segoe UI", 18),
                ForeColor = _categoryColors[categoryName],
                Location = new Point(15, 18),
                Size = new Size(40, 30),
                TextAlign = ContentAlignment.MiddleCenter
            };
            panel.Controls.Add(iconLabel);

            var lblCategory = new Label
            {
                Name = "lblCategory",
                Text = GetCategoryDisplayName(categoryName),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(70, 12),
                Size = new Size(180, 25)
            };
            panel.Controls.Add(lblCategory);

            var lblCount = new Label
            {
                Name = "lblCount",
                Text = "0 файлов",
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = _categoryColors[categoryName],
                Location = new Point(540, 14),
                Size = new Size(120, 20),
                TextAlign = ContentAlignment.MiddleRight
            };
            panel.Controls.Add(lblCount);

            var progressBar = new ProgressBar
            {
                Name = "progressBar",
                Location = new Point(70, 38),
                Size = new Size(590, 16),
                Maximum = 100,
                Value = 0
            };
            panel.Controls.Add(progressBar);
            return panel;
        }

        private string GetCategoryIcon(string category) => category switch
        {
            "Images" => "🖼️",
            "Videos" => "🎬",
            "Documents" => "📄",
            "Audio" => "🎵",
            "Archives" => "📦",
            "Executables" => "️⚙️",
            "Code" => "💻",
            _ => "📁"
        };

        private string GetCategoryDisplayName(string category) => category switch
        {
            "Images" => "Изображения",
            "Videos" => "Видео",
            "Documents" => "Документы",
            "Audio" => "Аудио",
            "Archives" => "Архивы",
            "Executables" => "Программы",
            "Code" => "Код",
            "Other" => "Другое",
            _ => category
        };

        private void BtnSort_Click(object sender, EventArgs e)
        {
            HideFAQWithAnimation();
            if (_isRunning) return;
            if (!Directory.Exists(_currentPath))
            {
                MessageBox.Show("Папка не найдена.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _isRunning = true;
            _moveHistory.Clear();
            _createdFolders.Clear();
            btnUndo.Visible = false;

            btnSort.Enabled = false;
            btnChangeFolder.Enabled = false;
            btnSort.BackColor = Color.FromArgb(80, 80, 150);
            btnSort.Text = "Работаю...";
            lblStatus.Text = "Сортировка файлов...";
            lblStatus.ForeColor = Color.FromArgb(255, 200, 100);

            foreach (var cp in _categoryProgressBars.Values) { cp.ProgressBar.Value = 0; cp.CountLabel.Text = "0 файлов"; }

            try
            {
                SortDownloads();
                if (_moveHistory.Count > 0) btnUndo.Visible = true;
                lblStatus.Text = "Сортировка завершена успешно!";
                lblStatus.ForeColor = Color.FromArgb(100, 255, 150);
                SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Ошибка: " + ex.Message;
                lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
                SystemSounds.Hand.Play();
            }
            finally
            {
                _isRunning = false;
                btnSort.Enabled = true;
                btnChangeFolder.Enabled = true;
                btnSort.BackColor = Color.FromArgb(88, 101, 242);
                btnSort.Text = "НАЧАТЬ СОРТИРОВКУ";
            }
        }

        private void BtnUndo_Click(object sender, EventArgs e)
        {
            HideFAQWithAnimation();
            if (_isRunning || _moveHistory.Count == 0) return;
            if (MessageBox.Show("Вернуть все файлы на исходные места и удалить пустые папки?", "Отмена операции",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;

            _isRunning = true;
            btnUndo.Enabled = false;
            btnSort.Enabled = false;
            btnChangeFolder.Enabled = false;
            lblStatus.Text = "Возвращаю файлы...";
            lblStatus.ForeColor = Color.FromArgb(200, 200, 100);

            try
            {
                int successCount = 0;
                foreach (var record in _moveHistory.AsEnumerable().Reverse())
                    if (File.Exists(record.NewPath) && !File.Exists(record.OriginalPath))
                        try { File.Move(record.NewPath, record.OriginalPath); successCount++; } catch { }

                int deletedFolders = 0;
                foreach (var folder in _createdFolders)
                    try { if (Directory.Exists(folder) && Directory.GetFiles(folder).Length == 0) { Directory.Delete(folder); deletedFolders++; } } catch { }

                lblStatus.Text = $"Отменено. Файлов: {successCount}, Удалено папок: {deletedFolders}.";
                lblStatus.ForeColor = Color.FromArgb(100, 255, 150);
                btnUndo.Visible = false;
                _moveHistory.Clear();
                _createdFolders.Clear();
                SystemSounds.Asterisk.Play();
            }
            finally
            {
                _isRunning = false;
                btnUndo.Enabled = true;
                btnSort.Enabled = true;
                btnChangeFolder.Enabled = true;
            }
        }

        private void SortDownloads()
        {
            var files = Directory.EnumerateFiles(_currentPath, "*.*", SearchOption.TopDirectoryOnly)
                                 .Where(f => !IsSystemOrHidden(f)).ToList();
            if (files.Count == 0) { lblStatus.Text = "Папка пуста."; return; }

            var filesByCategory = new Dictionary<string, List<string>>();
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                string category = string.IsNullOrEmpty(ext) ? "Other" : GetCategory(ext);
                if (!filesByCategory.ContainsKey(category)) filesByCategory[category] = new List<string>();
                filesByCategory[category].Add(file);
            }

            foreach (var kvp in filesByCategory)
            {
                string category = kvp.Key;
                var categoryFiles = kvp.Value;
                if (!_categoryProgressBars.ContainsKey(category)) continue;

                var cp = _categoryProgressBars[category];
                cp.ProgressBar.Maximum = categoryFiles.Count;
                cp.CountLabel.Text = categoryFiles.Count + " файлов";
                cp.CountLabel.ForeColor = _categoryColors[category];

                string targetDir = Path.Combine(_currentPath, category);
                if (!Directory.Exists(targetDir)) { Directory.CreateDirectory(targetDir); _createdFolders.Add(targetDir); }

                int processed = 0;
                foreach (var file in categoryFiles)
                {
                    string targetPath = Path.Combine(targetDir, Path.GetFileName(file));
                    if (File.Exists(targetPath)) targetPath = GetUniquePath(targetDir, Path.GetFileName(file));

                    try
                    {
                        _moveHistory.Add(new MoveRecord { OriginalPath = file, NewPath = targetPath });
                        File.Move(file, targetPath);
                        processed++;
                    }
                    catch { if (_moveHistory.Count > 0) _moveHistory.RemoveAt(_moveHistory.Count - 1); }
                    cp.ProgressBar.Value = processed;
                }
            }
        }

        private bool IsSystemOrHidden(string path)
        {
            try { var attr = File.GetAttributes(path); return (attr & FileAttributes.Hidden) != 0 || (attr & FileAttributes.System) != 0; }
            catch { return true; }
        }

        private string GetCategory(string ext)
        {
            foreach (var kvp in _categoryMap) if (kvp.Value.Contains(ext)) return kvp.Key;
            return "Other";
        }

        private string GetUniquePath(string directory, string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1; string newPath;
            do { newPath = Path.Combine(directory, name + "_" + counter + ext); counter++; } while (File.Exists(newPath));
            return newPath;
        }
    }

    public class CategoryProgress
    {
        public ProgressBar ProgressBar { get; set; }
        public Label CountLabel { get; set; }
    }
}