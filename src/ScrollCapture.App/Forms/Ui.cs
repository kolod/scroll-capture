using System.Drawing;
using System.Windows.Forms;

namespace ScrollCapture.App.Forms
{
    /// <summary>Small helpers for auto-sizing layouts that tolerate long translations.</summary>
    internal static class Ui
    {
        public static FlowLayoutPanel Flow(bool rightToLeft = false)
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                Dock = DockStyle.Top,
                Margin = new Padding(0)
            };
        }

        public static Button Button()
        {
            return new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(100, 28),
                Padding = new Padding(8, 0, 8, 0)
            };
        }

        /// <summary>A label that lines up with the combo boxes and number fields next to it.</summary>
        public static Label FieldLabel()
        {
            return new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        }
    }

    /// <summary>A combo box item: a value with its localized text.</summary>
    internal sealed class Choice<T>
    {
        public Choice(T value, string text)
        {
            Value = value;
            Text = text;
        }

        public T Value { get; }

        public string Text { get; }

        public override string ToString() => Text;
    }
}
