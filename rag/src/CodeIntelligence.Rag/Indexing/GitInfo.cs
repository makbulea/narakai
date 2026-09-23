using System.Diagnostics;

namespace CodeIntelligence.Rag.Indexing;

/// <summary>Best-effort detection of the current git commit SHA for a repository root, used to pin search results to a code version (spec item 17).</summary>
public static class GitInfo
{
    public static async Task<string?> TryGetCurrentCommitAsync(string repositoryRootPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path.Combine(repositoryRootPath, ".git")))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = repositoryRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // git not installed / not runnable — indexing still proceeds without a commit SHA.
            return null;
        }
    }
}
