using System.Management;
using System.Security.Principal;

namespace FramePilot.PrivilegedHelper;

/// <summary>
/// Minimal elevated helper. It is started per operation through UAC, accepts exactly one verb from a fixed allowlist,
/// takes no paths, commands or free-form text, executes no shell and exits. There is no long-lived IPC endpoint to attack.
/// </summary>
public static class Program
{
    public const int Created = 0;
    public const int NotCreated = 10;
    public const int NotElevated = 2;
    public const int Error = 3;
    public const int BadUsage = 64;

    private static readonly HashSet<string> AllowedVerbs = new(StringComparer.Ordinal) { "create-restore-point" };

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1 || !AllowedVerbs.Contains(args[0])) return BadUsage;
        if (!IsElevated()) return NotElevated;
        try
        {
            return args[0] switch
            {
                "create-restore-point" => CreateRestorePoint(),
                _ => BadUsage
            };
        }
        catch
        {
            return Error;
        }
    }

    private static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Creates a System Restore point with a fixed description and confirms it by counting restore points.</summary>
    private static int CreateRestorePoint()
    {
        var scope = new ManagementScope(@"\\.\root\default");
        scope.Connect();
        var before = CountRestorePoints(scope);

        using var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
        using var inParams = cls.GetMethodParameters("CreateRestorePoint");
        inParams["Description"] = "FramePilot: before applying changes";
        inParams["RestorePointType"] = 12u;  // MODIFY_SETTINGS
        inParams["EventType"] = 100u;        // BEGIN_SYSTEM_CHANGE
        using var result = cls.InvokeMethod("CreateRestorePoint", inParams, null);
        var rv = Convert.ToInt64(result?["ReturnValue"] ?? -1L);
        if (rv != 0) return NotCreated;

        // Windows may silently skip creation (frequency limit or System Protection off): confirm by count.
        var after = CountRestorePoints(scope);
        return after > before ? Created : NotCreated;
    }

    private static int CountRestorePoints(ManagementScope scope)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT SequenceNumber FROM SystemRestore"));
            using var col = searcher.Get();
            return col.Count;
        }
        catch
        {
            return -1;
        }
    }
}
