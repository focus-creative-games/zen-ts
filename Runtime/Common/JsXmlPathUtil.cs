// Copyright 2026 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;

namespace ZenTS
{
    /// <summary>
    /// Shared path expansion for MarshalAs / JsAlias / JsExtensions XML Settings lists.
    /// </summary>
    public static class JsXmlPathUtil
    {
        public static List<string> ExpandToXmlFiles(
            IEnumerable<string> configuredPaths,
            string projectRoot,
            string pathLabel)
        {
            var files = new List<string>();
            if (configuredPaths == null)
            {
                return files;
            }

            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in configuredPaths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string path = raw.Trim();
                if (!Path.IsPathRooted(path))
                {
                    path = Path.GetFullPath(Path.Combine(projectRoot ?? Directory.GetCurrentDirectory(), path));
                }
                else
                {
                    path = Path.GetFullPath(path);
                }

                if (File.Exists(path))
                {
                    if (!string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "[ZenTS] " + pathLabel + " XML path is not an .xml file: " + path);
                    }

                    if (seenFiles.Add(path))
                    {
                        files.Add(path);
                    }

                    continue;
                }

                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.GetFiles(path, "*.xml", SearchOption.AllDirectories))
                    {
                        string full = Path.GetFullPath(file);
                        if (seenFiles.Add(full))
                        {
                            files.Add(full);
                        }
                    }

                    continue;
                }

                throw new InvalidOperationException(
                    "[ZenTS] " + pathLabel + " XML path not found: " + path);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }
    }
}
