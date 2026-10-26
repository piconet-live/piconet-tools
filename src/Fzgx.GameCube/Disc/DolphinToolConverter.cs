using System.Diagnostics;

namespace Fzgx.GameCube.Disc;

/// <summary>
/// Converts compressed disc images (RVZ, WIA, GCZ) to a plain ISO with
/// Dolphin's command-line tool, so <see cref="GcmDisc"/> can read them.
/// </summary>
/// <remarks>
/// Under WSL the Windows build of DolphinTool (<c>DolphinTool.exe</c>) is
/// common; its file arguments must be Windows paths, so a path mapper (for
/// example <c>wslpath -w</c>) converts them.
/// </remarks>
public sealed class DolphinToolConverter
{
    private static readonly string[] _convertible = [".rvz", ".wia", ".gcz"];

    private readonly string _toolPath;
    private readonly Func<string, string> _mapPath;

    /// <summary>Uses the DolphinTool at <paramref name="toolPath"/>.</summary>
    /// <param name="toolPath">DolphinTool or DolphinTool.exe.</param>
    /// <param name="mapPath">Maps file arguments for the tool; by default, WSL paths for a .exe under WSL.</param>
    public DolphinToolConverter(string toolPath, Func<string, string>? mapPath = null)
    {
        ArgumentNullException.ThrowIfNull(toolPath);
        _toolPath = toolPath;
        _mapPath = mapPath ?? DefaultPathMapper(toolPath);
    }

    /// <summary>True for image formats that must be converted before reading.</summary>
    public static bool NeedsConversion(string imagePath) =>
        _convertible.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase);

    /// <summary>The process DolphinTool is run as, for inspection and tests.</summary>
    public ProcessStartInfo CreateStartInfo(string input, string output)
    {
        var info = new ProcessStartInfo(_toolPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "convert", "--format=iso", $"--input={_mapPath(input)}", $"--output={_mapPath(output)}" })
        {
            info.ArgumentList.Add(argument);
        }
        return info;
    }

    /// <summary>
    /// Converts <paramref name="input"/> to an ISO at <paramref name="output"/>.
    /// Writes to a temporary name first, so an interrupted conversion never
    /// leaves a truncated ISO that looks finished.
    /// </summary>
    public async Task ConvertAsync(string input, string output, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(input))
        {
            throw new FileNotFoundException($"No disc image at '{input}'.", input);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var partial = output + ".converting";
        File.Delete(partial);

        using var process = Process.Start(CreateStartInfo(input, partial))
            ?? throw new InvalidOperationException($"Could not start '{_toolPath}'.");
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        var messages = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0 || !File.Exists(partial))
        {
            File.Delete(partial);
            var detail = $"{await errors.ConfigureAwait(false)}{await messages.ConfigureAwait(false)}".Trim();
            throw new InvalidOperationException($"DolphinTool failed (exit code {process.ExitCode}): {detail}");
        }
        File.Move(partial, output, overwrite: true);
    }

    private static Func<string, string> DefaultPathMapper(string toolPath)
    {
        var windowsTool = toolPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        return windowsTool && OperatingSystem.IsLinux() ? ToWindowsPath : Path.GetFullPath;
    }

    /// <summary>Converts a WSL path to a Windows path with <c>wslpath -w</c>.</summary>
    private static string ToWindowsPath(string path)
    {
        var full = Path.GetFullPath(path);
        var info = new ProcessStartInfo("wslpath") { RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add("-w");
        info.ArgumentList.Add(full);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not run wslpath.");
        var mapped = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 && mapped.Length > 0
            ? mapped
            : throw new InvalidOperationException($"wslpath could not map '{full}'.");
    }
}
