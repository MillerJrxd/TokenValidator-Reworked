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

        private CancellationTokenSource _scanCancellationTokenSource;
        private bool _disposed = false;
        private readonly object _lockObject = new object();

        public Scan()
        {
            _scanCancellationTokenSource = new CancellationTokenSource();
        }

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
                catch (ObjectDisposedException)
                {
                    
                }
                finally
                {
                    CleanupResources();
                }
            }
        }

        private void CleanupResources()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
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
                catch (Exception)
                {
                    
                }

                _scanCancellationTokenSource = new CancellationTokenSource();
            }
        }

        private async Task<string> ScanAllScreensForQrCodeAsync(System.Windows.Forms.Screen[] screens, CancellationToken cancellationToken)
        {
            var resultFound = new ConcurrentQueue<string>();
            var tcs = new TaskCompletionSource<string>();

            using (var internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                try
                {
                    cancellationToken.Register(() => tcs.TrySetCanceled());

                    var tasks = new List<Task>();

                    var timeoutTask = Task.Delay(8000, internalCts.Token)
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
                        }, internalCts.Token);

                        tasks.Add(task);
                    }

                    var resultTask = await Task.WhenAny(tcs.Task, Task.WhenAll(tasks));

                    internalCts.Cancel();

                    if (resultTask == tcs.Task && !tcs.Task.IsCanceled)
                    {
                        if (resultFound.TryDequeue(out string qrResult))
                        {
                            return qrResult;
                        }
                    }
                    await Task.WhenAll(tasks.Select(t => t.ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously)));

                    foreach (var task in tasks)
                    {
                        if (task.Status != TaskStatus.RanToCompletion)
                        {
                            task.Dispose();
                        }
                    }
                    tasks.Clear();

                    return null;
                }
                catch (TaskCanceledException)
                {
                    return null;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                finally
                {
                    internalCts.Cancel();
                }
            }
        }

        private async Task<string> ScanScreenForQrCodeAsync(System.Windows.Forms.Screen screen, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            try
            {
                using (var screenshot = new Bitmap(screen.Bounds.Width, screen.Bounds.Height))
                {
                    using (var graphics = Graphics.FromImage(screenshot))
                    {
                        graphics.CopyFromScreen(screen.Bounds.Left, screen.Bounds.Top, 0, 0, screenshot.Size);
                    }

                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    var result = ScanBitmapForQrCode(screenshot);
                    if (result != null)
                        return result;

                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    int gridSize = 2;
                    int sectionWidth = screenshot.Width / gridSize;
                    int sectionHeight = screenshot.Height / gridSize;
                    int overlap = 100; 

                    var sections = new List<(int x, int y, int width, int height)>();

                    for (int y = 0; y < gridSize; y++)
                    {
                        for (int x = 0; x < gridSize; x++)
                        {
                            int startX = Math.Max(0, x * sectionWidth - overlap);
                            int startY = Math.Max(0, y * sectionHeight - overlap);
                            int width = Math.Min(sectionWidth + 2 * overlap, screenshot.Width - startX);
                            int height = Math.Min(sectionHeight + 2 * overlap, screenshot.Height - startY);

                            sections.Add((startX, startY, width, height));
                        }
                    }

                    foreach (var section in sections)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return null;

                        using (var sectionBitmap = screenshot.Clone(
                            new Rectangle(section.x, section.y, section.width, section.height),
                            screenshot.PixelFormat))
                        {
                            var sectionResult = await Task.Run(() => TryEnhancedScanning(sectionBitmap, cancellationToken), cancellationToken);
                            if (sectionResult != null)
                                return sectionResult;
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }

            return null;
        }

        public async Task<string> ScanAtCursorAsync(System.Drawing.Point cursorPosition, int scanSize = 900)
        {
            ResetCancellationToken();

            const int TIMEOUTMS = 8000;

            try
            {
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_scanCancellationTokenSource.Token))
                {
                    timeoutCts.CancelAfter(TIMEOUTMS);

                    try
                    {
                        int left = cursorPosition.X - scanSize / 2;
                        int top = cursorPosition.Y - scanSize / 2;
                        if (left < 0) left = 0;
                        if (top < 0) top = 0;

                        using (var screenshot = new Bitmap(scanSize, scanSize))
                        {
                            using (var graphics = Graphics.FromImage(screenshot))
                            {
                                graphics.CopyFromScreen(left, top, 0, 0, screenshot.Size);
                            }

                            if (timeoutCts.Token.IsCancellationRequested)
                                return null;

                            var result = ScanBitmapForQrCode(screenshot);
                            if (result != null)
                                return result;

                            if (timeoutCts.Token.IsCancellationRequested)
                                return null;

                            return null;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                    catch (Exception ex)
                    {
                        if (!timeoutCts.Token.IsCancellationRequested)
                        {
                            throw;
                        }
                        return null;
                    }
                }
            }
            finally
            {
                CleanupResources();
            }
        }

        private static string ScanBitmapForQrCode(Bitmap bitmap)
        {
            var result = _barcodeReader.Decode(bitmap);
            return result?.Text;
        }

        private static string TryEnhancedScanning(Bitmap originalBitmap, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            var result = ScanBitmapForQrCode(originalBitmap);
            if (result != null) return result;
            if (cancellationToken.IsCancellationRequested) return null;

            double[] scales = [0.75, 1.25, 1.5];

            int[] processingPriority = [1, 0]; 

            foreach (int processingLevel in processingPriority)
            {
                if (cancellationToken.IsCancellationRequested) return null;

                using (var processed = EnhanceImage(originalBitmap, processingLevel))
                {
                    result = ScanBitmapForQrCode(processed);
                    if (result != null) return result;
                    if (cancellationToken.IsCancellationRequested) return null;

                    double[] priorityScales = processingLevel < 1 ? scales : [1.25];

                    foreach (var scale in priorityScales)
                    {
                        if (cancellationToken.IsCancellationRequested) return null;

                        using (var scaled = ScaleBitmap(processed, scale))
                        {
                            result = ScanBitmapForQrCode(scaled);
                            if (result != null) return result;
                            if (cancellationToken.IsCancellationRequested) return null;
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
                    if (cancellationToken.IsCancellationRequested) return null;

                    using (var scaledThresholded = ScaleBitmap(thresholded, 1.25))
                    {
                        result = ScanBitmapForQrCode(scaledThresholded);
                        if (result != null) return result;
                    }
                }
            }

            return null;
        }

        private static Bitmap ScaleBitmap(Bitmap original, double scale)
        {
            int width = (int)(original.Width * scale);
            int height = (int)(original.Height * scale);

            var result = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(result))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(original, 0, 0, width, height);
            }
            return result;
        }

        private static Bitmap EnhanceImage(Bitmap original, int level)
        {
            var result = new Bitmap(original.Width, original.Height);

            float contrast = 1.0f + (level * 0.25f);
            float brightness = -0.1f + (level * 0.05f);

            float[][] colorMatrixElements = [
                [contrast, 0, 0, 0, 0],
                [0, contrast, 0, 0, 0],
                [0, 0, contrast, 0, 0],
                [0, 0, 0, 1, 0],
                [brightness, brightness, brightness, 0, 1]
            ];

            using (var graphics = Graphics.FromImage(result))
            {
                var colorMatrix = new ColorMatrix(colorMatrixElements);
                var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                graphics.DrawImage(
                    original,
                    new Rectangle(0, 0, original.Width, original.Height),
                    0, 0, original.Width, original.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }

            if (level >= 2)
            {
                result = ApplySharpen(result, level - 1);
            }

            return result;
        }

        private static Bitmap ApplySharpen(Bitmap image, int strength)
        {
            float sharpness = 0.5f + (strength * 0.5f);

            float[][] sharpenFilter = [
                [-sharpness, -sharpness, -sharpness],
                [-sharpness, 9 + sharpness, -sharpness],
                [-sharpness, -sharpness, -sharpness]
            ];

            Bitmap result = new Bitmap(image.Width, image.Height);

            using (Graphics g = Graphics.FromImage(result))
            {
                var matrix = new ColorMatrix([
                    [1, 0, 0, 0, 0],
                    [0, 1, 0, 0, 0],
                    [0, 0, 1, 0, 0],
                    [0, 0, 0, 1, 0],
                    [0, 0, 0, 0, 1]
                ]);

                var attributes = new ImageAttributes();
                attributes.SetColorMatrix(matrix);

                g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height),
                    0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }

            return result;
        }

        private static Bitmap AdaptiveThreshold(Bitmap original)
        {
            Bitmap result = new Bitmap(original.Width, original.Height);

            using (var fastOriginal = new LockBitmap(original))
            using (var fastResult = new LockBitmap(result))
            {
                fastOriginal.LockBits();
                fastResult.LockBits();

                int kernelSize = 15;
                int halfKernel = kernelSize / 2;
                int constant = 15;

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
    }
}
