using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoiceMeeter_Overlay
{
    /// <summary>
    /// Voicemeeter overlay window displaying Strip[5..7].Gain.
    /// - F8: toggle visibility
    /// - F7: toggle click-through / interactive mode
    /// - F6: toggle vertical / horizontal sliders
    /// - In interactive mode: double-click to choose slider color, drag window with left mouse
    /// </summary>
    public partial class Form1 : Form
    {      
        [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_IsParametersDirty")]
        private static extern int VBVMR_IsParametersDirty();

        // ===== Win32 interop =====

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, Keys vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_HOTKEY = 0x0312;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        private const int HOTKEY_ID_SHOWHIDE = 1;
        private const int HOTKEY_ID_CLICKTHROUGH = 2;
        private const int HOTKEY_ID_LAYOUT = 3;    
        // ===== Data & state =====

        private readonly float[] _virtualGains = new float[3]; // Strip[5..7].Gain values
        private readonly string[] _channelNames =
        {
            "Channel 1",    // Strip[5]
            "Channel 2",     // Strip[6]
            "Channel 3"   // Strip[7]
        };

        private System.Windows.Forms.Timer _timer;

        private bool _isClickThrough = false;   // start in interactive mode
        private bool _isVerticalLayout = true;
        private bool _showSettings = false;

        private Color _gainBarColor = Color.LightSkyBlue;

        // Hotkey bindings.
        private Keys _showHideKey;
        private Keys _clickThroughKey;
        private Keys _layoutKey;

        // Settings UI
        private Panel _settingsPanel = null!;
        private Button _btnMetersTab = null!;
        private Button _btnSettingsTab = null!;
        private CheckBox _chkClickThrough = null!;
        private RadioButton _radHorizontal = null!;
        private RadioButton _radVertical = null!;
        private Button _btnSliderColor = null!;

        private TextBox _txtShowHideKey = null!;
        private TextBox _txtClickThroughKey = null!;
        private TextBox _txtLayoutKey = null!;

        private bool _captureShowHideKey;
        private bool _captureClickThroughKey;
        private bool _captureLayoutKey;

        private bool _vmParamsInitialized = false;
        /// <summary>
        /// Main overlay form constructor.
        /// </summary>
        public Form1()
        {
            // Default keybinds.
            _showHideKey = Keys.F8;   // default show/hide
            _clickThroughKey = Keys.F9;
            _layoutKey = Keys.F10;

            InitializeComponent();
            SetupOverlayWindow();
            LoginVoicemeeter();
            SetupTimer();

            DoubleBuffered = true;
            MouseDown += Form1_MouseDown;
            MouseDoubleClick += Form1_MouseDoubleClick;
        }

        /// <summary>
        /// Construct basic UI controls (tabs + settings panel).
        /// </summary>
        private void InitializeComponent()
        {
            SuspendLayout();

            // Base form size (wider + taller >300px).
            ClientSize = new Size(750, 550);
            Text = "Voicemeeter Overlay";
            KeyPreview = true;

            // Tab buttons (top-left).
            _btnMetersTab = new Button
            {
                BackColor = _gainBarColor,
                Text = "Meters",
                Location = new Point(10, 5),
                Size = new Size(120, 24),
                FlatStyle = FlatStyle.Flat
            };
            _btnMetersTab.FlatAppearance.BorderSize = 0;
            _btnMetersTab.Click += (s, e) => ShowSettings(false);            
            Controls.Add(_btnMetersTab);
            
            _btnSettingsTab = new Button
            {
                BackColor = _gainBarColor,
                Text = "Settings",
                Location = new Point(650, 5),
                Size = new Size(80, 24),
                FlatStyle = FlatStyle.Flat
            };
            _btnSettingsTab.FlatAppearance.BorderSize = 0;
            _btnSettingsTab.Click += (s, e) => ShowSettings(true);         
            Controls.Add(_btnSettingsTab);

            // Settings panel below the tab row.
            _settingsPanel = new Panel
            {
                BackColor = Color.White,
                Location = new Point(0, 30),
                Size = new Size(ClientSize.Width, ClientSize.Height - 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Padding = new Padding(16, 10, 16, 10),
                Visible = false
            };
            Controls.Add(_settingsPanel);

            BuildSettingsUI();

            ResumeLayout(false);
        }

        /// <summary>
        /// Build controls inside the Settings panel.
        /// </summary>
        private void BuildSettingsUI()
        {
            int y = 10;

            // Layout group
            Label lblLayout = new Label
            {
                Text = "Layout:",
                AutoSize = true,
                Location = new Point(10, y + 4)
            };
            _settingsPanel.Controls.Add(lblLayout);

            _radHorizontal = new RadioButton
            {
                Text = "Horizontal",
                AutoSize = true,
                Location = new Point(80, y),
                Checked = false
            };
            _radHorizontal.CheckedChanged += (s, e) =>
            {
                if (_radHorizontal.Checked)
                {
                    _isVerticalLayout = false;
                    Invalidate();
                }
            };
            _settingsPanel.Controls.Add(_radHorizontal);

            _radVertical = new RadioButton
            {
                Text = "Vertical",
                AutoSize = true,
                Location = new Point(180, y),
                Checked = true
            };
            _radVertical.CheckedChanged += (s, e) =>
            {
                if (_radVertical.Checked)
                {
                    _isVerticalLayout = true;
                    Invalidate();
                }
            };
            _settingsPanel.Controls.Add(_radVertical);

            y += 30;

            // Click-through
            _chkClickThrough = new CheckBox
            {
                Text = "Click-through mode (overlay ignores mouse)",
                AutoSize = true,
                Location = new Point(10, y),
                Checked = _isClickThrough
            };
            _chkClickThrough.CheckedChanged += (s, e) =>
            {
                if (_clickThroughKey != Keys.None) {
                    _isClickThrough = _chkClickThrough.Checked;
                    ApplyClickThrough(_isClickThrough);
                    Invalidate();
                }
                else
                {
                    _chkClickThrough.Checked = false;
                    MessageBox.Show("Please set a keybind for Click Through!");
                   
                }
                ;
            };
            _settingsPanel.Controls.Add(_chkClickThrough);

            y += 35;

            // Slider color
            Label lblColor = new Label
            {
                Text = "Slider color:",
                AutoSize = true,
                Location = new Point(10, y + 4)
            };
            _settingsPanel.Controls.Add(lblColor);

            _btnSliderColor = new Button
            {
                Text = "Change…",
                Location = new Point(100, y),
                Size = new Size(90, 24)
            };
            _btnSliderColor.Click += BtnSliderColor_Click;
            _settingsPanel.Controls.Add(_btnSliderColor);

            y += 40;

            // Separator
            Label sep = new Label
            {
                Text = "Keybinds (click box, then press a key):",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(10, y)
            };
            _settingsPanel.Controls.Add(sep);

            y += 30;

            // We’ll align keybind rows in a compact grid.
            AddKeybindRow(
                _settingsPanel,
                ref y,
                "Show / Hide Overlay",
                ref _txtShowHideKey,
                StartCaptureShowHide,
                ShowHideKeyBox_KeyDown,
                _showHideKey
            );

            AddKeybindRow(
                _settingsPanel,
                ref y,
                "Toggle Click-through",
                ref _txtClickThroughKey,
                StartCaptureClickThrough,
                ClickThroughKeyBox_KeyDown,
                _clickThroughKey
            );

            AddKeybindRow(
                _settingsPanel,
                ref y,
                "Toggle Layout",
                ref _txtLayoutKey,
                StartCaptureLayout,
                LayoutKeyBox_KeyDown,
                _layoutKey
            );

            y += 10;

            // Restore defaults
            Button btnDefaults = new Button
            {
                BackColor = Color.White,
                Text = "Restore defaults",
                Location = new Point(10, y),
                Size = new Size(130, 28)
            };
            btnDefaults.Click += BtnDefaults_Click;
            _settingsPanel.Controls.Add(btnDefaults);
        }

        /// <summary>
        /// Helper to create a single keybind row.
        /// </summary>
        private void AddKeybindRow(
            Panel panel,
            ref int y,
            string labelText,
            ref TextBox keyBox,
            EventHandler clickHandler,
            KeyEventHandler keyHandler,
            Keys initialKey)
        {
            Label lbl = new Label
            {
                Text = labelText,
                AutoSize = true,
                Location = new Point(10, y + 4)
            };
            panel.Controls.Add(lbl);

            keyBox = new TextBox
            {
                Location = new Point(200, y),
                Size = new Size(90, 22),
                ReadOnly = true,
                Text = KeyToDisplay(initialKey),
                TextAlign = HorizontalAlignment.Center
            };
            keyBox.Click += clickHandler;
            keyBox.KeyDown += keyHandler;
            panel.Controls.Add(keyBox);

            y += 28;
        }

        /// <summary>
        /// Setup overlay window chrome & position.
        /// </summary>
        private void SetupOverlayWindow()
        {
            // Resizable window now.
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            BackColor = Color.Black;
            TransparencyKey = BackColor;
            StartPosition = FormStartPosition.Manual;

            Width = 750;
            Height = 350;
            MaximizeBox = false;
            MinimizeBox = true;
            MaximumSize = new Size(850, 600);
            MaximumSize = new Size(850, 600);
            Location = new Point(
                Screen.PrimaryScreen.Bounds.Width / 2 - Width / 6,
                40
            );
        }

        /// <summary>
        /// After handle is created, register initial hotkeys and apply click-through.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterAllHotkeys();
            ApplyClickThrough(_isClickThrough);
        }

        /// <summary>
        /// On first load: start on Settings tab and show info message.
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            ShowSettings(true); // start on settings

            MessageBox.Show(
                "Voicemeeter Overlay\n\n" +
                "- Displays live gains for Strip[5], Strip[6], Strip[7].\n" +
                "- Default keybind: F8 toggles show/hide.\n" +
                "- Use the Settings tab to change layout, click-through, and keybinds.",
                "Voicemeeter Overlay",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        /// <summary>
        /// Logs into Voicemeeter Remote API.
        /// </summary>
        private void LoginVoicemeeter()
        {
            try
            {
                VoicemeeterRemote.VBVMR_Login();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Voicemeeter login failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Timer to poll Voicemeeter every 50 ms.
        /// </summary>
        private void SetupTimer()
        {
            _timer = new System.Windows.Forms.Timer
            {
                Interval = 50
            };
            _timer.Tick += (s, e) =>
            {
                UpdateData();
                Invalidate();
            };
            _timer.Start();
        }

        /// <summary>
        /// Poll Strip[5..7].Gain from Voicemeeter.
        /// </summary>
        private void UpdateData()
        {
            // Refresh Voicemeeter’s internal mirror & check for changes
            int dirty = VBVMR_IsParametersDirty();

            // First run: force one read even if dirty == 0
            if (dirty == 0 && _vmParamsInitialized)
                return;

            _vmParamsInitialized = true;

            float minDb = -60f;
            float maxDb = 12f;

            for (int i = 0; i < 3; i++)
            {
                int stripIndex = 5 + i;                  // 5, 6, 7
                string param = $"Strip[{stripIndex}].Gain";

                if (VoicemeeterRemote.VBVMR_GetParameterFloat(param, out float gain) == 0)
                {
                    // clamp just in case (optional)
                    if (gain < minDb) gain = minDb;
                    if (gain > maxDb) gain = maxDb;
                    _virtualGains[i] = gain;
                }
            }
        }

        /// <summary>
        /// Show either meters or settings.
        /// </summary>
        private void ShowSettings(bool show)
        {
            _btnMetersTab.Visible = show;
            _btnSettingsTab.Visible = !show;
            _showSettings = show;
            
            _btnSettingsTab.Location = new Point(show ? 650 : 10, 5);

            _settingsPanel.Visible = show;

            _btnMetersTab.Enabled = show;
            _btnSettingsTab.Enabled = !show;

            if (_showSettings)
            {
                
            };

            Invalidate();
        }

        /// <summary>
        /// Apply click-through (true) or interactive (false) window style.
        /// </summary>
        private void ApplyClickThrough(bool enable)
        {   
            if (Handle == IntPtr.Zero) return;

            int style = GetWindowLong(Handle, GWL_EXSTYLE);
            style |= WS_EX_LAYERED;

            if (enable)
                style |= WS_EX_TRANSPARENT;
            else
                style &= ~WS_EX_TRANSPARENT;

            SetWindowLong(Handle, GWL_EXSTYLE, style);
        }

        /// <summary>
        /// Handle global hotkeys for show/hide, click-through and layout.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID_SHOWHIDE)
                {
                    Visible = !Visible;
                }
                else if (id == HOTKEY_ID_CLICKTHROUGH)
                {
                    _isClickThrough = !_isClickThrough;
                    _chkClickThrough.Checked = _isClickThrough;
                    ApplyClickThrough(_isClickThrough);
                }
                else if (id == HOTKEY_ID_LAYOUT)
                {
                    _isVerticalLayout = !_isVerticalLayout;
                    _radHorizontal.Checked = !_isVerticalLayout;
                    _radVertical.Checked = _isVerticalLayout;
                    Invalidate();
                }
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// Drag window with left mouse when not click-through.
        /// </summary>
        private void Form1_MouseDown(object? sender, MouseEventArgs e)
        {
            if (!_isClickThrough && e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        /// <summary>
        /// Double-click to change slider color (only when interactive).
        /// </summary>
        private void Form1_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (_isClickThrough) return;
            if (e.Button != MouseButtons.Left) return;

            using (var dlg = new ColorDialog())
            {
                dlg.Color = _gainBarColor;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _gainBarColor = dlg.Color;
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// Paint background, header and either meters or settings background.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background card area with padding.
            var cardRect = new Rectangle(6, 30, ClientSize.Width - 12, ClientSize.Height - 36);

            //using (Brush bg = new SolidBrush(Color.FromArgb(20, 10, 10, 20)))
            //{
            //    g.FillPath(bg, GetRoundedRectanglePath(cardRect, 14));
            //}         

            if (_showSettings)
            {

                // Settings handled by controls; nothing else to draw.
                return;
            }

            // Draw meters inside card area.
            if (_isVerticalLayout)
                DrawVerticalMeters(g, cardRect);
            else
                DrawHorizontalMeters(g, cardRect);
        }

        /// <summary>
        /// Draw 3 horizontal meters with 0.0 dB center marker and position bar.
        /// </summary>
        private void DrawHorizontalMeters(Graphics g, Rectangle cardRect)
        {
            int marginLeft = cardRect.X + 16;
            int marginTop = cardRect.Y + 16;
            int barWidth = cardRect.Width - 260; // leave space on right for labels
            int barHeight = 22;
            int spacing = 14;

            float minDb = -60f;
            float maxDb = 12f;
            float range = maxDb - minDb; // 72

            for (int i = 0; i < 3; i++)
            {
                int stripIndex = 5 + i;
                float gain = _virtualGains[i];

                float norm = (gain - minDb) / range;
                norm = Math.Max(0f, Math.Min(1f, norm));

                float zeroNorm = (0f - minDb) / range; // 0 dB center

                int y = marginTop + i * (barHeight + spacing);
                Rectangle trackRect = new Rectangle(marginLeft, y, barWidth, barHeight);

                using (GraphicsPath path = GetRoundedRectanglePath(trackRect, barHeight / 2))
                using (Brush trackBg = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                {
                    g.FillPath(trackBg, path);

                    // 0 dB vertical line
                    int zeroX = trackRect.X + (int)(zeroNorm * trackRect.Width);
                    using (Pen zeroPen = new Pen(Color.FromArgb(20, 200, 200, 200), 2))
                    {
                        g.DrawLine(zeroPen, zeroX, trackRect.Y + 2, zeroX, trackRect.Bottom - 2);
                    }

                    // Position bar (thin indicator)
                    int posX = trackRect.X + (int)(norm * trackRect.Width);
                    Rectangle posRect = new Rectangle(posX - 2, trackRect.Y + 2, 4, trackRect.Height - 4);
                    using (Brush posBrush = new SolidBrush(_gainBarColor))
                    {
                        g.FillRectangle(posBrush, posRect);
                    }

                    // Track border
                    using (Pen border = new Pen(_gainBarColor, 1))
                    {
                        g.DrawPath(border, path);
                    }
                }

                // Value + names on right side of card
                string channelLabel = _channelNames[i];
                //string paramName = $"Strip[{stripIndex}].Gain";
                string valueText = $"{gain:0.0} dB";

                int rightX = cardRect.Right - 225;

                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString(channelLabel, Font, textBrush, rightX, y);
                    //g.DrawString(paramName, Font, textBrush, rightX, y + 14);
                    g.DrawString(valueText, Font, textBrush, rightX + 100, y );
                }
            }
        }

        /// <summary>
        /// Draw 3 vertical meters with 0.0 dB horizontal marker.
        /// </summary>
        private void DrawVerticalMeters(Graphics g, Rectangle cardRect)
        {
            int marginLeft = cardRect.X + 40;
            int marginBottom = 60;
            int barWidth = 25;
            int barHeight = cardRect.Height - 90;
            int spacing = 80;

            float minDb = -60f;
            float maxDb = 12f;
            float range = maxDb - minDb;

            int baseY = cardRect.Bottom - marginBottom;

            for (int i = 0; i < 3; i++)
            {
                int stripIndex = 5 + i;
                float gain = _virtualGains[i];

                float norm = (gain - minDb) / range;
                norm = Math.Max(0f, Math.Min(1f, norm));

                float zeroNorm = (0f - minDb) / range;

                int x = marginLeft + i * (barWidth + spacing);
                Rectangle trackRect = new Rectangle(x, baseY - barHeight, barWidth, barHeight);

                using (GraphicsPath path = GetRoundedRectanglePath(trackRect, barWidth / 2))
                using (Brush trackBg = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                {
                    g.FillPath(trackBg, path);

                    // 0 dB horizontal line
                    int zeroY = trackRect.Bottom - (int)(zeroNorm * trackRect.Height);
                    using (Pen zeroPen = new Pen(Color.FromArgb(180, 200, 200, 200), 2))
                    {
                        g.DrawLine(zeroPen, trackRect.X + 2, zeroY, trackRect.Right - 2, zeroY);
                    }

                    // Position bar
                    int posY = trackRect.Bottom - (int)(norm * trackRect.Height);
                    Rectangle posRect = new Rectangle(trackRect.X + 2, posY - 2, trackRect.Width - 4, 4);
                    using (Brush posBrush = new SolidBrush(_gainBarColor))
                    {
                        g.FillRectangle(posBrush, posRect);
                    }

                    // Border
                    using (Pen border = new Pen(_gainBarColor, 1))
                    {
                        g.DrawPath(border, path);
                    }
                }

                // Labels under each bar
                string label = _channelNames[i];
                //string paramName = $"Strip[{stripIndex}].Gain";
                string valueText = $"{gain:0.0} dB";

                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString(label, Font, textBrush, x - 20, trackRect.Bottom + 5);
                    //g.DrawString(paramName, Font, textBrush, x - 8, trackRect.Bottom + 18);
                    g.DrawString(valueText, Font, textBrush, x - 5, trackRect.Bottom + 25);
                }
            }
        }

        /// <summary>
        /// Rounded rectangle helper.
        /// </summary>
        private GraphicsPath GetRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            Rectangle arc = new Rectangle(rect.Location, new Size(d, d));

            // TL
            path.AddArc(arc, 180, 90);

            // TR
            arc.X = rect.Right - d;
            path.AddArc(arc, 270, 90);

            // BR
            arc.Y = rect.Bottom - d;
            path.AddArc(arc, 0, 90);

            // BL
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Change slider color from settings.
        /// </summary>
        private void BtnSliderColor_Click(object? sender, EventArgs e)
        {
            if (_isClickThrough) return;
            using (var dlg = new ColorDialog())
            {
                dlg.Color = _gainBarColor;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _gainBarColor = dlg.Color;
                    _btnMetersTab.BackColor = _gainBarColor;
                    _btnSettingsTab.BackColor = _gainBarColor;
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// Restore defaults (layout, click-through, keybinds).
        /// </summary>
        private void BtnDefaults_Click(object? sender, EventArgs e)
        {
            _isVerticalLayout = true;
            _radHorizontal.Checked = false;
            _radVertical.Checked = true;

            _isClickThrough = false;
            _chkClickThrough.Checked = false;
            ApplyClickThrough(false);

            _showHideKey = Keys.F8;
            _clickThroughKey = Keys.F9;
            _layoutKey = Keys.F10;

            _txtShowHideKey.Text = KeyToDisplay(_showHideKey);
            _txtClickThroughKey.Text = KeyToDisplay(_clickThroughKey);
            _txtLayoutKey.Text = KeyToDisplay(_layoutKey);

            RegisterAllHotkeys();
            Invalidate();
        }

        // ===== Keybind handling =====

        private void StartCaptureShowHide(object? sender, EventArgs e)
        {
            _captureShowHideKey = true;
            _txtShowHideKey.Text = "Press key...";
            _txtShowHideKey.Focus();
        }

        private void StartCaptureClickThrough(object? sender, EventArgs e)
        {
            _captureClickThroughKey = true;
            _txtClickThroughKey.Text = "Press key...";
            _txtClickThroughKey.Focus();
        }

        private void StartCaptureLayout(object? sender, EventArgs e)
        {
            _captureLayoutKey = true;
            _txtLayoutKey.Text = "Press key...";
            _txtLayoutKey.Focus();
        }

        private void ShowHideKeyBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!_captureShowHideKey) return;
            e.SuppressKeyPress = true;

            _captureShowHideKey = false;
            _showHideKey = e.KeyCode == Keys.Escape ? Keys.None : e.KeyCode;
            _txtShowHideKey.Text = KeyToDisplay(_showHideKey);
            RegisterAllHotkeys();
        }

        private void ClickThroughKeyBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!_captureClickThroughKey) return;
            e.SuppressKeyPress = true;

            _captureClickThroughKey = false;
            _clickThroughKey = e.KeyCode == Keys.Escape ? Keys.None : e.KeyCode;
            _txtClickThroughKey.Text = KeyToDisplay(_clickThroughKey);
            RegisterAllHotkeys();
        }

        private void LayoutKeyBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!_captureLayoutKey) return;
            e.SuppressKeyPress = true;

            _captureLayoutKey = false;
            _layoutKey = e.KeyCode == Keys.Escape ? Keys.None : e.KeyCode;
            _txtLayoutKey.Text = KeyToDisplay(_layoutKey);
            RegisterAllHotkeys();
        }

        private string KeyToDisplay(Keys key)
        {
            return key == Keys.None ? "<None>" : key.ToString();
        }

        /// <summary>
        /// Register or update all global hotkeys from current key assignments.
        /// </summary>
        private void RegisterAllHotkeys()
        {
            if (Handle == IntPtr.Zero) return;

            // Clear existing
            UnregisterHotKey(Handle, HOTKEY_ID_SHOWHIDE);
            UnregisterHotKey(Handle, HOTKEY_ID_CLICKTHROUGH);
            UnregisterHotKey(Handle, HOTKEY_ID_LAYOUT);

            // Re-register
            if (_showHideKey != Keys.None)
                RegisterHotKey(Handle, HOTKEY_ID_SHOWHIDE, 0, _showHideKey);
            if (_clickThroughKey != Keys.None)
                RegisterHotKey(Handle, HOTKEY_ID_CLICKTHROUGH, 0, _clickThroughKey);
            if (_layoutKey != Keys.None)
                RegisterHotKey(Handle, HOTKEY_ID_LAYOUT, 0, _layoutKey);
        }

        /// <summary>
        /// Stop timer and logout on close.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            _timer?.Stop();
            try { VoicemeeterRemote.VBVMR_Logout(); } catch { }

            UnregisterHotKey(Handle, HOTKEY_ID_SHOWHIDE);
            UnregisterHotKey(Handle, HOTKEY_ID_CLICKTHROUGH);
            UnregisterHotKey(Handle, HOTKEY_ID_LAYOUT);
        }
    }
}
