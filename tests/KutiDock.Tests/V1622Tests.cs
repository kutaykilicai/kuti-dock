using KutiDock.Core;
using Xunit;

/// <summary>1.6.22: Genie görüntü dizileri yeniden kullanılır (her 2,5 sn'de yeni ~14 MB dizi ayrılmaz).</summary>
public class V1622CaptureBufferTests
{
    [Fact]
    public void Fit_ReusesSameLength_AllocatesOtherwise()
    {
        var a = new uint[2560 * 1440];
        Assert.Same(a, CaptureBuffer.Fit(a, 2560 * 1440));
        var b = CaptureBuffer.Fit(a, 1280 * 720); // pencere boyutu değişti
        Assert.NotSame(a, b);
        Assert.Equal(1280 * 720, b.Length);
        Assert.Equal(10, CaptureBuffer.Fit(null, 10).Length);
    }

    [Fact]
    public void Spare_IsTakenOnce_AndSingle()
    {
        var pool = new CaptureBuffer();
        Assert.Null(pool.Take());
        var old = new uint[4];
        pool.Give(old);
        Assert.Same(old, pool.Take());
        Assert.Null(pool.Take()); // alındı: aynı dizi iki yakalamaya birden verilmez
        pool.Give(new uint[4]);
        var newer = new uint[4];
        pool.Give(newer);
        Assert.Same(newer, pool.Take()); // tek yedek
        pool.Give(null);
        Assert.Null(pool.Take());
    }
}

/// <summary>1.6.22: hata.log ~1 MB'ta hata.log.1'e döner (tek yedek).</summary>
public class V1622RotatingLogTests
{
    [Fact]
    public void Rotates_AtLimit_KeepsSingleBackup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kd-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var log = Path.Combine(dir, "hata.log");
            RotatingLog.Append(log, new string('a', 60), maxBytes: 100);
            RotatingLog.Append(log, new string('b', 60), maxBytes: 100); // 60 < 100: dönmez
            Assert.Equal(120, new FileInfo(log).Length);
            Assert.False(File.Exists(log + ".1"));
            RotatingLog.Append(log, "c", maxBytes: 100); // 120 >= 100: döner
            Assert.Equal("c", File.ReadAllText(log));
            Assert.Equal(120, new FileInfo(log + ".1").Length);
            RotatingLog.Append(log, new string('d', 150), maxBytes: 100);
            RotatingLog.Append(log, "e", maxBytes: 100); // eski yedeğin üstüne
            Assert.Equal("e", File.ReadAllText(log));
            Assert.Equal("c" + new string('d', 150), File.ReadAllText(log + ".1"));
            Assert.Equal(2, Directory.GetFiles(dir).Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DefaultLimit_IsOneMegabyte() => Assert.Equal(1024 * 1024, RotatingLog.MaxBytes);
}
