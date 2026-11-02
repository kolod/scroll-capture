using System;
using System.Windows.Forms;
using ScrollCapture.App.Forms;
using ScrollCapture.App.Localization;
using ScrollCapture.App.Settings;

namespace ScrollCapture.App
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0)
            {
                return CommandLine.Run(args);
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowFatal(e.ExceptionObject as Exception);

            var settings = PortableSettings.Load();
            var language = LanguageService.IsSupported(settings.Language) ? settings.Language : LanguageService.DetectOsLanguage();
            LanguageService.SetLanguage(language);

            using (var form = new MainForm(settings))
            {
                Application.Run(form);
            }

            return 0;
        }

        private static void ShowFatal(Exception ex)
        {
            try
            {
                MessageBox.Show(
                    Strings.Format("Error_Unhandled", ex?.ToString() ?? "unknown"),
                    Strings.Get("App_Name"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                // Nothing else can be done here.
            }
        }
    }
}
