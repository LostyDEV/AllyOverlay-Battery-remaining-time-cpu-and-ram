using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Management;
using Microsoft.VisualBasic.Devices;
using System.Text;

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

                    Thread.Sleep(50);
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }
    }

    public static class AudioUtils
    {
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB636FD75"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, ERole dwStateMask, out IMMDeviceCollection ppDevices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
        }

        [ComImport, Guid("D666063F-158E-4E75-B7F9-565576EEA703"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            int OpenPropertyStore(EStgmAccess stgmAccess, out IPropertyStore ppProperties);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            int GetState(out EDeviceState pdwState);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
            int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
            int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
            int VolumeStepUp(ref Guid pguidEventContext);
            int VolumeStepDown(ref Guid pguidEventContext);
            int QueryHardwareSupport(out uint pdwHardwareSupportMask);
            int GetVolumeRange(out float pfMin, out float pfMax, out float pfIncrement);
        }

        [ComImport, Guid("657804FA-D6AD-4496-8A60-352752000393"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IAudioEndpointVolumeCallback { }

        [ComImport, Guid("0BD729AE-E5A0-44DB-8397-CC5392D6B0D5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMMDeviceCollection { }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBF6BD6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPropertyStore { }

        internal const int CLSCTX_ALL = 0x17;
        
        internal enum EDataFlow
        {
            eRender,
            eCapture,
            eAll,
            EDataFlow_enum_count
        }

        internal enum ERole
        {
            eConsole,
            eCommunications,
            eMultimedia,
            ERole_enum_count
        }

        internal enum EDeviceState
        {
            ACTIVE = 0x00000001,
            DISABLED = 0x00000002,
            NOTPRESENT = 0x00000004,
            UNPLUGGED = 0x00000008,
            ALL = ACTIVE | DISABLED | NOTPRESENT | UNPLUGGED
        }

        internal enum EStgmAccess
        {
            STGM_READ = 0x00000000
        }

        internal static Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        internal static Guid IID_IMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB636FD75");
        internal static Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        public static bool IsMasterVolumeMuted()
        {
            IMMDeviceEnumerator? deviceEnumerator = null;
            IMMDevice? audioDevice = null;
            IAudioEndpointVolume? epVolume = null;
            bool muted = false;

            try
            {
                deviceEnumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator)!);
                if (deviceEnumerator == null) return false;

                deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out audioDevice);
                if (audioDevice == null) return false;

                object? endpointVolumeObject;
                Guid iid = IID_IAudioEndpointVolume;
                audioDevice.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out endpointVolumeObject);
                epVolume = (IAudioEndpointVolume)endpointVolumeObject!;

                epVolume.GetMute(out muted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking master volume mute state: {ex.Message}");
                return false;
            }
            finally
            {
                if (epVolume != null) Marshal.ReleaseComObject(epVolume);
                if (audioDevice != null) Marshal.ReleaseComObject(audioDevice);
                if (deviceEnumerator != null) Marshal.ReleaseComObject(deviceEnumerator);
            }
            return muted;
        }

        public static bool IsMicrophoneMuted()
        {
            IMMDeviceEnumerator? deviceEnumerator = null;
            IMMDevice? audioDevice = null;
            IAudioEndpointVolume? epVolume = null;
            bool muted = false;

            try
            {
                deviceEnumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator)!);
                if (deviceEnumerator == null) return false;

                deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out audioDevice);
                if (audioDevice == null) return false;

                object? endpointVolumeObject;
                Guid iid = IID_IAudioEndpointVolume;
                audioDevice.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out endpointVolumeObject);
                epVolume = (IAudioEndpointVolume)endpointVolumeObject!;

                epVolume.GetMute(out muted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking microphone mute state: {ex.Message}");
                return false;
            }
            finally
            {
                if (epVolume != null) Marshal.ReleaseComObject(epVolume);
                if (audioDevice != null) Marshal.ReleaseComObject(audioDevice);
                if (deviceEnumerator != null) Marshal.ReleaseComObject(deviceEnumerator);
            }
            return muted;
        }
    }


    public partial class OverlayForm : Form
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x80000;
        private const int LWA_ALPHA = 0x2;

        // Corrected and added the missing constants
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int HWND_TOPMOST_INT = -1;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
        
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
        public const int WM_HOTKEY = 0x0312;
        public const int HOTKEY_ID = 9000;

        private readonly System.Windows.Forms.Timer _timer;
        private bool IsVisible = true;
        private string _displayText = "";

        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private PowerStatus _powerStatus = SystemInformation.PowerStatus;
        
        private readonly Button _closeButton;

        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.Text = "";
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Width / 2 - this.Width / 2, 0);
            this.Size = new Size(300, 200);
            this.TopMost = true;

            SetWindowLong(this.Handle, GWL_EXSTYLE, (IntPtr)((long)GetWindowLong(this.Handle, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);

            RegisterHotKey(this.Handle, HOTKEY_ID, (int)(Keys.Control | Keys.Shift), (int)Keys.L);

            _closeButton = new Button();
            _closeButton.Text = "X";
            _closeButton.ForeColor = Color.White;
            _closeButton.BackColor = Color.Red;
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.FlatStyle = FlatStyle.Flat;
            _closeButton.Font = new Font("Inter", 8, FontStyle.Bold);
            _closeButton.Size = new Size(20, 20);
            _closeButton.Location = new Point(this.Width - _closeButton.Width - 5, 5);
            _closeButton.Click += CloseButton_Click;
            _closeButton.Visible = false;
            this.Controls.Add(_closeButton);

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            }
            catch (Exception ex)
            {
                _displayText = $"Error initializing counters: {ex.Message}";
                _cpuCounter = null;
                _ramCounter = null;
            }

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += OnTimerTick;
            _timer.Start();

            OnTimerTick(null, null);
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            this.Close();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            float cpuUsage = _cpuCounter != null ? _cpuCounter.NextValue() : 0;
            float availableRamMB = _ramCounter != null ? _ramCounter.NextValue() : 0;

            _powerStatus = SystemInformation.PowerStatus;
            double remainingSeconds = _powerStatus.BatteryLifeRemaining;
            string timeRemaining = "N/A";

            if (remainingSeconds != -1)
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

            bool masterVolumeMuted = AudioUtils.IsMasterVolumeMuted();
            bool microphoneMuted = AudioUtils.IsMicrophoneMuted();

            string volumeStatus = masterVolumeMuted ? "Muted" : "On";
            string micStatus = microphoneMuted ? "Muted" : "On";

            _displayText = $"Time Left: {timeRemaining}\n" +
                           $"CPU: {cpuUsage.ToString("F1")}%\n" +
                           $"RAM: {availableRamMB.ToString("F0")} MB Free\n" +
                           $"Volume: {volumeStatus}\n" +
                           $"Mic: {micStatus}";

            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!IsVisible) return;

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            using (SolidBrush backgroundBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                e.Graphics.FillRectangle(backgroundBrush, new Rectangle(0, 0, this.Width, this.Height));
            }

            using (Font font = new Font("Inter", 12, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.Red))
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
                UpdateWindowPosition();
                this.Show();
            }
            else
            {
                this.Hide();
            }

            _closeButton.Visible = IsVisible;
            this.Invalidate();
        }

        private void UpdateWindowPosition()
        {
            const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW;
            SetWindowPos(this.Handle, HWND_TOPMOST_INT, 0, 0, 0, 0, flags);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID)
            {
                ToggleVisibility();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
