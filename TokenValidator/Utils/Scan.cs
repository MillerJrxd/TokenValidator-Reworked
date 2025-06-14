using ZXing.Common;
using ZXing;
using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using TokenValidator.Models;

namespace TokenValidator.Utils
{
    public class Scan
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
                string result = await ScanAllScreensForQrCodeAsync(screens, _scanCancellationTokenSource.Token);
                return result;
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
            _scanCancellationTokenSource.Dispose();
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

                    var result = ScanBitmapForQrCode(screenshot);
                    if (result != null) return result;
                    cancellationToken.ThrowIfCancellationRequested();

                    int gridSize = (screenshot.Width > 2560 || screenshot.Height > 1440) ? 4 :
                                   (screenshot.Width > 1920 || screenshot.Height > 1080) ? 3 : 2;
                    int sectionWidth = screenshot.Width / gridSize;
                    int sectionHeight = screenshot.Height / gridSize;
                    int overlap = 150;

                    var sections = new List<(int x, int y, int width, int height)>();

                    for (int y = 0; y < gridSize; y++)
                    {
                        for (int x = 0; x < gridSize; x++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            int startX = Math.Max(0, x * sectionWidth - overlap);
                            int startY = Math.Max(0, y * sectionHeight - overlap);
                            int width = Math.Min(sectionWidth + 2 * overlap, screenshot.Width - startX);
                            int height = Math.Min(sectionHeight + 2 * overlap, screenshot.Height - startY);

                            sections.Add((startX, startY, width, height));
                        }
                    }

                    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var options = new ParallelOptions { CancellationToken = cts.Token, MaxDegreeOfParallelism = Environment.ProcessorCount };

                    string finalResult = null;

                    try
                    {
                        Parallel.ForEach(sections, options, (section, state) =>
                        {
                            using (var sectionBitmap = screenshot.Clone(
                                new Rectangle(section.x, section.y, section.width, section.height),
                                screenshot.PixelFormat))
                            {
                                var localResult = TryEnhancedScanning(sectionBitmap, cts.Token);
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

        private static string TryEnhancedScanning(Bitmap originalBitmap, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            var result = ScanBitmapForQrCode(originalBitmap);
            if (result != null) return result;
            if (cancellationToken.IsCancellationRequested) return null;

            var processingLevels = new[] { 0, 1, 2, -1 };
            var scalingFactors = new[] { 2.0, 3.0, 1.5, 2.5, 4.0 };

            foreach (double scale in scalingFactors)
            {
                if (cancellationToken.IsCancellationRequested) return null;

                using (var scaled = ScaleBitmap(originalBitmap, scale))
                {
                    result = ScanBitmapForQrCode(scaled);
                    if (result != null) return result;

                    foreach (int level in new[] { 0, 1 })
                    {
                        if (cancellationToken.IsCancellationRequested) return null;

                        using (var enhanced = EnhanceImage(scaled, level))
                        {
                            result = ScanBitmapForQrCode(enhanced);
                            if (result != null) return result;
                        }
                    }
                }
            }

            foreach (int level in processingLevels)
            {
                if (cancellationToken.IsCancellationRequested) return null;

                using (var processed = EnhanceImage(originalBitmap, level))
                {
                    result = ScanBitmapForQrCode(processed);
                    if (result != null) return result;

                    foreach (double scale in new[] { 2.0, 3.0, 1.5 })
                    {
                        if (cancellationToken.IsCancellationRequested) return null;

                        using (var scaled = ScaleBitmap(processed, scale))
                        {
                            result = ScanBitmapForQrCode(scaled);
                            if (result != null) return result;
                        }
                    }
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                using (var thresholded = AdaptiveThreshold(originalBitmap))
                {
                    result = ScanBitmapForQrCode(thresholded);
                    if (result != null) return result;

                    foreach (double scale in new[] { 2.0, 3.0, 4.0 })
                    {
                        if (cancellationToken.IsCancellationRequested) return null;

                        using (var scaledThresholded = ScaleBitmap(thresholded, scale))
                        {
                            result = ScanBitmapForQrCode(scaledThresholded);
                            if (result != null) return result;
                        }
                    }
                }

                using (var otsu = OtsuThreshold(originalBitmap))
                {
                    result = ScanBitmapForQrCode(otsu);
                    if (result != null) return result;

                    foreach (double scale in new[] { 2.0, 3.0 })
                    {
                        if (cancellationToken.IsCancellationRequested) return null;

                        using (var scaledOtsu = ScaleBitmap(otsu, scale))
                        {
                            result = ScanBitmapForQrCode(scaledOtsu);
                            if (result != null) return result;
                        }
                    }
                }

                using (var filtered = MedianFilter(originalBitmap))
                {
                    result = ScanBitmapForQrCode(filtered);
                    if (result != null) return result;

                    using (var scaledFiltered = ScaleBitmap(filtered, 2.0))
                    {
                        result = ScanBitmapForQrCode(scaledFiltered);
                        if (result != null) return result;
                    }
                }
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
                if (scale > 2.0)
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

        private static Bitmap AdaptiveThreshold(Bitmap original)
        {
            Bitmap result = new(original.Width, original.Height);

            using (var fastOriginal = new LockBitmap(original))
            using (var fastResult = new LockBitmap(result))
            {
                fastOriginal.LockBits();
                fastResult.LockBits();

                int kernelSize = 21;
                int halfKernel = kernelSize / 2;
                int constant = 10;

                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        Color pixel = fastOriginal.GetPixel(x, y);
                        int grayValue = (int)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);

                        int sum = 0;
                        int count = 0;

                        for (int ky = Math.Max(0, y - halfKernel); ky < Math.Min(original.Height, y + halfKernel + 1); ky++)
                        {
                            for (int kx = Math.Max(0, x - halfKernel); kx < Math.Min(original.Width, x + halfKernel + 1); kx++)
                            {
                                Color neighborPixel = fastOriginal.GetPixel(kx, ky);
                                int neighborGray = (int)(0.299 * neighborPixel.R + 0.587 * neighborPixel.G + 0.114 * neighborPixel.B);
                                sum += neighborGray;
                                count++;
                            }
                        }

                        int average = sum / count;
                        Color newColor = (grayValue < average - constant) ? Color.Black : Color.White;
                        fastResult.SetPixel(x, y, newColor);
                    }
                }
                fastResult.UnlockBits();
                fastOriginal.UnlockBits();
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

        private static Bitmap MedianFilter(Bitmap original)
        {
            Bitmap result = new(original.Width, original.Height);
            int kernelSize = 3;
            int halfKernel = kernelSize / 2;

            using (var fastOriginal = new LockBitmap(original))
            using (var fastResult = new LockBitmap(result))
            {
                fastOriginal.LockBits();
                fastResult.LockBits();

                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        List<int> values = new List<int>();

                        for (int ky = Math.Max(0, y - halfKernel); ky < Math.Min(original.Height, y + halfKernel + 1); ky++)
                        {
                            for (int kx = Math.Max(0, x - halfKernel); kx < Math.Min(original.Width, x + halfKernel + 1); kx++)
                            {
                                Color pixel = fastOriginal.GetPixel(kx, ky);
                                int grayValue = (int)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
                                values.Add(grayValue);
                            }
                        }

                        values.Sort();
                        int median = values[values.Count / 2];
                        Color newColor = Color.FromArgb(median, median, median);
                        fastResult.SetPixel(x, y, newColor);
                    }
                }

                fastResult.UnlockBits();
                fastOriginal.UnlockBits();
            }

            return result;
        }
        #endregion
    }
}
