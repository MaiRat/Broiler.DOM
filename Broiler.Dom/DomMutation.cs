using System.Collections.Generic;

namespace Broiler.Dom;

public enum DomMutationType
{
    ChildList,
    Attributes,
    CharacterData,
    Adoption,
}

public sealed record DomMutationRecord(
    DomMutationType Type,
    DomNode Target,
    IReadOnlyList<DomNode>? AddedNodes = null,
    IReadOnlyList<DomNode>? RemovedNodes = null,
    DomNode? PreviousSibling = null,
    DomNode? NextSibling = null,
    string? AttributeName = null,
    string? AttributeNamespace = null,
    string? OldValue = null,
    string? NewValue = null,
    DomDocument? OldDocument = null,
    DomDocument? NewDocument = null);
