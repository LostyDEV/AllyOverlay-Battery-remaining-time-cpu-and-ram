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

        public const int VK_LBUTTON = 0x01; // Left mouse button
        private static readonly int TopScreenTolerance = 20; // Pixels from the top edge to start checking for drag
        private static readonly int DragThreshold = 50; // Minimum drag distance to trigger visibility toggle

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
            // Run touch detection in a background thread to avoid blocking the UI
            Thread thread = new Thread(() =>
            {
                while (true)
                {
                    // Check if the left mouse button is currently being held down
                    if (GetAsyncKeyState(VK_LBUTTON) != 0)
                    {
                        // If this is the start of a new click
                        if (!isMouseDown)
                        {
                            isMouseDown = true;
                            Point cursorPos = Cursor.Position;
                            // Only consider drag if starting from the very top of the screen
                            if (cursorPos.Y <= TopScreenTolerance)
                            {
                                startY = cursorPos.Y; // Record the starting Y position
                            }
                        }

                        // If a drag is in progress from the top
                        if (isMouseDown && startY > 0) // Ensure startY was set
                        {
                            Point currentPos = Cursor.Position;
                            // Check if the drag distance exceeds the threshold
                            if (currentPos.Y - startY >= DragThreshold)
                            {
                                // Toggle overlay visibility on the UI thread
                                _form.Invoke(new MethodInvoker(() =>
                                {
                                    if (!_form.IsVisible)
                                    {
                                        _form.ToggleVisibility();
                                    }
                                }));
                                isMouseDown = false; // Reset mouse state to prevent repeated toggles
                                startY = 0; // Reset start Y position
                            }
                        }
                    }
                    else
                    {
                        // If the mouse button is released, reset states
                        isMouseDown = false;
                        startY = 0;
                    }
                    Thread.Sleep(10); // Small delay to reduce CPU usage
                }
            });
            thread.IsBackground = true; // Allow the application to exit even if this thread is running
            thread.Start();
        }
    }

    public class OverlayForm : Form
    {
        // Constants for Windows API functions (P/Invoke)
        private const int GWL_EXSTYLE = -20; // Extended Window Style flag index
        private const int WS_EX_TOPMOST = 0x0008; // Window is always on top
        private const int WS_EX_NOACTIVATE = 0x08000000; // Window cannot be activated
        private const int WS_EX_TOOLWINDOW = 0x00000080; // Window is a tool window (not shown in taskbar)
        private const int WS_EX_TRANSPARENT = 0x00000020; // Window is transparent to mouse clicks
        private const int LWA_ALPHA = 0x2; // Flag for SetLayeredWindowAttributes to use the alpha value

        // Import the SetWindowLongPtr and GetWindowLongPtr functions for 64-bit compatibility
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        // Import functions for setting window transparency and position
        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // Constants for SetWindowPos function
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1); // Place window above all non-topmost windows
        private const uint SWP_NOSIZE = 0x0001; // If calling SetWindowPos, the cx and cy parameters are ignored
        private const uint SWP_NOMOVE = 0x0002; // If calling SetWindowPos, the X and Y parameters are ignored
        private const uint SWP_SHOWWINDOW = 0x0040; // If the window is not already visible, show it

        // Hotkey registration imports
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const int WM_HOTKEY = 0x0312; // Message sent by Windows when a hotkey is pressed
        public const int HOTKEY_ID = 1; // Unique ID for our hotkey

        private string _displayText = ""; // Text to display on the overlay
        private System.Windows.Forms.Timer _timer; // Timer for updating displayed information
        private System.Windows.Forms.Timer _devTextTimer; // Timer for fading out the developer text
        private bool _showDevText = true; // Flag to control visibility of developer text
        private Button _closeButton; // Button to close the application

        // Performance counters and power status, made nullable to handle potential initialization errors
        private PowerStatus? _powerStatus;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private PerformanceCounter? _gpuMemoryCounter; // Counter for GPU VRAM usage

        public bool IsVisible { get; private set; } = true; // Property to track overlay's visibility

        public OverlayForm()
        {
            // Form properties for a borderless, transparent overlay
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.Size = new Size(300, 150); // Initial size of the overlay
            this.StartPosition = FormStartPosition.Manual; // Position will be set manually

            // Center the overlay horizontally on the primary screen
            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int x = (screenWidth - this.Width) / 2;
            int y = 0; // Position at the very top
            this.Location = new Point(x, y);
            this.TopMost = true; // Keep the overlay on top of all other windows
            this.AllowTransparency = true; // Allow transparency
            this.BackColor = Color.Black; // Set background to black for transparency key
            this.TransparencyKey = Color.Black; // Use black as the transparent color

            // Apply extended window styles for transparency, non-activation, and tool window behavior
            IntPtr currentStyle = GetWindowLongPtr(this.Handle, GWL_EXSTYLE);
            SetWindowLongPtr(this.Handle, GWL_EXSTYLE, (IntPtr)((long)currentStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            
            // Set the initial alpha for transparency
            SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);

            // Initialize the close button
            _closeButton = new Button();
            _closeButton.Text = "X";
            _closeButton.ForeColor = Color.White;
            _closeButton.BackColor = Color.DarkRed;
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            _closeButton.Size = new Size(20, 20);
            _closeButton.Location = new Point(this.Width - _closeButton.Width - 5, 5); // Position in the top-right corner
            _closeButton.Click += (sender, e) => { this.Close(); }; // Close the application on click
            _closeButton.Visible = false; // Initially hidden
            this.Controls.Add(_closeButton);

            // Register the hotkey (Shift + L) to toggle visibility
            RegisterHotKey(this.Handle, HOTKEY_ID, (int)Keys.Shift, (int)Keys.L);

            // Initialize performance counters and power status
            try
            {
                _powerStatus = SystemInformation.PowerStatus; // Get current power status
                // Processor time for all cores aggregated
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                // Available physical memory in MB
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                
                // Initialize the GPU memory counter. **IMPORTANT**: Replace the instance name if this does not work on your system.
                // You can find correct instance names using Performance Monitor (perfmon.msc).
                _gpuMemoryCounter = new PerformanceCounter("GPU Engine", "Dedicated Usage", "pid_0_luid_0x00000000_0x0000F5AC_eng_3D");
            }
            catch (Exception ex)
            {
                // If any counter fails to initialize, display an error message
                _displayText = $"Error: {ex.Message}";
                _cpuCounter = null;
                _ramCounter = null;
                _gpuMemoryCounter = null;
            }

            // Setup timer for updating displayed information every second
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000; // 1000 milliseconds = 1 second
            _timer.Tick += OnTimerTick; // Hook up the event handler
            _timer.Start(); // Start the timer

            // Setup timer for fading out the developer text after a few seconds
            _devTextTimer = new System.Windows.Forms.Timer();
            _devTextTimer.Interval = 3000; // 3 seconds
            _devTextTimer.Tick += (sender, e) =>
            {
                _showDevText = false; // Hide developer text
                _devTextTimer.Stop(); // Stop this timer
                this.Invalidate(); // Redraw the form to remove the text
            };
            _devTextTimer.Start(); // Start the developer text timer
        }

        // Override the OnPaint method to draw the overlay content
        protected override void OnPaint(PaintEventArgs e)
        {
            // Do not draw if the overlay is hidden
            if (!IsVisible) return;

            // Enable anti-aliasing for smoother text and graphics
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            
            // Draw a semi-transparent black background for the overlay
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) // 150 is alpha for transparency
            {
                e.Graphics.FillRectangle(brush, new Rectangle(0, 0, this.Width, this.Height));
            }

            // Draw the main system information text
            using (Font font = new Font("Inter", 12, FontStyle.Bold)) // Using Inter font, bold, size 12
            using (SolidBrush textBrush = new SolidBrush(Color.White)) // White text color
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Near; // Align text to the left
                sf.LineAlignment = StringAlignment.Near; // Align text to the top
                // Draw the text within a rectangle, leaving some padding
                e.Graphics.DrawString(_displayText, font, textBrush, new RectangleF(10, 10, this.Width - 20, this.Height - 20), sf);
            }
            
            // Draw the "Developed by LostyDEV" text if _showDevText is true
            if (_showDevText)
            {
                using (Font font = new Font("Inter", 10, FontStyle.Italic)) // Smaller, italic font
                using (SolidBrush devTextBrush = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) // Semi-transparent white
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center; // Center align text horizontally
                    sf.LineAlignment = StringAlignment.Far; // Align text to the bottom
                    // Draw text centered at the bottom of the overlay
                    e.Graphics.DrawString("Developed by LostyDEV", font, devTextBrush, new RectangleF(0, 0, this.Width, this.Height - 5), sf);
                }
            }
        }

        // Event handler for the timer tick
        private void OnTimerTick(object sender, EventArgs e)
        {
            // Get CPU usage
            float cpuUsage = _cpuCounter != null ? _cpuCounter.NextValue() : 0;
            // Get available RAM in MB
            float availableRamMB = _ramCounter != null ? _ramCounter.NextValue() : 0;
            
            // Refresh power status
            _powerStatus = SystemInformation.PowerStatus;
            // Calculate battery life percentage and remaining time
            int batteryLifePercent = (int)(_powerStatus.BatteryLifePercent * 100);
            double remainingSeconds = _powerStatus.BatteryLifeRemaining;
            
            string timeRemaining = "Not available"; // Default text
            if (remainingSeconds != -1) // -1 indicates battery is plugged in or status is unknown
            {
                TimeSpan ts = TimeSpan.FromSeconds(remainingSeconds);
                if (ts.TotalHours >= 1)
                {
                    // Format as hours and minutes if remaining time is an hour or more
                    timeRemaining = $"{ts.TotalHours:0}h {ts.Minutes}m remaining";
                }
                else
                {
                    // Format as minutes if remaining time is less than an hour
                    timeRemaining = $"{ts.Minutes}m remaining";
                }
            }

            // Get GPU VRAM usage
            float gpuMemoryUsage = 0;
            if (_gpuMemoryCounter != null)
            {
                gpuMemoryUsage = _gpuMemoryCounter.NextValue();
            }

            // Construct the string to display on the overlay
            _displayText = $"Time Left: {timeRemaining}\n" +
                           $"CPU: {cpuUsage.ToString("F1")}% (Total)\n" + // Display CPU usage formatted to one decimal place
                           $"RAM: {availableRamMB.ToString("F0")} MB Free\n" + // Display RAM usage as whole number
                           $"GPU VRAM: {gpuMemoryUsage:F0} MB"; // Display GPU VRAM usage as whole number

            this.Invalidate(); // Request a redraw of the form to show updated text
        }

        // Method to toggle the visibility of the overlay
        public void ToggleVisibility()
        {
            IsVisible = !IsVisible; // Flip the visibility state
            if (IsVisible)
            {
                UpdateWindowPosition(); // Ensure it's on top and correctly positioned
                this.Show(); // Make the form visible
            }
            else
            {
                this.Hide(); // Hide the form
            }
            
            // Show or hide the close button based on the overlay's visibility
            _closeButton.Visible = IsVisible;
        }

        // Helper method to ensure the window stays on top and at the top of the screen
        private void UpdateWindowPosition()
        {
            const uint flags = SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW; // Flags to keep current size/position and show window
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags); // Set window to be topmost
        }

        // Override WndProc to handle Windows messages, specifically the hotkey message
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m); // Pass message to the base class handler

            // Check if the message is a hotkey message and if it's our registered hotkey ID
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID)
            {
                ToggleVisibility(); // Call the method to toggle overlay visibility
            }
        }
    }
}
