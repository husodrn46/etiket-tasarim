using AkelEtiket.Core.Imaging;

namespace AkelEtiket.Core.Tests.Core;

public class MonoBitmapTests
{
    [Theory]
    [InlineData(800, 100)]
    [InlineData(801, 101)]
    [InlineData(1, 1)]
    [InlineData(8, 1)]
    [InlineData(9, 2)]
    [InlineData(813, 102)]
    public void Stride_IsBytesRoundedUp(int width, int expectedStride)
    {
        var bmp = new MonoBitmap(width, 3);
        Assert.Equal(expectedStride, bmp.Stride);
        Assert.Equal(expectedStride * 3, bmp.Data.Length);
    }

    [Fact]
    public void SetPixel_UsesMsbFirst_AndOneIsBlack()
    {
        var bmp = new MonoBitmap(16, 1);
        bmp.SetPixel(0, 0, true);
        bmp.SetPixel(9, 0, true);
        Assert.Equal(0x80, bmp.Data[0]);
        Assert.Equal(0x40, bmp.Data[1]);
        Assert.True(bmp[0, 0]);
        Assert.False(bmp[1, 0]);
        bmp.SetPixel(0, 0, false);
        Assert.Equal(0, bmp.Data[0]);
    }

    [Fact]
    public void Fill_Black_KeepsPaddingBitsWhite()
    {
        var bmp = new MonoBitmap(10, 2);
        bmp.Fill(true);
        Assert.Equal(0xFF, bmp.Data[0]);
        Assert.Equal(0xC0, bmp.Data[1]); // yalnızca 2 geçerli bit
        Assert.Equal(20, bmp.CountBlack());
        bmp.Invert();
        Assert.Equal(0, bmp.CountBlack());
    }

    [Fact]
    public void FillRect_MatchesPerPixelReference()
    {
        var random = new Random(42);
        for (int iteration = 0; iteration < 200; iteration++)
        {
            int w = random.Next(1, 40), h = random.Next(1, 10);
            var fast = new MonoBitmap(w, h);
            var reference = new bool[w, h];
            for (int op = 0; op < 5; op++)
            {
                int x = random.Next(-5, w + 5), y = random.Next(-3, h + 3);
                int rw = random.Next(0, 30), rh = random.Next(0, 8);
                bool black = random.Next(2) == 0;
                fast.FillRect(x, y, rw, rh, black);
                for (int yy = Math.Max(0, y); yy < Math.Min(h, y + rh); yy++)
                for (int xx = Math.Max(0, x); xx < Math.Min(w, x + rw); xx++)
                    reference[xx, yy] = black;
            }
            for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
                Assert.Equal(reference[xx, yy], fast[xx, yy]);
            AssertPaddingClear(fast);
        }
    }

    [Fact]
    public void DrawBitmap_WithMaskAndOffsets_MatchesPerPixelReference()
    {
        var random = new Random(7);
        for (int iteration = 0; iteration < 300; iteration++)
        {
            int tw = random.Next(1, 50), th = random.Next(1, 12);
            int sw = random.Next(1, 30), sh = random.Next(1, 12);
            var target = RandomBitmap(random, tw, th);
            var source = RandomBitmap(random, sw, sh);
            MonoBitmap? mask = random.Next(3) == 0 ? null : RandomBitmap(random, sw, sh);
            int dx = random.Next(-20, tw + 5), dy = random.Next(-8, th + 3);

            var expected = target.Clone();
            for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                if (mask is not null && !mask[x, y]) continue;
                int tx = x + dx, ty = y + dy;
                if (expected.Contains(tx, ty)) expected[tx, ty] = source[x, y];
            }

            target.DrawBitmap(source, dx, dy, mask);
            Assert.True(expected.ContentEquals(target), $"iterasyon {iteration}: dx={dx} dy={dy} mask={(mask is null ? "yok" : "var")}");
            AssertPaddingClear(target);
        }
    }

    [Fact]
    public void RotateQuarterTurns_IsLosslessAndCorrect()
    {
        var random = new Random(3);
        var bmp = RandomBitmap(random, 13, 7);
        var r90 = bmp.RotateQuarterTurns(1);
        Assert.Equal(7, r90.Width);
        Assert.Equal(13, r90.Height);
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
            Assert.Equal(bmp[x, y], r90[bmp.Height - 1 - y, x]);

        var back = r90.RotateQuarterTurns(1).RotateQuarterTurns(1).RotateQuarterTurns(1);
        Assert.True(bmp.ContentEquals(back));
        Assert.True(bmp.RotateQuarterTurns(2).RotateQuarterTurns(2).ContentEquals(bmp));
        Assert.True(bmp.RotateQuarterTurns(-1).ContentEquals(bmp.RotateQuarterTurns(3)));
    }

    [Fact]
    public void Gray8_RoundTrip_ProducesOnlyBlackAndWhite()
    {
        var gray = new byte[] { 0, 127, 128, 255, 10, 200 };
        var bmp = MonoBitmap.FromGray8(gray, 3, 2);
        var back = bmp.ToGray8();
        Assert.All(back, v => Assert.True(v is 0 or 255));
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 255 }, back);
    }

    [Fact]
    public void GetInkBounds_FindsBlackRegion()
    {
        var bmp = new MonoBitmap(40, 20);
        Assert.Null(bmp.GetInkBounds());
        bmp.FillRect(5, 3, 10, 4, true);
        bmp.SetPixel(33, 15, true);
        Assert.Equal((5, 3, 29, 13), bmp.GetInkBounds());
    }

    [Fact]
    public void Constructor_RejectsWrongDataLength()
    {
        Assert.Throws<ArgumentException>(() => new MonoBitmap(10, 2, new byte[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonoBitmap(0, 2));
    }

    private static MonoBitmap RandomBitmap(Random random, int w, int h)
    {
        var bmp = new MonoBitmap(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            bmp[x, y] = random.Next(2) == 0;
        return bmp;
    }

    private static void AssertPaddingClear(MonoBitmap bmp)
    {
        int pad = bmp.Stride * 8 - bmp.Width;
        if (pad == 0) return;
        byte paddingMask = (byte)((1 << pad) - 1);
        for (int y = 0; y < bmp.Height; y++)
            Assert.Equal(0, bmp.Data[y * bmp.Stride + bmp.Stride - 1] & paddingMask);
    }
}
