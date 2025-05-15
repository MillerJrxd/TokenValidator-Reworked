using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TokenValidator.Utils;
using MessageBox = System.Windows.MessageBox;

namespace TokenValidator
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : Window
    {
        private bool _initialSeasonalEffectsState;
        private string _initialHotkey;
        private bool isRecordingHotkey = false;

        public Settings()
        {
            InitializeComponent();
            LoadSettings();
            _initialSeasonalEffectsState = Properties.Settings.Default.SeasonalEffects;
            _initialHotkey = Properties.Settings.Default.QrScanHotkey;
        }

        private void LoadSettings()
        {
            hotkeyTextBox.Text = Properties.Settings.Default.QrScanHotkey ?? "ALT + F12";
            hotkeyTextBox.Text = string.IsNullOrEmpty(Properties.Settings.Default.QrScanHotkey) == true ? "ALT + F12" : Properties.Settings.Default.QrScanHotkey;
        }

        private void RecordHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRecordingHotkey)
            {
                isRecordingHotkey = false;
                hotkeyTextBox.IsReadOnly = true;
                recordHotkeyButton.Content = "Record";
            }
            else
            {
                isRecordingHotkey = true;
                hotkeyTextBox.Text = "Press any key...";
                hotkeyTextBox.IsReadOnly = false;
                recordHotkeyButton.Content = "Stop";
                hotkeyTextBox.Focus();
            }
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!isRecordingHotkey) return;

            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            string hotkey = "";

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                hotkey += "CTRL + ";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                hotkey += "SHIFT + ";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                hotkey += "ALT + ";

            hotkey += key.ToString();
            hotkeyTextBox.Text = hotkey;

            isRecordingHotkey = false;
            hotkeyTextBox.IsReadOnly = true;
            recordHotkeyButton.Content = "Record";
        }

        private void HotkeyTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!isRecordingHotkey) return;

            e.Handled = true;

            string hotkey = "";

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                hotkey += "CTRL + ";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                hotkey += "SHIFT + ";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                hotkey += "ALT + ";

            hotkey += e.ChangedButton.ToString();
            hotkeyTextBox.Text = hotkey;

            isRecordingHotkey = false;
            hotkeyTextBox.IsReadOnly = true;
            recordHotkeyButton.Content = "Record";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            bool seasonalEffectsChanged = Properties.Settings.Default.SeasonalEffects != seasonalEffectsCheckBox.IsChecked;

            Properties.Settings.Default.QrScanHotkey = hotkeyTextBox.Text;
            Properties.Settings.Default.SeasonalEffects = seasonalEffectsCheckBox.IsChecked ?? false;
            Properties.Settings.Default.Save();

            MessageBox.Show("Settings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            if (seasonalEffectsChanged)
            {
                ThemeManager.UpdateSeasonalEffects();
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.SeasonalEffects = _initialSeasonalEffectsState;
            Properties.Settings.Default.QrScanHotkey = _initialHotkey;
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.SeasonalEffects = _initialSeasonalEffectsState;
            Properties.Settings.Default.QrScanHotkey = _initialHotkey;
            DialogResult = false;
            Close();
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
        }
    }
}
