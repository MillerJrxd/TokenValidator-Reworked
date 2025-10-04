using MaterialDesignThemes.Wpf;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TokenValidator.Models;
using TokenValidator.Utils;
using ZXing;
using ZXing.Common;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace TokenValidator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Hotkey and Cursor Position Detection
        //Credits to zabszk
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private static System.Drawing.Point GetCursorPosition()
        {
            POINT lpPoint;
            GetCursorPos(out lpPoint);
            return lpPoint;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public static implicit operator System.Drawing.Point(POINT point)
            {
                return new System.Drawing.Point(point.X, point.Y);
            }
        }

        private HwndSource _source;
        private const int HOTKEY_ID = 1;

        private enum KeyModifier
        {
            None = 0,
            Alt = 1,
            Control = 2,
            Shift = 4,
            WinKey = 8
        }
        #endregion

        #region Variables
        private readonly string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/SCP Secret Laboratory/");
        private string MsgHeader = "";
        private static string? _apiToken;
        private static bool _authenticated = true;
        private readonly CancellationTokenSource _scanCancellationTokenSource = new();
        private readonly Scan _qrScanner = new();
        private bool _isScanning = false;
        private static readonly SolidColorBrush ErrorBrush = new(System.Windows.Media.Color.FromRgb(220, 20, 60));
        private static readonly SolidColorBrush SuccessBrush = new(System.Windows.Media.Color.FromRgb(0, 191, 255));
        public VersionViewModel ViewModel { get; private set; }
        private Border? _copiedNotification;
        #endregion

        #region Constructor
        public MainWindow()
        {
            InitializeComponent();
            InitLightweightComponents();
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await InitHeavyComponents();
            }), DispatcherPriority.Background);
        }

        private async Task InitHeavyComponents()
        {
            CreateLogFolder();
            await Task.Run(() => Logging.ClearLogs());

            await LoadAuthentication();
            await InitHotkey();
        }

        private void InitLightweightComponents()
        {
            ViewModel = new VersionViewModel();
            DataContext = ViewModel;
            MsgHeader = ViewModel.VersionInfo;

            UpdateHotkeyTooltip();

            if (_authenticated == false)
            {
                scanQRButton.IsEnabled = false;
                fromClipboardButton.IsEnabled = false;
                copyUserIDButton.IsEnabled = false;
            }
        }

        private async Task InitHotkey()
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    var wndHelper = new WindowInteropHelper(this);
                    _source = HwndSource.FromHwnd(wndHelper.Handle);
                    _source.AddHook(HwndHook);
                    RegisterHotkeyFromSettings(wndHelper.Handle);
                    ThemeManager.Initialize(this);
                });
            });
        }

        private async Task LoadAuthentication()
        {
            string apiTokenPath = Path.Combine(appFolder, "StaffAPI.txt");

            await Task.Run(() =>
            {
                if (File.Exists(apiTokenPath))
                {
                    try
                    {
                        _apiToken = File.ReadAllLines(apiTokenPath, Encoding.UTF8)[0];
                        _authenticated = true;

                        Dispatcher.Invoke(() =>
                        {
                            authedLabel.Text = "Authenticated using staff API token.";
                            authedLabel.Foreground = SuccessBrush;
                            statusPanel.Background = SuccessBrush;
                            statusLabel.Text = "Ready";
                        });
                    }
                    catch (Exception ex)
                    {
                        _authenticated = false;
                        Dispatcher.Invoke(() => UpdateAuthenticationUI(_authenticated));
                    }
                }
                else
                {
                    _authenticated = false;
                    Dispatcher.Invoke(() => UpdateAuthenticationUI(_authenticated));
                }
            });
        }

        private void UpdateAuthenticationUI(bool isAuthenticated)
        {
            if (!isAuthenticated)
            {
                authedLabel.Text = "Not authenticated using staff API token.";
                authedLabel.Foreground = ErrorBrush;
                statusPanel.Background = ErrorBrush;
                statusIcon.Kind = PackIconKind.AlertCircle;

                scanQRButton.IsEnabled = false;
                fromClipboardButton.IsEnabled = false;
                copyUserIDButton.IsEnabled = false;
            }
        }
        #endregion

        #region Hotkey Registration
        private void RegisterHotkeyFromSettings(IntPtr hWnd)
        {
            int modifier = (int)KeyModifier.Alt;
            int key = 0x7B; 

            string hotkey = Properties.Settings.Default.QrScanHotkey;
            if (!string.IsNullOrEmpty(hotkey))
            {
                try
                {
                    modifier = 0;

                    var parts = hotkey.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries);

                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        switch (parts[i].ToUpper())
                        {
                            case "ALT":
                                modifier |= (int)KeyModifier.Alt;
                                break;
                            case "CTRL":
                                modifier |= (int)KeyModifier.Control;
                                break;
                            case "SHIFT":
                                modifier |= (int)KeyModifier.Shift;
                                break;
                            case "WIN":
                                modifier |= (int)KeyModifier.WinKey;
                                break;
                        }
                    }

                    string keyPart = parts.Last();
                    if (keyPart.StartsWith('F') && int.TryParse(keyPart.Substring(1), out int fKey) && fKey >= 1 && fKey <= 24)
                    {
                        key = 0x70 + (fKey - 1);
                    }
                    else
                    {
                        key = (int)Enum.Parse<Keys>(keyPart, true);
                    }
                }
                catch
                {
                    modifier = (int)KeyModifier.Alt;
                    key = 0x7B;
                }
            }

            UnregisterHotKey(hWnd, HOTKEY_ID);

            if (!RegisterHotKey(hWnd, HOTKEY_ID, modifier, key))
            {
                MessageBox.Show($"Failed to register hotkey: {hotkey ?? "ALT + F12"}. It might be in use by another application.",
                              MsgHeader, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ScanQRAtCursor();
                handled = true;
            }
            return IntPtr.Zero;
        }
        #endregion

        #region Scans
        private async void ScanQR_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning)
                return;

            _isScanning = true;
            scanQRButton.IsEnabled = false;
            fromClipboardButton.IsEnabled = false;
            copyUserIDButton.IsEnabled = false;

            double left = Left;
            double top = Top;
            double width = Width;
            double height = Height;

            statusLabel.Text = "Scanning for QR codes...";
            statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(169, 169, 169));

            Visibility = Visibility.Hidden;
            await Task.Delay(100);

            using (var timeoutCts = new CancellationTokenSource(10000))
            {
                try
                {
                    var screens = System.Windows.Forms.Screen.AllScreens;

                    var timeoutTask = Task.Delay(10000, timeoutCts.Token);

                    var scanTask = _qrScanner.ScanAllScreensAsync(screens);

                    var completedTask = await Task.WhenAny(scanTask, timeoutTask);

                    Visibility = Visibility.Visible;

                    if (completedTask == scanTask)
                    {
                        timeoutCts.Cancel();
                        var result = await scanTask;

                        if (result != null)
                        {
                            await ValidateTokenAsync(result);
                        }
                        else
                        {
                            statusLabel.Text = "QR code not found.";
                            statusPanel.Background = ErrorBrush;
                            MessageBox.Show("QR code not found.", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                            statusIcon.Kind = PackIconKind.AlertCircle;
                        }
                    }
                    else
                    {
                        _qrScanner.CancelScan();
                        statusLabel.Text = "Scan timed out.";
                        statusPanel.Background = ErrorBrush;
                        MessageBox.Show("QR code scan timed out. Please try again.", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                        statusIcon.Kind = PackIconKind.AlertCircle;
                    }
                }
                catch (Exception ex)
                {
                    Visibility = Visibility.Visible;
                    statusLabel.Text = "Scan error: " + ex.Message;
                    statusPanel.Background = ErrorBrush;
                    MessageBox.Show($"Error scanning QR code: {ex.Message}", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                    statusIcon.Kind = PackIconKind.AlertCircle;
                }
            }

            if (Left != left || Top != top)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();

            _isScanning = false;
            scanQRButton.IsEnabled = true;
            fromClipboardButton.IsEnabled = true;
            copyUserIDButton.IsEnabled = _authenticated;
        }

        private async void FromClipboard_Click(object sender, RoutedEventArgs e)
        {
            var token = Clipboard.GetText().Replace("\n\r", "<br>").Replace("\n", "<br>");

            scanQRButton.IsEnabled = false;
            fromClipboardButton.IsEnabled = false;

            await ValidateTokenAsync(token);

            scanQRButton.IsEnabled = true;
            fromClipboardButton.IsEnabled = true;
        }

        private async void ScanQRAtCursor()
        {
            if (_isScanning)
                return;

            _isScanning = true;

            try
            {
                using (var timeoutCts = new CancellationTokenSource(5000))
                {
                    var pos = GetCursorPosition();

                    var screenshot = new Bitmap(900, 900);
                    var graphics = Graphics.FromImage(screenshot);
                    graphics.CopyFromScreen(pos.X - 450, pos.Y - 450, 0, 0, screenshot.Size);

                    var bitmap = new Bitmap(screenshot);

                    BarcodeReaderGeneric barcodeReader = new()
                    {
                        AutoRotate = true,
                        Options = new DecodingOptions
                        {
                            PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                            TryHarder = true,
                            TryInverted = true,
                            CharacterSet = "UTF-8"
                        }
                    };

                    Result result = barcodeReader.Decode(bitmap);

                    if (result == null)
                    {
                        MessageBox.Show("QR code not found.", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                        statusIcon.Kind = PackIconKind.AlertCircle;
                    }
                    else
                    {
                        string decoded = result.ToString().Trim();
                        await ValidateTokenAsync(decoded);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning QR code: {ex.Message}", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                _isScanning = false;
            }
        }
        #endregion

        #region Token Validation/Processing result
        private async Task ValidateTokenAsync(string token)
        {
            ClearResults();

            statusLabel.Text = "Token validation in progress...";
            statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(169, 169, 169));

            try
            {
                string cleanedToken = token.Trim()
                        .Replace("\r\n", "\n")
                        .Replace("\r", "\n");

                var result = await Task.Run(() => ValidateToken(cleanedToken));

                ProcessValidationResult(result);
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Token validation failed: {ex.Message}";
                statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(169, 169, 169));
                statusIcon.Kind = PackIconKind.AlertCircle;
            }
        }

        private static Dictionary<string, string> ValidateToken(string auth)
        {
            try
            {
                string encodedAuth = WebUtility.UrlEncode(auth);
                string postData = $"auth={encodedAuth}";

                if (_authenticated)
                {
                    postData += $"&token={WebUtility.UrlEncode(_apiToken)}";
                }

                using (var client = new HttpQuery())
                {
                    var result = client.PostAsync("https://api.scpslgame.com/v5/tools/validatetoken.php", postData).Result;

                    return JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, string>
               {
                   { "success", "false" },
                   { "error", ex.Message }
               };
            }
        }
        

        private void ProcessValidationResult(Dictionary<string, string> decoded)
        {
            if (decoded["success"] == "false")
            {
                statusLabel.Text = "Error: " + decoded["error"];
                statusPanel.Background = ErrorBrush;
                statusIcon.Kind = PackIconKind.AlertCircle;
                return;
            }

            if (decoded["verified"] == "false")
            {
                statusLabel.Text = "Digital signature invalid";
                statusPanel.Background = ErrorBrush;
                statusIcon.Kind = PackIconKind.AlertCircle;
                return;
            }

            if (decoded.ContainsKey("UserID"))
                userIDLabel.Text = decoded["UserID"];

            if (decoded.ContainsKey("Nickname"))
                nicknameLabel.Text = Base64Decode(decoded["Nickname"]);

            if (decoded.ContainsKey("Issuance time"))
                issuanceLabel.Text = decoded["Issuance time"];

            if (decoded.ContainsKey("Expiration time"))
                expirationLabel.Text = decoded["Expiration time"];

            bool isNewToken = decoded.ContainsKey("newToken") && decoded["newToken"] == "true";
            bool hasCleanStatus = decoded.ContainsKey("clean");
            bool hasGlobalBanStatus = decoded.ContainsKey("GlobalBan");

            if (!hasCleanStatus || !hasGlobalBanStatus)
            {
                if (!isNewToken)
                {
                    statusLabel.Text = "Signature verification successful, token is old.";
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 205, 170));
                    statusIcon.Kind = PackIconKind.CheckCircle;
                }
                else
                {
                    statusLabel.Text = "Signature verification successful";
                    statusPanel.Background = SuccessBrush;
                    statusIcon.Kind = PackIconKind.CheckCircle;
                }
                return;
            }

            bool isClean = decoded["clean"] == "true";
            bool isGlobalBanned = decoded["GlobalBan"] == "true";

            if (isClean)
            {
                if (!isNewToken)
                {
                    statusLabel.Text = "Signature verification successful, token is old.";
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 205, 170));
                    statusIcon.Kind = PackIconKind.CheckCircle;
                }
                else
                {
                    statusLabel.Text = "Signature verification successful, not banned in any game.";
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 128, 128));
                    statusIcon.Kind = PackIconKind.CheckCircle;
                }
            }
            else
            {
                if (isGlobalBanned)
                {
                    if (!isNewToken)
                    {
                        statusLabel.Text = "Signature verification successful, banned in SCP:SL, token is old.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 0));
                        statusIcon.Kind = PackIconKind.Alert;
                    }
                    else
                    {
                        statusLabel.Text = "Signature verification successful, banned in SCP:SL.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 0));
                        statusIcon.Kind = PackIconKind.Alert;
                    }
                }
                else
                {
                    if (!isNewToken)
                    {
                        statusLabel.Text = "Signature verification successful, banned in other games, token is old.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0));
                        statusIcon.Kind = PackIconKind.Alert;
                    }
                    else
                    {
                        statusLabel.Text = "Signature verification successful, banned in other games.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0));
                        statusIcon.Kind = PackIconKind.Alert;
                    }
                }
            }
        }
        #endregion

        #region UI Helpers/Helper methods
        private void ClearResults()
        {
            userIDLabel.Text = "";
            nicknameLabel.Text = "";
            issuanceLabel.Text = "";
            expirationLabel.Text = "";
        }

        private static string Base64Decode(string base64EncodedData)
        {
            try
            {
                var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
                return Encoding.UTF8.GetString(base64EncodedBytes);
            }
            catch
            {
                return "[Decoding Error]";
            }
        }

        private void CopyUserIDButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(userIDLabel.Text))
            {
                try
                {
                    Clipboard.SetText(userIDLabel.Text);
                    ShowCopiedNotification();
                }
                catch (COMException ex) when (ex.ErrorCode == unchecked((int)0x800401D0))
                {

                    throw;
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_source != null)
            {
                _source.RemoveHook(HwndHook);
                var wndHelper = new WindowInteropHelper(this);
                UnregisterHotKey(wndHelper.Handle, HOTKEY_ID);
            }

            _scanCancellationTokenSource?.Cancel();
            _qrScanner.CancelScan();

            Task.Delay(100).Wait();

            _scanCancellationTokenSource?.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void CreateLogFolder()
        {
            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TokenValidator");
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
                return;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Settings settingsWindow = new()
            {
                Owner = this
            };

            string currentHotkey = Properties.Settings.Default.QrScanHotkey;
            bool currentSeasonalSettings = Properties.Settings.Default.SeasonalEffects;

            if (settingsWindow.ShowDialog() == true)
            {
                if (Properties.Settings.Default.QrScanHotkey != currentHotkey)
                {
                    var wndHelper = new WindowInteropHelper(this);
                    RegisterHotkeyFromSettings(wndHelper.Handle);
                    UpdateHotkeyTooltip();
                }
                else if (Properties.Settings.Default.SeasonalEffects != currentSeasonalSettings)
                {
                    ThemeManager.UpdateSeasonalEffects();
                }
            }
        }

        private void UpdateHotkeyTooltip()
        {
            string hotkey = Properties.Settings.Default.QrScanHotkey;
            if (string.IsNullOrEmpty(hotkey))
            {
                hotkey = "ALT + F12";
            }
            scanQrTooltip.Content = $"Press {hotkey.ToUpper()} to scan QR from screen around the mouse pointer.";
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            if (ThemeManager.SeasonalEffectsEnabled &&
                (sizeInfo.NewSize.Width > sizeInfo.PreviousSize.Width ||
                 sizeInfo.NewSize.Height > sizeInfo.PreviousSize.Height))
            {
                ThemeManager.ApplySeasonalTheme(this);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            ThemeManager.ClearSeasonalEffects();
            base.OnClosed(e);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            ThemeManager.Initialize(this);
        }

        private async void ShowCopiedNotification()
        {
            if (_copiedNotification != null)
            {
                var mainGrid1 = this.Content as Grid;
                mainGrid1?.Children.Remove(_copiedNotification);
                _copiedNotification = null;
            }

            _copiedNotification = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 0, 191, 255)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(24, 14, 24, 14),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 60, 0, 0),
                Opacity = 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 4,
                    BlurRadius = 20,
                    Opacity = 0.4
                }
            };

            var stackPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };

            var icon = new PackIcon
            {
                Kind = PackIconKind.CheckCircle,
                Width = 22,
                Height = 22,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            // Create the text
            var textBlock = new TextBlock
            {
                Text = "User ID Copied!",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };

            stackPanel.Children.Add(icon);
            stackPanel.Children.Add(textBlock);
            _copiedNotification.Child = stackPanel;

            var mainGrid = this.Content as Grid;
            if (mainGrid != null)
            {
                mainGrid.Children.Add(_copiedNotification);
            }

            var fadeInAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };

            var slideDownAnimation = new System.Windows.Media.Animation.ThicknessAnimation
            {
                From = new Thickness(0, 40, 0, 0),
                To = new Thickness(0, 60, 0, 0),
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };

            _copiedNotification.BeginAnimation(Border.OpacityProperty, fadeInAnimation);
            _copiedNotification.BeginAnimation(Border.MarginProperty, slideDownAnimation);

            await Task.Delay(2000);

            var fadeOutAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(600),
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
                }
            };

            var slideUpAnimation = new System.Windows.Media.Animation.ThicknessAnimation
            {
                From = new Thickness(0, 60, 0, 0),
                To = new Thickness(0, 40, 0, 0),
                Duration = TimeSpan.FromMilliseconds(600),
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
                }
            };

            _copiedNotification.BeginAnimation(Border.OpacityProperty, fadeOutAnimation);
            _copiedNotification.BeginAnimation(Border.MarginProperty, slideUpAnimation);

            await Task.Delay(600);

            if (mainGrid != null && _copiedNotification != null)
            {
                mainGrid.Children.Remove(_copiedNotification);
                _copiedNotification = null;
            }
        }
        #endregion
    }
}