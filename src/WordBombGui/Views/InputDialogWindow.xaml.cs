// Prompts for a float within [min,max]. Port of ui.AskFloat in
// ui/dialogs_windows.go.
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace WordBombTool.Views;

public partial class InputDialogWindow : Window
{
    private readonly double _min, _max;
    public double Value { get; private set; }

    public InputDialogWindow(string title, string prompt, double min, double max, double initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        _min = min;
        _max = max;
        ValueBox.Text = initial.ToString("0.####", CultureInfo.InvariantCulture);
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    private void OnValueBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryAccept();
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => TryAccept();
    private void OnCancelClick(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void TryAccept()
    {
        if (!double.TryParse(ValueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            ShowError($"Enter a number between {_min:0.####} and {_max:0.####}.");
            return;
        }
        if (v < _min || v > _max)
        {
            ShowError($"Value must be between {_min:0.####} and {_max:0.####}.");
            return;
        }
        Value = v;
        DialogResult = true;
        Close();
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }

    /// <summary>Prompts for a float in [min,max]. Returns the value and true on
    /// OK, or (0,false) if cancelled. Must be called on the UI thread.</summary>
    public static (double value, bool ok) Ask(Window? owner, string title, string prompt, double min, double max, double initial)
    {
        var win = new InputDialogWindow(title, prompt, min, max, initial);
        if (owner != null && owner.IsVisible) win.Owner = owner;
        var result = win.ShowDialog();
        return result == true ? (win.Value, true) : (0, false);
    }
}
