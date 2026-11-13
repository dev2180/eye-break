using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace EyeBreakSetup
{
    public partial class MainWindow : Window
    {
        bool _finished;
        string _dir;
        readonly bool _update;

        /// <param name="update">Started by the app's Update button: keep the current folder and choices, install
        /// straight away, then reopen Eye Break.</param>
        public MainWindow(bool update = false)
        {
            InitializeComponent();
            _update = update;
            PathBox.Text = Installer.InstalledDir ?? Installer.DefaultDir;
            FooterText.Text = $"v{Installer.Version}  ·  installs for you only, no admin needed";
            if (update)
            {
                DesktopBox.IsChecked = Installer.HasDesktopShortcut;
                StartupBox.IsChecked = Installer.StartsWithWindows;
                LaunchBox.IsChecked = true;
                Loaded += (_, __) => InstallBtn_Click(InstallBtn, null);
            }
            Music.Start();
            Closed += (_, __) => Music.Stop();
        }

        void MusicBtn_Click(object sender, RoutedEventArgs e)
        {
            Music.Toggle();
            MusicBtn.Opacity = Music.On ? 1 : 0.35;
        }

        void Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose where the Eye Break folder goes" })
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    PathBox.Text = Path.Combine(d.SelectedPath, "EyeBreak");
        }

        async void InstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_finished)
            {
                if (LaunchBox.IsChecked == true && _dir != null) Installer.Launch(_dir);
                Close();
                return;
            }

            // Always install into its own "EyeBreak" folder, so uninstalling can never remove anything else.
            string dir = PathBox.Text.Trim().TrimEnd('\\');
            if (!Path.GetFileName(dir).Equals("EyeBreak", StringComparison.OrdinalIgnoreCase)) dir = Path.Combine(dir, "EyeBreak");
            bool desktop = DesktopBox.IsChecked == true, startup = StartupBox.IsChecked == true;

            SetupPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            InstallBtn.IsEnabled = CloseBtn.IsEnabled = false;

            var progress = new Progress<(double done, string file)>(p =>
            {
                Bar.Value = p.done * 100;
                StatusText.Text = $"{(_update ? "Updating" : "Installing")}…  {p.done:P0}";
                FileText.Text = p.file;
            });

            try
            {
                await Task.Run(() => Installer.Install(dir, desktop, startup, progress));
                _dir = dir;
                Bar.Value = 100;
                StatusText.Text = "Eye Break is installed";
                FileText.Text = "Find it in the Start menu" + (desktop ? " or on your desktop." : ".") +
                                " Remove it any time from Settings › Apps › Installed apps.";
                InstallBtn.Content = "FINISH";
                _finished = true;
                if (_update)
                {
                    StatusText.Text = $"Eye Break is updated to {Installer.Version}";
                    FileText.Text = "Opening it again…";
                    await Task.Delay(1500);
                    Installer.Launch(dir);
                    Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Setup couldn't finish";
                FileText.Text = ex.Message;
                InstallBtn.Content = "CLOSE";
                _finished = true;
                LaunchBox.IsChecked = false;
            }
            InstallBtn.IsEnabled = CloseBtn.IsEnabled = true;
        }
    }
}
