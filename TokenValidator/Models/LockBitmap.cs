using System.Drawing.Imaging;
using TokenValidator.Utils;

namespace TokenValidator.Models
{
    public class LockBitmap : IDisposable
    {
        #region Variables/Constructor
        Bitmap source = null;
        IntPtr Iptr = IntPtr.Zero;
        BitmapData bitmapData = null;

        public byte[] Pixels { get; set; }
        public int Depth { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public LockBitmap(Bitmap source)
        {
            this.source = source;
            this.Width = source.Width;
            this.Height = source.Height;
            this.Depth = Bitmap.GetPixelFormatSize(source.PixelFormat);
        }
        #endregion

        #region Class Methods
        public void LockBits()
        {
            try
            {
                bitmapData = source.LockBits(new Rectangle(0, 0, Width, Height),
                    ImageLockMode.ReadWrite, source.PixelFormat);

                int pixelCount = Width * Height;
                Pixels = new byte[pixelCount * 4];

                Iptr = bitmapData.Scan0;
                System.Runtime.InteropServices.Marshal.Copy(Iptr, Pixels, 0, Pixels.Length);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                throw;
            }
        }

        public unsafe Color GetPixel(int x, int y)
        {
            if (bitmapData == null)
                return source.GetPixel(x, y);

            fixed (byte* ptr = Pixels)
            {
                int offset = ((y * Width) + x) * 4;
                byte* pixel = ptr + offset;

                return Color.FromArgb(*(pixel + 3), *(pixel + 2), *(pixel + 1), *pixel);
            }
        }

        public unsafe void SetPixel(int x, int y, Color color)
        {
            if (bitmapData == null)
            {
                source.SetPixel(x, y, color);
                return;
            }
            
            fixed (byte* ptr = Pixels)
            {
                int offset = ((y * Width) + x) * 4;
                byte* pixel = ptr + offset;

                *pixel = color.B;
                *(pixel + 1) = color.G;
                *(pixel + 2) = color.R;
                *(pixel + 3) = color.A;
            }
        }

        public void UnlockBits()
        {
            if (bitmapData != null)
            {
                System.Runtime.InteropServices.Marshal.Copy(Pixels, 0, Iptr, Pixels.Length);
                source.UnlockBits(bitmapData);
                bitmapData = null;
            }
        }
        #endregion

        public void Dispose()
        {
            UnlockBits();
            source?.Dispose();
        }
    }
}
