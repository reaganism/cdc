using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using ICSharpCode.Decompiler.Metadata;

namespace Reaganism.CDC.Utilities;

internal static class PathUtil
{
    private static readonly string[] non_srs_directories = [".git", ".vs", ".idea", "bin", "obj"];

    public static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    public static string GetOutputPath(string path, MetadataFile metadataFile)
    {
        var fileName = default(string);

        if (path.EndsWith(".dll"))
        {
            // ReSharper disable once AccessToModifiedClosure - Captured and
            //                                                  used before any
            //                                                  mutations.
            var assemblyReference = metadataFile.AssemblyReferences.SingleOrDefault(x => path.EndsWith(x.Name + ".dll"));
            if (assemblyReference is not null)
            {
                path = Path.Combine(path[..(path.Length - assemblyReference.Name.Length - 5)], assemblyReference.Name + ".dll");
            }

            fileName = Path.GetFileName(path);
        }
        else
        {
            // TODO
            fileName = Path.GetFileName(path);
        }

        var rootNamespace = AssemblyUtil.GetAssemblyTitle(metadataFile);
        if (path.StartsWith(rootNamespace))
        {
            path = path[(rootNamespace.Length + 1)..];
        }

        var pathWithoutFileName = path[..^fileName.Length];
        pathWithoutFileName = pathWithoutFileName.Replace('\\', '/').Replace('.', '/');
        return pathWithoutFileName + '/' + fileName;
    }

    public static bool DeleteEmptyDirectories(string directory)
    {
        return !Directory.Exists(directory) || Recurse(directory);

        static bool Recurse(string directory)
        {
            var allEmpty = true;

            foreach (var subDirectory in Directory.EnumerateDirectories(directory))
            {
                allEmpty &= Recurse(subDirectory);
            }

            if (!allEmpty || Directory.EnumerateDirectories(directory).Any())
            {
                return false;
            }

            Directory.Delete(directory);
            return true;
        }
    }

#region File enumeration
    public static IEnumerable<(string fullPath, string relativePath)> EnumerateSourceFiles(string directory)
    {
        return EnumerateFiles(directory)
           .Where(x => !x.relativePath.Split('/', '\\').Any(non_srs_directories.Contains));
    }

    public static IEnumerable<(string fullPath, string relativePath)> EnumerateFiles(string directory)
    {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                        .Select(x => (fullPath: x, relativePath: GetRelativePath(directory, x)));
    }

    private static string GetRelativePath(string directory, string fullPath)
    {
        // Sanitization: remove or add directory separator character as needed.
        {
            if (directory[^1] == Path.DirectorySeparatorChar)
            {
                directory = directory[..^1];
            }

            if (fullPath[^1] != Path.DirectorySeparatorChar)
            {
                fullPath += Path.DirectorySeparatorChar;
            }
        }

        if (fullPath + Path.DirectorySeparatorChar == directory)
        {
            return string.Empty;
        }

        Debug.Assert(fullPath.StartsWith(directory));
        {
            return fullPath[directory.Length..];
        }
    }
#endregion

#region Directory creation
    public static DirectoryInfo CreateParentDirectory(string path)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(Path.GetDirectoryName(path)));
        return Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }
#endregion

#region File copying
    public static void Copy(string fromPath, string toPath)
    {
        CreateParentDirectory(toPath);
        File.Copy(fromPath, toPath, true);
    }
#endregion
}