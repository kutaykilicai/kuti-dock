using KutiDock.Core;
using Xunit;

/// <summary>1.6.11: aramadaki Everything anahtarı Everything'i bulup başlatır; WARP arayüzü yalnızca dock'tan bağlanırken açılır.</summary>
public class V1611Tests
{
    [Fact]
    public void FindExe_PrefersProgramFiles_ThenX86()
    {
        var pf = @"C:\Program Files"; var x86 = @"C:\Program Files (x86)";
        string a = Path.Combine(pf, "Everything", "Everything.exe"), b = Path.Combine(x86, "Everything", "Everything.exe");
        Assert.Equal(a, EverythingIpc.FindExe(p => p == a || p == b, pf, x86));
        Assert.Equal(b, EverythingIpc.FindExe(p => p == b, pf, x86));
        Assert.Null(EverythingIpc.FindExe(_ => false, pf, x86));
        Assert.Equal(b, EverythingIpc.FindExe(p => p == b, null, "", x86));
    }

    [Fact]
    public void Everything_StartsInBackground_ExitClosesOnlyClient()
    {
        Assert.Equal("-startup", EverythingIpc.StartArgs);
        Assert.Equal("-exit", EverythingIpc.ExitArgs);
    }

    [Theory]
    [InlineData(true, false, true)]   // bağlan, arayüz kapalı: önce arayüz
    [InlineData(true, true, false)]   // arayüz zaten açık
    [InlineData(false, false, false)] // bağlantıyı keserken arayüz açılmaz
    public void Warp_GuiOnlyOnConnect(bool connect, bool guiRunning, bool launch) =>
        Assert.Equal(launch, Warp.ShouldLaunchGui(connect, guiRunning));
}
