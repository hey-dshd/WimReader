using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WimReader
{
    internal class Program
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    PrintUsage();
                    return 1;
                }

                if (args[0] == "--list")
                {
                    return HandleListOperation(args);
                }
                else if (args[0] == "--extract")
                {
                    return HandleExtractOperation(args);
                }
                else if (args[0] == "--read")
                {
                    return HandleReadOperation(args);
                }
                else
                {
                    Console.WriteLine($"[!] Unknown operation: {args[0]}");
                    PrintUsage();
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[!] {ex.Message}");
                return 1;
            }
        }

        static int HandleListOperation(string[] args)
        {
            string wimPath = null;
            string directoryPath = null;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--image" && i + 1 < args.Length)
                {
                    wimPath = args[i + 1];
                    i++;
                }
                else if (args[i] == "--path" && i + 1 < args.Length)
                {
                    directoryPath = args[i + 1];
                    i++;
                }
                else if (wimPath == null)
                {
                    wimPath = args[i];
                }
            }

            if (string.IsNullOrEmpty(wimPath))
            {
                Console.WriteLine("[!] --list requires WIM file path (use --image <path>)");
                PrintUsage();
                return 1;
            }

            using (var wimService = new WimService())
            {
                wimService.OpenWim(wimPath);
                
                int imageCount = wimService.GetImageCount();
                if (imageCount == 0)
                {
                    Console.WriteLine("[!] WIM file contains no images.");
                    return 1;
                }

                string displayPath = string.IsNullOrEmpty(directoryPath) ? "root" : directoryPath;
                
                var mergedFiles = new Dictionary<string, WimFileInfo>(StringComparer.OrdinalIgnoreCase);
                var fileImageMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

                for (int imageIndex = 1; imageIndex <= imageCount; imageIndex++)
                {
                    var files = wimService.ListFiles(imageIndex, directoryPath);
                    
                    foreach (var file in files)
                    {
                        if (!mergedFiles.ContainsKey(file.Path))
                        {
                            mergedFiles[file.Path] = file;
                            fileImageMap[file.Path] = new List<int>();
                        }
                        fileImageMap[file.Path].Add(imageIndex);
                    }
                }

                if (imageCount > 1)
                {
                    Console.WriteLine($"[*] WIM file contains {imageCount} image(s). Showing merged view.");
                }
                Console.WriteLine();

                if (mergedFiles.Count > 0)
                {
                    var sortedFiles = mergedFiles.Values.OrderBy(f =>
                    {
                        bool isDir = f.Attributes != null && f.Attributes.Contains("D");
                        return isDir ? 0 : 1;
                    }).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();

                    Console.WriteLine($"{"Path",-60} {"Size",15} {"Modified",20} {"Attributes",10} {"Images",10}");
                    Console.WriteLine(new string('-', 115));

                    foreach (var file in sortedFiles)
                    {
                        string size = file.Size.ToString("N0");
                        string modified = file.LastModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A";
                        var imageList = fileImageMap[file.Path].OrderBy(i => i).ToList();
                        string images = imageList.Count == imageCount ? "all" : $"[{string.Join(",", imageList)}]";
                        
                        Console.WriteLine($"{file.Path,-60} {size,15} {modified,20} {file.Attributes ?? "",10} {images,10}");
                    }

                    Console.WriteLine();
                    Console.WriteLine($"[*] Total unique files: {mergedFiles.Count}");
                    
                    if (imageCount > 1)
                    {
                        var allPaths = mergedFiles.Keys.ToList();
                        var commonPaths = allPaths.Where(path => fileImageMap[path].Count == imageCount).ToList();
                        var uniquePaths = allPaths.Except(commonPaths).ToList();
                        
                        Console.WriteLine($"[*] Files present in all {imageCount} images: {commonPaths.Count}");
                        Console.WriteLine($"[*] Files unique to specific images: {uniquePaths.Count}");
                    }
                }
                else
                {
                    Console.WriteLine("[*] No files found");
                }
            }

            return 0;
        }

        static int HandleExtractOperation(string[] args)
        {
            string wimPath = null;
            string sourcePath = null;
            string destinationPath = null;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--image" && i + 1 < args.Length)
                {
                    wimPath = args[i + 1];
                    i++;
                }
                else if (sourcePath == null)
                {
                    sourcePath = args[i];
                }
                else if (destinationPath == null)
                {
                    destinationPath = args[i];
                }
            }

            if (string.IsNullOrEmpty(wimPath))
            {
                Console.WriteLine("[!] --extract requires WIM file path (use --image <path>)");
                PrintUsage();
                return 1;
            }

            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
            {
                Console.WriteLine("[!] --extract requires source path and destination path");
                PrintUsage();
                return 1;
            }

            using (var wimService = new WimService())
            {
                wimService.OpenWim(wimPath);
                
                int imageCount = wimService.GetImageCount();
                if (imageCount == 0)
                {
                    Console.WriteLine("[!] WIM file contains no images.");
                    return 1;
                }

                string normalizedSourcePath = NormalizeFilePath(sourcePath);
                
                int foundImageIndex = -1;
                for (int i = 1; i <= imageCount; i++)
                {
                    try
                    {
                        wimService.ReadFileContent(i, normalizedSourcePath);
                        foundImageIndex = i;
                        break;
                    }
                    catch (FileNotFoundException)
                    {
                        continue;
                    }
                }

                if (foundImageIndex == -1)
                {
                    Console.WriteLine($"[!] File '{sourcePath}' not found in any image of the WIM file.");
                    if (imageCount > 1)
                    {
                        Console.WriteLine($"[*] Searched all {imageCount} image(s) in the WIM file.");
                    }
                    return 1;
                }

                if (imageCount > 1)
                {
                    Console.WriteLine($"[*] Found file in image {foundImageIndex} of {imageCount}.");
                }

                wimService.ExtractFile(foundImageIndex, normalizedSourcePath, destinationPath);
                Console.WriteLine($"[*] Extracted '{sourcePath}' to '{destinationPath}'");
            }

            return 0;
        }

        static string NormalizeFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string normalized = path.Replace('/', '\\').Trim();
            
            while (normalized.StartsWith("\\"))
            {
                normalized = normalized.Substring(1);
            }
            
            if (string.IsNullOrEmpty(normalized))
                return path;
            
            normalized = "\\" + normalized.TrimEnd('\\');
            
            return normalized;
        }

        static bool IsBinaryFile(byte[] content)
        {
            if (content == null || content.Length == 0)
                return false;

            int nullBytes = 0;
            int nonPrintable = 0;
            int sampleSize = Math.Min(content.Length, 8192);

            for (int i = 0; i < sampleSize; i++)
            {
                byte b = content[i];
                if (b == 0)
                {
                    nullBytes++;
                }
                else if (b < 32 && b != 9 && b != 10 && b != 13)
                {
                    nonPrintable++;
                }
            }

            if (nullBytes > 0)
                return true;

            double nonPrintableRatio = (double)nonPrintable / sampleSize;
            return nonPrintableRatio > 0.3;
        }

        static int HandleReadOperation(string[] args)
        {
            string wimPath = null;
            string sourcePath = null;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--image" && i + 1 < args.Length)
                {
                    wimPath = args[i + 1];
                    i++;
                }
                else if (sourcePath == null)
                {
                    sourcePath = args[i];
                }
            }

            if (string.IsNullOrEmpty(wimPath))
            {
                Console.WriteLine("[!] --read requires WIM file path (use --image <path>)");
                PrintUsage();
                return 1;
            }

            if (string.IsNullOrEmpty(sourcePath))
            {
                Console.WriteLine("[!] --read requires source file path");
                PrintUsage();
                return 1;
            }

            using (var wimService = new WimService())
            {
                wimService.OpenWim(wimPath);
                
                int imageCount = wimService.GetImageCount();
                if (imageCount == 0)
                {
                    Console.WriteLine("[!] WIM file contains no images.");
                    return 1;
                }

                string normalizedSourcePath = NormalizeFilePath(sourcePath);
                
                int foundImageIndex = -1;
                byte[] fileContent = null;

                for (int i = 1; i <= imageCount; i++)
                {
                    try
                    {
                        fileContent = wimService.ReadFileContent(i, normalizedSourcePath);
                        foundImageIndex = i;
                        break;
                    }
                    catch (FileNotFoundException)
                    {
                        continue;
                    }
                }

                if (foundImageIndex == -1)
                {
                    Console.WriteLine($"[!] File '{sourcePath}' not found in any image of the WIM file.");
                    if (imageCount > 1)
                    {
                        Console.WriteLine($"[*] Searched all {imageCount} image(s) in the WIM file.");
                    }
                    return 1;
                }

                if (imageCount > 1)
                {
                    Console.Error.WriteLine($"[*] Found file in image {foundImageIndex} of {imageCount}.");
                }

                if (IsBinaryFile(fileContent))
                {
                    string base64 = Convert.ToBase64String(fileContent);
                    Console.WriteLine($"[!] Base64: {base64}");
                }
                else
                {
                    string text = System.Text.Encoding.UTF8.GetString(fileContent);
                    Console.Write(text);
                }
            }

            return 0;
        }

        static void PrintUsage()
        {
            Console.WriteLine("WimReader - List and extract files from WIM archives");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wimreader.exe --list --image <wim-path> [--path <directory-path>]");
            Console.WriteLine("  wimreader.exe --extract --image <wim-path> <source-path> <dest-path>");
            Console.WriteLine("  wimreader.exe --read --image <wim-path> <source-path>");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  wimreader.exe --list --image \"\\\\server\\share\\image.wim\"");
            Console.WriteLine("  wimreader.exe --list --image \"C:\\local\\image.wim\" --path \"Windows\\System32\"");
            Console.WriteLine("  wimreader.exe --list --image \"\\\\server\\share\\image.wim\" --path \"Program Files\"");
            Console.WriteLine("  wimreader.exe --extract --image \"\\\\server\\share\\image.wim\" \"Windows\\System32\\notepad.exe\" \"C:\\output\\notepad.exe\"");
            Console.WriteLine("  wimreader.exe --read --image \"\\\\server\\share\\image.wim\" \"Windows\\System32\\drivers\\etc\\hosts\"");
            Console.WriteLine("  wimreader.exe --read --image \"C:\\image.wim\" \"Windows\\System32\\notepad.exe\"");
        }
    }
}
