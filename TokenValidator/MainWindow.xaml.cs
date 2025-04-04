using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using System.Windows.Threading;

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

        private const string MsgHeader = "SCP:SL Token Validator v3.0.0";
        private static DecodingOptions _decodeOptions;
        private static string _apiToken;
        private static bool _authenticated;
        private readonly CancellationTokenSource _scanCancellationTokenSource;
        private static readonly BarcodeReaderGeneric _barcodeReader = new BarcodeReaderGeneric
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

        public MainWindow()
        {
            InitializeComponent();

            CreateLogFolder();

            _decodeOptions = new DecodingOptions
            {
                PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                TryHarder = true,
                TryInverted = true,
                CharacterSet = "UTF-8"
            };

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

            this.Loaded += (s, e) =>
            {
                var wndHelper = new WindowInteropHelper(this);
                _source = HwndSource.FromHwnd(wndHelper.Handle);
                _source.AddHook(HwndHook);

                RegisterHotKey(wndHelper.Handle, HOTKEY_ID, (int)KeyModifier.Alt, 0x7B);
            };

            if (_authenticated == false)
            {
                scanQRButton.IsEnabled = false;
                fromClipboardButton.IsEnabled = false;
                copyUserIDButton.IsEnabled = false;
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
            scanQRButton.IsEnabled = false;
            fromClipboardButton.IsEnabled = false;

            statusLabel.Text = "Scanning for QR codes...";
            statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(169, 169, 169)); // DarkGray
            this.Visibility = Visibility.Hidden;

            try
            {
                var screens = Screen.AllScreens;
                var result = await Task.Run(() => ScanAllScreensForQrCodeParallel(screens));

                if (result != null)
                {
                    await ValidateTokenAsync(result);
                    this.Visibility = Visibility.Visible;
                }
                else
                {
                    this.Visibility = Visibility.Visible;
                    statusLabel.Text = "QR code not found.";
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60));
                    MessageBox.Show("QR code not found.", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning QR code: {ex.Message}", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                scanQRButton.IsEnabled = true;
                fromClipboardButton.IsEnabled = true;
            }
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

        private void ScanQRAtCursor()
        {
            try
            {
                var pos = GetCursorPosition();
                int scanSize = 900;

                using (var screenshot = new System.Drawing.Bitmap(scanSize, scanSize))
                {
                    using (var graphics = System.Drawing.Graphics.FromImage(screenshot))
                    {
                        graphics.CopyFromScreen(pos.X - scanSize / 2, pos.Y - scanSize / 2, 0, 0, screenshot.Size);
                    }

                    DisplayBitmapInUI(screenshot);

                    var result = _barcodeReader.Decode(screenshot);
                    if (result == null)
                    {
                        var enhancedResult = TryEnhancedScanning(screenshot);
                        if (enhancedResult == null)
                        {
                            MessageBox.Show("QR code not found.", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        else
                        {
                            var _ = ValidateTokenAsync(enhancedResult);
                        }
                    }
                    else
                    {
                        string decoded = result.ToString().Trim();
                        var _ = ValidateTokenAsync(decoded);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning QR code: {ex.Message}", MsgHeader, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> ScanAllScreensForQrCodeParallel(System.Windows.Forms.Screen[] screens)
        {
            var tasks = screens.Select(screen => Task.Run(() => ScanScreenForQrCode(screen))).ToArray();
            var results = await Task.WhenAll(tasks);
            return results.FirstOrDefault(r => r != null);
        }

        private string ScanScreenForQrCode(System.Windows.Forms.Screen screen)
        {
            using (var screenshot = new System.Drawing.Bitmap(screen.Bounds.Width, screen.Bounds.Height))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(screenshot))
                {
                    graphics.CopyFromScreen(screen.Bounds.Left, screen.Bounds.Top, 0, 0, screenshot.Size);
                }

                Dispatcher.Invoke(() => DisplayBitmapInUI(screenshot));

                var result = ScanBitmapForQrCode(screenshot);
                if (result != null)
                    return result;

                result = TryEnhancedScanning(screenshot);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static string ScanBitmapForQrCode(System.Drawing.Bitmap bitmap)
        {
            var result = _barcodeReader.Decode(bitmap);
            return result?.Text;
        }

        private static string TryEnhancedScanning(System.Drawing.Bitmap originalBitmap)
        {
            // Original bitmap
            var result = ScanBitmapForQrCode(originalBitmap);
            if (result != null) return result;

            // Different scales
            foreach (var scale in new[] { 0.5, 0.75, 1.25, 1.5, 2.0 })
            {
                using (var scaled = ScaleBitmap(originalBitmap, scale))
                {
                    result = ScanBitmapForQrCode(scaled);
                    if (result != null) return result;
                }
            }

            // Different processing options
            using (var processed = EnhanceImage(originalBitmap))
            {
                result = ScanBitmapForQrCode(processed);
                if (result != null) return result;

                // Also try the enhanced image at different scales
                foreach (var scale in new[] { 0.75, 1.25 })
                {
                    using (var scaledProcessed = ScaleBitmap(processed, scale))
                    {
                        result = ScanBitmapForQrCode(scaledProcessed);
                        if (result != null) return result;
                    }
                }
            }

            return null;
        }

        private static System.Drawing.Bitmap ScaleBitmap(System.Drawing.Bitmap original, double scale)
        {
            int width = (int)(original.Width * scale);
            int height = (int)(original.Height * scale);

            var result = new System.Drawing.Bitmap(width, height);
            using (var graphics = System.Drawing.Graphics.FromImage(result))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(original, 0, 0, width, height);
            }
            return result;
        }

        private static System.Drawing.Bitmap EnhanceImage(System.Drawing.Bitmap original)
        {
            // Apply contrast enhancement
            var result = new System.Drawing.Bitmap(original.Width, original.Height);

            float contrast = 1.5f;
            float brightness = 0.0f;

            // Color matrix to apply contrast
            float[][] colorMatrixElements = [
                [contrast, 0, 0, 0, 0],
                [0, contrast, 0, 0, 0],
                [0, 0, contrast, 0, 0],
                [0, 0, 0, 1, 0],
                [brightness, brightness, brightness, 0, 1]
            ];

            using (var graphics = System.Drawing.Graphics.FromImage(result))
            {
                var colorMatrix = new System.Drawing.Imaging.ColorMatrix(colorMatrixElements);
                var attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                graphics.DrawImage(
                    original,
                    new System.Drawing.Rectangle(0, 0, original.Width, original.Height),
                    0, 0, original.Width, original.Height,
                    System.Drawing.GraphicsUnit.Pixel,
                    attributes);
            }
            return result;
        }

        private void DisplayBitmapInUI(System.Drawing.Bitmap bitmap)
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            previewImage.Source = bitmapSource;
        }

        private async Task ValidateTokenAsync(string token)
        {
            ClearResults();

            statusLabel.Text = "Token validation in progress...";
            statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(169, 169, 169)); // DarkGray

            try
            {
                var result = await Task.Run(() => ValidateToken(token));

                ProcessValidationResult(result);
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Token validation failed: {ex.Message}";
                statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(169, 169, 169)); // DarkGray
            }
        }

        private static Dictionary<string, string> ValidateToken(string auth)
        {
            try
            {
                string postData = $"auth={WebUtility.UrlEncode(auth)}";
                if (_authenticated)
                {
                    postData += $"&token={_apiToken}";
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
                statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60)); // Crimson
                return;
            }

            if (decoded["verified"] == "false")
            {
                statusLabel.Text = "Digital signature invalid";
                statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 20, 60)); // Crimson
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
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 205, 170)); // MediumAquamarine
                }
                else
                {
                    statusLabel.Text = "Signature verification successful";
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 191, 255)); // DeepSkyBlue
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
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 205, 170)); // MediumAquamarine
                }
                else
                {
                    statusLabel.Text = "Signature verification successful, not banned in any game.";
                    statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 128, 128)); // Teal
                }
            }
            else
            {
                if (isGlobalBanned)
                {
                    if (!isNewToken)
                    {
                        statusLabel.Text = "Signature verification successful, banned in SCP:SL, token is old.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 0)); // OrangeRed
                    }
                    else
                    {
                        statusLabel.Text = "Signature verification successful, banned in SCP:SL.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 0)); // OrangeRed
                    }
                }
                else
                {
                    if (!isNewToken)
                    {
                        statusLabel.Text = "Signature verification successful, banned in other games, token is old.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0)); // DarkOrange
                    }
                    else
                    {
                        statusLabel.Text = "Signature verification successful, banned in other games.";
                        statusPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0)); // Orange
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
            previewImage.Source = null;
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
        }

        private static void CreateLogFolder()
        {
            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TokenValidator");
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
                return;
            }
            return;
        }
    }
}