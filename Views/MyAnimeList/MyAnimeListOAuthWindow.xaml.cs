using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace MSCS.Views.MyAnimeList
{
    public partial class MyAnimeListOAuthWindow : Window
    {
        private readonly Uri _authorizationUri;

        public MyAnimeListOAuthWindow(Uri authorizationUri)
        {
            InitializeComponent();
            _authorizationUri = authorizationUri ?? throw new ArgumentNullException(nameof(authorizationUri));
            Loaded += OnLoaded;
        }

        public string? AuthorizationCode { get; private set; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CodeTextBox.Focus();
        }

        private void ProceedButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _authorizationUri.ToString(),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SubmitButton_OnClick(object sender, RoutedEventArgs e)
        {
            SubmitCode();
        }

        private void CodeTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SubmitCode();
                e.Handled = true;
            }
        }

        private void SubmitCode()
        {
            var code = CodeTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                System.Windows.MessageBox.Show(this, "Enter the authorization code provided by MyAnimeList before continuing.", "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AuthorizationCode = code;
            DialogResult = true;
        }
    }
}