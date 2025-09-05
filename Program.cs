using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;
using System.Collections.Generic;
using System.Management;

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
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int LWA_ALPHA = 0x2;

        // Use IntPtr for both 32-bit and 64-bit compatibility
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
        private System.Windows.Forms.Timer? _timer;
        private System.Windows.Forms.Timer? _devTextTimer;
        private bool _showDevText = true;
        private Button? _closeButton;

        // Make fields nullable to resolve warnings
        private PowerStatus? _powerStatus;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private PerformanceCounter[]? _gpuMemoryCounters;
        private float _gpuTotalMB = 0;
        
        public bool IsVisible { get; private set; } = true;

        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.Size = new Size(300, 150);
            this.StartPosition = FormStartPosition.Manual;

            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            this.Location = new Point((screenWidth - this.Width) / 2, 0);

            this.TopMost = true;
            this.AllowTransparency = true;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;

            // Use the correct 64-bit compatible P/Invoke functions directly
            IntPtr currentStyle = GetWindowLongPtr(this.Handle, GWL_EXSTYLE);
            SetWindowLongPtr(this.Handle, GWL_EXSTYLE, (IntPtr)((long)currentStyle.ToInt64() | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);

            _closeButton = new Button
            {
                Text = "X",
                ForeColor = Color.White,
                BackColor = Color.DarkRed,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Size = new Size(20, 20),
                Location = new Point(this.Width - 25, 5),
                Visible = false
            };
            _closeButton.Click += (s, e) => this.Close();
            this.Controls.Add(_closeButton);

            RegisterHotKey(this.Handle, HOTKEY_ID, (int)Keys.Shift, (int)Keys.L);

            try
            {
                _powerStatus = SystemInformation.PowerStatus;
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");

                var category = new PerformanceCounterCategory("GPU Engine");
                var instanceNames = category.GetInstanceNames();
                var counters = new List<PerformanceCounter>();

                foreach (string instance in instanceNames)
                {
                    try
                    {
                        foreach (PerformanceCounter counter in category.GetCounters(instance))
                        {
                            if (counter.CounterName == "Dedicated Usage")
                            {
                                counters.Add(counter);
                            }
                        }
                    }
                    catch { /* skip inaccessible counters */ }
                }

                _gpuMemoryCounters = counters.ToArray();
            }
            catch (Exception ex)
            {
                _displayText = $"Error: {ex.Message}";
                _cpuCounter = null;
                _ramCounter = null;
                _gpuMemoryCounters = Array.Empty<PerformanceCounter>();
            }

            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += OnTimerTick;
            _timer.Start();

            _devTextTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _devTextTimer.Tick += (s, e) =>
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
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(150, 0, 0, 0)), new Rectangle(0, 0, this.Width, this.Height));
            using (Font font = new Font("Inter", 12, FontStyle.Bold))
            using (Brush brush = new SolidBrush(Color.White))
            {
                e.Graphics.DrawString(_displayText, font, brush, new RectangleF(10, 10, this.Width - 20, this.Height - 20));
            }

            if (_showDevText)
            {
                using (Font font = new Font("Inter", 10, FontStyle.Italic))
                using (Brush brush = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                {
                    e.Graphics.DrawString("Developed by LostyDEV", font, brush, new RectangleF(0, 0, this.Width, this.Height - 5),
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far });
                }
            }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            float cpuUsage = _cpuCounter?.NextValue() ?? 0;
            float availableRamMB = _ramCounter?.NextValue() ?? 0;

            _powerStatus = SystemInformation.PowerStatus;
            double remainingSeconds = _powerStatus.BatteryLifeRemaining;
            string timeRemaining = remainingSeconds != -1 ? $"{TimeSpan.FromSeconds(remainingSeconds):h\\h\\ m\\m}" : "N/A";

            float gpuMemoryUsageMB = 0;
            if (_gpuMemoryCounters != null && _gpuMemoryCounters.Length > 0)
            {
                foreach (var counter in _gpuMemoryCounters)
                {
                    gpuMemoryUsageMB += counter.NextValue();
                }
                gpuMemoryUsageMB /= (1024 * 1024);
            }

            _displayText = $"Time Left: {timeRemaining}\n" +
                           $"CPU: {cpuUsage:F1}%\n" +
                           $"RAM: {availableRamMB:F0} MB Free\n" +
                           $"GPU VRAM: {gpuMemoryUsageMB:F0} MB";
            this.Invalidate();
        }

        public void ToggleVisibility()
        {
            IsVisible = !IsVisible;
            if (IsVisible) this.Show(); else this.Hide();
            if (_closeButton != null) _closeButton.Visible = IsVisible;
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
