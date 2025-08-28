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
        private string? _displayText;
        private bool _isVisible;
        
        // The custom close button
        private Button _closeButton;

        // Performance counters for system metrics
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _ramCounter;

        // Private field to store the time the form was loaded
        private DateTime _splashTextTime;

        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true; // This forces the window to stay on top
            this.WindowState = FormWindowState.Normal;
            this.Size = new Size(300, 100); // Resized to fit the reduced information
            
            // Load the last saved position from the Registry
            LoadWindowPosition();

            this.BackColor = Color.Black;
            this.TransparencyKey = this.BackColor;

            // Initialize and style the close button
            _closeButton = new Button();
            _closeButton.Text = "X";
            _closeButton.Font = new Font("Arial", 8, FontStyle.Bold);
            _closeButton.Size = new Size(20, 20);
            _closeButton.BackColor = Color.Red;
            _closeButton.ForeColor = Color.White;
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.Location = new Point(this.Width - _closeButton.Width - 5, 5);
            
            // Add the click event handler to the button
            _closeButton.Click += (s, e) => this.Close();

            // Add the button to the form's controls
            this.Controls.Add(_closeButton);

            _timer = new System.Windows.Forms.Timer()
            {
                Interval = 1000,
                Enabled = true,
            };
            _timer.Tick += Timer_Tick;

            _isVisible = true;

            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_SHIFT, VK_L);
            this.FormClosing += (s, e) => 
            {
                // Save the window position to the Registry when the form is closing
                SaveWindowPosition();
                UnregisterHotKey(this.Handle, HOTKEY_ID);
            };

            // This event handler enables dragging the window
            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };

            // Initialize performance counters
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");

            // Store the time when the form was created
            _splashTextTime = DateTime.Now;
        }


        private void LoadWindowPosition()
        {
            try
            {
                // Open the specific Registry key for this application
                RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\BatteryOverlay");
                if (key != null)
                {
                    // Read the saved position values
                    this.Left = Convert.ToInt32(key.GetValue("XPosition", 20));
                    this.Top = Convert.ToInt32(key.GetValue("YPosition", 20));
                    key.Close();
                }
            }
            catch (Exception)
            {
                // If there is an error, just use the default position
                this.Top = 20;
                this.Left = 20;
            }
        }
        
        private void SaveWindowPosition()
        {
            try
            {
                // Create or open the specific Registry key for this application
                RegistryKey? key = Registry.CurrentUser.CreateSubKey("SOFTWARE\\BatteryOverlay");
                
                // Write the current window position to the Registry
                if (key != null)
                {
                    key.SetValue("XPosition", this.Left);
                    key.SetValue("YPosition", this.Top);
                    key.Close();
                }
            }
            catch (Exception)
            {
                // Do nothing if saving fails, as it's not critical
            }
        }
        
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_isVisible && _displayText != null)
            {
                // Used a smaller font size to fix the DPI issues
                using (Font font = new Font("Arial", 12, FontStyle.Bold))
                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    StringFormat format = new StringFormat();
                    format.LineAlignment = StringAlignment.Near;
                    format.Alignment = StringAlignment.Near;

                    // Set up the layout
                    float x = 5;
                    float y = 5;

                    e.Graphics.DrawString(_displayText, font, textBrush, x, y);
                }
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isVisible)
                return;

            // Get performance data from the counters
            float cpuUsage = _cpuCounter.NextValue();
            float availableRamMB = _ramCounter.NextValue();
            
            // Get battery status
            PowerStatus powerStatus = SystemInformation.PowerStatus;
            
            int remainingSeconds = powerStatus.BatteryLifeRemaining;
            string timeRemaining;
            if (remainingSeconds == -1)
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
            
            string developerText = "";
            if ((DateTime.Now - _splashTextTime).TotalSeconds <= 10)
            {
                developerText = "\n\nDeveloped by LostyDEV";
            }

            // Format the final display text with all metrics
            _displayText = $"Time Left: {timeRemaining}\n" +
                           $"CPU: {cpuUsage.ToString("F1")}%\n" +
                           $"RAM: {availableRamMB.ToString("F0")} MB Free" +
                           developerText;
            
            this.Invalidate();
        }

        public void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            UpdateWindowPosition();
            this.Invalidate();
            
            // Show or hide the close button when visibility is toggled
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
