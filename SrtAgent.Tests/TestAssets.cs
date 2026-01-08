using System;
using System.IO;

namespace SrtAgent.Tests;

internal static class TestAssets
{
    // If you don't want hard-coded machine-specific paths, set env var:
    //   SRTAGENT_TEST_MKV=F:\\Ñ¸À×ÏÂÔØ\\Now.You.See.Me.Now.You.Dont.2025.MULTi.VFQ.2160p.HDR.WEB-DL.H265-Slay3R.mkv
    private const string DefaultMkvPath = @"F:\Ñ¸À×ÏÂÔØ\Now.You.See.Me.Now.You.Dont.2025.MULTi.VFQ.2160p.HDR.WEB-DL.H265-Slay3R.mkv";

    public static string SampleMkvPath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("SRTAGENT_TEST_MKV");
            var path = string.IsNullOrWhiteSpace(env) ? DefaultMkvPath : env;
            return Path.GetFullPath(path);
        }
    }

    public static void AssertSampleMkvExists()
    {
        if (!File.Exists(SampleMkvPath))
        {
            throw new FileNotFoundException(
                $"Test asset not found: '{SampleMkvPath}'. " +
                "Set env var SRTAGENT_TEST_MKV to the mkv file path on this machine.");
        }
    }
}
