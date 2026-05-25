using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DomainLinksDesktop;

internal sealed class LocalChatStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _chatDirectoryPath;

    public LocalChatStore()
    {
        _chatDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DomainLinks",
            "Chats"
        );
    }

    public bool HasLocalChatFiles()
    {
        return Directory.Exists(_chatDirectoryPath)
            && Directory.EnumerateFiles(_chatDirectoryPath, "*.json", SearchOption.TopDirectoryOnly).Any();
    }

    public IReadOnlyList<ChatRootFileState> LoadAll()
    {
        if (!Directory.Exists(_chatDirectoryPath))
        {
            return [];
        }

        var items = new List<ChatRootFileState>();
        foreach (var path in Directory.EnumerateFiles(_chatDirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(path);
                var state = JsonSerializer.Deserialize<ChatRootFileState>(json, JsonOptions);
                if (state is null || string.IsNullOrWhiteSpace(state.RootCollectionCode))
                {
                    continue;
                }

                items.Add(state);
            }
            catch
            {
                // Ignore malformed local chat files so one bad file does not block startup.
            }
        }

        return items;
    }

    public LocalChatFileSnapshot SaveCollection(CollectionItem collection)
    {
        Directory.CreateDirectory(_chatDirectoryPath);

        var state = BuildState(collection);
        state.LastModifiedUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(state, JsonOptions);
        var fileName = BuildFileName(collection.DisplayName, collection.CollectionCode);
        var fullPath = Path.Combine(_chatDirectoryPath, fileName);

        File.WriteAllText(fullPath, json);
        DeleteAlternateFiles(collection.CollectionCode, fullPath);

        return new LocalChatFileSnapshot
        {
            RootCollectionCode = state.RootCollectionCode,
            RootDisplayName = state.RootDisplayName,
            FileName = fileName,
            JsonContent = json,
            ClientModifiedUtc = state.LastModifiedUtc,
        };
    }

    public void DeleteCollection(string collectionCode)
    {
        if (!Directory.Exists(_chatDirectoryPath))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(_chatDirectoryPath, $"*--{collectionCode}.json", SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }
    }

    public void RestoreFiles(IEnumerable<LocalChatFileSnapshot> files)
    {
        Directory.CreateDirectory(_chatDirectoryPath);

        foreach (var file in files)
        {
            var fullPath = Path.Combine(_chatDirectoryPath, file.FileName);
            File.WriteAllText(fullPath, file.JsonContent);
            DeleteAlternateFiles(file.RootCollectionCode, fullPath);
        }
    }

    private ChatRootFileState BuildState(CollectionItem collection)
    {
        return new ChatRootFileState
        {
            RootCollectionCode = collection.CollectionCode,
            RootDisplayName = collection.DisplayName,
            LastModifiedUtc = DateTimeOffset.UtcNow,
            Threads = collection.Threads.Select(thread => new SavedChatThreadState
            {
                Title = thread.Title,
                Messages = thread.Messages.Select(message => new SavedChatMessageState
                {
                    Role = message.Role,
                    Content = message.Content,
                    SupplementalText = message.SupplementalText,
                    CreatedAtUtc = message.CreatedAtUtc,
                    Stats = message.Stats,
                }).ToList(),
            }).ToList(),
        };
    }

    private void DeleteAlternateFiles(string collectionCode, string keepFullPath)
    {
        foreach (var path in Directory.EnumerateFiles(_chatDirectoryPath, $"*--{collectionCode}.json", SearchOption.TopDirectoryOnly))
        {
            if (!string.Equals(path, keepFullPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
            }
        }
    }

    private static string BuildFileName(string displayName, string collectionCode)
    {
        var safeName = Regex.Replace(displayName.Trim(), @"[^\w\s-]+", string.Empty);
        safeName = Regex.Replace(safeName, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Root Chat";
        }

        safeName = safeName.Length > 80 ? safeName[..80].Trim() : safeName;
        return $"{safeName}--{collectionCode}.json";
    }
}
