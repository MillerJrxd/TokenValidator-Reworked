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
        #region Variables
        private readonly bool _initialSeasonalEffectsState;
        private readonly string _initialHotkey;
        private bool isRecordingHotkey = false;
        #endregion

        #region Constructor
        public Settings()
        {
            InitializeComponent();

            try
            {
                Properties.Settings.Default.Reload();

                _initialSeasonalEffectsState = Properties.Settings.Default.SeasonalEffects;
                _initialHotkey = Properties.Settings.Default.QrScanHotkey;

                LoadSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                _initialSeasonalEffectsState = false;
                _initialHotkey = "ALT + F12";
                LoadFallbackSettings();
            }
        }
        #endregion

        #region Hotkey Handling
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
        #endregion

        #region Click Events
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool seasonalEffectsChanged = Properties.Settings.Default.SeasonalEffects != seasonalEffectsCheckBox.IsChecked;

                if (seasonalEffectsChanged)
                {
                    ThemeManager.UpdateSesonalEffects();
                }

                Properties.Settings.Default.QrScanHotkey = hotkeyTextBox.Text;
                Properties.Settings.Default.SeasonalEffects = seasonalEffectsCheckBox.IsChecked ?? false;
                Properties.Settings.Default.Save();

                MessageBox.Show("Settings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
            }
            finally
            {
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.SeasonalEffects = _initialSeasonalEffectsState;
            Properties.Settings.Default.QrScanHotkey = _initialHotkey;
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.SeasonalEffects = _initialSeasonalEffectsState;
            Properties.Settings.Default.QrScanHotkey = _initialHotkey;
            DialogResult = false;
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        #endregion

        #region Helper Methods
        private void LoadFallbackSettings()
        {
            hotkeyTextBox.Text = _initialHotkey;
            seasonalEffectsCheckBox.IsChecked = _initialSeasonalEffectsState;
        }

        private void LoadSettings()
        {
            Properties.Settings.Default.Reload();
            hotkeyTextBox.Text = string.IsNullOrEmpty(Properties.Settings.Default.QrScanHotkey) == true ? "ALT + F12" : Properties.Settings.Default.QrScanHotkey;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
        }
        #endregion
    }
}
