using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Broiler.Dom;

public class DomElement : DomNode
{
    private readonly Dictionary<(string? NamespaceUri, string LocalName), DomAttribute> _attributes = [];
    private readonly ReadOnlyDictionary<(string? NamespaceUri, string LocalName), DomAttribute> _readOnlyAttributes;

    internal DomElement(DomDocument ownerDocument, DomName name)
        : this(ownerDocument, name, DomNodeType.Element)
    {
    }

    protected DomElement(DomDocument ownerDocument, DomName name, DomNodeType nodeType)
        : base(nodeType, ownerDocument)
    {
        Name = name;
        _readOnlyAttributes = _attributes.AsReadOnly();
    }

    public DomName Name { get; private set; }

    public string TagName => Name.QualifiedName;

    public string LocalName => Name.LocalName;

    public string? NamespaceUri => Name.NamespaceUri;

    public string? Prefix => Name.Prefix;

    public IReadOnlyDictionary<(string? NamespaceUri, string LocalName), DomAttribute> Attributes =>
        _readOnlyAttributes;

    public string? Id
    {
        get => GetAttribute("id");
        set
        {
            if (value is null)
                RemoveAttribute("id");
            else
                SetAttribute("id", value);
        }
    }

    public string? ClassName
    {
        get => GetAttribute("class");
        set
        {
            if (value is null)
                RemoveAttribute("class");
            else
                SetAttribute("class", value);
        }
    }

    public bool HasAttribute(string qualifiedName) => GetAttribute(qualifiedName) is not null;

    public string? GetAttribute(string qualifiedName)
    {
        var key = (NamespaceUri: (string?)null, LocalName: qualifiedName.ToLowerInvariant());
        return _attributes.TryGetValue(key, out var attribute) ? attribute.Value : null;
    }

    public string? GetAttributeNS(string? namespaceUri, string localName)
    {
        var key = (NormalizeNamespace(namespaceUri), localName);
        return _attributes.TryGetValue(key, out var attribute) ? attribute.Value : null;
    }

    public void SetAttribute(string qualifiedName, string value) =>
        // Per DOM, Element.setAttribute() does NO namespace splitting: the whole
        // qualified name is the attribute's local name (no namespace). This also
        // keys identically to GetAttribute/RemoveAttribute below, and avoids
        // throwing on prefixed names like SVG's "xlink:href".
        SetAttributeCore(DomName.CreateLocal(qualifiedName.ToLowerInvariant()), value);

    public void SetAttributeNS(string? namespaceUri, string qualifiedName, string value) =>
        SetAttributeCore(new DomName(namespaceUri, qualifiedName), value);

    public bool RemoveAttribute(string qualifiedName) =>
        RemoveAttributeCore(null, qualifiedName.ToLowerInvariant());

    public bool RemoveAttributeNS(string? namespaceUri, string localName) =>
        RemoveAttributeCore(NormalizeNamespace(namespaceUri), localName);

    protected void SetName(DomName name) => Name = name;

    internal override DomNode CloneShallow(DomDocument ownerDocument)
    {
        var clone = new DomElement(ownerDocument, Name);
        foreach (var attribute in _attributes.Values)
            clone._attributes.Add((attribute.NamespaceUri, attribute.LocalName), attribute);
        return clone;
    }

    private void SetAttributeCore(DomName name, string value)
    {
        var key = (name.NamespaceUri, name.LocalName);
        var oldValue = _attributes.TryGetValue(key, out var oldAttribute)
            ? oldAttribute.Value
            : null;

        if (string.Equals(oldValue, value, StringComparison.Ordinal) &&
            oldAttribute.Name == name)
        {
            return;
        }

        _attributes[key] = new DomAttribute(name, value);
        if (name.NamespaceUri is null && string.Equals(name.LocalName, "id", StringComparison.OrdinalIgnoreCase))
            OwnerDocument.UpdateElementId(this, oldValue, value);

        MarkChanged();
        OwnerDocument.PublishMutation(new DomMutationRecord(
            DomMutationType.Attributes,
            this,
            AttributeName: name.QualifiedName,
            AttributeNamespace: name.NamespaceUri,
            OldValue: oldValue,
            NewValue: value));
    }

    private bool RemoveAttributeCore(string? namespaceUri, string localName)
    {
        var key = (namespaceUri, localName);
        if (!_attributes.Remove(key, out var removed))
            return false;

        if (namespaceUri is null && string.Equals(localName, "id", StringComparison.OrdinalIgnoreCase))
            OwnerDocument.UpdateElementId(this, removed.Value, null);

        MarkChanged();
        OwnerDocument.PublishMutation(new DomMutationRecord(
            DomMutationType.Attributes,
            this,
            AttributeName: removed.QualifiedName,
            AttributeNamespace: removed.NamespaceUri,
            OldValue: removed.Value));
        return true;
    }

    private static string? NormalizeNamespace(string? namespaceUri) =>
        string.IsNullOrEmpty(namespaceUri) ? null : namespaceUri;
}
