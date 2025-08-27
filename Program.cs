using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace OverlayApp
{
    static class Program
    {
        private static OverlayForm form;
        private static TouchDetector detector;

        [STAThread]
        static void Main()
        {
            // Set the application to be DPI-aware for sharp text on high-resolution screens
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
        // Import necessary Windows functions
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

        // Constants for window management
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int HWND_TOPMOST = -1;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_L = 0x4C;
        private const int HOTKEY_ID = 9000;
        
        // Constants for dragging
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private System.Windows.Forms.Timer _timer;
        private string _displayText;
        private bool _isVisible;

        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true; // This forces the window to stay on top
            this.WindowState = FormWindowState.Normal;
            this.Size = new Size(350, 80); // Adjusted size to be more compact
            this.Top = 20;
            this.Left = 20;

            this.BackColor = Color.Black;
            this.TransparencyKey = this.BackColor;

            _timer = new System.Windows.Forms.Timer()
            {
                Interval = 1000,
                Enabled = true,
            };
            _timer.Tick += Timer_Tick;

            _isVisible = true;

            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_SHIFT, VK_L);
            this.FormClosing += (s, e) => UnregisterHotKey(this.Handle, HOTKEY_ID);

            // This event handler enables dragging the window
            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_isVisible)
            {
                using (Font font = new Font("Arial", 20, FontStyle.Bold)) // Font size is now larger for 1080p
                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    StringFormat format = new StringFormat();
                    format.LineAlignment = StringAlignment.Center;
                    format.Alignment = StringAlignment.Center;

                    // Draw the simplified display string
                    e.Graphics.DrawString(_displayText, font, textBrush, new RectangleF(0, 0, this.Width, this.Height), format);
                }
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_isVisible)
                return;

            // Get battery status
            PowerStatus powerStatus = SystemInformation.PowerStatus;
            
            // Percentage
            int percent = (int)(powerStatus.BatteryLifePercent * 100);

            // Remaining Time
            int remainingSeconds = powerStatus.BatteryLifeRemaining;
            string timeRemaining;
            if (remainingSeconds == -1) // Indicates charging or unknown
            {
                timeRemaining = "Calculating...";
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

            // Power status
            string powerStatusText = powerStatus.PowerLineStatus == PowerLineStatus.Online ? "Charging" : "Discharging";

            // Format the final display string with only percentage and time left
            _displayText = $"Battery: {percent}% - Time Left: {timeRemaining}";
            
            this.Invalidate();
        }

        public void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            UpdateWindowPosition();
            this.Invalidate();
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
