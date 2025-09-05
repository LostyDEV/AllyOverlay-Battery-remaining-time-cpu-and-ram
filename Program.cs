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
                                // Drag down gesture detected, show the form
                                _form.Invoke(new MethodInvoker(() =>
                                {
                                    if (!_form.IsVisible)
                                    {
                                        _form.ToggleVisibility();
                                    }
                                }));
                                // Reset for next gesture
                                isMouseDown = false;
                            }
                        }
                    }
                    else
                    {
                        isMouseDown = false;
                    }

                    // Sleep for a short duration to prevent high CPU usage
                    Thread.Sleep(10);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }
    }

    public class OverlayForm : Form
    {
        // P
