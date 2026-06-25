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
    private string _iconGlyph = string.Empty;
    private int _branchCollectionCount;
    private int _branchPolicyCount;
    private int _branchControlCount;
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

    public string IconGlyph
    {
        get => _iconGlyph;
        set => SetField(ref _iconGlyph, value);
    }

    public int BranchCollectionCount
    {
        get => _branchCollectionCount;
        set
        {
            if (SetField(ref _branchCollectionCount, value))
            {
                OnPropertyChanged(nameof(TreeMetaText));
                OnPropertyChanged(nameof(TreeMetaCollectionText));
            }
        }
    }

    public int BranchPolicyCount
    {
        get => _branchPolicyCount;
        set
        {
            if (SetField(ref _branchPolicyCount, value))
            {
                OnPropertyChanged(nameof(TreeMetaText));
                OnPropertyChanged(nameof(TreeMetaPolicyText));
            }
        }
    }

    public int BranchControlCount
    {
        get => _branchControlCount;
        set
        {
            if (SetField(ref _branchControlCount, value))
            {
                OnPropertyChanged(nameof(TreeMetaText));
                OnPropertyChanged(nameof(TreeMetaControlText));
            }
        }
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

    [JsonIgnore]
    public string TreeMetaText
    {
        get
        {
            var parts = new List<string>();
            if (BranchCollectionCount > 0)
            {
                parts.Add($"col:{BranchCollectionCount}");
            }

            if (BranchPolicyCount > 0)
            {
                parts.Add($"pol:{BranchPolicyCount}");
            }

            if (BranchControlCount > 0)
            {
                parts.Add($"ctl:{BranchControlCount}");
            }

            return parts.Count == 0 ? string.Empty : $"({string.Join("  ", parts)})";
        }
    }

    [JsonIgnore]
    public string TreeMetaCollectionText => BranchCollectionCount > 0 ? $"col:{BranchCollectionCount}" : string.Empty;

    [JsonIgnore]
    public string TreeMetaPolicyText => BranchPolicyCount > 0 ? $"pol:{BranchPolicyCount}" : string.Empty;

    [JsonIgnore]
    public string TreeMetaControlText => BranchControlCount > 0 ? $"ctl:{BranchControlCount}" : string.Empty;

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

public sealed class ControlTypeItem
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
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
    public string RetrievalMode { get; set; } = string.Empty;
    public string RetrievalWarning { get; set; } = string.Empty;
    public AskResponseMetrics? Metrics { get; set; }
}

public sealed class AskSourceItem
{
    public string CollectionCode { get; set; } = string.Empty;
    public string CollectionDisplayName { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string ContentUnitId { get; set; } = string.Empty;
    public int TokenCount { get; set; }
}

public sealed class ContextPreviewResponse
{
    public string RetrievalMode { get; set; } = string.Empty;
    public string RetrievalWarning { get; set; } = string.Empty;
    public List<string> UsedCollectionCodes { get; set; } = [];
    public int ContextUnitCount { get; set; }
    public int ContextTokenCount { get; set; }
    public int ContextCharCount { get; set; }
    public int SourceCount { get; set; }
    public List<AskSourceItem> Sources { get; set; } = [];
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

public sealed class RetrievalModeItem
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class ChatThreadItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private CollectionItem? _parentCollection;
    private bool _isSelected;
    private bool _isMultiSelected;
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

