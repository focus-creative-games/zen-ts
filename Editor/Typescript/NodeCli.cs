using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ZTS.Editor.Typescript
{
    internal static class NodeCli
    {
        public static void RunOrThrow(string workingDirectory, string fileName, string arguments)
        {
            int code = Run(workingDirectory, fileName, arguments, out string stdout, out string stderr);
            if (code != 0)
            {
                throw new InvalidOperationException(
                    $"[ZTS] '{fileName} {arguments}' failed (exit {code})\n{stdout}\n{stderr}");
            }

            if (!string.IsNullOrEmpty(stdout))
            {
                Debug.Log($"[ZTS] {fileName}: {stdout.Trim()}");
            }
        }

        public static int Run(
            string workingDirectory,
            string fileName,
            string arguments,
            out string stdout,
            out string stderr)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

#if UNITY_EDITOR_WIN
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c " + Quote(fileName) + " " + arguments;
#endif

            using (var proc = Process.Start(psi))
            {
                if (proc == null)
                {
                    throw new InvalidOperationException(
                        "[ZTS] failed to start Node/npm. Install Node LTS and ensure it is on PATH.");
                }

                stdout = proc.StandardOutput.ReadToEnd();
                stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }

        public static string LocalBin(string tsProjectRoot, string tool)
        {
#if UNITY_EDITOR_WIN
            string cmd = Path.Combine(tsProjectRoot, "node_modules", ".bin", tool + ".cmd");
            if (File.Exists(cmd))
            {
                return cmd;
            }
#endif
            string unix = Path.Combine(tsProjectRoot, "node_modules", ".bin", tool);
            return File.Exists(unix) ? unix : null;
        }

        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "\"\"";
            }

            if (s.IndexOfAny(new[] { ' ', '\t' }) < 0)
            {
                return s;
            }

            return "\"" + s + "\"";
        }
    }
}
