using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;

namespace OverlayApp
{
    static class Program
    {
        private static OverlayForm? form;
        private static TouchDetector? detector;

        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            form = new OverlayForm();
            detector = new TouchDetector(form);
            Application.Run(form);
        }
    }

    public class TouchDetector
    {
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public const int VK_LBUTTON = 0x01;
        private static readonly int TopScreenTolerance = 20;
        private static readonly int DragThreshold = 50;

        private static bool isMouseDown = false;
        private static int startY = 0;

        private OverlayForm _form;

        public TouchDetector(OverlayForm form)
        {
            _form = form;
            StartTouchDetection();
        }

        private void StartTouchDetection()
        {
            Thread thread = new Thread(() =>
            {
                while (true)
                {
                    if (GetAsyncKeyState(VK_LBUTTON) != 0)
                    {
                        if (!isMouseDown)
                        {
                            isMouseDown = true;
                            Point cursorPos = Cursor.Position;
                            if (cursorPos.Y <= TopScreenTolerance)
                            {
                                startY = cursorPos.Y;
                            }
                        }

                        if (isMouseDown)
                        {
                            Point currentPos = Cursor.Position;
                            if (currentPos.Y - startY >= DragThreshold)
                            {
                                _form.Invoke(new MethodInvoker(() =>
                                {
                                    if (!_form.IsVisible)
                                    {
                                        _form.ToggleVisibility();
                                    }
                                }));
                                isMouseDown = false;
                            }
                        }
                    }
                    else
                    {
                        isMouseDown = false;
                    }
                    Thread.Sleep(10);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }
    }

    public class OverlayForm : Form
    {
        // Corrected P/Invoke declarations for 64-bit compatibility
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOPMOST = 0x0008;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int LWA_ALPHA = 0x2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const int WM_HOTKEY = 0x0312;
        public const int HOTKEY_ID = 1;

        private string _displayText = "";
        private System.Windows.Forms.Timer _timer;
        private System.Windows.Forms.Timer _devTextTimer;
        private bool _showDevText = true;
        private Button _closeButton;

        // Make fields nullable to address warnings
        private PowerStatus? _powerStatus;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private PerformanceCounter? _gpuMemoryCounter;

        public bool IsVisible { get; private set; } = true;

        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.Size = new Size(300, 150);
            this.StartPosition = FormStartPosition.Manual;

            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int x = (screenWidth - this.Width) / 2;
            int y = 0;
            this.Location = new Point(x, y);
            this.TopMost = true;
            this.AllowTransparency = true;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;

            // Corrected SetWindowLongPtr call for 64-bit compatibility
            IntPtr currentStyle = GetWindowLongPtr(this.Handle, GWL_EXSTYLE);
            SetWindowLongPtr(this.Handle, GWL_EXSTYLE, (IntPtr)((long)currentStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);

            _closeButton = new Button();
            _closeButton.Text = "X";
            _closeButton.ForeColor = Color.White;
            _closeButton.BackColor = Color.DarkRed;
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            _closeButton.Size = new Size(20, 20);
            _closeButton.Location = new Point(this.Width - _closeButton.Width - 5, 5);
            _closeButton.Click += (sender, e) => { this.Close(); };
            _closeButton.Visible = false;
            this.Controls.Add(_closeButton);

            RegisterHotKey(this.Handle, HOTKEY_ID, (int)Keys.Shift, (int)Keys.L);

            try
            {
                _powerStatus = SystemInformation.PowerStatus;
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                
                // Initialize the PerformanceCounter for GPU memory.
                _gpuMemoryCounter = new PerformanceCounter("GPU Engine", "Dedicated Usage", "pid_0_luid_0x00000000_0x0000F5AC_eng_3D");
            }
            catch (Exception ex)
            {
                _displayText = $"Error: {ex.Message}";
                _cpuCounter = null;
                _ramCounter = null;
                _gpuMemoryCounter = null;
            }

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += OnTimerTick;
            _timer.Start();

            _devTextTimer = new System.Windows.Forms.Timer();
            _devTextTimer.Interval = 3000;
            _devTextTimer.Tick += (sender, e) =>
            {
                _showDevText = false;
                _devTextTimer.Stop();
                this.Invalidate();
            };
            _devTextTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!IsVisible) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                e.Graphics.FillRectangle(brush, new Rectangle(0, 0, this.Width, this.Height));
            }

            using (Font font = new Font("Inter", 12, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Near;
                e.Graphics.DrawString(_displayText, font, textBrush, new RectangleF(10, 10, this.Width - 20, this.Height - 20), sf);
            }
            if (_showDevText)
            {
                using (Font font = new Font("Inter", 10, FontStyle.Italic))
                using (SolidBrush devTextBrush = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Far;
                    e.Graphics.DrawString("Developed by LostyDEV", font, devTextBrush, new RectangleF(0, 0, this.Width, this.Height - 5), sf);
                }
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            float cpuUsage = _cpuCounter != null ? _cpuCounter.NextValue() : 0;
            float availableRamMB = _ramCounter != null ? _ramCounter.NextValue() : 0;
            
            // Handle possible null reference
            _powerStatus = SystemInformation.PowerStatus;
            int batteryLifePercent = (int)(_powerStatus.BatteryLifePercent * 100);
            double remainingSeconds = _powerStatus.BatteryLifeRemaining;
            
            string timeRemaining = "Not available";
            if (remainingSeconds != -1)
            {
                TimeSpan ts = TimeSpan.FromSeconds(remainingSeconds);
                if (ts.TotalHours >= 1)
                {
                    timeRemaining = $"{ts.TotalHours:0}h {ts.Minutes}m remaining";
                }
                else
                {
                    timeRemaining = $"{ts.Minutes}m remaining";
                }
            }

            // Get GPU memory usage
            float gpuMemoryUsage = 0;
            if (_gpuMemoryCounter != null)
            {
                gpuMemoryUsage = _gpuMemoryCounter.NextValue();
            }

            // Format the final display text with all metrics
            _displayText = $"Time Left: {timeRemaining}\n" +
                           $"CPU: {cpuUsage.ToString("F1")}%\n" +
                           $"RAM: {availableRamMB.ToString("F0")} MB Free\n" +
                           $"GPU VRAM: {gpuMemoryUsage:F0} MB";

            this.Invalidate();
        }

        public void ToggleVisibility()
        {
            IsVisible = !IsVisible;
            if (IsVisible)
            {
                UpdateWindowPosition();
                this.Show();
            }
            else
            {
                this.Hide();
            }
            _closeButton.Visible = IsVisible;
        }

        private void UpdateWindowPosition()
        {
            const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW;
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID)
            {
                ToggleVisibility();
            }
        }
    }
}
