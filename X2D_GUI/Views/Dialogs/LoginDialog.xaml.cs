using System;
using System.Globalization;
using System.Windows;
using GeotechSlopeApp.Backend;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class LoginDialog : Window
{
    private readonly int _attempt;
    private readonly int _maxAttempts;

    public string EnteredUsername { get; private set; } = string.Empty;
    public string EnteredPassword { get; private set; } = string.Empty;

    public LoginDialog(int attempt, int maxAttempts, DateTime expiryUtc, string licensee)
    {
        InitializeComponent();
        _attempt = attempt;
        _maxAttempts = maxAttempts;

        SubtitleTextBlock.Text =
            $"Locked-license build for {licensee} - expires {expiryUtc:yyyy-MM-dd} UTC";

        FooterTextBlock.Text =
            $"Attempt {attempt} of {maxAttempts} - {LicenseGate.BuildSignature}";

        if (attempt > 1)
        {
            StatusTextBlock.Text =
                $"Invalid username or password. {Math.Max(0, maxAttempts - attempt + 1)} attempt(s) remaining.";
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        EnteredUsername = UsernameTextBox.Text ?? string.Empty;
        EnteredPassword = PasswordBox.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(EnteredUsername) || string.IsNullOrEmpty(EnteredPassword))
        {
            StatusTextBlock.Text = "Please enter both username and password.";
            return;
        }

        DialogResult = true;
        Close();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
