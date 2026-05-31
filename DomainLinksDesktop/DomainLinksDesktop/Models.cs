using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DomainLinksDesktop;

public sealed class DomainItem : INotifyPropertyChanged
{
    private string _domainId = string.Empty;
    private string _domainParentId = string.Empty;
    private int? _domainTypeId;
    private int? _domainOrientationId;
    private int _displayOrder;
    private string _domainCode = string.Empty;
    private string _domainType = string.Empty;
    private string _domainOrientationCode = string.Empty;
    private string _domainOrientation = string.Empty;
    private string _displayName = string.Empty;
    private string _description = string.Empty;
    private string _status = string.Empty;
    private bool? _isIncluded;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isGroup;

    public string DomainId
    {
        get => _domainId;
        set => SetField(ref _domainId, value);
    }

    public string DomainParentId
    {
        get => _domainParentId;
        set => SetField(ref _domainParentId, value);
    }

    public int? DomainTypeId
    {
        get => _domainTypeId;
        set => SetField(ref _domainTypeId, value);
    }

    public int? DomainOrientationId
    {
        get => _domainOrientationId;
        set => SetField(ref _domainOrientationId, value);
    }

    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetField(ref _displayOrder, value);
    }

    public string DomainCode
    {
        get => _domainCode;
        set => SetField(ref _domainCode, value);
    }

    public string DomainType
    {
        get => _domainType;
        set => SetField(ref _domainType, value);
    }

    public string DomainOrientationCode
    {
        get => _domainOrientationCode;
        set => SetField(ref _domainOrientationCode, value);
    }

    public string DomainOrientation
    {
        get => _domainOrientation;
        set => SetField(ref _domainOrientation, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }
    public bool? IsIncluded
    {
        get => _isIncluded;
        set => SetField(ref _isIncluded, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsGroup
    {
        get => _isGroup;
        set => SetField(ref _isGroup, value);
    }

    [JsonIgnore]
    public DomainItem? ParentDomain { get; set; }

    [JsonIgnore]
    public DomainItem? SourceDomain { get; set; }

    public ObservableCollection<DomainItem> ChildDomains { get; } = new();
    public ObservableCollection<CollectionItem> Collections { get; } = new();
    public ObservableCollection<object> TreeChildren { get; } = new();

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

public sealed class DomainTypeItem
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DomainLevel { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class DomainOrientationItem
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public sealed class CollectionItem : INotifyPropertyChanged
{
    private bool _isIncluded;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isEditing;

    public string CollectionId { get; set; } = string.Empty;
    public string CollectionCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
    public string DomainDisplayName { get; set; } = string.Empty;
    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetField(ref _isIncluded, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
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

    [JsonIgnore]
    public DomainItem? ParentDomain { get; set; }

    public ObservableCollection<ChatThreadItem> Threads { get; } = new();

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
    public int EmbeddedContentUnitCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CollectionCode { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsExpanded { get; set; }
    public ObservableCollection<ContentUnitListItem> Chunks { get; } = new();

    [JsonIgnore]
    public string EmbedStatusDisplay =>
        ContentUnitCount <= 0
            ? "No chunks"
            : EmbeddedContentUnitCount >= ContentUnitCount
                ? "Embedded"
                : EmbeddedContentUnitCount > 0
                    ? $"{EmbeddedContentUnitCount}/{ContentUnitCount} embedded"
                    : "Ready";
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

public sealed class DomainAssistResponse
{
    public string Answer { get; set; } = string.Empty;
    public string SystemPromptLabel { get; set; } = string.Empty;
    public AskResponseMetrics? Metrics { get; set; }
}

public sealed class SuggestedChildDomain
{
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DomainType { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
}

public sealed class DomainChildSuggestionResponse
{
    public SuggestedChildDomain? Suggestion { get; set; }
    public string SqlPreview { get; set; } = string.Empty;
    public string SystemPromptLabel { get; set; } = string.Empty;
    public AskResponseMetrics? Metrics { get; set; }
}

public sealed class DomainChildExecutionResponse
{
    public DomainItem? CreatedDomain { get; set; }
    public string SqlPreview { get; set; } = string.Empty;
}

public sealed class PromptPreviewResponse
{
    public string Model { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
}

public sealed class DomainDeletePreviewResponse
{
    public string DomainCode { get; set; } = string.Empty;
    public int DomainCount { get; set; }
    public int CollectionCount { get; set; }
    public int DocumentCount { get; set; }
}

public sealed class CollectionDeletePreviewResponse
{
    public string CollectionCode { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
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
