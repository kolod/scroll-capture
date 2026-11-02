using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScrollCapture.App.Capture;
using ScrollCapture.App.Localization;
using ScrollCapture.App.Platform;
using ScrollCapture.App.Settings;
using ScrollCapture.Core.Imaging;
using ScrollCapture.Core.Stitching;

namespace ScrollCapture.App.Forms
{
    /// <summary>Main window: the capture button, options, a preview of the result and saving.</summary>
    internal sealed class MainForm : Form, ILocalizable
    {
        private readonly PortableSettings _settings;
        private readonly MenuStrip _menu = new MenuStrip();
        private readonly ToolStripMenuItem _languageMenu = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _helpMenu = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _aboutItem = new ToolStripMenuItem();
        private readonly Button _capture = Ui.Button();
        private readonly Label _hint = new Label { AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
        private readonly Label _scrollLabel = Ui.FieldLabel();
        private readonly ComboBox _scrollMethod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly Label _captureLabel = Ui.FieldLabel();
        private readonly ComboBox _captureMethod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        private readonly Label _delayLabel = Ui.FieldLabel();
        private readonly NumericUpDown _delay = new NumericUpDown { Minimum = 0, Maximum = 5000, Increment = 50, Width = 70 };
        private readonly CheckBox _startFromTop = new CheckBox { AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        private readonly CheckBox _askWhereToSave = new CheckBox { AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        private readonly CheckBox _fitWidth = new CheckBox { AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        private readonly CheckBox _trimBackground = new CheckBox { AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        private readonly ImagePreview _preview = new ImagePreview { Dock = DockStyle.Fill };
        private readonly Label _status = new Label { AutoSize = true, Margin = new Padding(3, 9, 3, 3) };
        private readonly Button _save = Ui.Button();
        private readonly Button _copy = Ui.Button();
        private TaskbarProgress _taskbar;
        private FrameBitmap _result;
        private CaptureResult _lastCapture;
        private bool _busy;

        public MainForm(PortableSettings settings)
        {
            _settings = settings;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = UiFont.For(LanguageService.Current);
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcon.Icon;

            foreach (var language in LanguageService.Supported)
            {
                var item = new ToolStripMenuItem(language.NativeName) { Tag = language.Code };
                item.Click += (s, e) => ChangeLanguage((string)((ToolStripMenuItem)s).Tag);
                _languageMenu.DropDownItems.Add(item);
            }

            _aboutItem.Click += (s, e) =>
            {
                using (var about = new AboutDialog())
                {
                    about.ShowDialog(this);
                }
            };
            _helpMenu.DropDownItems.Add(_aboutItem);
            _menu.Items.AddRange(new ToolStripItem[] { _languageMenu, _helpMenu });

            _capture.MinimumSize = new Size(200, 40);
            _capture.Font = new Font(Font, FontStyle.Bold);
            _capture.Click += (s, e) => StartCapture();

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.Controls.Add(_capture, 0, 0);
            top.Controls.Add(_hint, 1, 0);

            var options = Ui.Flow();
            options.Margin = new Padding(0, 6, 0, 0);
            options.Controls.AddRange(new Control[] { _scrollLabel, _scrollMethod, _captureLabel, _captureMethod, _delayLabel, _delay });
            top.Controls.Add(options, 0, 1);
            top.SetColumnSpan(options, 2);

            var checks = Ui.Flow();
            checks.Controls.AddRange(new Control[] { _startFromTop, _trimBackground, _askWhereToSave, _fitWidth });
            top.Controls.Add(checks, 0, 2);
            top.SetColumnSpan(checks, 2);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, Padding = new Padding(8, 4, 8, 8) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(_status, 0, 0);
            bottom.Controls.Add(_copy, 1, 0);
            bottom.Controls.Add(_save, 2, 0);
            _save.Click += (s, e) => Save();
            _copy.Click += (s, e) => CopyToClipboard();

            var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
            previewHost.Controls.Add(_preview);

            Controls.Add(previewHost);
            Controls.Add(bottom);
            Controls.Add(top);
            Controls.Add(_menu);
            MainMenuStrip = _menu;

            _delay.Value = Clamp(settings.DelayMs, _delay);
            _startFromTop.Checked = settings.StartFromTop;
            _askWhereToSave.Checked = settings.AskWhereToSave;
            _fitWidth.Checked = settings.FitWidth;
            _preview.FitWidth = settings.FitWidth;
            _fitWidth.CheckedChanged += (s, e) => _preview.FitWidth = _fitWidth.Checked;
            _trimBackground.Checked = settings.TrimBackground;
            _trimBackground.CheckedChanged += (s, e) =>
            {
                if (_lastCapture != null && !_busy)
                {
                    ShowResult(_lastCapture);
                }
            };

            LanguageService.LanguageChanged += OnLanguageChanged;
            ApplyStrings();
            SelectByName<ScrollMethod>(_scrollMethod, settings.ScrollMethod);
            SelectByName<CaptureMethod>(_captureMethod, settings.CaptureMethod);
            UpdateButtons();
        }

        public void ApplyStrings()
        {
            Text = Strings.Get("App_Title");
            _languageMenu.Text = Strings.Get("Menu_Language");
            _helpMenu.Text = Strings.Get("Menu_Help");
            _aboutItem.Text = Strings.Get("Menu_About");
            foreach (ToolStripMenuItem item in _languageMenu.DropDownItems)
            {
                item.Checked = string.Equals((string)item.Tag, LanguageService.Current, StringComparison.Ordinal);
            }

            _capture.Text = Strings.Get("Main_Capture");
            _hint.Text = Strings.Get("Main_Hint");
            _scrollLabel.Text = Strings.Get("Options_ScrollMethod");
            _captureLabel.Text = Strings.Get("Options_CaptureMethod");
            _delayLabel.Text = Strings.Get("Options_Delay");
            _startFromTop.Text = Strings.Get("Options_StartFromTop");
            _askWhereToSave.Text = Strings.Get("Options_AskWhereToSave");
            _fitWidth.Text = Strings.Get("Options_FitWidth");
            _trimBackground.Text = Strings.Format("Options_TrimBackground", BackgroundTrimmer.DefaultMargin);
            _save.Text = Strings.Get("Main_Save");
            _copy.Text = Strings.Get("Main_Copy");
            _preview.EmptyText = Strings.Get("Preview_Empty");
            _preview.Invalidate();
            FillChoices(_scrollMethod, "Scroll_");
            FillChoices(_captureMethod, "Capture_");
            _status.Text = _lastCapture == null ? Strings.Get("Status_Ready") : ResultText(_lastCapture);
            WrapHint();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _taskbar = new TaskbarProgress(Handle);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            WrapHint();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true; // the capture restores this window when it ends
                return;
            }

            StoreOptions();
            _settings.SaveIfChanged();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                LanguageService.LanguageChanged -= OnLanguageChanged;
                _result?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static decimal Clamp(int value, NumericUpDown box) => Math.Max(box.Minimum, Math.Min(box.Maximum, value));

        private static void FillChoices<T>(ComboBox box, T[] values, string prefix)
            where T : struct
        {
            var selected = (box.SelectedItem as Choice<T>)?.Value;
            box.Items.Clear();
            foreach (var value in values)
            {
                box.Items.Add(new Choice<T>(value, Strings.Get(prefix + value)));
            }

            int index = selected.HasValue ? Array.IndexOf(values, selected.Value) : 0;
            box.SelectedIndex = Math.Max(0, index);
        }

        private static void SelectByName<T>(ComboBox box, string name)
            where T : struct
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is Choice<T> choice && string.Equals(choice.Value.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                }
            }
        }

        private static T Selected<T>(ComboBox box) => ((Choice<T>)box.SelectedItem).Value;

        private void FillChoices(ComboBox box, string prefix)
        {
            if (box == _scrollMethod)
            {
                FillChoices(box, (ScrollMethod[])Enum.GetValues(typeof(ScrollMethod)), prefix);
            }
            else
            {
                FillChoices(box, (CaptureMethod[])Enum.GetValues(typeof(CaptureMethod)), prefix);
            }
        }

        private void WrapHint()
        {
            int width = ClientSize.Width - _capture.Width - _hint.Margin.Horizontal - 40;
            _hint.MaximumSize = new Size(Math.Max(100, width), 0);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            Font = UiFont.For(LanguageService.Current);
            _capture.Font = new Font(Font, FontStyle.Bold);
            ApplyStrings();
        }

        private void ChangeLanguage(string code)
        {
            _settings.Language = code;
            LanguageService.SetLanguage(code);
        }

        private void StoreOptions()
        {
            _settings.ScrollMethod = Selected<ScrollMethod>(_scrollMethod).ToString();
            _settings.CaptureMethod = Selected<CaptureMethod>(_captureMethod).ToString();
            _settings.DelayMs = (int)_delay.Value;
            _settings.StartFromTop = _startFromTop.Checked;
            _settings.AskWhereToSave = _askWhereToSave.Checked;
            _settings.FitWidth = _fitWidth.Checked;
            _settings.TrimBackground = _trimBackground.Checked;
        }

        private CaptureOptions ReadOptions()
        {
            return new CaptureOptions
            {
                ScrollMethod = Selected<ScrollMethod>(_scrollMethod),
                CaptureMethod = Selected<CaptureMethod>(_captureMethod),
                DelayMs = (int)_delay.Value,
                StartFromTop = _startFromTop.Checked,
                Stitch = new StitchOptions { IgnoreRightColumns = Math.Max(24, 24 * DeviceDpi / 96) }
            };
        }

        private async void StartCapture()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            UpdateButtons();
            StoreOptions();
            var options = ReadOptions();
            var state = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
            CaptureResult result = null;
            Exception error = null;
            try
            {
                // Out of the way of the window to capture (and of the frozen screenshot).
                WindowState = FormWindowState.Minimized;
                await Task.Delay(400);

                var target = await WindowPicker.PickAsync(Font);
                if (target == null)
                {
                    return;
                }

                await Task.Delay(150); // let the screen repaint without the picker
                NativeMethods.SetForegroundWindow(target.Root);
                await Task.Delay(200);

                _taskbar?.SetState(TaskbarState.Indeterminate);
                var session = new CaptureSession(target, options);
                session.Progress += (frames, height) => Text = Strings.Format("Title_Capturing", height, frames);
                try
                {
                    result = await session.RunAsync();
                }
                catch (Exception ex) when (CaptureSession.IsCaptureFailure(ex) || ex is OutOfMemoryException)
                {
                    error = ex;
                }
            }
            finally
            {
                _taskbar?.SetState(TaskbarState.None);
                Text = Strings.Get("App_Title");
                WindowState = state;
                Activate();
                ToForeground();
                _busy = false;
                UpdateButtons();
            }

            if (error != null)
            {
                MessageBox.Show(this, Strings.Format("Error_CaptureFailed", error.Message), Strings.Get("App_Name"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (result != null)
            {
                ShowResult(result);
                if (_askWhereToSave.Checked)
                {
                    Save();
                }
            }
        }

        // Shows the capture, trimmed or not; the untrimmed image is kept so the option can be switched afterwards.
        private void ShowResult(CaptureResult capture)
        {
            PixelFrame image;
            try
            {
                image = _trimBackground.Checked ? BackgroundTrimmer.Trim(capture.Image) : capture.Image;
            }
            catch (OutOfMemoryException)
            {
                image = capture.Image; // no room for a trimmed copy: show the whole capture
            }

            var bitmap = new FrameBitmap(image);
            _preview.Image = bitmap.Bitmap;
            _result?.Dispose();
            _result = bitmap;
            _lastCapture = capture;
            _status.Text = ResultText(capture);
            UpdateButtons();
        }

        private string ResultText(CaptureResult capture)
        {
            return Strings.Format("Status_Result", _result.Width, _result.Height, capture.Frames, Strings.Get("Stop_" + capture.Reason));
        }

        private void UpdateButtons()
        {
            _capture.Enabled = !_busy;
            _save.Enabled = !_busy && _result != null;
            _copy.Enabled = !_busy && _result != null;
        }

        private async void Save()
        {
            if (_result == null)
            {
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                Title = Strings.Get("Dialog_SaveTitle"),
                Filter = Strings.Get("Dialog_ImageFilter"),
                FilterIndex = Math.Max(1, Math.Min(4, _settings.LastFormat)),
                FileName = Strings.Get("Save_DefaultName") + "-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture),
                InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                AddExtension = true,
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var format = ImageFiles.FromFilterIndex(dialog.FilterIndex);
                if (!ImageFiles.Fits(_result, format))
                {
                    MessageBox.Show(this, Strings.Format("Error_JpegTooLarge", ImageFiles.JpegLimit), Strings.Get("App_Name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var image = _result;
                var path = dialog.FileName;
                _busy = true;
                UpdateButtons();
                UseWaitCursor = true;
                _status.Text = ResultText(_lastCapture) + "  " + Strings.Get("Save_Saving");
                try
                {
                    // PNG of a tall capture takes a while: our encoder reads only the pixels, so it
                    // runs in the background. GDI+ formats use the bitmap the preview draws from.
                    if (ImageFiles.IsThreadSafe(format))
                    {
                        await Task.Run(() => ImageFiles.Write(image, path, format));
                    }
                    else
                    {
                        ImageFiles.Write(image, path, format);
                    }

                    _settings.LastFolder = Path.GetDirectoryName(path);
                    _settings.LastFormat = dialog.FilterIndex;
                    _status.Text = ResultText(_lastCapture) + "  " + Strings.Format("Save_Saved", path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ExternalException || ex is OutOfMemoryException)
                {
                    _status.Text = ResultText(_lastCapture);
                    MessageBox.Show(this, Strings.Format("Error_Save", ex.Message), Strings.Get("App_Name"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    UseWaitCursor = false;
                    _busy = false;
                    UpdateButtons();
                }
            }
        }

        private void CopyToClipboard()
        {
            if (_result == null)
            {
                return;
            }

            try
            {
                Clipboard.SetImage(_result.Bitmap);
                _status.Text = ResultText(_lastCapture) + "  " + Strings.Get("Copy_Done");
            }
            catch (Exception ex) when (ex is ExternalException || ex is OutOfMemoryException)
            {
                MessageBox.Show(this, Strings.Format("Error_Copy", ex.Message), Strings.Get("App_Name"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Windows only lets the foreground program activate windows; join its input queue to get the focus back.
        private void ToForeground()
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == Handle)
            {
                return;
            }

            uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
            uint ownThread = NativeMethods.GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != ownThread && NativeMethods.AttachThreadInput(ownThread, foregroundThread, true);
            try
            {
                NativeMethods.SetForegroundWindow(Handle);
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(ownThread, foregroundThread, false);
                }
            }
        }
    }
}
