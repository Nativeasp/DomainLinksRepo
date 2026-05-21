using System.Collections.ObjectModel;

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
}

public sealed class AskSourceItem
{
    public string CollectionCode { get; set; } = string.Empty;
    public string CollectionDisplayName { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string ContentUnitId { get; set; } = string.Empty;
}

public sealed class ChatThreadItem
{
    public string Title { get; set; } = string.Empty;
    public CollectionItem? ParentCollection { get; set; }
    public bool IsSelected { get; set; }
    public bool IsEditing { get; set; }
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();
}

public sealed class ChatMessageItem
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
