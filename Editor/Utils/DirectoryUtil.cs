// Copyright 2026 Code Philosophy

using System;
using System.IO;
using System.Threading;

namespace ZTS.Utils
{
    public static class DirectoryUtil
    {
        public static void RemoveDir(string dir, bool log = false)
        {
            if (log)
            {
                UnityEngine.Debug.Log($"RemoveDir dir:{dir}");
            }

            int maxTryCount = 5;
            for (int i = 0; i < maxTryCount; ++i)
            {
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        return;
                    }

                    foreach (var file in Directory.GetFiles(dir))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }

                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        RemoveDir(subDir);
                    }

                    Directory.Delete(dir, true);
                    break;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[ZTS] RemoveDir:{dir} with exception:{e}. try count:{i}");
                    Thread.Sleep(100);
                }
            }
        }

        public static void RecreateDir(string dir)
        {
            if (Directory.Exists(dir))
            {
                RemoveDir(dir, true);
            }

            Directory.CreateDirectory(dir);
        }

        public static void CopyDir(string src, string dst, bool log = false)
        {
            if (log)
            {
                UnityEngine.Debug.Log($"CopyDir {src} => {dst}");
            }

            if (Directory.Exists(dst))
            {
                RemoveDir(dst);
            }
            else
            {
                string parentDir = Path.GetDirectoryName(Path.GetFullPath(dst));
                Directory.CreateDirectory(parentDir);
            }

            UnityEditor.FileUtil.CopyFileOrDirectory(src, dst);
        }
    }
}
