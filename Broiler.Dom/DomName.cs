using System;

namespace Broiler.Dom;

public readonly record struct DomName
{
    public DomName(string? namespaceUri, string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            throw new ArgumentException("A qualified name is required.", nameof(qualifiedName));

        var separator = qualifiedName.IndexOf(':');
        if (separator != qualifiedName.LastIndexOf(':') ||
            separator == 0 ||
            separator == qualifiedName.Length - 1)
        {
            throw DomException.Namespace($"'{qualifiedName}' is not a valid qualified name.");
        }

        NamespaceUri = string.IsNullOrEmpty(namespaceUri) ? null : namespaceUri;
        Prefix = separator < 0 ? null : qualifiedName[..separator];
        LocalName = separator < 0 ? qualifiedName : qualifiedName[(separator + 1)..];

        if (Prefix is not null && NamespaceUri is null)
            throw DomException.Namespace("A prefixed name requires a namespace URI.");

        if (string.Equals(Prefix, "xml", StringComparison.Ordinal) &&
            !string.Equals(NamespaceUri, DomNamespaces.Xml, StringComparison.Ordinal))
        {
            throw DomException.Namespace("The xml prefix requires the XML namespace.");
        }

        if ((string.Equals(qualifiedName, "xmlns", StringComparison.Ordinal) ||
             string.Equals(Prefix, "xmlns", StringComparison.Ordinal)) &&
            !string.Equals(NamespaceUri, DomNamespaces.Xmlns, StringComparison.Ordinal))
        {
            throw DomException.Namespace("The xmlns name requires the XMLNS namespace.");
        }

        QualifiedName = qualifiedName;
    }

    // Non-splitting constructor: the whole name is the local name, with no
    // prefix or namespace processing (used by CreateLocal).
    private DomName(string localName)
    {
        LocalName = localName;
        QualifiedName = localName;
        Prefix = null;
        NamespaceUri = null;
    }

    /// <summary>
    /// Creates a name with NO namespace and NO prefix processing: the entire
    /// <paramref name="localName"/> becomes the local (and qualified) name even
    /// if it contains a ':'. This matches DOM <c>Element.setAttribute()</c>,
    /// which performs no namespace splitting — e.g. on an HTML-parsed SVG
    /// element <c>setAttribute("xlink:href", …)</c> stores an attribute named
    /// literally "xlink:href" (no namespace) rather than throwing. The
    /// namespace-aware constructor is for <c>setAttributeNS</c>.
    /// </summary>
    public static DomName CreateLocal(string localName)
    {
        if (string.IsNullOrWhiteSpace(localName))
            throw new ArgumentException("A local name is required.", nameof(localName));
        return new DomName(localName);
    }

    public string? NamespaceUri { get; }

    public string? Prefix { get; }

    public string LocalName { get; }

    public string QualifiedName { get; }
}

public static class DomNamespaces
{
    public const string Html = "http://www.w3.org/1999/xhtml";
    public const string Svg = "http://www.w3.org/2000/svg";
    public const string MathMl = "http://www.w3.org/1998/Math/MathML";
    public const string Xml = "http://www.w3.org/XML/1998/namespace";
    public const string Xmlns = "http://www.w3.org/2000/xmlns/";
}
