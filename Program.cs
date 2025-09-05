using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading.Tasks;

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
                    if (GetAsyncKeyState(VK_LBUTTON) != 0)
                    {
                        if (!isMouseDown)
                        {
                            isMouseDown = true;
                            startY = Cursor.Position.Y; // Store initial Y position when mouse is pressed
                        }
                        else
                        {
                            int currentY = Cursor.Position.Y;
                            // Check if the mouse was pressed near the top and dragged down sufficiently
                            if (startY < TopScreenTolerance && currentY - startY > DragThreshold)
                            {
                                _form.ToggleVisibility();
                            }
                        }
                    }
                    else
                    {
                        isMouseDown = false; // Reset when mouse button is released
                    }

                    Thread.Sleep(50); // Prevents high CPU usage
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }
    }

    public partial class OverlayForm : Form
    {
        // P/Invoke declarations for window management
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x80000;
        private const int LWA_ALPHA = 0x2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong); // Corrected to IntPtr

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        // Corrected signature for SetWindowPos: hWndInsertAfter expects an int
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


        // Hotkey constants
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const int WM_HOTKEY = 0x0312;
        public const int HOTKEY_ID = 9000; // Unique ID for the hotkey

        // Window position constants
        private const int HWND_TOPMOST_INT = -1; // Use an int for the SetWindowPos parameter

        private readonly System.Windows.Forms.Timer _timer;
        private bool IsVisible = true;
        private string _displayText = "";

        // System metrics
        private PerformanceCounter? _cpuCounter; // Make it nullable
        private PerformanceCounter? _ramCounter; // Make it nullable

        private readonly Button _closeButton;

        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.Text = "";
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            // Set initial position to top-center of the screen
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Width / 2 - this.Width / 2, 0);
            this.Size = new Size(300, 150); // Example size, adjust as needed
            this.TopMost = true;

            // Make the window click-through and non-focusable
            // Use IntPtr for dwNewLong in SetWindowLong
            SetWindowLong(this.Handle, GWL_EXSTYLE, (IntPtr)((long)GetWindowLong(this.Handle, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA); // Fully opaque initially

            // Register the hotkey (e.g., Ctrl + Shift + L) to toggle visibility
            // Example: Ctrl + Shift + L (VK_L is 0x4C)
            RegisterHotKey(this.Handle, HOTKEY_ID, (uint)Keys.Control | (uint)Keys.Shift, (uint)Keys.L);

            // Set up the close button
            _closeButton = new Button();
            _closeButton.Text = "X";
            _closeButton.ForeColor = Color.White;
            _closeButton.BackColor = Color.DarkRed; // Rich red
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.Font = new Font("Inter", 8, FontStyle.Bold); // Consistent font
            _closeButton.Size = new Size(20, 20);
            _closeButton.Location = new Point(this.Width - _closeButton.Width - 5, 5);
            _closeButton.Click += CloseButton_Click;
            _closeButton.Visible = false; // Hidden by default until toggled
            this.Controls.Add(_closeButton);

            // Initialize system performance counters
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                // Removed GPU counter as per your request
            }
            catch (Exception ex)
            {
                // Handle exceptions if performance counters are not accessible
                _displayText = $"Error initializing counters: {ex.Message}";
                _cpuCounter = null;
                _ramCounter = null;
            }

            // Set up the main timer for updates
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000; // 1 second
            _timer.Tick += OnTimerTick;
            _timer.Start();

            // Initial update to set the text immediately
            OnTimerTick(null, null);
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            this.Close();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            // Update display text with live metrics
            float cpuUsage = _cpuCounter != null ? _cpuCounter.NextValue() : 0;
            float availableRamMB = _ramCounter != null ? _ramCounter.NextValue() : 0;

            // Get battery status
            PowerStatus powerStatus = SystemInformation.PowerStatus;
            double remainingSeconds = powerStatus.BatteryLifeRemaining;
            string timeRemaining = "N/A";

            if (remainingSeconds != -1) // -1 indicates battery is not present or information is unavailable
            {
                TimeSpan ts = TimeSpan.FromSeconds(remainingSeconds);
                if (ts.TotalHours >= 1)
                {
                    timeRemaining = $"{ts.Hours:0}h {ts.Minutes}m remaining";
                }
                else
                {
                    timeRemaining = $"{ts.Minutes}m remaining";
                }
            }

            // Format the final display text with all metrics
            // Changed color to Red for "rich red" as requested previously
            _displayText = $"Time Left: {timeRemaining}\n" +
                           $"CPU: {cpuUsage.ToString("F1")}%\n" +
                           $"RAM: {availableRamMB.ToString("F0")} MB Free";

            this.Invalidate(); // Request a redraw of the form
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!IsVisible) return;

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // Draw a semi-transparent background for the overlay
            using (SolidBrush backgroundBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0))) // Slightly transparent black
            {
                e.Graphics.FillRectangle(backgroundBrush, new Rectangle(0, 0, this.Width, this.Height));
            }

            // Draw the metrics text with a rich red color
            using (Font font = new Font("Inter", 12, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.Red)) // Changed to Rich Red
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Near;
                e.Graphics.DrawString(_displayText, font, textBrush, new RectangleF(10, 10, this.Width - 20, this.Height - 20), sf);
            }
        }

        public void ToggleVisibility()
        {
            IsVisible = !IsVisible;
            if (IsVisible)
            {
                UpdateWindowPosition(); // Ensure it's on top when shown
                this.Show();
            }
            else
            {
                this.Hide();
            }

            // Show or hide the close button when visibility is toggled
            _closeButton.Visible = IsVisible;
            this.Invalidate(); // Redraw to update visibility of elements
        }

        private void UpdateWindowPosition()
        {
            // Use the corrected HWND_TOPMOST_INT which is an int
            const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW;
            SetWindowPos(this.Handle, HWND_TOPMOST_INT, 0, 0, 0, 0, flags);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // Check if the message is a hotkey press
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID)
            {
                ToggleVisibility();
            }
        }

        // Clean up resources when the form is closing
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unregister the hotkey
                UnregisterHotKey(this.Handle, HOTKEY_ID);
                _timer.Stop();
                _timer.Dispose();
                _cpuCounter?.Dispose();
                _ramCounter?.Dispose();
                _closeButton.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
