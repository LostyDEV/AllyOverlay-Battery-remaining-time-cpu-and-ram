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
            // Set DPI awareness for sharper text on high-resolution displays.
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
        // P/Invoke to get keyboard state
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public const int VK_LBUTTON = 0x01; // Left mouse button virtual key code
        private static readonly int TopScreenTolerance = 20; // How close to the top edge to trigger detection
        private static readonly int DragThreshold = 50; // How far down the user must drag to show overlay

        private static bool isMouseDown = false;
        private static int startY = 0; // Y-coordinate where the drag started

        private OverlayForm _form;

        public TouchDetector(OverlayForm form)
        {
            _form = form;
            StartTouchDetection();
        }

        private void StartTouchDetection()
        {
            // Start a background thread to continuously check for mouse input
            Thread thread = new Thread(() =>
            {
                while (true)
                {
                    // Check if the left mouse button is pressed
                    if (GetAsyncKeyState(VK_LBUTTON) != 0)
                    {
                        if (!isMouseDown) // If this is the start of a click
                        {
                            isMouseDown = true;
                            Point cursorPos = Cursor.Position;
                            // Check if the click started near the top of the screen
                            if (cursorPos.Y <= TopScreenTolerance)
                            {
                                startY = cursorPos.Y; // Record the starting Y position
                            }
                        }

                        if (isMouseDown) // If we are currently holding the mouse button down
                        {
                            Point currentPos = Cursor.Position;
                            // Check if the drag distance has exceeded the threshold
                            if (currentPos.Y - startY >= DragThreshold)
                            {
                                // Invoke the form's method on the UI thread to toggle visibility
                                _form.Invoke(new MethodInvoker(() =>
                                {
                                    if (!_form.IsVisible)
                                    {
                                        _form.ToggleVisibility();
                                    }
                                }));
                                // Reset the mouse state to prevent repeated triggers from a single drag
                                isMouseDown = false;
                            }
                        }
                    }
                    else // If the mouse button is not pressed
                    {
                        isMouseDown = false; // Reset the mouse down state
                    }

                    // Sleep for a short duration to prevent high CPU usage by this thread
                    Thread.Sleep(10);
                }
            });
            thread.IsBackground = true; // Allow the application to exit even if this thread is running
            thread.Start();
        }
    }

    public class OverlayForm : Form
    {
        // P/Invoke declarations for window management
        private const int GWL_EXSTYLE = -20; // Extended Window Styles index
        private const int WS_EX_TOPMOST = 0x0008; // Window is always on top
        private const int WS_EX_NOACTIVATE = 0x08000000; // Window cannot be activated
        private const int WS_EX_TOOLWINDOW = 0x00000080; // Window is not shown in the taskbar
        private const int WS_EX_TRANSPARENT = 0x00000020; // Window is transparent to mouse clicks
        private const int WS_EX_LAYERED = 0x80000; // Window has per-pixel alpha or color key
        private const int LWA_ALPHA = 0x2; // Use alpha value to set transparency

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // Constants for SetWindowPos
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1); // Place window above all non-topmost windows
        private const uint SWP_NOSIZE = 0x0001; // Do not modify the size of the window
        private const uint SWP_NOMOVE = 0x0002; // Do not modify the position of the window
        private const uint SWP_SHOWWINDOW = 0x0040; // Show the window

        // Hotkey registration
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const int WM_HOTKEY = 0x0312; // Window message for hotkey notification
        public const int HOTKEY_ID = 1; // Unique identifier for our hotkey

        // UI related fields
        private string _displayText = ""; // Holds the text to be displayed on the overlay
        private System.Windows.Forms.Timer _timer; // Timer to update the overlay periodically
        private System.Windows.Forms.Timer _devTextTimer; // Timer to briefly show developer text
        private bool _showDevText = true; // Flag to control visibility of developer text
        private Button _closeButton; // Button to close the application

        // System metrics performance counters
        private PowerStatus _powerStatus; // Status of system power (battery, AC)
        private PerformanceCounter _cpuCounter; // Counter for CPU total usage
        private PerformanceCounter _ramCounter; // Counter for available RAM
        private PerformanceCounter _gpuMemoryCounter; // Counter for GPU dedicated memory usage

        public bool IsVisible { get; private set; } = true; // Tracks if the overlay is currently visible

        public OverlayForm()
        {
            // Form initial setup for an overlay window
            this.FormBorderStyle = FormBorderStyle.None; // No border
            this.ShowInTaskbar = false; // Not visible in the taskbar
            this.Size = new Size(300, 150); // Initial size of the overlay
            this.StartPosition = FormStartPosition.Manual; // Position set manually

            // Position the overlay in the center of the top of the primary screen
            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int x = (screenWidth - this.Width) / 2;
            int y = 0; // Top of the screen
            this.Location = new Point(x, y);

            // Window style settings for an overlay
            this.TopMost = true; // Always on top of other windows
            this.AllowTransparency = true; // Allows for transparency
            this.BackColor = Color.Black; // Set background to black for transparency key
            this.TransparencyKey = Color.Black; // Make black transparent

            // Apply extended window styles for overlay behavior
            SetWindowLong(this.Handle, GWL_EXSTYLE, (IntPtr)((long)GetWindowLong(this.Handle, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            // Set transparency for the window (fully opaque initially)
            SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);

            // Setup the close button
            _closeButton = new Button();
            _closeButton.Text = "X";
            _closeButton.ForeColor = Color.White;
            _closeButton.BackColor = Color.DarkRed;
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            _closeButton.Size = new Size(20, 20);
            _closeButton.Location = new Point(this.Width - _closeButton.Width - 5, 5); // Top-right corner
            _closeButton.Click += (sender, e) => { this.Close(); }; // Close the form on click
            _closeButton.Visible = false; // Initially hidden
            this.Controls.Add(_closeButton);

            // Register the hotkey (Shift + L) to toggle overlay visibility
            RegisterHotKey(this.Handle, HOTKEY_ID, (int)Keys.Shift, (int)Keys.L);

            // Initialize performance counters and system information
            try
            {
                _powerStatus = SystemInformation.PowerStatus; // Get battery status
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); // Total CPU usage
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes"); // Available RAM in MB

                // Initialize PerformanceCounter for GPU dedicated memory usage.
                // NOTE: The instance name "pid_0_luid_0x00000000_0x0000F5AC_eng_3D" is an EXAMPLE.
                // YOU MUST VERIFY AND ADJUST THIS INSTANCE NAME FOR YOUR SPECIFIC SYSTEM.
                // Use Windows Performance Monitor (perfmon.exe) to find the correct name for your GPU.
                _gpuMemoryCounter = new PerformanceCounter("GPU Engine", "Dedicated Usage", "pid_0_luid_0x00000000_0x0000F5AC_eng_3D");
            }
            catch (Exception ex)
            {
                // If any counter fails to initialize, display an error message
                _displayText = $"Error initializing metrics: {ex.Message}";
                _cpuCounter = null;
                _ramCounter = null;
                _gpuMemoryCounter = null;
            }

            // Setup the main timer to update the overlay every second
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000; // Update every 1000 milliseconds (1 second)
            _timer.Tick += OnTimerTick;
            _timer.Start();

            // Setup a timer to briefly show developer text on startup
            _devTextTimer = new System.Windows.Forms.Timer();
            _devTextTimer.Interval = 3000; // Show for 3 seconds
            _devTextTimer.Tick += (sender, e) =>
            {
                _showDevText = false; // Hide developer text
                _devTextTimer.Stop();
                this.Invalidate(); // Redraw the form to remove the text
            };
            _devTextTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Only draw if the overlay is visible
            if (!IsVisible) return;

            // Enable high-quality rendering
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // Draw a semi-transparent background rectangle
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) // 150 is the alpha value (transparency)
            {
                e.Graphics.FillRectangle(brush, new Rectangle(0, 0, this.Width, this.Height));
            }

            // Draw the main display text (CPU, RAM, GPU VRAM, Battery)
            using (Font font = new Font("Inter", 12, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Near;
                // Draw the text within the bounds of the overlay form
                e.Graphics.DrawString(_displayText, font, textBrush, new RectangleF(10, 10, this.Width - 20, this.Height - 20), sf);
            }

            // Draw the developer text briefly on startup
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
            // Retrieve CPU usage
            float cpuUsage = _cpuCounter != null ? _cpuCounter.NextValue() : 0;

            // Retrieve available RAM
            float availableRamMB = _ramCounter != null ? _ramCounter.NextValue() : 0;
            
            // Get battery status
            _powerStatus = SystemInformation.PowerStatus;
            int batteryLifePercent = (int)(_powerStatus.BatteryLifePercent * 100);
            double remainingSeconds = _powerStatus.BatteryLifeRemaining;
            
            string timeRemaining = "Not available";
            if (remainingSeconds != -1) // If battery life remaining is known
            {
                TimeSpan ts = TimeSpan.FromSeconds(remainingSeconds);
                if (ts.TotalHours >= 1)
                {
                    timeRemaining = $"{ts.TotalHours:0}h {ts.Minutes}m remaining"; // Format as hours and minutes
                }
                else
                {
                    timeRemaining = $"{ts.Minutes}m remaining"; // Format as minutes
                }
            }

            // Retrieve GPU dedicated memory usage
            float gpuMemoryUsage = 0;
            if (_gpuMemoryCounter != null)
            {
                try
                {
                    gpuMemoryUsage = _gpuMemoryCounter.NextValue(); // Get the value in MB
                }
                catch (Exception ex)
                {
                    // Handle potential errors if the counter becomes unavailable
                    Console.WriteLine($"Error getting GPU memory: {ex.Message}");
                    gpuMemoryUsage = 0; // Reset or indicate error
                }
            }

            // Format the final display text with all collected metrics
            _displayText = $"Time Left: {timeRemaining}\n" +
                           $"CPU: {cpuUsage.ToString("F1")}%\n" + // CPU usage formatted to 1 decimal place
                           $"RAM: {availableRamMB.ToString("F0")} MB Free\n" + // RAM usage formatted to whole MB
                           $"GPU VRAM: {gpuMemoryUsage:F0} MB"; // GPU VRAM usage formatted to whole MB
            
            this.Invalidate(); // Request a redraw of the form to show the updated text
        }

        /// <summary>
        /// Toggles the visibility of the overlay form.
        /// </summary>
        public void ToggleVisibility()
        {
            IsVisible = !IsVisible; // Flip the visibility state
            if (IsVisible)
            {
                UpdateWindowPosition(); // Ensure it's on top when shown
                this.Show();
            }
            else
            {
                this.Hide(); // Hide the form
            }
            
            _closeButton.Visible = IsVisible; // Show/hide the close button with the overlay
        }

        /// <summary>
        /// Ensures the window stays on top and is visible.
        /// </summary>
        private void UpdateWindowPosition()
        {
            const uint flags = SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW;
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
        }

        /// <summary>
        /// Handles window messages, including hotkey presses.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m); // Process the message with the default handler

            // Check if the message is a hotkey notification and if it's our registered hotkey ID
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID)
            {
                ToggleVisibility(); // Call the method to show/hide the overlay
            }
        }
    }
}
