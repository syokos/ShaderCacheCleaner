using System.Windows;
using ShaderCacheCleaner.Helpers;

namespace ShaderCacheCleaner.Views;

public partial class DarkDialog : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

    public DarkDialog()
    {
        Background = DarkTitleBar.WindowBackgroundBrush;
        SourceInitialized += (_, _) => DarkTitleBar.ApplyImmediate(this);
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Loaded += (_, _) => DarkTitleBar.ApplyImmediate(this);
    }

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage _ = MessageBoxImage.None)
    {
        var dlg = new DarkDialog
        {
            Owner = owner ?? Application.Current?.MainWindow,
            Background = DarkTitleBar.WindowBackgroundBrush,
            Opacity = 0
        };

        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.ConfigureButtons(buttons);

        if (dlg.Owner is not null)
        {
            dlg.MaxWidth = Math.Min(440, Math.Max(320, dlg.Owner.ActualWidth - 40));
            dlg.Width = dlg.MaxWidth;
        }

        dlg.ContentRendered += (_, _) =>
        {
            DarkTitleBar.ApplyImmediate(dlg);
            dlg.Opacity = 1;
        };

        dlg.ShowDialog();
        return dlg.Result;
    }

    private void ConfigureButtons(MessageBoxButton buttons)
    {
        BtnYes.Visibility = Visibility.Collapsed;
        BtnNo.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;
        BtnOk.Visibility = Visibility.Collapsed;

        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnYes.Focus();
                break;
            case MessageBoxButton.YesNoCancel:
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Visible;
                BtnYes.Focus();
                break;
            case MessageBoxButton.OKCancel:
                BtnOk.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Visible;
                BtnOk.Content = "OK";
                BtnOk.Focus();
                break;
            default:
                BtnOk.Visibility = Visibility.Visible;
                BtnOk.Focus();
                break;
        }
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        DialogResult = true;
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        DialogResult = false;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        DialogResult = false;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        DialogResult = true;
    }
}
