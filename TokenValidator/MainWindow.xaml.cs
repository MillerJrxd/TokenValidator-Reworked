using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ZXing;
using ZXing.Common;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using TokenValidator.Utils;

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

        private const string MsgHeader = "SCP:SL Token Validator v1.6.0";
        private static string _apiToken;
        private static bool _authenticated;
        private readonly CancellationTokenSource _scanCancellationTokenSource = new CancellationTokenSource();
        private readonly Scan _qrScanner = new Scan();
        private bool _isScanning = false;
        private System.Threading.Timer _scanTimeoutTimer;

        public MainWindow()
        {
            InitializeComponent();

            CreateLogFolder();
            Logging.ClearLogs();
            UpdateHotkeyTooltip();
            ThemeManager.Initialize(this);

            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/SCP Secret Laboratory/");

            string apiTokenPath = Path.Combine(appFolder, "StaffAPI.txt");
            if (File.Exists(apiTokenPath))
            {
                try
                {
                    _apiToken = File.ReadAllLines(apiTokenPath)[0];
                    _authenticated = true;
                    authedLabel.Text = "Authenticated using staff API token.";
                    authedLabel.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 191, 255));
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 191, 255));
                    statusLabel.Text = "Ready";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading StaffAPI.txt: {ex.Message}\nFile is empty or the first line does not contain your token.", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                    _authenticated = false;
                    authedLabel.Text = "Not authenticated using staff API token.";
                    authedLabel.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
                }

            }
            else
            {
                _authenticated = false;
                authedLabel.Text = "Not authenticated using staff API token.";
                authedLabel.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
                statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
            }

            Loaded += (s, e) =>
            {
                var wndHelper = new WindowInteropHelper(this);
                _source = HwndSource.FromHwnd(wndHelper.Handle);
                _source.AddHook(HwndHook);

                RegisterHotkeyFromSettings(wndHelper.Handle);
            };

            if (_authenticated == false)
            {
                scanQRButton.IsEnabled = false;
                fromClipboardButton.IsEnabled = false;
                copyUserIDButton.IsEnabled = false;
            }
        }

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
                    if (keyPart.StartsWith("F") && int.TryParse(keyPart.Substring(1), out int fKey) && fKey >= 1 && fKey <= 24)
                    {
                        key = 0x70 + (fKey - 1);
                    }
                    else
                    {
                        key = (int)Enum.Parse(typeof(System.Windows.Forms.Keys), keyPart, true);
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
                            statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
                            MessageBox.Show("QR code not found.", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        _qrScanner.CancelScan();

                        statusLabel.Text = "Scan timed out.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
                        MessageBox.Show("QR code scan timed out. Please try again.", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    Visibility = Visibility.Visible;
                    statusLabel.Text = "Scan error: " + ex.Message;
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
                    MessageBox.Show($"Error scanning QR code: {ex.Message}", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
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

            using (var timeoutCts = new CancellationTokenSource(5000))
            {
                try
                {
                    var pos = GetCursorPosition();

                    var screenshot = new Bitmap(900, 900);
                    var graphics = Graphics.FromImage(screenshot);
                    graphics.CopyFromScreen(pos.X - 450, pos.Y - 450, 0, 0, screenshot.Size);

                    var bitmap = new Bitmap(screenshot);

                    BarcodeReaderGeneric barcodeReader = new BarcodeReaderGeneric
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
                        return;
                    }
                    else
                    {
                        string decoded = result.ToString().Trim();
                        await ValidateTokenAsync(decoded);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error scanning QR code: {ex.Message}", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();

            _isScanning = false;
        }

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

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "SCP SL Token Validation Tool");

                    var content = new System.Net.Http.StringContent(
                        postData,
                        Encoding.UTF8,
                        "application/x-www-form-urlencoded");

                    var response = client.PostAsync("https://api.scpslgame.com/v5/tools/validatetoken.php", content).Result;
                    string responseJson = response.Content.ReadAsStringAsync().Result;

                    return JsonConvert.DeserializeObject<Dictionary<string, string>>(responseJson);
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
                statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
                return;
            }

            if (decoded["verified"] == "false")
            {
                statusLabel.Text = "Digital signature invalid";
                statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
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
                }
                else
                {
                    statusLabel.Text = "Signature verification successful";
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 191, 255));
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
                }
                else
                {
                    statusLabel.Text = "Signature verification successful, not banned in any game.";
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 128, 128));
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
                    }
                    else
                    {
                        statusLabel.Text = "Signature verification successful, banned in SCP:SL.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 0));
                    }
                }
                else
                {
                    if (!isNewToken)
                    {
                        statusLabel.Text = "Signature verification successful, banned in other games, token is old.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0));
                    }
                    else
                    {
                        statusLabel.Text = "Signature verification successful, banned in other games.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0));
                    }
                }
            }
        }

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
                Clipboard.SetText(userIDLabel.Text);
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
            _scanTimeoutTimer?.Dispose();
            _qrScanner.CancelScan();

            Task.Delay(100).Wait();

            _scanCancellationTokenSource?.Dispose();
            _scanTimeoutTimer = null;

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
            Settings settingsWindow = new Settings();
            settingsWindow.Owner = this;

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
            hotkey.ToUpper();
            scanQrTooltip.Content = $"Press {hotkey} to scan QR from screen around the mouse pointer.";
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
    }
}