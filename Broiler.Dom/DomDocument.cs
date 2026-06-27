using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Dom;

public sealed class DomDocument : DomNode
{
    private readonly Dictionary<string, HashSet<DomElement>> _elementsById = new(StringComparer.Ordinal);

    public DomDocument() : base(DomNodeType.Document, null)
    {
    }

    public override DomDocument OwnerDocument => this;

    public DomElement? DocumentElement => ChildNodes.OfType<DomElement>().FirstOrDefault();

    public DomDocumentType? DocumentType => ChildNodes.OfType<DomDocumentType>().FirstOrDefault();

    public DomElement? Head => DocumentElement?.ChildNodes.OfType<DomElement>()
        .FirstOrDefault(static element => string.Equals(element.LocalName, "head", StringComparison.OrdinalIgnoreCase));

    public DomElement? Body => DocumentElement?.ChildNodes.OfType<DomElement>()
        .FirstOrDefault(static element => string.Equals(element.LocalName, "body", StringComparison.OrdinalIgnoreCase));

    public ulong Version { get; private set; }

    public event Action<DomMutationRecord>? Mutated;

    public DomElement CreateElement(string localName) =>
        new(this, new DomName(DomNamespaces.Html, localName.ToLowerInvariant()));

    public DomElement CreateElementNS(string? namespaceUri, string qualifiedName) =>
        new(this, new DomName(namespaceUri, qualifiedName));

    public DomText CreateTextNode(string data) => new(this, data);

    public DomComment CreateComment(string data) => new(this, data);

    public DomDocumentFragment CreateDocumentFragment() => new(this);

    public DomDocumentType CreateDocumentType(string name, string publicId = "", string systemId = "") =>
        new(this, name, publicId, systemId);

    public DomElement? GetElementById(string id)
    {
        if (!_elementsById.TryGetValue(id, out var candidates) || candidates.Count == 0)
            return null;

        return Descendants().OfType<DomElement>().FirstOrDefault(candidates.Contains);
    }

    public IEnumerable<DomElement> GetElementsByTagName(string localName) =>
        Descendants()
            .OfType<DomElement>()
            .Where(static element => element.NodeType == DomNodeType.Element)
            .Where(element =>
                localName == "*" ||
                string.Equals(element.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    public DomNode AdoptNode(DomNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is DomDocument)
            throw DomException.HierarchyRequest("A document cannot be adopted.");

        var oldDocument = node.OwnerDocument;
        node.ParentNode?.RemoveChild(node);

        if (ReferenceEquals(oldDocument, this))
            return node;

        node.SetOwnerDocument(this);
        PublishMutation(new DomMutationRecord(
            DomMutationType.Adoption,
            node,
            OldDocument: oldDocument,
            NewDocument: this));
        return node;
    }

    public DomNode ImportNode(DomNode node, bool deep = false)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is DomDocument)
            throw DomException.HierarchyRequest("A document cannot be imported.");

        var clone = node.CloneShallow(this);
        if (deep)
        {
            foreach (var child in node.ChildNodes)
                clone.AppendChild(ImportNode(child, true));
        }

        return clone;
    }

    internal override DomNode CloneShallow(DomDocument ownerDocument) =>
        throw new InvalidOperationException("Document cloning is not supported by the Phase 1 kernel.");

    internal void PublishMutation(DomMutationRecord mutation)
    {
        Version++;
        Mutated?.Invoke(mutation);
    }

    internal void UpdateElementId(DomElement element, string? oldId, string? newId)
    {
        if (!element.IsConnected)
            return;

        if (!string.IsNullOrEmpty(oldId) && _elementsById.TryGetValue(oldId, out var oldSet))
        {
            oldSet.Remove(element);
            if (oldSet.Count == 0)
                _elementsById.Remove(oldId);
        }

        if (!string.IsNullOrEmpty(newId))
        {
            if (!_elementsById.TryGetValue(newId, out var newSet))
            {
                newSet = [];
                _elementsById.Add(newId, newSet);
            }

            newSet.Add(element);
        }
    }

    internal void IndexConnectedSubtree(DomNode node)
    {
        foreach (var element in node.InclusiveDescendants().OfType<DomElement>())
            UpdateElementId(element, null, element.Id);
    }

    internal void UnindexConnectedSubtree(DomNode node)
    {
        foreach (var element in node.InclusiveDescendants().OfType<DomElement>())
            UpdateElementId(element, element.Id, null);
    }
}
