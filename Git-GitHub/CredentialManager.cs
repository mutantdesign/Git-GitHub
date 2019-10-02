using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace Git_GitHub
{
    public class CredentialManager
    {
        public static (string username, string password) Fill(string hostUrl)
        {
            var inputProperties = CreateInputProperties(hostUrl);
            var outputProperties = Run("fill", inputProperties);
            return (outputProperties["username"], outputProperties["password"]);
        }

        public static void Reject(string hostUrl)
        {
            var inputProperties = CreateInputProperties(hostUrl);
            Run("reject", inputProperties);
        }

        static Dictionary<string, string> CreateInputProperties(string hostUrl)
        {
            var uri = new Uri(hostUrl);
            return new Dictionary<string, string>
            {
                ["protocol"] = uri.Scheme,
                ["host"] = uri.Authority,
                ["path"] = uri.AbsolutePath
            };
        }

        static IDictionary<string, string> Run(string command, string host)
        {
            var hostUrl = new Uri(host);
            var inputProperties = new Dictionary<string, string>
            {
                ["protocol"] = hostUrl.Scheme,
                ["host"] = hostUrl.Authority,
                ["path"] = hostUrl.AbsolutePath
            };

            return Run(command, inputProperties);
        }

        static IDictionary<string, string> Run(string command, IDictionary<string, string> inputProperties)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"credential {command}",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };

            startInfo.Environment["GCM_AUTHORITY"] = "GitHub";

            using (var process = Process.Start(startInfo))
            {
                foreach (var property in inputProperties)
                {
                    process.StandardInput.WriteLine($"{property.Key}={property.Value}");
                }

                process.StandardInput.Close();

                var outputProperties = new Dictionary<string, string>();
                while (process.StandardOutput.ReadLine() is string line)
                {
                    var split = line.Split('=');
                    if (split.Length != 2)
                    {
                        continue;
                    }

                    var (key, value) = (split[0], split[1]);
                    outputProperties[key] = value;
                }

                process.WaitForExit();
                return outputProperties;
            }
        }
    }
}
