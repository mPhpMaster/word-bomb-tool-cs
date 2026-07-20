// Modal message boxes. Port of app/msgbox_windows.go -- WPF's built-in
// MessageBox makes the original's raw MessageBoxW P/Invoke unnecessary.
using System.Windows;

namespace WordBombTool;

public static class MessageBoxes
{
    public static bool YesNo(string title, string text) =>
        System.Windows.MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public static void Info(string title, string text) =>
        System.Windows.MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
