using System;
using System.IO;

namespace SigortaTakip.Helpers
{
    public static class EnvLoader
    {
        public static void Load(string rootPath)
        {
            var envFilePath = Path.Combine(rootPath, ".env");
            if (!File.Exists(envFilePath)) return;

            foreach (var line in File.ReadAllLines(envFilePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var index = trimmed.IndexOf('=');
                if (index == -1) continue;

                var key = trimmed.Substring(0, index).Trim();
                var val = trimmed.Substring(index + 1).Trim();
                
                // Remove leading/trailing quotes if present
                if ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'")))
                {
                    val = val.Substring(1, val.Length - 2);
                }

                // Real environment variables (e.g. set by the host/container) take
                // precedence; a leftover .env file must not silently override them.
                if (Environment.GetEnvironmentVariable(key) == null)
                {
                    Environment.SetEnvironmentVariable(key, val);
                }
            }
        }
    }
}
