using SunshineAlley.Core;

namespace SunshineAlley.Platform.Storage;

public static class DirectoryWriteAccess
{
    public static async Task<DirectoryValidationResult> ValidateAsync(
        string directory,
        string description,
        CancellationToken cancellationToken = default)
    {
        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return DirectoryValidationResult.Invalid(
                directory,
                $"The {description} path is invalid: {exception.Message}");
        }

        if (!Directory.Exists(normalized))
        {
            return DirectoryValidationResult.Invalid(
                normalized,
                $"The {description} does not exist.");
        }

        if (PathSecurity.ContainsReparsePoint(normalized))
        {
            return DirectoryValidationResult.Invalid(
                normalized,
                $"The {description} cannot traverse a symbolic link because the launcher modifies files there.");
        }

        string probe = Path.Combine(
            normalized,
            $".sunshine-alley-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.Asynchronous))
            {
                await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Delete(probe);
            return DirectoryValidationResult.Valid(normalized);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return DirectoryValidationResult.Invalid(
                normalized,
                $"The launcher cannot write to the {description}. Choose a user-writable location; sudo is not used. {exception.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
                // Preserve the actionable validation result. The uniquely named
                // one-byte probe can be removed manually if the filesystem held it.
            }
        }
    }
}
