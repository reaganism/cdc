using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using JetBrains.Annotations;

using Reaganism.CDC.Utilities;
using Reaganism.FBI.Diffing;

namespace Reaganism.CDC.Diffing;

/// <summary>
///     Handles diffing a project.
/// </summary>
[PublicAPI]
public static class ProjectDiffer
{
    /// <summary>
    ///     Diffs the project as specified in the settings.
    /// </summary>
    /// <param name="settings">The project diff settings.</param>
    [PublicAPI]
    public static void Diff(DifferSettings settings)
    {
        var actions = new List<Action>();

        // Get files that exist in the modified directory and diff them against
        // files in the original directory.
        foreach (var (filePath, relativePath) in PathUtil.EnumerateSourceFiles(settings.ModifiedDirectory))
        {
            if (!File.Exists(Path.Combine(settings.OriginalDirectory, relativePath)))
            {
                // If the file doesn't exist in the original directory, it's
                // new; we can directly copy it to the output directory.
                actions.Add(
                    () => File.Copy(filePath, Path.Combine(settings.PatchesDirectory, relativePath))
                );
            }
            else
            {
                // If the file does exist then we need to perform an actual diff
                // on it and generate any applicable patch files.
                settings.GetFileDiffType(relativePath, out var fileDiffType, out var ignore);

                if (ignore)
                {
                    continue;
                }

                switch (fileDiffType)
                {
                    case FileDiffType.TextualDiff:
                        actions.Add(
                            () => DiffFile(settings, relativePath)
                        );
                        break;

                    case FileDiffType.BinaryDiff:
                        actions.Add(
                            () => DiffBinaryFile(settings, relativePath)
                        );
                        break;

                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        ParallelUtil.Execute(actions);

        // Clean up any patch files that are no longer needed.  That is, any
        // patch files that was generated for a file that no longer exists in
        // the current version of the modified directory.  This happens if the
        // modified directory once contained modifications to a file that has
        // since been reverted to its original version or has been deleted
        // outright.
        foreach (var (filePath, relativePath) in PathUtil.EnumerateFiles(settings.PatchesDirectory))
        {
            // Relative path without the ".patch" extension.
            var targetPath = relativePath.EndsWith(".patch") ? relativePath[..^6] : relativePath;

            if (!File.Exists(Path.Combine(settings.ModifiedDirectory, targetPath)))
            {
                File.Delete(filePath);
            }
        }

        PathUtil.DeleteEmptyDirectories(settings.ModifiedDirectory);

        // Determine what files have been deleted in the modified directory
        // (ones that existed in the source directory but do not exist in the
        // modified directory) and write them to a file.
        var removedFiles = PathUtil.EnumerateSourceFiles(settings.OriginalDirectory)
                                   .Where(x => !File.Exists(Path.Combine(settings.ModifiedDirectory, x.relativePath)))
                                   .Select(x => x.relativePath)
                                   .ToArray();

        var removedFileList = Path.Combine(settings.PatchesDirectory, DifferSettings.REMOVED_FILES_LIST_NAME);
        if (removedFiles.Length > 0)
        {
            File.WriteAllLines(removedFileList, removedFiles);
        }
        else
        {
            File.Delete(removedFileList);
        }
    }

    private static void DiffFile(DifferSettings settings, string relativePath)
    {
        var patchFile = Differ.DiffFiles(
            new LineMatchedDiffer(),
            Path.Combine(settings.OriginalDirectory, relativePath),
            Path.Combine(settings.ModifiedDirectory, relativePath)
        );

        var patchPath = Path.Combine(settings.PatchesDirectory, relativePath + ".patch");
        if (patchFile.Patches.Count != 0)
        {
            PathUtil.CreateParentDirectory(patchPath);
            File.WriteAllText(patchPath, patchFile.ToString(true));
        }
        else
        {
            File.Delete(patchPath);
        }
    }

    // Only checks whether files are the same and copies the binary file to the
    // patches directory if they aren't.  Does not generate any patch files such
    // as xdelta.
    private static void DiffBinaryFile(DifferSettings settings, string relativePath)
    {
        var originalFilePath = Path.Combine(settings.OriginalDirectory, relativePath);
        var modifiedFilePath = Path.Combine(settings.ModifiedDirectory, relativePath);

        var originalFileSize = new FileInfo(originalFilePath).Length;
        var modifiedFileSize = new FileInfo(modifiedFilePath).Length;

        // Quick check: we know they files aren't the same if their sizes are
        // different.
        if (originalFileSize != modifiedFileSize)
        {
            File.Copy(modifiedFilePath, Path.Combine(settings.PatchesDirectory, relativePath));
        }

        // Now we need to actually check whether their bytes are the same.
        var originalFileBytes = File.ReadAllBytes(originalFilePath);
        var modifiedFileBytes = File.ReadAllBytes(modifiedFilePath);

        if (!originalFileBytes.SequenceEqual(modifiedFileBytes))
        {
            File.Copy(modifiedFilePath, Path.Combine(settings.PatchesDirectory, relativePath));
        }
    }
}