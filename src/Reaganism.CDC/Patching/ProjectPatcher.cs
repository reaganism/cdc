using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using JetBrains.Annotations;

using Reaganism.CDC.Diffing;
using Reaganism.CDC.Utilities;
using Reaganism.FBI.Patching;

namespace Reaganism.CDC.Patching;

/// <summary>
///     Handles patching a project.
/// </summary>
[PublicAPI]
public static class ProjectPatcher
{
    internal sealed class PatcherState
    {
        private readonly ConcurrentBag<FilePatcher> patchResults = [];

        internal IEnumerable<FilePatcher> GetResults()
        {
            return patchResults;
        }

        public void AddPatchResult(FilePatcher patcher)
        {
            patchResults.Add(patcher);
        }

        public CompiledPatchState Compile()
        {
            return new CompiledPatchState(this);
        }
    }

    [PublicAPI]
    public readonly struct CompiledPatchState
    {
        [PublicAPI]
        public int Successes { [PublicAPI] get; }

        [PublicAPI]
        public int Failures { [PublicAPI] get; }

        [PublicAPI]
        public int Warnings { [PublicAPI] get; }

        [PublicAPI]
        public int Exacts { [PublicAPI] get; }

        [PublicAPI]
        public int Offsets { [PublicAPI] get; }

        [PublicAPI]
        public int Fuzzies { [PublicAPI] get; }

        internal CompiledPatchState(PatcherState state)
        {
            var patchResults = state.GetResults();
            var allResults   = patchResults.SelectMany(x => x.Results).ToList();
            {
                Successes = allResults.Count(x => x.Success);
                Failures  = allResults.Count(x => !x.Success);
                Warnings  = allResults.Count(x => x.OffsetWarning);
                Exacts    = allResults.Count(x => x.Mode == Patcher.Mode.Exact);
                Offsets   = allResults.Count(x => x.Mode == Patcher.Mode.Offset);
                Fuzzies   = allResults.Count(x => x.Mode == Patcher.Mode.Fuzzy);
            }
        }
    }

    /// <summary>
    ///     Patches the project as specified in the settings.
    /// </summary>
    /// <param name="settings">The project patch settings.</param>
    /// <returns>A report of the finished patch operation.</returns>
    public static CompiledPatchState Patch(PatcherSettings settings)
    {
        var state = new PatcherState();

        var removedFilesList = Path.Combine(settings.PatchesDirectory, DifferSettings.REMOVED_FILES_LIST_NAME);

        var noCopy   = File.Exists(removedFilesList) ? new HashSet<string>(File.ReadAllLines(removedFilesList)) : [];
        var newFiles = new HashSet<string>();

        var patchActions     = new List<Action>();
        var patchCopyActions = new List<Action>();
        var copyActions      = new List<Action>();

        foreach (var (filePath, relativePath) in PathUtil.EnumerateFiles(settings.ModifiedDirectory))
        {
            if (relativePath.EndsWith(".patch"))
            {
                // This is a patch.
                patchActions.Add(
                    () =>
                    {
                        var patcher = PatchFile(settings, filePath, state);
                        newFiles.Add(Path.GetFullPath(PathUtil.NormalizePath(patcher.ModifiedPath)));
                    }
                );
            }
            else if (relativePath != removedFilesList)
            {
                // This is copied file (whether it be new or a modified binary
                // file).  Excludes our metadata removed_files.list file.
                var destination = Path.GetFullPath(Path.Combine(settings.ModifiedDirectory, relativePath));

                patchCopyActions.Add(() => File.Copy(filePath, destination));
                newFiles.Add(destination);
            }
        }

        foreach (var (filePath, relativePath) in PathUtil.EnumerateSourceFiles(settings.OriginalDirectory))
        {
            if (noCopy.Contains(relativePath))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(settings.ModifiedDirectory, relativePath));
            copyActions.Add(() => File.Copy(filePath, destination));
            newFiles.Add(destination);
        }

        ParallelUtil.Execute(patchActions);
        ParallelUtil.Execute(patchCopyActions);
        ParallelUtil.Execute(copyActions);

        foreach (var (filePath, _) in PathUtil.EnumerateSourceFiles(settings.ModifiedDirectory))
        {
            if (!newFiles.Contains(Path.GetFullPath(filePath)))
            {
                File.Delete(filePath);
            }
        }

        PathUtil.DeleteEmptyDirectories(settings.ModifiedDirectory);

        return state.Compile();
    }

    private static FilePatcher PatchFile(PatcherSettings settings, string patchFilePath, PatcherState state)
    {
        var patcher = FilePatcher.FromPatchFile(patchFilePath);
        state.AddPatchResult(patcher);
        {
            patcher.Patch(settings.Mode);
            PathUtil.CreateParentDirectory(patcher.ModifiedPath);
            patcher.Save();
        }

        return patcher;
    }
}