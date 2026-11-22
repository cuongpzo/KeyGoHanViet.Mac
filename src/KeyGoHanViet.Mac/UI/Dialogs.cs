using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace KeyGoHanViet.Mac.UI;

/// <summary>
/// Hộp thoại thông báo và hỏi đáp.
///
/// Avalonia không có sẵn thứ tương đương <c>MessageBox.Show</c> của WinForms,
/// nên dựng lấy một cái tối giản thay vì kéo thêm cả một thư viện ngoài về chỉ
/// để hiện vài dòng chữ.
/// </summary>
internal static class Dialogs
{
    /// <summary>Hiện thông báo với một nút Đóng.</summary>
    internal static Task ShowMessage(Window? owner, string title, string message) =>
        Show(owner, title, message, "Đóng", cancelText: null);

    /// <summary>Hỏi Có/Không. Trả true nếu người dùng chọn <paramref name="confirmText"/>.</summary>
    internal static Task<bool> Confirm(Window? owner, string title, string message,
                                       string confirmText = "Đồng ý", string cancelText = "Huỷ") =>
        Show(owner, title, message, confirmText, cancelText);

    private static async Task<bool> Show(Window? owner, string title, string message,
                                         string confirmText, string? cancelText)
    {
        bool confirmed = false;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var dialog = new Window
        {
            Title = title,
            Icon = MacTrayIcons.App,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            MinWidth = 380,
        };

        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, MinWidth = 90 };
            cancel.Click += (_, _) => dialog.Close();
            buttons.Children.Add(cancel);
        }

        var confirm = new Button { Content = confirmText, MinWidth = 90, IsDefault = true };
        confirm.Click += (_, _) => { confirmed = true; dialog.Close(); };
        buttons.Children.Add(confirm);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                buttons,
            },
        };

        if (owner is not null) await dialog.ShowDialog(owner);
        else
        {
            var tcs = new TaskCompletionSource();
            dialog.Closed += (_, _) => tcs.TrySetResult();
            dialog.Show();
            await tcs.Task;
        }

        return confirmed;
    }
}
