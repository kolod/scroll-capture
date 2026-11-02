using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ScrollCapture.App.Capture;
using ScrollCapture.App.Platform;

namespace ScrollCapture.App
{
    /// <summary>
    /// Capture without the window, for scripts:
    /// <c>ScrollCapture.exe (--window HWND | --point X,Y) --out FILE [--scroll METHOD]
    /// [--capture METHOD] [--delay MS] [--max-height PX] [--no-top] [--frames DIR]</c>.
    /// Exit code 0 on success, 1 on a capture or save error, 2 on bad arguments.
    /// </summary>
    internal static class CommandLine
    {
        private const string Usage =
            "Usage: ScrollCapture.exe (--window HWND | --point X,Y) --out FILE [--scroll Auto|MouseWheel|ScrollMessages|PageDownKey]\n" +
            "       [--capture Screen|PrintWindow] [--delay MS] [--max-height PX] [--no-top] [--frames DIR]\n" +
            "HWND is decimal or 0x-hex; X,Y are physical screen pixels; the format follows the extension (.png .jpg .bmp .tif).\n" +
            "--frames saves every screenshot to DIR (diagnostics).";

        public static int Run(string[] args)
        {
            AttachConsole(-1); // print to the console that started us, if any
            IntPtr window = IntPtr.Zero;
            string output = null;
            var options = new CaptureOptions();
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--window":
                            var text = Value(args, ref i);
                            window = (IntPtr)(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                                ? long.Parse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                                : long.Parse(text, CultureInfo.InvariantCulture));
                            break;
                        case "--point":
                            var parts = Value(args, ref i).Split(',');
                            window = WindowFinder.WindowAt(new System.Drawing.Point(
                                int.Parse(parts[0], CultureInfo.InvariantCulture),
                                int.Parse(parts[1], CultureInfo.InvariantCulture)));
                            break;
                        case "--out":
                            output = Path.GetFullPath(Value(args, ref i));
                            break;
                        case "--scroll":
                            options.ScrollMethod = (ScrollMethod)Enum.Parse(typeof(ScrollMethod), Value(args, ref i), true);
                            break;
                        case "--capture":
                            options.CaptureMethod = (CaptureMethod)Enum.Parse(typeof(CaptureMethod), Value(args, ref i), true);
                            break;
                        case "--delay":
                            options.DelayMs = Math.Max(0, int.Parse(Value(args, ref i), CultureInfo.InvariantCulture));
                            break;
                        case "--max-height":
                            options.MaxHeight = Math.Max(1, int.Parse(Value(args, ref i), CultureInfo.InvariantCulture));
                            break;
                        case "--no-top":
                            options.StartFromTop = false;
                            break;
                        case "--frames":
                            options.FrameFolder = Path.GetFullPath(Value(args, ref i));
                            break;
                        default:
                            throw new FormatException("Unknown argument: " + args[i]);
                    }
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException || ex is IndexOutOfRangeException)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(Usage);
                return 2;
            }

            var target = window == IntPtr.Zero || !NativeMethods.IsWindow(window) ? null : CaptureTarget.ForWindow(window);
            if (target == null || output == null)
            {
                Console.Error.WriteLine(target == null ? "No visible window to capture." : "--out is required.");
                Console.Error.WriteLine(Usage);
                return 2;
            }

            if (options.CaptureMethod == CaptureMethod.Screen || options.ScrollMethod != ScrollMethod.ScrollMessages)
            {
                // Screen pixels and simulated input need the window in front.
                NativeMethods.SetForegroundWindow(target.Root);
                Thread.Sleep(300);
            }

            return RunSession(target, options, output);
        }

        private static int RunSession(CaptureTarget target, CaptureOptions options, string output)
        {
            int exitCode = 1;
            using (var context = new ApplicationContext())
            using (var start = new System.Windows.Forms.Timer { Interval = 1 })
            {
                // The session awaits on the UI thread: run it inside a message loop.
                start.Tick += async (s, e) =>
                {
                    start.Stop();
                    try
                    {
                        var result = await new CaptureSession(target, options).RunAsync();
                        using (var bitmap = FrameGrabber.ToBitmap(result.Image))
                        {
                            var format = ImageFiles.FromExtension(output);
                            if (!ImageFiles.Fits(bitmap, format))
                            {
                                throw new IOException("JPEG images cannot be larger than " + ImageFiles.JpegLimit + " px.");
                            }

                            ImageFiles.Write(bitmap, output, format);
                        }

                        Console.WriteLine("{0}: {1} x {2} px from {3} screenshots ({4}).", output, result.Image.Width, result.Image.Height, result.Frames, result.Reason);
                        exitCode = 0;
                    }
                    catch (Exception ex) when (CaptureSession.IsCaptureFailure(ex) || ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Console.Error.WriteLine(ex.Message);
                    }
                    finally
                    {
                        context.ExitThread();
                    }
                };
                start.Start();
                Application.Run(context);
            }

            return exitCode;
        }

        private static string Value(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                throw new FormatException(args[i] + " needs a value.");
            }

            return args[++i];
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(int processId);
    }
}
