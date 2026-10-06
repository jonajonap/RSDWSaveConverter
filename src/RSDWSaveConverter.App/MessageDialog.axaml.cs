using Avalonia.Controls;
using System.Threading.Tasks;

namespace RSDWSaveConverter.App;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public MessageDialog(string title, string message, bool isConfirmation = false) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        CancelButton.IsVisible = isConfirmation;

        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    public static Task<bool> ShowConfirmAsync(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, isConfirmation: true);
        return dialog.ShowDialog<bool>(owner);
    }

    public static Task ShowInfoAsync(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, isConfirmation: false);
        return dialog.ShowDialog<bool>(owner);
    }
}
