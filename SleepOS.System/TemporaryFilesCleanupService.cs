namespace SleepOS.WindowsSystem;

public sealed record CleanupSummary(long Bytes, int Files, int SkippedItems);

public interface ITemporaryFilesCleanupService
{
    CleanupSummary Scan();
    CleanupSummary Clean();
}

public sealed class TemporaryFilesCleanupService : ITemporaryFilesCleanupService
{
    private static readonly TimeSpan MinimumAge = TimeSpan.FromDays(30);
    private readonly string _temporaryDirectory;

    public TemporaryFilesCleanupService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"))
    {
    }

    public TemporaryFilesCleanupService(string temporaryDirectory)
    {
        _temporaryDirectory = Path.GetFullPath(temporaryDirectory);
    }

    public CleanupSummary Scan()
    {
        var (files, skippedItems) = FindCandidates();
        return new CleanupSummary(files.Sum(file => file.Bytes), files.Count, skippedItems);
    }

    public CleanupSummary Clean()
    {
        var (files, skippedItems) = FindCandidates();
        long deletedBytes = 0;
        var deletedFiles = 0;

        foreach (var file in files)
        {
            try
            {
                if (!File.Exists(file.Path))
                {
                    skippedItems++;
                    continue;
                }

                File.Delete(file.Path);
                deletedBytes += file.Bytes;
                deletedFiles++;
            }
            catch (IOException)
            {
                skippedItems++;
            }
            catch (UnauthorizedAccessException)
            {
                skippedItems++;
            }
        }

        return new CleanupSummary(deletedBytes, deletedFiles, skippedItems);
    }

    private (List<CandidateFile> Files, int SkippedItems) FindCandidates()
    {
        var files = new List<CandidateFile>();
        var skippedItems = 0;
        if (!Directory.Exists(_temporaryDirectory))
        {
            return (files, skippedItems);
        }

        try
        {
            if ((File.GetAttributes(_temporaryDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                return (files, 1);
            }
        }
        catch (IOException)
        {
            return (files, 1);
        }
        catch (UnauthorizedAccessException)
        {
            return (files, 1);
        }

        var cutoff = DateTime.UtcNow - MinimumAge;
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(_temporaryDirectory);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(currentDirectory);
            }
            catch (IOException)
            {
                skippedItems++;
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                skippedItems++;
                continue;
            }

            foreach (var entry in entries)
            {
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pendingDirectories.Push(entry);
                        continue;
                    }

                    var fileInfo = new FileInfo(entry);
                    if (fileInfo.LastWriteTimeUtc < cutoff)
                    {
                        files.Add(new CandidateFile(entry, fileInfo.Length));
                    }
                }
                catch (IOException)
                {
                    skippedItems++;
                }
                catch (UnauthorizedAccessException)
                {
                    skippedItems++;
                }
            }
        }

        return (files, skippedItems);
    }

    private sealed record CandidateFile(string Path, long Bytes);
}