    public bool IsMultiSelected
    {
        get => _isMultiSelected;
        set => SetField(ref _isMultiSelected, value);
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
    public string RetrievalMode { get; set; } = string.Empty;
    public string RetrievalWarning { get; set; } = string.Empty;
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

public sealed class ControlSuggestionItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string ControlTypeCode { get; set; } = string.Empty;
    public string ControlTypeDescription { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
    public string ControlCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ControlObjective { get; set; } = string.Empty;
    public string EvidenceExpectation { get; set; } = string.Empty;
    public string SqlPreview { get; set; } = string.Empty;
}

public sealed class ControlSuggestionResponse
{
    public List<ControlSuggestionItem> Suggestions { get; set; } = [];
    public AskResponseMetrics? Metrics { get; set; }
}

public sealed class ControlExecutionResponse
{
    public ControlListItem? CreatedControl { get; set; }
    public string SqlPreview { get; set; } = string.Empty;
}

public sealed class ControlListItem
{
    public string ControlId { get; set; } = string.Empty;
    public int ControlTypeId { get; set; }
    public string ControlCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ControlObjective { get; set; } = string.Empty;
    public string EvidenceExpectation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ControlTypeCode { get; set; } = string.Empty;
    public string ControlTypeName { get; set; } = string.Empty;
    public string ControlTypeDescription { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
    public string DomainDisplayName { get; set; } = string.Empty;
    public bool IsCurrentDomainControl { get; set; }
}

public sealed class SelectableControlItem : INotifyPropertyChanged
{
    private bool _isIncluded;
    private string _groupLabel = string.Empty;

    public string ControlId { get; set; } = string.Empty;
    public string ControlCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
    public string DomainDisplayName { get; set; } = string.Empty;
    public string ControlTypeCode { get; set; } = string.Empty;
    public string ControlTypeName { get; set; } = string.Empty;

    public string DetailLine =>
        $"{DomainDisplayName} | {ControlTypeName} ({ControlTypeCode}) | {ControlCode}";

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetField(ref _isIncluded, value);
    }

    public string GroupLabel
    {
        get => _groupLabel;
        set => SetField(ref _groupLabel, value);
    }

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

public sealed class ControlGroupingModeItem
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class AiControlGroupingAssignmentResponse
{
    public string ControlCode { get; set; } = string.Empty;
    public string GroupLabel { get; set; } = string.Empty;
}

public sealed class AiControlGroupingResponse
{
    public List<PolicyDraftControlGroupingItem> Groups { get; set; } = [];
    public List<AiControlGroupingAssignmentResponse> Assignments { get; set; } = [];
    public AskResponseMetrics? Metrics { get; set; }
}

public sealed class PolicyDraftControlGroupingItem
{
    public string GroupLabel { get; set; } = string.Empty;
    public List<string> ControlCodes { get; set; } = [];
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

public sealed class PolicyDraftContentResponse
{
    public string DocumentTitle { get; set; } = string.Empty;
    public string RootDomainName { get; set; } = string.Empty;
    public string RootDomainCode { get; set; } = string.Empty;
    public string RootBreadcrumb { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public List<string> Objectives { get; set; } = [];
    public List<string> Principles { get; set; } = [];
    public List<string> Accountability { get; set; } = [];
    public List<string> Transparency { get; set; } = [];
    public List<string> Strategy { get; set; } = [];
    public List<PolicyDraftControlResponse> Controls { get; set; } = [];
    public List<string> Consequences { get; set; } = [];
    public AskResponseMetrics? Metrics { get; set; }
}

public sealed class PolicyDraftSavedStatementResponse
{
    public string StatementText { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public string ReviewStatus { get; set; } = string.Empty;
}

public sealed class PolicyDraftSavedControlResponse
{
    public string ControlCode { get; set; } = string.Empty;
    public string ControlName { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
    public string DomainDisplayName { get; set; } = string.Empty;
    public string ControlTypeCode { get; set; } = string.Empty;
    public string ControlTypeName { get; set; } = string.Empty;
    public string GroupLabel { get; set; } = string.Empty;
    public int GroupDisplayOrder { get; set; }
    public int ControlDisplayOrder { get; set; }
    public string ControlExplanation { get; set; } = string.Empty;
    public List<PolicyDraftSavedStatementResponse> PolicyStatements { get; set; } = [];
}

public sealed class LoadedPolicyDraftResponse
{
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyCode { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public string VersionText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RootDomainName { get; set; } = string.Empty;
    public string RootDomainCode { get; set; } = string.Empty;
    public string RootBreadcrumb { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public List<PolicyDraftSavedStatementResponse> Objectives { get; set; } = [];
    public List<PolicyDraftSavedStatementResponse> Principles { get; set; } = [];
    public List<PolicyDraftSavedStatementResponse> Accountability { get; set; } = [];
    public List<PolicyDraftSavedStatementResponse> Transparency { get; set; } = [];
    public List<PolicyDraftSavedStatementResponse> Strategy { get; set; } = [];
    public List<PolicyDraftSavedControlResponse> Controls { get; set; } = [];
    public List<PolicyDraftSavedStatementResponse> Consequences { get; set; } = [];
}

public sealed class PolicyDraftControlResponse
{
    public string ControlCode { get; set; } = string.Empty;
    public string ControlName { get; set; } = string.Empty;
    public string DomainCode { get; set; } = string.Empty;
    public string DomainDisplayName { get; set; } = string.Empty;
    public string ControlTypeCode { get; set; } = string.Empty;
    public string ControlTypeName { get; set; } = string.Empty;
    public string GroupLabel { get; set; } = string.Empty;
    public int GroupDisplayOrder { get; set; }
    public int ControlDisplayOrder { get; set; }
    public List<string> PolicyStatements { get; set; } = [];
}

public sealed class PolicyDraftLineRetryResponse
{
    public string Text { get; set; } = string.Empty;
    public string SectionKey { get; set; } = string.Empty;
    public string ControlCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public AskResponseMetrics? Metrics { get; set; }
}

public sealed class SavedPolicyDraftResponse
{
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyCode { get; set; } = string.Empty;
    public string PolicyTitle { get; set; } = string.Empty;
    public string VersionText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RootDomainCode { get; set; } = string.Empty;
    public string RootDomainName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
}

public sealed class PolicyControlExplanationResponse
{
    public string PolicyId { get; set; } = string.Empty;
    public string ControlCode { get; set; } = string.Empty;
    public string ControlName { get; set; } = string.Empty;
    public string ExplanationText { get; set; } = string.Empty;
    public string SourceModelName { get; set; } = string.Empty;
}

public sealed class PolicyListItem
{
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyCode { get; set; } = string.Empty;
    public string PolicyTitle { get; set; } = string.Empty;
    public string VersionText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TemplatePath { get; set; } = string.Empty;
    public string SourceModelName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public string RootDomainCode { get; set; } = string.Empty;
    public string RootDomainName { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public int SectionCount { get; set; }
    public int ObjectiveCount { get; set; }
    public int PrincipleCount { get; set; }
    public int ControlStatementCount { get; set; }
}

public sealed class PolicyTableCountItem
{
    public string TableName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
}

public sealed class PolicyCleanupResponse
{
    public string Status { get; set; } = string.Empty;
    public List<string> ClearedTables { get; set; } = [];
    public List<PolicyTableCountItem> Counts { get; set; } = [];
}
