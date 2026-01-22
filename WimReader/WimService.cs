using System;
using System.Collections.Generic;
using System.IO;
using DiscUtils.Wim;

namespace WimReader
{
    public class WimFileInfo
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime? LastModified { get; set; }
        public string Attributes { get; set; }
    }

    public class WimService : IDisposable
    {
        private WimFile _wimFile;
        private FileStream _wimStream;
        private bool _disposed = false;

        public void OpenWim(string wimPath)
        {
            if (!File.Exists(wimPath) && !IsNetworkPath(wimPath))
            {
                throw new FileNotFoundException($"[!] WIM file not found: {wimPath}");
            }

            _wimStream = new FileStream(wimPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _wimFile = new WimFile(_wimStream);
            
            if (_wimFile == null)
            {
                _wimStream?.Dispose();
                throw new InvalidOperationException($"[!] Failed to open WIM file: {wimPath}");
            }
        }

        public int GetImageCount()
        {
            EnsureWimFileOpened();
            return _wimFile.ImageCount;
        }

        public List<WimFileInfo> ListFiles(int imageIndex, string directoryPath = null)
        {
            EnsureWimFileOpened();
            ValidateImageIndex(imageIndex);

            var files = new List<WimFileInfo>();
            var fileSystem = _wimFile.GetImage(imageIndex - 1);

            string targetPath = NormalizeDirectoryPath(directoryPath);
            
            if (!fileSystem.DirectoryExists(targetPath))
            {
                return files;
            }

            ListDirectoryContents(fileSystem, targetPath, files);

            files.Sort((a, b) =>
            {
                bool aIsDir = a.Attributes.Contains("D");
                bool bIsDir = b.Attributes.Contains("D");
                
                // If a is a directory and b is not, a comes first
                if (aIsDir && !bIsDir)
                    return -1;
                // If a is not a directory and b is, b comes first
                if (!aIsDir && bIsDir)
                    return 1;
                
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });

            return files;
        }

        private string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "\\";

            string normalized = path.Replace('/', '\\').Trim();
            
            while (normalized.StartsWith("\\"))
            {
                normalized = normalized.Substring(1);
            }
            
            if (string.IsNullOrEmpty(normalized))
                return "\\";
            
            normalized = "\\" + normalized.TrimEnd('\\');
            
            return normalized;
        }

        private void EnsureWimFileOpened()
        {
            if (_wimFile == null)
            {
                throw new InvalidOperationException("[!] WIM file not opened");
            }
        }

        private void ValidateImageIndex(int imageIndex)
        {
            if (imageIndex < 1 || imageIndex > _wimFile.ImageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(imageIndex), $"[!] Image index must be between 1 and {_wimFile.ImageCount}");
            }
        }

        private void ListDirectoryContents(WimFileSystem fileSystem, string directoryPath, List<WimFileInfo> files)
        {
            try
            {
                string normalizedPath = directoryPath;

                string[] entries = fileSystem.GetFileSystemEntries(normalizedPath);

                foreach (var entry in entries)
                {
                    string pathToUse = entry;
                    
                    if (!entry.StartsWith("\\"))
                    {
                        if (normalizedPath == "\\")
                            pathToUse = "\\" + entry;
                        else
                            pathToUse = normalizedPath + "\\" + entry;
                    }

                    bool isDirectory = fileSystem.DirectoryExists(pathToUse);
                    bool isFile = fileSystem.FileExists(pathToUse);

                    if (!isDirectory && !isFile)
                        continue;

                    long size = 0;
                    DateTime? lastWrite = null;
                    System.IO.FileAttributes attrs = System.IO.FileAttributes.Normal;

                    if (isFile)
                    {
                        try
                        {
                            var fileInfo = fileSystem.GetFileInfo(pathToUse);
                            if (fileInfo != null)
                            {
                                size = fileInfo.Length;
                                lastWrite = fileInfo.LastWriteTime;
                                attrs = fileInfo.Attributes;
                            }
                        }
                        catch
                        {
                            try
                            {
                                using (var stream = fileSystem.OpenFile(pathToUse, FileMode.Open, FileAccess.Read))
                                {
                                    size = stream.Length;
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                    else
                    {
                        attrs = System.IO.FileAttributes.Directory;
                    }

                    files.Add(new WimFileInfo
                    {
                        Path = pathToUse,
                        Size = size,
                        LastModified = lastWrite,
                        Attributes = GetAttributesString(attrs)
                    });
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[!] Error traversing directory '{directoryPath}': {ex.Message}");
            }
        }

        public byte[] ReadFileContent(int imageIndex, string sourcePath)
        {
            EnsureWimFileOpened();
            ValidateImageIndex(imageIndex);

            var fileSystem = _wimFile.GetImage(imageIndex - 1);

            if (!fileSystem.FileExists(sourcePath))
            {
                throw new FileNotFoundException($"[!] File '{sourcePath}' not found in WIM image {imageIndex}");
            }

            using (var sourceStream = fileSystem.OpenFile(sourcePath, FileMode.Open, FileAccess.Read))
            using (var memoryStream = new MemoryStream())
            {
                sourceStream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

        public void ExtractFile(int imageIndex, string sourcePath, string destinationPath)
        {
            EnsureWimFileOpened();
            ValidateImageIndex(imageIndex);

            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            var fileSystem = _wimFile.GetImage(imageIndex - 1);

            if (!fileSystem.FileExists(sourcePath))
            {
                throw new FileNotFoundException($"[!] File '{sourcePath}' not found in WIM image {imageIndex}");
            }

            using (var sourceStream = fileSystem.OpenFile(sourcePath, FileMode.Open, FileAccess.Read))
            using (var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
            {
                sourceStream.CopyTo(destStream);
            }
        }

        private bool IsNetworkPath(string path)
        {
            return path.StartsWith(@"\\") || (path.Length >= 2 && path[1] == ':');
        }

        private string GetAttributesString(System.IO.FileAttributes attributes)
        {
            var attrs = new List<string>();
            if ((attributes & System.IO.FileAttributes.ReadOnly) != 0) attrs.Add("R");
            if ((attributes & System.IO.FileAttributes.Hidden) != 0) attrs.Add("H");
            if ((attributes & System.IO.FileAttributes.System) != 0) attrs.Add("S");
            if ((attributes & System.IO.FileAttributes.Directory) != 0) attrs.Add("D");
            if ((attributes & System.IO.FileAttributes.Archive) != 0) attrs.Add("A");
            return string.Join("", attrs);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _wimFile = null;
                _wimStream?.Dispose();
                _wimStream = null;
                _disposed = true;
            }
        }
    }
}