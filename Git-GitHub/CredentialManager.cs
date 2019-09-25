using System;
using System.Diagnostics;

namespace Git_GitHub
{
    public class CredentialManager
    {
        public static void Run(string command, string host)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"credential-manager {command}",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };

            startInfo.Environment["GCM_AUTHORITY"] = "GitHub";

            using (var process = Process.Start(startInfo))
            {
                var hostUrl = new Uri(host);
                process.StandardInput.WriteLine($"protocol={hostUrl.Scheme}");
                process.StandardInput.WriteLine($"host={hostUrl.Authority}");
                process.StandardInput.WriteLine($"path={hostUrl.AbsolutePath}");
                process.StandardInput.Close();

                while (true)
                {
                    var line = process.StandardOutput.ReadLine();
                    if (line == null) break;
                    Console.WriteLine(line);
                }

                process.WaitForExit();
            }
        }
    }
}
