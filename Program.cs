using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading.Tasks; // Added for Task.Delay
using System.Management; // Added for WMI

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
                            startY = Cursor.Position.Y;
                        }
                        else
                        {
                            int currentY = Cursor.Position.Y;
                            if (startY < TopScreenTolerance && currentY - startY > DragThreshold)
                            {
                                _form.ToggleVisibility();
                            }
                        }
                    }
                    else
                    {
                        isMouseDown = false;
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
        // ... (rest of the class) ...
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private bool IsVisible = true;
        private string _displayText = "";

        // WMI related
        private PerformanceCounter _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        private readonly DateTime _startTime = DateTime.Now;

        private readonly Button _closeButton;

        // Constants for window placement
        private const int HWND_TOPMOST = -1;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 9000;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.Text = "";
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(0, 0);
            this.Size = new Size(Screen.PrimaryScreen.WorkingArea.Width, Screen.PrimaryScreen.WorkingArea.Height);
            this.TopMost = true;

            // Set the background color to be transparent
            this.BackColor = Color.LimeGreen;
            this.TransparencyKey = this.BackColor;

            // Register the hotkey (e.g., Ctrl+Alt+F1) to toggle visibility
            const uint MOD_CONTROL = 0x0002;
            const uint MOD_ALT = 0x0001;
            const int VK_F1 = 0x70;
            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_F1);

            // Close button
            _closeButton = new Button
            {
                Text = "X",
                ForeColor = Color.White,
                BackColor = Color.Red,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Arial", 10, FontStyle.Bold),
                Size = new Size(20, 20),
                Location = new Point(this.ClientSize.Width - 25, 5),
                Visible = IsVisible // Initially visible
            };
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.Click += CloseButton_Click;
            this.Controls.Add(_closeButton);

            // Start a timer to update stats
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000; // Update every 1 second
            _timer.Tick += (sender, e) => UpdateStats();
            _timer.Start();

            // Initial update
            UpdateStats();
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            this.Close();
        }

        private double GetCpuTemperature()
        {
            double temperature = 0;
            try
            {
                // Query the WMI class for thermal zone temperature
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    // The value is in Kelvin * 10, so convert it to Celsius
                    temperature = (Convert.ToDouble(obj["CurrentTemperature"].ToString()) - 2732) / 10.0;
                    break; // We only need the first reading
                }
            }
            catch (Exception ex)
            {
                // Handle any errors that may occur
                Console.WriteLine($"Error getting CPU temperature: {ex.Message}");
            }
            return temperature;
        }

        private void UpdateStats()
        {
            // Calculate elapsed time
            TimeSpan elapsedTime = DateTime.Now - _startTime;
            string timeRemaining = "";
            TimeSpan totalTime = TimeSpan.FromMinutes(45);
            TimeSpan ts = totalTime - elapsedTime;

            // Get CPU and RAM usage
            float cpuUsage = _cpuCounter.NextValue();
            long availableRamMB = new Microsoft.VisualBasic.Devices.ComputerInfo().AvailablePhysicalMemory / (1024 * 1024);
            
            // Check if there is time left on the timer
            if (ts.TotalSeconds > 0)
            {
                if (ts.TotalHours >= 1)
                {
                    timeRemaining = $"{ts.TotalHours:0}h {ts.Minutes}m remaining";
                }
                else
                {
                    timeRemaining = $"{ts.Minutes}m remaining";
                }
            }
            else
            {
                timeRemaining = "Timer finished";
            }
            
            double cpuTemp = GetCpuTemperature();

            // Format the final display text with all metrics
            _displayText = $"Time Left: {timeRemaining}\n" +
                           $"CPU: {cpuUsage.ToString("F1")}%\n" +
                           $"CPU Temp: {cpuTemp.ToString("F1")} °C\n" +
                           $"RAM: {availableRamMB.ToString("F0")} MB Free";
            
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
            
            // Show or hide the close button when visibility is toggled
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
