using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
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
                    if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0)
                    {
                        int y = Cursor.Position.Y;
                        if (y < TopScreenTolerance)
                        {
                            if (!isMouseDown)
                            {
                                isMouseDown = true;
                                startY = y;
                            }
                        }
                        else
                        {
                            if (isMouseDown && y - startY > DragThreshold)
                            {
                                _form.ToggleVisibility();
                                isMouseDown = false;
                            }
                        }
                    }
                    else
                    {
                        isMouseDown = false;
                    }
                    Thread.Sleep(16);
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }
    }

    public class OverlayForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int HWND_TOPMOST = -1;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_L = 0x4C;
        private const int HOTKEY_ID = 9000;
        
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private System.Windows.Forms.Timer _timer;
        private System.Windows.Forms.Timer _initialDisplayTimer;
        private string? _displayText;
        private bool _isVisible;
        private bool _isInitialDisplay = true;
        private const int InitialDisplayDuration = 10000;
        
        private Button _closeButton;

        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _ramCounter;

        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.WindowState = FormWindowState.Normal;
            this.Size = new Size(300, 100);
            
            LoadWindowPosition();

            this.BackColor = Color.Black;
            this.TransparencyKey = this.BackColor;

            _closeButton = new Button();
            _closeButton.Text = "X";
            _closeButton.Font = new Font("Arial", 8, FontStyle.Bold);
            _closeButton.Size = new Size(20, 20);
            _closeButton.BackColor = Color.Red;
            _closeButton.ForeColor = Color.White;
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.Location = new Point(this.Width - _closeButton.Width - 5, 5);
            
            _closeButton.Click += (s, e) => this.Close();

            this.Controls.Add(_closeButton);

            _timer = new System.Windows.Forms.Timer()
            {
                Interval = 1000,
                Enabled = false,
            };
            _timer.Tick += Timer_Tick;

            _initialDisplayTimer = new System.Windows.Forms.Timer()
            {
                Interval = InitialDisplayDuration,
                Enabled = true,
            };
            _initialDisplayTimer.Tick += InitialDisplayTimer_Tick;

            _isVisible = true;
            _displayText = "Developed by LostyDEV";

            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_SHIFT, VK_L);
            this.FormClosing += (s, e) => 
            {
                SaveWindowPosition();
                UnregisterHotKey(this.Handle, HOTKEY_ID);
            };

            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };

            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
        }

        private void InitialDisplayTimer_Tick(object? sender, EventArgs e)
        {
            _isInitialDisplay = false;
            _initialDisplayTimer.Stop();
            _timer.Enabled = true;
            Timer_Tick(null, EventArgs.Empty);
        }

        private void LoadWindowPosition()
        {
            try
            {
                RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\BatteryOverlay");
                if (key != null)
                {
                    this.Left = Convert.ToInt32(key.GetValue("XPosition", 20));
                    this.Top = Convert.ToInt32(key.GetValue("YPosition", 20));
                    key.Close();
                }
            }
            catch (Exception)
            {
                this.Top = 20;
                this.Left = 20;
            }
        }
        
        private void SaveWindowPosition()
        {
            try
            {
                RegistryKey? key = Registry.CurrentUser.CreateSubKey("SOFTWARE\\BatteryOverlay");
                
                if (key != null)
                {
                    key.SetValue("XPosition", this.Left);
                    key.SetValue("YPosition", this.Top);
                    key.Close();
                }
            }
            catch (Exception)
            {
                // Do nothing
            }
        }
        
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_isVisible && _displayText != null)
            {
                using (Font font = new Font("Arial", 12, FontStyle.Bold))
                using (Brush textBrush = new SolidBrush(Color.DarkRed))
                {
                    StringFormat format = new StringFormat();
                    format.LineAlignment = StringAlignment.Near;
                    format.Alignment = StringAlignment.Near;

                    float x = 5;
                    float y = 5;
                    
                    if (_isInitialDisplay)
                    {
                        format.LineAlignment = StringAlignment.Center;
                        format.Alignment = StringAlignment.Center;
                        x = this.ClientSize.Width / 2;
                        y = this.ClientSize.Height / 2;
                    }
                    
                    e.Graphics.DrawString(_displayText, font, textBrush, x, y, format);
                }
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isVisible || _isInitialDisplay)
                return;

            float cpuUsage = _cpuCounter.NextValue();
            float availableRamMB = _ramCounter.NextValue();
            
            PowerStatus powerStatus = SystemInformation.PowerStatus;
            
            int remainingSeconds = powerStatus.BatteryLifeRemaining;
            string timeRemaining;
            
            if (remainingSeconds == -1)
            {
                if (powerStatus.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging))
                {
                    timeRemaining = "Charging";
                }
                else
                {
                    timeRemaining = "Estimating...";
                }
            }
            else
            {
                TimeSpan ts = TimeSpan.FromSeconds(remainingSeconds);
                if (ts.TotalHours >= 1)
                {
                    timeRemaining = $"{(int)ts.TotalHours}h {ts.Minutes}m remaining";
                }
                else
                {
                    timeRemaining = $"{ts.Minutes}m remaining";
                }
            }
            
            _displayText = $"Time Left: {timeRemaining}\n" +
                           $"CPU: {cpuUsage.ToString("F1")}%\n" +
                           $"RAM: {availableRamMB.ToString("F0")} MB Free";
            
            this.Invalidate();
        }

        public void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            UpdateWindowPosition();
            this.Invalidate();
            
            _closeButton.Visible = _isVisible;
        }

        private void UpdateWindowPosition()
        {
            const int flags = SWP_NOMOVE | SWP_NOSIZE;
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
