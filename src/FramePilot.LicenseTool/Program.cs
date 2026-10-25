using System.Security.Cryptography;
using FramePilot.Licensing;

// Vendor-side tool. Never ship the private key with the application.
//   fp-license keygen <privateKeyOut.pem>
//   fp-license issue <privateKey.pem> <email> <machineCode> <Pro|Founders> <months>
//   fp-license verify <key> <publicKeyBase64> <machineCode>

if (args.Length == 0)
{
    Console.WriteLine("Usage: keygen <out.pem> | issue <key.pem> <email> <machineCode> <Pro|Founders> <months> | verify <key> <pubB64> <machineCode>");
    return 1;
}

switch (args[0])
{
    case "keygen" when args.Length == 2:
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(args[1], ec.ExportPkcs8PrivateKeyPem());
        Console.WriteLine("Private key written to " + args[1]);
        Console.WriteLine("Public key (put into FramePilot.App LicenseConfig.PublicKey):");
        Console.WriteLine(Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo()));
        return 0;
    }
    case "issue" when args.Length == 6:
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(File.ReadAllText(args[1]));
        var tier = Enum.Parse<LicenseTier>(args[4], ignoreCase: true);
        var months = int.Parse(args[5]);
        var now = DateTimeOffset.UtcNow;
        var payload = new LicensePayload("L-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), args[2], tier, args[3].Trim(), now, now.AddMonths(months));
        Console.WriteLine(LicenseVerifier.Sign(payload, ec));
        return 0;
    }
    case "verify" when args.Length == 4:
    {
        var state = LicenseVerifier.Verify(args[1], args[2], args[3], DateTimeOffset.UtcNow);
        Console.WriteLine($"{state.Status} {state.Payload}");
        return state.Status == LicenseStatus.Valid ? 0 : 2;
    }
    default:
        Console.WriteLine("Invalid arguments.");
        return 1;
}
