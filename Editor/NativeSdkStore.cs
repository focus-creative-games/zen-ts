// Copyright 2026 Code Philosophy. MIT License.
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ZenTS
{
    /// <summary>Verified immutable SDK snapshots outside the resolved UPM package.</summary>
    public static class NativeSdkStore
    {
        [Serializable]
        public sealed class FileEntry
        {
            public string path; public string sha256;
        }
        [Serializable]
        public sealed class Descriptor
        {
            public int schema; public int abi; public string target; public string sourceCommit;
            public string fingerprint; public FileEntry[] files; public FileEntry[] sources;
        }
        [Serializable]
        public sealed class Entry
        {
            public string target; public string fingerprint; public string relativePath;
        }
        [Serializable]
        public sealed class Catalog
        {
            public int schema = 1; public Entry[] entries = new Entry[0];
        }
        [Serializable]
        private sealed class Policy
        {
            public string source_commit;
        }
        [Serializable]
        private sealed class Provenance
        {
            public string provider; public string source_commit; public string target;
            public bool source_pristine; public bool deps_pristine; public bool jitless;
            public bool android_standard_vtable_abi; public int android_sdk_api_level;
            public string[] upstream_patches;
        }
        public static string Root => Path.Combine(CommonDirs.InstallRootDir, "NativeSDK");
        public static string CatalogPath => Path.Combine(Root, "installed.json");
        public static string EditorTarget => Application.platform == RuntimePlatform.WindowsEditor ? "windows-x64" :
            Application.platform == RuntimePlatform.OSXEditor ?
            (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "macos-arm64" : "macos-x64") : null;
        private static readonly string[] Targets = { "windows-x64", "macos-arm64", "macos-x64", "ios-arm64", "ios-simulator-x64", "ios-simulator-arm64", "android-arm64", "android-x64" };
        public static string HashFile(string path)
        {
            using (var hash = SHA256.Create())
            using (var file = File.OpenRead(path))
                return Hex(hash.ComputeHash(file));
        }
        private static string HashSource(string path) => HashText(File.ReadAllText(path).Replace("\r\n", "\n"));
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static string HashText(string text)
        {
            using (var hash = SHA256.Create())
                return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
        public static string Child(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative))
                throw new InvalidDataException("Invalid SDK relative path");
            string full = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("SDK path escapes its root: " + relative);
            return full;
        }
        private static readonly Dictionary<string, string> ValidatedStamps = new Dictionary<string, string>(StringComparer.Ordinal);
        public static Descriptor Validate(string root, bool force = false)
        {
            var descriptor = JsonUtility.FromJson<Descriptor>(File.ReadAllText(Path.Combine(root, "installation.json")));
            var policy = JsonUtility.FromJson<Policy>(File.ReadAllText(Path.Combine(CommonDirs.ZentsDataPathInPackage, "v8/official-source-lock.json")));
            if (descriptor == null || descriptor.schema != 1 || descriptor.abi != 1 || !Targets.Contains(descriptor.target) || descriptor.sourceCommit != policy.source_commit || descriptor.files == null || descriptor.sources == null || descriptor.sources.Length == 0)
                throw new InvalidDataException("[ZenTS] Incompatible native SDK descriptor");
            var stamp = new StringBuilder(HashFile(Path.Combine(root, "installation.json"))).Append(policy.source_commit);
            foreach (var item in descriptor.files)
            {
                var info = new FileInfo(Child(root, item.path));
                stamp.Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append(';');
            }
            foreach (var item in descriptor.sources)
            {
                var info = new FileInfo(Child(CommonDirs.PackageResolvedRoot, item.path));
                stamp.Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append(';');
            }
            string stampValue = stamp.ToString();
            if (!force && ValidatedStamps.TryGetValue(root, out string saved) && saved == stampValue)
                return descriptor;
            var ordered = descriptor.files.OrderBy(x => x.path, StringComparer.Ordinal).ToArray();
            if (ordered.Select(x => x.path).Distinct(StringComparer.Ordinal).Count() != ordered.Length || !ordered.SequenceEqual(descriptor.files))
                throw new InvalidDataException("[ZenTS] Invalid SDK file table");
            var content = new StringBuilder("zents-native-sdk-v1\n").Append(descriptor.target).Append('\n').Append(descriptor.sourceCommit).Append('\n');
            foreach (var file in ordered)
            {
                content.Append(file.path).Append('\0').Append(file.sha256).Append('\n');
                if (HashFile(Child(root, file.path)) != file.sha256)
                    throw new InvalidDataException("[ZenTS] SDK checksum mismatch: " + file.path);
            }
            if (HashText(content.ToString()) != descriptor.fingerprint)
                throw new InvalidDataException("[ZenTS] SDK fingerprint mismatch");
            foreach (var file in descriptor.sources)
                if (HashFile(Child(CommonDirs.PackageResolvedRoot, file.path)) != file.sha256 && HashSource(Child(CommonDirs.PackageResolvedRoot, file.path)) != file.sha256)
                    throw new InvalidDataException("[ZenTS] Rebuild SDK for the installed package: " + file.path);
            if (!ordered.Any(x => x.path == "official-sdk.json"))
                throw new InvalidDataException("[ZenTS] SDK provenance missing");
            var manifest = JsonUtility.FromJson<Provenance>(File.ReadAllText(Path.Combine(root, "official-sdk.json")));
            if (manifest.provider != "official-v8-source" || manifest.source_commit != policy.source_commit || manifest.target != descriptor.target || !manifest.source_pristine || !manifest.deps_pristine || (manifest.upstream_patches != null && manifest.upstream_patches.Length != 0))
                throw new InvalidDataException("[ZenTS] Official pristine V8 SDK required");
            if (descriptor.target.StartsWith("ios", StringComparison.Ordinal) && !manifest.jitless)
                throw new InvalidDataException("[ZenTS] iOS SDK must be JITless");
            if (descriptor.target.StartsWith("android", StringComparison.Ordinal) && (!manifest.android_standard_vtable_abi || manifest.android_sdk_api_level != 21))
                throw new InvalidDataException("[ZenTS] Android SDK ABI mismatch");
            string[] libraries = descriptor.target == "windows-x64" ? new[] { "v8_monolith.lib", "zents_v8_engine.lib" } : new[] { "libv8_monolith.a", "libzents_v8_engine.a" };
            foreach (string library in libraries)
                if (!ordered.Any(x => x.path == "lib/" + library))
                    throw new InvalidDataException("[ZenTS] Missing SDK library: " + library);
            if (descriptor.target == "windows-x64" || descriptor.target.StartsWith("macos", StringComparison.Ordinal))
            {
                string plugin = descriptor.target == "windows-x64" ? "zents_v8.dll" : "libzents_v8.dylib";
                if (!ordered.Any(x => x.path == "plugin/" + plugin))
                    throw new InvalidDataException("[ZenTS] Editor plugin missing");
                if (descriptor.target.StartsWith("macos", StringComparison.Ordinal) && !ordered.Any(x => x.path == "plugin/libMonoHookUtils_OSX.dylib"))
                    throw new InvalidDataException("[ZenTS] MonoHook helper missing");
            }
            ValidatedStamps[root] = stampValue;
            return descriptor;
        }
        public static Catalog ReadCatalog()
        {
            if (!File.Exists(CatalogPath))
                return new Catalog();
            var value = JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath));
            if (value == null || value.schema != 1 || value.entries == null || value.entries.Select(x => x.target).Distinct().Count() != value.entries.Length)
                throw new InvalidDataException("[ZenTS] Invalid SDK catalog");
            return value;
        }
        public static string Resolve(string target)
        {
            var entry = ReadCatalog().entries.SingleOrDefault(x => x.target == target);
            if (entry == null)
                throw new BuildFailedException("[ZenTS] Native SDK not installed: " + target + ". Use ZenTS/Native SDK/Import SDK, or import_platform.py --project <project> --sdk <SDK>. PackageCache is never modified.");
            string path = Child(Root, entry.relativePath);
            var descriptor = Validate(path);
            if (descriptor.target != target || descriptor.fingerprint != entry.fingerprint || entry.relativePath != entry.fingerprint + "/" + target)
                throw new InvalidDataException("[ZenTS] SDK catalog identity mismatch");
            return path;
        }
        public static bool HasTarget(string target) => target != null && ReadCatalog().entries.Any(x => x.target == target);
        public static string Import(string source)
        {
            var descriptor = Validate(source, true);
            Directory.CreateDirectory(Root);
            string destination = Child(Root, descriptor.fingerprint + "/" + descriptor.target);
            // Shared with the Python importer; prevents lost catalog updates across processes.
            using (var gate = new FileStream(Path.Combine(Root, ".catalog.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                if (gate.Length == 0)
                {
                    gate.WriteByte(0);
                    gate.Flush();
                }
                gate.Lock(0, 1);
                try
                {
                    if (!Directory.Exists(destination))
                    {
                        string stage = Path.Combine(Root, ".stage-" + Guid.NewGuid().ToString("N"));
                        try
                        {
                            Directory.CreateDirectory(stage);
                            foreach (var file in descriptor.files)
                            {
                                string output = Child(stage, file.path);
                                Directory.CreateDirectory(Path.GetDirectoryName(output));
                                File.Copy(Child(source, file.path), output);
                            }
                            File.Copy(Path.Combine(source, "installation.json"), Path.Combine(stage, "installation.json"));
                            Validate(stage);
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            Directory.Move(stage, destination);
                        }
                        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
                    }
                    else
                        Validate(destination);
                    var catalog = ReadCatalog();
                    catalog.entries = catalog.entries.Where(x => x.target != descriptor.target).Concat(new[] { new Entry { target = descriptor.target, fingerprint = descriptor.fingerprint, relativePath = descriptor.fingerprint + "/" + descriptor.target } }).OrderBy(x => x.target, StringComparer.Ordinal).ToArray();
                    string temporary = CatalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(temporary, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
                    if (File.Exists(CatalogPath))
                        File.Replace(temporary, CatalogPath, null);
                    else
                        File.Move(temporary, CatalogPath);
                }
                finally { gate.Unlock(0, 1); }
            }
            return destination;
        }
        [MenuItem("ZenTS/Native SDK/Import SDK...")]
        public static void ImportMenu()
        {
            string folder = EditorUtility.OpenFolderPanel("Import a prepared ZenTS native SDK", "", "");
            if (string.IsNullOrEmpty(folder))
                return;
            Import(folder);
            NativeEditorPlugins.Ensure();
            Debug.Log("[ZenTS] SDK imported into " + Root);
        }
        [MenuItem("ZenTS/Native SDK/Repair Editor Plugins")]
        public static void RepairEditorPlugins() => NativeEditorPlugins.Ensure();
    }
}
