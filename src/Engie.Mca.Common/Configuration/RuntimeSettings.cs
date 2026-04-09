using System;
using System.IO;

namespace Engie.Mca.Common.Configuration;

public static class RuntimeSettings
{
    public static string GetLogsDirectory()
    {
        return Environment.GetEnvironmentVariable("LOGS_DIRECTORY")
            ?? Path.Combine(Path.GetTempPath(), "logs", "blocks");
    }

    public static string GetServiceBaseUrl(string environmentVariableName, string fallback)
    {
        var configured = Environment.GetEnvironmentVariable(environmentVariableName);
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }

    public static int GetNonNegativeInt(string environmentVariableName, int fallback)
    {
        var configured = Environment.GetEnvironmentVariable(environmentVariableName);
        return int.TryParse(configured, out var value) && value >= 0
            ? value
            : fallback;
    }

    public static int GetPositiveInt(string environmentVariableName, int fallback)
    {
        var configured = Environment.GetEnvironmentVariable(environmentVariableName);
        return int.TryParse(configured, out var value) && value >= 1
            ? value
            : fallback;
    }
}