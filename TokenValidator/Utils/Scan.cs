using ZXing.Common;
using ZXing;
using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using TokenValidator.Models;

namespace TokenValidator.Utils
{
    public class Scan : IDisposable
    {
        #region Variables/Constructor
        private static readonly BarcodeReaderGeneric _barcodeReader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                TryHarder = true,
                TryInverted = true,
                CharacterSet = "UTF-8",
                UseCode39ExtendedMode = true,
                AssumeCode39CheckDigit = false,
                ReturnCodabarStartEnd = false
            }
        };

        private static readonly BarcodeReaderGeneric _aggressiveReader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                TryHarder = true,
                TryInverted = true,
                CharacterSet = "UTF-8",
                PureBarcode = true,
                UseCode39ExtendedMode = true,
            }
        };

        private CancellationTokenSource _scanCancellationTokenSource;
        private readonly object _lockObject = new object();

        public Scan()
        {
            _scanCancellationTokenSource = new CancellationTokenSource();
        }
        #endregion

        #region Utility Methods
        public async Task<string> ScanAllScreensAsync(System.Windows.Forms.Screen[] screens)
        {
            ResetCancellationToken();

            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _scanCancellationTokenSource.Token);
                string result = await ScanAllScreensForQrCodeAsync(screens, _scanCancellationTokenSource.Token);
                return result;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                return null;
            }
            finally
            {
                CleanupResources();
            }
        }

        public void CancelScan()
        {
            lock (_lockObject)
            {
                try
                {
                    if (_scanCancellationTokenSource != null && !_scanCancellationTokenSource.IsCancellationRequested)
                    {
                        _scanCancellationTokenSource.Cancel();
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    Logging.LogException(ex);
                }
                finally
                {
                    CleanupResources();
                }
            }
        }

        private void CleanupResources()
        {
            lock (_lockObject)
            {
                try
                {
                    if (_scanCancellationTokenSource != null)
                    {
                        _scanCancellationTokenSource.Dispose();
                        _scanCancellationTokenSource = new CancellationTokenSource();
                    }
                }
                catch (Exception ex)
                {
                    Logging.LogException(ex);
                }
            }
        }

        private void ResetCancellationToken()
        {
            lock (_lockObject)
            {
                try
                {
                    if (_scanCancellationTokenSource != null)
                    {
                        if (!_scanCancellationTokenSource.IsCancellationRequested)
                        {
                            _scanCancellationTokenSource.Cancel();
                        }
                        _scanCancellationTokenSource.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logging.LogException(ex);
                }

                _scanCancellationTokenSource = new CancellationTokenSource();
            }
        }
        #endregion

        #region Scans
        private static async Task<string> ScanAllScreensForQrCodeAsync(System.Windows.Forms.Screen[] screens, CancellationToken cancellationToken)
        {
            var resultFound = new ConcurrentQueue<string>();
            var tcs = new TaskCompletionSource<string>();

            using (var internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                try
                {
                    cancellationToken.Register(() => tcs.TrySetCanceled());

                    var tasks = new List<Task>();
                    var timeoutTask = Task.Delay(12000, internalCts.Token)
                        .ContinueWith(t =>
                        {
                            if (!t.IsCanceled && !tcs.Task.IsCompleted)
                            {
                                tcs.TrySetResult(null);
                                internalCts.Cancel();
                            }
                        }, TaskContinuationOptions.ExecuteSynchronously);

                    foreach (var screen in screens)
                    {
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                string result = await ScanScreenForQrCodeAsync(screen, internalCts.Token);
                                if (result != null)
                                {
                                    resultFound.Enqueue(result);
                                    tcs.TrySetResult(result);
                                    internalCts.Cancel();
                                }
                            }
                            catch (OperationCanceledException)
                            {

                            }
                            catch (Exception ex)
                            {
                                Logging.LogException(ex);
                            }
                        }, internalCts.Token);

                        tasks.Add(task);
                    }

                    var resultTask = await Task.WhenAny(tcs.Task, Task.WhenAll(tasks));

                    internalCts.Cancel();

                    try
                    {
                        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMilliseconds(500));
                    }
                    catch
                    {

                    }

                    if (resultFound.TryDequeue(out string qrResult))
                    {
                        return qrResult;
                    }

                    return null;
                }
                catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
                {
                    Logging.LogException(ex);
                    return null;
                }
                finally
                {
                    internalCts.Cancel();
                }
            }
        }

        private static async Task<string> ScanScreenForQrCodeAsync(System.Windows.Forms.Screen screen, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            try
            {
                using (var screenshot = new Bitmap(screen.Bounds.Width, screen.Bounds.Height))
                using (var graphics = Graphics.FromImage(screenshot))
                {
                    graphics.CopyFromScreen(screen.Bounds.Left, screen.Bounds.Top, 0, 0, screenshot.Size);

                    var centerCropResult = TryCenterCropScan(screenshot, cancellationToken);
                    if (centerCropResult != null) return centerCropResult;

                    var result = ScanBitmapForQrCode(screenshot);
                    if (result != null) return result;
                    cancellationToken.ThrowIfCancellationRequested();

                    var scaleFactors = new[] { 1.5, 2.0, 2.5, 3.0, 4.0 };
                    foreach (var scale in scaleFactors)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        using (var scaled = ScaleBitmap(screenshot, scale))
                        {
                            result = ScanBitmapForQrCode(scaled);
                            if (result != null) return result;

                            using (var enhanced = SmartContrastAdjustment(scaled))
                            {
                                result = ScanBitmapForQrCode(enhanced);
                                if (result != null) return result;
                            }
                        }
                    }

                    var regions = DetectPotentialQRRegions(screenshot);

                    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var options = new ParallelOptions { CancellationToken = cts.Token, MaxDegreeOfParallelism = Environment.ProcessorCount };

                    string finalResult = null;

                    try
                    {
                        Parallel.ForEach(regions, options, (region, state) =>
                        {
                            using (var sectionBitmap = screenshot.Clone(region, screenshot.PixelFormat))
                            {
                                var localResult = TryEnchancedScanning(sectionBitmap, cts.Token);
                                if (localResult != null)
                                {
                                    finalResult = localResult;
                                    cts.Cancel();
                                    state.Stop();
                                }
                            }
                        });
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    return finalResult;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                if (!cancellationToken.IsCancellationRequested) throw;
                return null;
            }
            return null;
        }

        private static string ScanBitmapForQrCode(Bitmap bitmap)
        {
            var result = _barcodeReader.Decode(bitmap);
            if (result?.Text != null) return result.Text;

            result = _aggressiveReader.Decode(bitmap);
            return result?.Text;
        }

        private static string TryEnchancedScanning(Bitmap originalBitmap, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            var result = ScanBitmapForQrCode(originalBitmap);
            if (result != null) return result;
            if (cancellationToken.IsCancellationRequested) return null;

            using (var smartAdjusted = SmartContrastAdjustment(originalBitmap))
            {
                result = ScanBitmapForQrCode(smartAdjusted);
                if (result != null) return result;
            }
            if (cancellationToken.IsCancellationRequested) return null;

            var priorityScales = new[] { 1.5, 2.0, 2.5, 3.0, 4.0, 5.0 };
            foreach (double scale in priorityScales)
            {
                if (cancellationToken.IsCancellationRequested) return null;

                using (var scaled = ScaleBitmap(originalBitmap, scale))
                {
                    result = ScanBitmapForQrCode(scaled);
                    if (result != null) return result;

                    using (var scaledAdjusted = SmartContrastAdjustment(scaled))
                    {
                        result = ScanBitmapForQrCode(scaledAdjusted);
                        if (result != null) return result;
                    }
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                using (var otsu = OtsuThreshold(originalBitmap))
                {
                    result = ScanBitmapForQrCode(otsu);
                    if (result != null) return result;

                    using (var scaledOtsu = ScaleBitmap(otsu, 2.0))
                    {
                        result = ScanBitmapForQrCode(scaledOtsu);
                        if (result != null) return result;
                    }
                }
            }

            return null;
        }

        private static string TryCenterCropScan(Bitmap screenshot, CancellationToken cancellationToken)
        {
            try
            {
                var cropConfigs = new[]
                {
                    new { WidthPercent = 0.8, HeightPercent = 0.8, Name = "Theatre" },
                    new { WidthPercent = 0.7, HeightPercent = 0.7, Name = "Standard"},
                    new { WidthPercent = 0.9, HeightPercent = 0.6, Name = "Wide" }
                };

                foreach (var config in cropConfigs)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    int cropWidth = (int)(screenshot.Width * config.WidthPercent);
                    int cropHeight = (int)(screenshot.Height * config.HeightPercent);
                    int cropX = (screenshot.Width - cropWidth) / 2;
                    int cropY = (screenshot.Height - cropHeight) / 2;

                    var cropRect = new Rectangle(cropX, cropY, cropWidth, cropHeight);

                    using (var croppedBitmap = screenshot.Clone(cropRect, screenshot.PixelFormat))
                    {
                        var result = ScanBitmapForQrCode(croppedBitmap);
                        if (result != null) return result;

                        using (var scaled = ScaleBitmap(croppedBitmap, 2.0))
                        {
                            result = ScanBitmapForQrCode(scaled);
                            if (result != null) return result;
                            using (var enhanced = SmartContrastAdjustment(scaled))
                            {
                                result = ScanBitmapForQrCode(enhanced);
                                if (result != null) return result;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
            return null;
        }

        #endregion

        #region Image manipulation methods
        private static Bitmap ScaleBitmap(Bitmap original, double scale)
        {
            int width = (int)(original.Width * scale);
            int height = (int)(original.Height * scale);

            var result = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(result))
            {
                if (scale > 1.0)
                {
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                }
                else
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                }

                graphics.SmoothingMode = SmoothingMode.None;
                graphics.DrawImage(original, 0, 0, width, height);
            }
            return result;
        }

        private static Bitmap EnhanceImage(Bitmap original, int level)
        {
            var result = new Bitmap(original.Width, original.Height);

            float contrast = 1.0f + (level * 0.4f);
            float brightness = -0.15f + (level * 0.1f);

            if (level < 0)
            {
                contrast = 1.0f - (Math.Abs(level) * 0.2f);
                brightness = 0.1f;
            }

            float[][] colorMatrixElements = [
                [contrast, 0, 0, 0, 0],
                [0, contrast, 0, 0, 0],
                [0, 0, contrast, 0, 0],
                [0, 0, 0, 1, 0],
                [brightness, brightness, brightness, 0, 1]
            ];

            using (var graphics = Graphics.FromImage(result))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(new ColorMatrix(colorMatrixElements));
                graphics.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                    0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }

        private static Bitmap OtsuThreshold(Bitmap original)
        {
            Bitmap result = new Bitmap(original.Width, original.Height);

            int[] histogram = new int[256];
            using (var fastOriginal = new LockBitmap(original))
            {
                fastOriginal.LockBits();

                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        Color pixel = fastOriginal.GetPixel(x, y);
                        int grayValue = (int)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
                        histogram[grayValue]++;
                    }
                }
                fastOriginal.UnlockBits();
            }

            int total = original.Width * original.Height;
            float sum = 0;
            for (int i = 0; i < 256; i++)
                sum += i * histogram[i];

            float sumB = 0;
            int wB = 0, wF = 0;
            float varMax = 0;
            int threshold = 0;

            for (int i = 0; i < 256; i++)
            {
                wB += histogram[i];
                if (wB == 0) continue;

                wF = total - wB;
                if (wF == 0) break;

                sumB += i * histogram[i];
                float mB = sumB / wB;
                float mF = (sum - sumB) / wF;

                float varBetween = wB * wF * (mB - mF) * (mB - mF);

                if (varBetween > varMax)
                {
                    varMax = varBetween;
                    threshold = i;
                }
            }

            using (var fastOriginal = new LockBitmap(original))
            using (var fastResult = new LockBitmap(result))
            {
                fastOriginal.LockBits();
                fastResult.LockBits();

                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        Color pixel = fastOriginal.GetPixel(x, y);
                        int grayValue = (int)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
                        Color newColor = grayValue < threshold ? Color.Black : Color.White;
                        fastResult.SetPixel(x, y, newColor);
                    }
                }

                fastResult.UnlockBits();
                fastOriginal.UnlockBits();
            }

            return result;
        }
        #endregion

        #region QR Detection

        private static List<Rectangle> DetectPotentialQRRegions(Bitmap bitmap)
        {
            var regions = new List<Rectangle>();

            if (bitmap.Width > 1920 || bitmap.Height > 1080)
            {
                regions.AddRange(CreateGridRegions(bitmap));
                return regions;
            }

            var finderCandidates = FindFinderPatternCandidates(bitmap);

            if (finderCandidates.Count >= 2)
            {
                var bounds = CalculateQRBoundsFromCandidates(finderCandidates, bitmap);
                if (bounds != Rectangle.Empty)
                {
                    regions.Add(bounds);
                }
            }

            if (regions.Count == 0)
            {
                regions.AddRange(CreateGridRegions(bitmap));
            }

            return regions;
        }

        private static List<Point> FindFinderPatternCandidates(Bitmap bitmap)
        {
            var candidates = new List<Point>();

            using (var fastBitmap = new LockBitmap(bitmap))
            {
                fastBitmap.LockBits();

                int stepSize = Math.Max(4, Math.Min(bitmap.Width, bitmap.Height) / 200);
                for (int y = 20; y < bitmap.Height - 20; y += stepSize)
                {
                    for (int x = 20; x < bitmap.Width - 20; x += stepSize)
                    {
                        if (IsLikelyFinderPattern(fastBitmap, x, y))
                        {
                            candidates.Add(new Point(x, y));
                            x += stepSize * 3;
                        }
                    }
                }

                fastBitmap.UnlockBits();
            }

            return candidates;
        }

        private static bool IsLikelyFinderPattern(LockBitmap fastBitmap, int startX, int startY, int qrEstimatedSize = 50)
        {
            try
            {
                int checkDistance = Math.Max(3, qrEstimatedSize / 10);

                if (startX + checkDistance >= fastBitmap.Width || startY + checkDistance >= fastBitmap.Height)
                    return false;

                Color center = fastBitmap.GetPixel(startX, startY);
                Color right = fastBitmap.GetPixel(startX + checkDistance, startY);
                Color bottom = fastBitmap.GetPixel(startX, startY + checkDistance);

                int centerGray = (center.R + center.G + center.B) / 3;
                int rightGray = (right.R + right.G + right.B) / 3;
                int bottomGray = (bottom.R + bottom.G + bottom.B) / 3;

                return centerGray < 100 && (rightGray > centerGray + 50 || bottomGray > centerGray + 50);

            }
            catch
            {
                return false;
            }
        }

        private static Rectangle CalculateQRBoundsFromCandidates(List<Point> candidates, Bitmap bitmap)
        {
            if (candidates.Count < 2) return Rectangle.Empty;

            int minX = candidates.Min(p => p.X);
            int maxX = candidates.Max(p => p.X);
            int minY = candidates.Min(p => p.Y);
            int maxY = candidates.Max(p => p.Y);

            int padding = Math.Max(50, (maxX - minX) / 4);

            return new Rectangle(
                Math.Max(0, minX - padding),
                Math.Max(0, minY - padding),
                Math.Min(maxX - minX + 2 * padding, bitmap.Width - Math.Max(0, minX - padding)),
                Math.Min(maxY - minY + 2 * padding, bitmap.Height - Math.Max(0, minY - padding))
            );
        }

        private static List<Rectangle> CreateGridRegions(Bitmap bitmap)
        {
            int gridSize = 4;

            int sectionWidth = bitmap.Width / gridSize;
            int sectionHeight = bitmap.Height / gridSize;
            int overlap = Math.Max(150, Math.Min(sectionWidth, sectionHeight) / 3);

            var allRegions = new List<(Rectangle rect, double priority)>();

            Point center = new Point(bitmap.Width / 2, bitmap.Height / 2);

            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    int startX = Math.Max(0, x * sectionWidth - overlap);
                    int startY = Math.Max(0, y * sectionHeight - overlap);
                    int width = Math.Min(sectionWidth + 2 * overlap, bitmap.Width - startX);
                    int height = Math.Min(sectionHeight + 2 * overlap, bitmap.Height - startY);

                    var rect = new Rectangle(startX, startY, width, height);

                    Point rectCenter = new Point(startX + width / 2, startY + height / 2);
                    double distance = Math.Sqrt(Math.Pow(rectCenter.X - center.X, 2) + Math.Pow(rectCenter.Y - center.Y, 2));

                    allRegions.Add((rect, distance));
                }
            }

            return allRegions
                .OrderBy(r => r.priority)
                .Select(r => r.rect)
                .ToList();
        }

        private static (double Contrast, double Brightness) AnalyzeImageStats(Bitmap bitmap)
        {
            var values = new List<int>();

            using (var fastBitmap = new LockBitmap(bitmap))
            {
                fastBitmap.LockBits();

                int sampleStep = Math.Max(2, Math.Min(bitmap.Width, bitmap.Height) / 300);
                for (int y = 0; y < bitmap.Height; y += sampleStep)
                {
                    for (int x = 0; x < bitmap.Width; x += sampleStep)
                    {
                        Color pixel = fastBitmap.GetPixel(x, y);
                        int gray = (int)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
                        values.Add(gray);
                    }
                }

                fastBitmap.UnlockBits();
            }

            if (values.Count == 0) return (0.5, 0.5);

            double mean = values.Average();
            double variance = values.Average(v => Math.Pow(v - mean, 2));
            double contrast = Math.Sqrt(variance) / 255.0;
            double brightness = mean / 255.0;

            return (contrast, brightness);
        }

        private static Bitmap SmartContrastAdjustment(Bitmap original)
        {
            var stats = AnalyzeImageStats(original);

            if (stats.Contrast < 0.3)
            {
                return EnhanceImage(original, 2);
            }
            else if (stats.Brightness < 0.3)
            {
                return EnhanceImage(original, 1);
            }
            else if (stats.Brightness > 0.7)
            {
                return EnhanceImage(original, -1);
            }

            return new Bitmap(original);
        }
        #endregion

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
