using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DomainLinksDesktop;

public sealed class DomainItem
{
    public string DomainId { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
    public string DomainType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public ObservableCollection<CollectionItem> Collections { get; } = new();
}

public sealed class CollectionItem
{
    public string CollectionId { get; set; } = string.Empty;
    public string CollectionCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
    public string DomainDisplayName { get; set; } = string.Empty;
    public bool IsIncluded { get; set; }
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
    public bool IsEditing { get; set; }
    public ObservableCollection<ChatThreadItem> Threads { get; } = new();
}

public sealed class RetrievalProfileItem
{
    public string RetrievalProfileId { get; set; } = string.Empty;
    public string ProfileCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RetrievalMode { get; set; } = string.Empty;
}

public sealed class DocumentListItem
{
    public string DocumentId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int ContentUnitCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsExpanded { get; set; }
    public ObservableCollection<ContentUnitListItem> Chunks { get; } = new();
}

public sealed class ContentUnitListItem
{
    public string ContentUnitId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int UnitOrdinal { get; set; }
    public string UnitType { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
}

public sealed class AskResponse
{
    public string Answer { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<AskSourceItem> Sources { get; set; } = [];
    public AskResponseMetrics? Metrics { get; set; }
}

public sealed class AskSourceItem
{
    public string CollectionCode { get; set; } = string.Empty;
    public string CollectionDisplayName { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string ContentUnitId { get; set; } = string.Empty;
}

public sealed class BackendConfigResponse
{
    [JsonPropertyName("default_llm_provider")]
    public string DefaultLlmProvider { get; set; } = string.Empty;

    [JsonPropertyName("ollama_chat_model")]
    public string OllamaChatModel { get; set; } = string.Empty;
}

public sealed class OllamaTagsResponse
{
    public List<OllamaModelTagItem> Models { get; set; } = [];
}

public sealed class OllamaModelTagItem
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
}

public sealed class ModelOptionItem
{
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}

public sealed class ChatThreadItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private CollectionItem? _parentCollection;
    private bool _isSelected;
    private bool _isEditing;

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public CollectionItem? ParentCollection
    {
        get => _parentCollection;
        set => SetField(ref _parentCollection, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetField(ref _isEditing, value);
    }

    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public sealed class ChatMessageItem
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SupplementalText { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public ChatResponseStats? Stats { get; set; }
}

public sealed class ChatRootFileState
{
    public int SchemaVersion { get; set; } = 1;
    public string RootCollectionCode { get; set; } = string.Empty;
    public string RootDisplayName { get; set; } = string.Empty;
    public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<SavedChatThreadState> Threads { get; set; } = [];
}

public sealed class SavedChatThreadState
{
    public string Title { get; set; } = string.Empty;
    public List<SavedChatMessageState> Messages { get; set; } = [];
}

public sealed class SavedChatMessageState
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SupplementalText { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public ChatResponseStats? Stats { get; set; }
}

public sealed class AskStreamEvent
{
    public string Type { get; set; } = string.Empty;
    public string Delta { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public List<AskSourceItem> Sources { get; set; } = [];
    public AskResponseMetrics? Metrics { get; set; }
}

public sealed class AskResponseMetrics
{
    public string ModelName { get; set; } = string.Empty;
    public int TotalTokens { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public double DurationSeconds { get; set; }
    public double TokensPerSecond { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
}

public sealed class ChatResponseStats
{
    public string ModelName { get; set; } = string.Empty;
    public int TotalTokens { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public double DurationSeconds { get; set; }
    public double TokensPerSecond { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
}

public sealed class ChatBackupUserIdentity
{
    public string WindowsUserName { get; set; } = string.Empty;
    public string? WindowsSid { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string IdentityKeyKind { get; set; } = string.Empty;
    public string IdentityKeyValue { get; set; } = string.Empty;
}

public sealed class ChatBackupAvailabilityResponse
{
    public bool HasBackups { get; set; }
    public int FileCount { get; set; }
}

public sealed class ChatBackupRestoreResponse
{
    public List<ChatBackupFilePayload> Files { get; set; } = [];
}

public sealed class ChatBackupFilePayload
{
    public string RootCollectionCode { get; set; } = string.Empty;
    public string RootDisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string PayloadBase64 { get; set; } = string.Empty;
    public string ContentHashBase64 { get; set; } = string.Empty;
    public string CompressionType { get; set; } = string.Empty;
    public string EncryptionType { get; set; } = string.Empty;
    public int KeyVersion { get; set; }
    public DateTimeOffset ClientModifiedUtc { get; set; }
}

public sealed class LocalChatFileSnapshot
{
    public string RootCollectionCode { get; set; } = string.Empty;
    public string RootDisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string JsonContent { get; set; } = string.Empty;
    public DateTimeOffset ClientModifiedUtc { get; set; }
}
