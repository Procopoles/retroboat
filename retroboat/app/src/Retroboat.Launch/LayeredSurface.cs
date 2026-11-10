using System;
using System.Runtime.InteropServices;

namespace Retroboat.Launch
{
    /// <summary>
    /// A memória de pixels de uma janela em camadas (WS_EX_LAYERED): uma só, reaproveitada a cada quadro. Recebe ARGB
    /// pré-multiplicado e põe na tela com transparência por pixel, mais uma opacidade geral para entrar e sair.
    /// </summary>
    sealed class LayeredSurface : IDisposable
    {
        readonly int _width;
        readonly int _height;
        readonly IntPtr _memoryDc;
        readonly IntPtr _bitmap;
        readonly IntPtr _previous;
        readonly IntPtr _bits;

        public LayeredSurface(int width, int height)
        {
            _width = width;
            _height = height;

            var info = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = width,
                biHeight = -height, // Negativa: de cima para baixo, como os pixels que chegam aqui.
                biPlanes = 1,
                biBitCount = 32
            };

            _memoryDc = CreateCompatibleDC(IntPtr.Zero);
            _bitmap = CreateDIBSection(_memoryDc, ref info, 0, out _bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero)
                throw new InvalidOperationException("Não foi possível criar a superfície da janela em camadas.");

            _previous = SelectObject(_memoryDc, _bitmap);
        }

        public int Width => _width;
        public int Height => _height;

        /// <summary>Copia os pixels e mostra a janela na posição dada.</summary>
        public void Present(IntPtr window, int[] pixels, int x, int y, byte opacity = 255)
        {
            Marshal.Copy(pixels, 0, _bits, pixels.Length);
            Move(window, x, y, opacity);
        }

        /// <summary>Só muda a posição e a opacidade, com os pixels do último <see cref="Present"/>.</summary>
        public void Move(IntPtr window, int x, int y, byte opacity)
        {
            var position = new POINT { X = x, Y = y };
            var size = new SIZE { CX = _width, CY = _height };
            var source = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = opacity, AlphaFormat = 1 };
            UpdateLayeredWindow(window, IntPtr.Zero, ref position, ref size, _memoryDc, ref source, 0, ref blend, ULW_ALPHA);
        }

        public void Dispose()
        {
            SelectObject(_memoryDc, _previous);
            DeleteObject(_bitmap);
            DeleteDC(_memoryDc);
        }

        const int ULW_ALPHA = 2;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX; public int CY; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref POINT position, ref SIZE size, IntPtr source, ref POINT sourcePosition, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    }
}
