using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Broiler.Dom;

public abstract class DomNode
{
    private readonly List<DomNode> _children = [];
    private readonly ReadOnlyCollection<DomNode> _childNodes;
    private DomDocument? _ownerDocument;

    protected DomNode(DomNodeType nodeType, DomDocument? ownerDocument)
    {
        NodeType = nodeType;
        _ownerDocument = ownerDocument;
        _childNodes = _children.AsReadOnly();
    }

    public DomNodeType NodeType { get; }

    public virtual DomDocument OwnerDocument =>
        _ownerDocument ?? throw new InvalidOperationException("The document node owns itself.");

    public DomNode? ParentNode { get; private set; }

    public IReadOnlyList<DomNode> ChildNodes => _childNodes;

    public DomNode? FirstChild => _children.Count == 0 ? null : _children[0];

    public DomNode? LastChild => _children.Count == 0 ? null : _children[^1];

    public DomNode? PreviousSibling
    {
        get
        {
            if (ParentNode is null)
                return null;

            var index = ParentNode._children.IndexOf(this);
            return index > 0 ? ParentNode._children[index - 1] : null;
        }
    }

    public DomNode? NextSibling
    {
        get
        {
            if (ParentNode is null)
                return null;

            var index = ParentNode._children.IndexOf(this);
            return index >= 0 && index + 1 < ParentNode._children.Count
                ? ParentNode._children[index + 1]
                : null;
        }
    }

    public bool IsConnected => GetRootNode() is DomDocument;

    public ulong TreeVersion { get; private set; }

    public DomNode AppendChild(DomNode node) => InsertBefore(node, null);

    public DomNode InsertBefore(DomNode node, DomNode? referenceNode)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (referenceNode is not null && !ReferenceEquals(referenceNode.ParentNode, this))
            throw DomException.NotFound("The reference node is not a child of this node.");

        if (ReferenceEquals(node, referenceNode))
            return node;

        EnsureCanHaveChildren();
        EnsurePreInsertValidity(node, referenceNode);

        if (node is DomDocumentFragment fragment)
        {
            var fragmentChildren = fragment._children.ToArray();
            foreach (var child in fragmentChildren)
                InsertBefore(child, referenceNode);
            return fragment;
        }

        var targetDocument = this is DomDocument document ? document : OwnerDocument;
        if (!ReferenceEquals(node.OwnerDocument, targetDocument))
            targetDocument.AdoptNode(node);

        if (ReferenceEquals(node.ParentNode, this) && ReferenceEquals(node.NextSibling, referenceNode))
            return node;

        node.ParentNode?.RemoveChild(node);

        var index = referenceNode is null ? _children.Count : _children.IndexOf(referenceNode);
        var previousSibling = index > 0 ? _children[index - 1] : null;
        _children.Insert(index, node);
        node.ParentNode = this;

        if (IsConnected)
            targetDocument.IndexConnectedSubtree(node);

