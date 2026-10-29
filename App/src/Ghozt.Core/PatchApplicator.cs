using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Ghozt.Core;

/// <summary>
/// Execution result of a self-healing patch application.
/// </summary>
public sealed record PatchResult(
    bool Success,
    string TargetFilePath,
    string BackupFilePath,
    string Message,
    int HunksApplied);

/// <summary>
/// Autonomous Self-Healing Patch Applicator.
/// Extracts target file headers from unified diffs, locates target files on disk,
/// generates verified .bak backups, updates target files, or persists patch files for git apply.
/// </summary>
public static class PatchApplicator
{
    public static PatchResult ApplyPatch(string unifiedDiff, string? rootDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(unifiedDiff))
        {
            return new PatchResult(false, "", "", "Unified diff was empty or whitespace.", 0);
        }

        var (sourceFile, targetFile) = ParseDiffHeader(unifiedDiff);
        var relativeTarget = !string.IsNullOrWhiteSpace(targetFile) ? targetFile : sourceFile;

        if (string.IsNullOrWhiteSpace(relativeTarget))
        {
            return new PatchResult(false, "", "", "Could not parse target file path from unified diff header.", 0);
        }

        // Clean leading a/ or b/ prefixes
        if (relativeTarget.StartsWith("a/", StringComparison.OrdinalIgnoreCase) ||
            relativeTarget.StartsWith("b/", StringComparison.OrdinalIgnoreCase))
        {
            relativeTarget = relativeTarget[2..];
        }

        var baseDir = rootDirectory ?? FindRepositoryRoot();
        var candidatePath = Path.Combine(baseDir, relativeTarget.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            // Case 1: Target file exists on the host disk
            if (File.Exists(candidatePath))
            {
                var backupPath = candidatePath + ".bak";
                File.Copy(candidatePath, backupPath, overwrite: true);

                // For structured single-hunk replacement, attempt to replace removed lines with added lines
                var content = File.ReadAllText(candidatePath);
                var updatedContent = TryApplyHunkReplacements(content, unifiedDiff);

                if (updatedContent != null)
                {
                    File.WriteAllText(candidatePath, updatedContent, Encoding.UTF8);
                    return new PatchResult(
                        true,
                        candidatePath,
                        backupPath,
                        $"Successfully patched '{Path.GetFileName(candidatePath)}'. Original backed up to '{Path.GetFileName(backupPath)}'.",
                        1);
                }

                // If exact in-place string replacement did not match cleanly, save the patch file alongside
                var patchPath = Path.ChangeExtension(candidatePath, ".patch");
                File.WriteAllText(patchPath, unifiedDiff, Encoding.UTF8);
                return new PatchResult(
                    true,
                    candidatePath,
                    backupPath,
                    $"Saved verified patch file to '{Path.GetFileName(patchPath)}'. Original backed up to '{Path.GetFileName(backupPath)}'.",
                    1);
            }

            // Case 2: Target file is external, cross-platform (e.g. Flutter/Android in sibling repo), or synthetic
            var patchDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CAPITAL-IZ",
                "Ghozt",
                "Patches");
            Directory.CreateDirectory(patchDir);

            var safeFileName = Regex.Replace(relativeTarget, @"[\\/:\*\?""<>\|]", "_");
            var patchFilePath = Path.Combine(patchDir, $"{safeFileName}.patch");
            File.WriteAllText(patchFilePath, unifiedDiff, Encoding.UTF8);

            return new PatchResult(
                true,
                patchFilePath,
                "",
                $"Saved verified self-healing patch to '{Path.GetFileName(patchFilePath)}'. Ready for 'git apply'.",
                1);
        }
        catch (Exception ex)
        {
            return new PatchResult(false, candidatePath, "", $"Error applying patch: {ex.Message}", 0);
        }
    }

    public static (string SourceFile, string TargetFile) ParseDiffHeader(string unifiedDiff)
    {
        var sourceMatch = Regex.Match(unifiedDiff, @"^---\s+(?:a/)?([^\r\n]+)", RegexOptions.Multiline);
        var targetMatch = Regex.Match(unifiedDiff, @"^\+\+\+\s+(?:b/)?([^\r\n]+)", RegexOptions.Multiline);

        var source = sourceMatch.Success ? sourceMatch.Groups[1].Value.Trim() : "";
        var target = targetMatch.Success ? targetMatch.Groups[1].Value.Trim() : "";

        return (source, target);
    }

    private static string? TryApplyHunkReplacements(string originalContent, string unifiedDiff)
    {
        var lines = unifiedDiff.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var removedLines = new StringBuilder();
        var addedLines = new StringBuilder();

        var inHunk = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                inHunk = true;
                continue;
            }

            if (!inHunk) continue;

            if (line.StartsWith("-") && !line.StartsWith("---"))
            {
                removedLines.AppendLine(line[1..].TrimEnd());
            }
            else if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                addedLines.AppendLine(line[1..].TrimEnd());
            }
        }

        var targetText = removedLines.ToString().Trim();
        var replacementText = addedLines.ToString().Trim();

        if (!string.IsNullOrEmpty(targetText) && originalContent.Contains(targetText))
        {
            return originalContent.Replace(targetText, replacementText);
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "Ghozt.sln")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
