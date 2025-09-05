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

        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_SHOWWINDOW = 0x0040;
        private const int SWP_HIDEWINDOW = 0x0080;
        private const int HWND_TOPMOST = -1;

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_L = 0x4C;
        private const int HOTKEY_ID = 9000;

        private System.Windows.Forms.Timer _timer;
        private string _displayText;
        private bool _isVisible;

        public OverlayForm()
        {
            // Basic form settings for an overlay
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Size = new Size(400, 150); // Set a sensible, fixed size
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;
            this.Top = 0; // Position at the top of the screen
            this.Left = (Screen.PrimaryScreen.WorkingArea.Width - this.Width) / 2; // Center horizontally

            _timer = new System.Windows.Forms.Timer()
            {
                Interval = 1000,
                Enabled = true,
            };
            _timer.Tick += Timer_Tick;

            _isVisible = true;
            
            // Register hotkey to toggle visibility
            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_SHIFT, VK_L);
            this.FormClosing += (s, e) => UnregisterHotKey(this.Handle, HOTKEY_ID);
            
            UpdateWindowPosition();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            if (_isVisible)
            {
                using (Font font = new Font("Arial", 14, FontStyle.Bold))
                using (Brush backgroundBrush = new SolidBrush(Color.FromArgb(128, Color.Gray)))
                using (Brush fontBrush = new SolidBrush(Color.Green))
                using (Pen pen = new Pen(Color.Black, 2))
                {
                    // Calculate size of the text to draw a background box
                    SizeF textSize = e.Graphics.MeasureString(_displayText, font);

                    // Adjust position and size for the display box
                    float x = (this.Width - textSize.Width) / 2;
                    float y = (this.Height - textSize.Height) / 2;
                    
                    e.Graphics.FillRectangle(backgroundBrush, x, y, textSize.Width + 20, textSize.Height + 10);
                    e.Graphics.DrawString(_displayText, font, fontBrush, x + 10, y + 5);
                }
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_isVisible)
                return;

            PowerStatus powerStatus = SystemInformation.PowerStatus;
            
            string time = DateTime.Now.ToString("h:mm tt");
            string batteryStatus = (powerStatus.BatteryLifePercent * 100).ToString() + "%";
            
            // Get battery life remaining in seconds
            int remainingSeconds = powerStatus.BatteryLifeRemaining;
            string batteryTime;
            
            if (powerStatus.PowerLineStatus == PowerLineStatus.Online)
            {
                batteryTime = "- Charging";
            }
            else if (remainingSeconds != -1)
            {
                TimeSpan ts = TimeSpan.FromSeconds(remainingSeconds);
                batteryTime = $"{ts.Hours}h {ts.Minutes}m remaining";
            }
            else
            {
                batteryTime = "Calculating...";
            }

            _displayText = $"Time: {time}\nBattery: {batteryStatus}\nStatus: {powerStatus.PowerLineStatus}\nTime Left: {batteryTime}";

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
            // Flags to retain current size and position
            uint flags = SWP_NOMOVE | SWP_NOSIZE;
            
            if (_isVisible)
            {
                // Add the flag to show the window
                flags |= SWP_SHOWWINDOW;
            }
            else
            {
                // Add the flag to hide the window
                flags |= SWP_HIDEWINDOW;
            }
            
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