        MarkChanged();
        targetDocument.PublishMutation(new DomMutationRecord(
            DomMutationType.ChildList,
            this,
            AddedNodes: [node],
            PreviousSibling: previousSibling,
            NextSibling: referenceNode));
        return node;
    }

    public DomNode ReplaceChild(DomNode node, DomNode child)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(child);

        if (!ReferenceEquals(child.ParentNode, this))
            throw DomException.NotFound("The node to replace is not a child of this node.");

        if (ReferenceEquals(node, child))
            return child;

        var reference = child.NextSibling;
        RemoveChild(child);
        InsertBefore(node, reference);
        return child;
    }

    public DomNode RemoveChild(DomNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        var index = _children.IndexOf(child);
        if (index < 0)
            throw DomException.NotFound("The node to remove is not a child of this node.");

        var previousSibling = index > 0 ? _children[index - 1] : null;
        var nextSibling = index + 1 < _children.Count ? _children[index + 1] : null;
        var document = this is DomDocument owner ? owner : OwnerDocument;

        if (child.IsConnected)
            document.UnindexConnectedSubtree(child);

        _children.RemoveAt(index);
        child.ParentNode = null;
        MarkChanged();
        document.PublishMutation(new DomMutationRecord(
            DomMutationType.ChildList,
            this,
            RemovedNodes: [child],
            PreviousSibling: previousSibling,
            NextSibling: nextSibling));
        return child;
    }

    public void Remove() => ParentNode?.RemoveChild(this);

    public DomNode CloneNode(bool deep = false)
    {
        var clone = CloneShallow(OwnerDocument);
        if (deep)
        {
            foreach (var child in _children)
                clone.AppendChild(child.CloneNode(true));
        }

        return clone;
    }

    public DomNode GetRootNode()
    {
        DomNode current = this;
        while (current.ParentNode is not null)
            current = current.ParentNode;
        return current;
    }

    public IEnumerable<DomNode> Descendants()
    {
        foreach (var child in _children)
        {
            yield return child;
            foreach (var descendant in child.Descendants())
                yield return descendant;
        }
    }

    public IEnumerable<DomNode> InclusiveDescendants()
    {
        yield return this;
        foreach (var descendant in Descendants())
            yield return descendant;
    }

    public IEnumerable<DomNode> InclusiveAncestors()
    {
        for (DomNode? current = this; current is not null; current = current.ParentNode)
            yield return current;
    }

    public bool IsDescendantOf(DomNode ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        for (var current = ParentNode; current is not null; current = current.ParentNode)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    public void Normalize()
    {
        for (var index = 0; index < _children.Count;)
        {
            if (_children[index] is DomText text)
            {
                while (index + 1 < _children.Count && _children[index + 1] is DomText next)
                {
                    text.Data += next.Data;
                    RemoveChild(next);
                }

                if (text.Data.Length == 0)
                {
                    RemoveChild(text);
                    continue;
                }
            }
            else
            {
                _children[index].Normalize();
            }

            index++;
        }
    }

    internal abstract DomNode CloneShallow(DomDocument ownerDocument);

    internal void SetOwnerDocument(DomDocument document)
    {
        _ownerDocument = document;
        foreach (var child in _children)
            child.SetOwnerDocument(document);
    }

    protected void MarkChanged()
    {
        for (DomNode? current = this; current is not null; current = current.ParentNode)
            current.TreeVersion++;
    }

    private void EnsureCanHaveChildren()
    {
        if (this is DomText or DomComment or DomDocumentType)
            throw DomException.HierarchyRequest($"{NodeType} nodes cannot have children.");
    }

    private void EnsurePreInsertValidity(DomNode node, DomNode? referenceNode)
    {
        if (ReferenceEquals(node, this) || InclusiveAncestors().Contains(node))
            throw DomException.HierarchyRequest("A node cannot be inserted into itself or one of its descendants.");

        if (node is DomDocument)
            throw DomException.HierarchyRequest("A document cannot be inserted into another node.");

        if (this is not DomDocument document)
            return;

        var candidates = node is DomDocumentFragment
            ? node.ChildNodes
            : [node];

        if (candidates.Any(static candidate => candidate is DomText))
            throw DomException.HierarchyRequest("Text nodes cannot be direct children of a document.");

        if (candidates.Count(static candidate => candidate is DomElement) > 1 ||
            candidates.Count(static candidate => candidate is DomDocumentType) > 1)
        {
            throw DomException.HierarchyRequest("A document can contain only one element and one document type.");
        }

        var replacedOrMoved = ReferenceEquals(node.ParentNode, document) ? node : null;
        var existingElement = document._children
            .FirstOrDefault(child => child is DomElement && !ReferenceEquals(child, replacedOrMoved));
        var existingDoctype = document._children
            .FirstOrDefault(child => child is DomDocumentType && !ReferenceEquals(child, replacedOrMoved));

        if (existingElement is not null && candidates.Any(static candidate => candidate is DomElement))
            throw DomException.HierarchyRequest("The document already has a document element.");

        if (existingDoctype is not null && candidates.Any(static candidate => candidate is DomDocumentType))
            throw DomException.HierarchyRequest("The document already has a document type.");

        if (candidates.Any(static candidate => candidate is DomDocumentType))
        {
            var elementIndex = document._children.FindIndex(static child => child is DomElement);
            var insertionIndex = referenceNode is null ? document._children.Count : document._children.IndexOf(referenceNode);
            if (elementIndex >= 0 && insertionIndex > elementIndex)
                throw DomException.HierarchyRequest("A document type must precede the document element.");
        }

        if (candidates.Any(static candidate => candidate is DomElement))
        {
            var doctypeIndex = document._children.FindIndex(static child => child is DomDocumentType);
            var insertionIndex = referenceNode is null ? document._children.Count : document._children.IndexOf(referenceNode);
            if (doctypeIndex >= 0 && insertionIndex <= doctypeIndex)
                throw DomException.HierarchyRequest("The document element must follow the document type.");
        }
    }

}

internal static class DomNodeCollectionExtensions
{
    public static int IndexOfReference(this IReadOnlyList<DomNode> nodes, DomNode target)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            if (ReferenceEquals(nodes[index], target))
                return index;
        }
        return -1;
    }
}
