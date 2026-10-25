using FramePilot.GameAdapters;
using FramePilot.Telemetry;

namespace FramePilot.Tests;

public class PresentMonCsvTests
{
    private const string V2Csv =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,FrameType,CPUStartTime,MsBetweenAppStart,MsCPUBusy,MsCPUWait,MsGPULatency,MsGPUTime,MsGPUBusy,MsGPUWait,DisplayLatency,DisplayedTime,MsAnimationError,MsClickToPhotonLatency,MsAllInputToPhotonLatency,MsBetweenPresents,MsInPresentAPI,MsUntilDisplayed,MsBetweenDisplayChange,MsPCLatency\n" +
        "cs2.exe,1234,0x1,DXGI,0,0,1,Hardware: Independent Flip,Application,0.0,4.0,2.0,2.0,0.1,3.5,3.4,0.6,8.0,4.0,0.0,NA,NA,4.0,0.1,5.0,4.0,NA\n" +
        "cs2.exe,1234,0x1,DXGI,0,0,1,Hardware: Independent Flip,Application,4.0,4.2,2.0,2.2,0.1,3.6,3.5,0.7,8.2,4.2,0.0,NA,NA,4.2,0.1,5.0,4.2,NA\n" +
        "cs2.exe,1234,0x1,DXGI,0,0,1,Hardware: Independent Flip,Application,8.2,3.8,2.0,1.8,0.1,3.4,3.3,0.5,7.9,NA,0.0,NA,NA,3.8,0.1,NA,NA,NA\n" +
        "steamwebhelper.exe,99,0x9,DXGI,0,0,0,Composed: Flip,Application,0,16.6,1,1,1,1,1,1,1,1,0,NA,NA,16.6,0,1,16.6,NA\n" +
        "cs2.exe,1234,0x1,DXGI,0,0,1,Hardware: Indep"; // truncated last line (process killed mid-write)

    [Fact]
    public void Parses_V2Columns_AndPrefersMsBetweenPresents()
    {
        var p = PresentMonCsv.Parse(new StringReader(V2Csv), "cs2.exe");
        Assert.Equal("MsBetweenPresents", p.FrameTimeColumn);
        Assert.Equal(3, p.Frames.Count);
        Assert.Equal(4.2, p.Frames[1].FrameTimeMs, 6);
        Assert.Equal(3.5, p.Frames[1].GpuBusyMs!.Value, 6);
        Assert.Null(p.Frames[0].PcLatencyMs);                // "NA" stays missing
        Assert.False(p.Frames[2].Displayed);                  // DisplayedTime = NA
        Assert.Equal(12.0, p.Frames[2].TimeMs, 6);            // cumulative time
        Assert.True(p.RowsSkipped >= 2);                      // other process + truncated row
    }

    [Fact]
    public void Parses_V1Columns()
    {
        const string v1 = "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,msUntilRenderComplete,msUntilDisplayed\n" +
                          "cs2.exe,1,0x1,DXGI,0,0,0,Hardware: Legacy Flip,0,0.0,0.1,5.0,1,2\n" +
                          "cs2.exe,1,0x1,DXGI,0,0,0,Hardware: Legacy Flip,1,0.005,0.1,6.0,1,2\n";
        var p = PresentMonCsv.Parse(new StringReader(v1), "cs2");
        Assert.Equal(2, p.Frames.Count);
        Assert.False(p.Frames[1].Displayed);
        Assert.Null(p.Frames[0].GpuBusyMs);
    }

    [Fact]
    public void MissingFrameTimeColumn_Throws()
    {
        Assert.Throws<InvalidDataException>(() => PresentMonCsv.Parse(new StringReader("A,B\n1,2\n")));
    }

    [Fact]
    public void BusiestSwapChain_IsAnalysed()
    {
        const string csv = "Application,SwapChainAddress,MsBetweenPresents\n" +
                           "cs2.exe,0xA,5\ncs2.exe,0xA,5\ncs2.exe,0xA,5\ncs2.exe,0xB,30\n";
        var p = PresentMonCsv.Parse(new StringReader(csv), "cs2.exe");
        Assert.Equal("0xA", p.SwapChain);
        Assert.Equal(3, p.Frames.Count);
        Assert.NotEmpty(p.Warnings);
    }

    [Fact]
    public void QuotedFields_AreHandled()
    {
        Assert.Equal(new[] { "a", "b,c", "d\"e" }, PresentMonCsv.SplitCsv("a,\"b,c\",\"d\"\"e\""));
    }
}

public class PresentMonRunnerTests
{
    [Fact]
    public void Arguments_UseOnlyDocumentedOptions()
    {
        var args = PresentMonRunner.BuildArguments(new CaptureRequest("cs2", 120, @"C:\Data\s.csv", false));
        Assert.Equal(new[]
        {
            "--process_name", "cs2.exe", "--output_file", @"C:\Data\s.csv", "--timed", "120", "--terminate_after_timed",
            "--terminate_on_proc_exit", "--stop_existing_session", "--session_name", "FramePilotCapture", "--no_console_stats"
        }, args);
    }

    [Theory]
    [InlineData("cs2 & calc")]
    [InlineData("a\"b")]
    [InlineData("")]
    public void InvalidProcessNames_AreRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => PresentMonRunner.BuildArguments(new CaptureRequest(name, 60, @"C:\x.csv", false)));
    }

    [Fact]
    public void RelativeOrNonCsvOutput_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => PresentMonRunner.BuildArguments(new CaptureRequest("cs2", 60, "x.csv", false)));
        Assert.Throws<ArgumentException>(() => PresentMonRunner.BuildArguments(new CaptureRequest("cs2", 60, @"C:\x.exe", false)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PresentMonRunner.BuildArguments(new CaptureRequest("cs2", 5, @"C:\x.csv", false)));
    }

    [Fact]
    public void Quote_WrapsPathsWithSpaces()
    {
        Assert.Equal("\"C:\\My Data\\s.csv\"", PresentMonRunner.Quote(@"C:\My Data\s.csv"));
        Assert.Equal("--timed", PresentMonRunner.Quote("--timed"));
    }
}

public class VdfTests
{
    [Fact]
    public void Parses_LibraryFolders_NewFormat()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"      "C:\\Program Files (x86)\\Steam"
                    "apps"
                    {
                        "730"   "123"
                    }
                }
                "1"
                {
                    "path"      "D:\\SteamLibrary"
                }
            }
            """;
        var libs = GameDetector.ParseLibraryFolders(vdf).ToList();
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, libs);
    }

    [Fact]
    public void Parses_AppManifest()
    {
        const string acf = """
            "AppState"
            {
                "appid"     "730"
                "installdir"        "Counter-Strike Global Offensive"
                "buildid"       "15000000"
                // comment
            }
            """;
        var state = Vdf.Parse(acf).Child("AppState")!;
        Assert.Equal("Counter-Strike Global Offensive", state.Get("installdir"));
        Assert.Equal("15000000", state.Get("buildid"));
    }
}
