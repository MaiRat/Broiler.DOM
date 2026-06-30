namespace Broiler.Dom.Tests;

public sealed class DomKernelTests
{
    [Fact]
    public void Document_Builds_A_Typed_Tree_With_ReadOnly_Children()
    {
        var document = new DomDocument();
        var doctype = document.CreateDocumentType("html");
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");

        document.AppendChild(doctype);
        document.AppendChild(html);
        html.AppendChild(body);

        Assert.Same(document, html.OwnerDocument);
        Assert.Same(document, html.ParentNode);
        Assert.Same(html, body.ParentNode);
        Assert.Same(html, document.DocumentElement);
        Assert.Same(body, document.Body);
        Assert.True(body.IsConnected);
        Assert.IsAssignableFrom<IReadOnlyList<DomNode>>(html.ChildNodes);
        Assert.False(html.ChildNodes is List<DomNode>);
    }

    [Fact]
    public void Document_Rejects_Invalid_Hierarchy()
    {
        var document = new DomDocument();
        document.AppendChild(document.CreateElement("html"));

        var secondRoot = Assert.Throws<DomException>(
            () => document.AppendChild(document.CreateElement("svg")));
        var textChild = Assert.Throws<DomException>(
            () => document.AppendChild(document.CreateTextNode("nope")));

        Assert.Equal("HierarchyRequestError", secondRoot.Name);
        Assert.Equal("HierarchyRequestError", textChild.Name);
    }

    [Fact]
    public void InsertBefore_And_RemoveChild_Maintain_Sibling_Invariants()
    {
        var document = CreateHtmlDocument(out var body);
        var first = document.CreateElement("p");
        var second = document.CreateElement("p");
        var middle = document.CreateElement("span");

        body.AppendChild(first);
        body.AppendChild(second);
        body.InsertBefore(middle, second);

        Assert.Equal([first, middle, second], body.ChildNodes);
        Assert.Same(first, middle.PreviousSibling);
        Assert.Same(second, middle.NextSibling);

        body.RemoveChild(middle);

        Assert.Null(middle.ParentNode);
        Assert.Same(second, first.NextSibling);
        Assert.Same(first, second.PreviousSibling);
    }

    [Fact]
    public void DocumentFragment_Is_Unpacked_And_Emptied()
    {
        var document = CreateHtmlDocument(out var body);
        var fragment = document.CreateDocumentFragment();
        var first = document.CreateElement("span");
        var second = document.CreateTextNode("tail");
        fragment.AppendChild(first);
        fragment.AppendChild(second);

        body.AppendChild(fragment);

        Assert.Empty(fragment.ChildNodes);
        Assert.Equal([first, second], body.ChildNodes);
        Assert.Same(body, first.ParentNode);
        Assert.Same(body, second.ParentNode);
    }

    [Fact]
    public void Moving_A_Node_Never_Duplicates_It()
    {
        var document = CreateHtmlDocument(out var body);
        var left = document.CreateElement("section");
        var right = document.CreateElement("section");
        var child = document.CreateElement("span");
        body.AppendChild(left);
        body.AppendChild(right);
        left.AppendChild(child);

        right.AppendChild(child);

        Assert.Empty(left.ChildNodes);
        Assert.Equal([child], right.ChildNodes);
        Assert.Same(right, child.ParentNode);
    }

    [Fact]
    public void CrossDocument_Insert_Adopts_The_Whole_Subtree()
    {
        var source = CreateHtmlDocument(out var sourceBody);
        var target = CreateHtmlDocument(out var targetBody);
        var section = source.CreateElement("section");
        var child = source.CreateElement("span");
        section.AppendChild(child);
        sourceBody.AppendChild(section);

        targetBody.AppendChild(section);

        Assert.Same(target, section.OwnerDocument);
        Assert.Same(target, child.OwnerDocument);
        Assert.Empty(sourceBody.ChildNodes);
        Assert.Same(targetBody, section.ParentNode);
    }

    [Fact]
    public void ImportNode_Creates_Independent_Deep_Copy()
    {
        var source = CreateHtmlDocument(out var sourceBody);
        var target = CreateHtmlDocument(out _);
        var section = source.CreateElement("section");
        section.Id = "source";
        section.AppendChild(source.CreateTextNode("hello"));
        sourceBody.AppendChild(section);

        var imported = Assert.IsType<DomElement>(target.ImportNode(section, deep: true));
        imported.Id = "copy";

        Assert.Same(target, imported.OwnerDocument);
        Assert.Equal("copy", imported.Id);
        Assert.Equal("source", section.Id);
        Assert.Equal("hello", Assert.IsType<DomText>(imported.FirstChild).Data);
    }

    [Fact]
    public void Attributes_Are_Reflected_And_Namespace_Aware()
    {
        var document = new DomDocument();
        var element = document.CreateElementNS(DomNamespaces.Svg, "svg:use");

        element.Id = "icon";
        element.ClassName = "glyph";
        element.SetAttributeNS("http://www.w3.org/1999/xlink", "xlink:href", "#shape");

        Assert.Equal("icon", element.GetAttribute("id"));
        Assert.Equal("glyph", element.GetAttribute("class"));
        Assert.Equal("#shape", element.GetAttributeNS("http://www.w3.org/1999/xlink", "href"));
        Assert.Equal("svg", element.Prefix);
        Assert.Equal("use", element.LocalName);
        Assert.Equal(DomNamespaces.Svg, element.NamespaceUri);
        Assert.Throws<DomException>(() => document.CreateElementNS(null, "svg:use"));
    }

    [Fact]
    public void SetAttribute_Prefixed_Name_Stores_Verbatim_Without_Throwing()
    {
        // Per DOM, Element.setAttribute() performs NO namespace splitting: a
        // prefixed qualified name is the attribute's local name with no
        // namespace. Regression (WPT issue #1100 svg-use-animation-crash): this
        // used to throw "A prefixed name requires a namespace URI", crashing
        // HTML parsing of inline SVG that carries xlink:href.
        var document = new DomDocument();
        var element = document.CreateElementNS(DomNamespaces.Svg, "use");

        element.SetAttribute("xlink:href", "#target");

        Assert.Equal("#target", element.GetAttribute("xlink:href"));
        // Not placed in any namespace — setAttribute does not split the prefix.
        Assert.Null(element.GetAttributeNS("http://www.w3.org/1999/xlink", "href"));
    }

    [Fact]
    public void Id_Index_Tracks_Connection_Mutation_And_Tree_Order()
    {
        var document = CreateHtmlDocument(out var body);
        var first = document.CreateElement("div");
        var second = document.CreateElement("div");
        first.Id = "shared";
        second.Id = "shared";

        Assert.Null(document.GetElementById("shared"));

        body.AppendChild(first);
        body.AppendChild(second);
        Assert.Same(first, document.GetElementById("shared"));

        body.RemoveChild(first);
        Assert.Same(second, document.GetElementById("shared"));

        second.Id = "renamed";
        Assert.Null(document.GetElementById("shared"));
        Assert.Same(second, document.GetElementById("renamed"));
    }

    [Fact]
    public void Traversal_Is_Deterministic_Preorder()
    {
        var document = CreateHtmlDocument(out var body);
        var section = document.CreateElement("section");
        var text = document.CreateTextNode("one");
        var span = document.CreateElement("span");
        body.AppendChild(section);
        section.AppendChild(text);
        section.AppendChild(span);

        Assert.Equal(
            [body, section, text, span],
            body.InclusiveDescendants().ToArray());
        Assert.Equal(
            [document.DocumentElement!, body, section, span],
            document.GetElementsByTagName("*"));
    }

    [Fact]
    public void Normalize_Merges_Adjacent_Text_And_Removes_Empty_Text()
    {
        var document = CreateHtmlDocument(out var body);
        body.AppendChild(document.CreateTextNode("hello"));
        body.AppendChild(document.CreateTextNode(""));
        body.AppendChild(document.CreateTextNode(" world"));

        body.Normalize();

        var text = Assert.IsType<DomText>(Assert.Single(body.ChildNodes));
        Assert.Equal("hello world", text.Data);
    }

    [Fact]
    public void Mutations_Are_Reported_With_Versioning()
    {
        var document = CreateHtmlDocument(out var body);
        var records = new List<DomMutationRecord>();
        document.Mutated += records.Add;
        var initialVersion = document.Version;
        var element = document.CreateElement("div");

        body.AppendChild(element);
        element.SetAttribute("data-state", "ready");
        element.AppendChild(document.CreateTextNode("hello"));
        Assert.IsType<DomText>(element.FirstChild).Data = "updated";

        Assert.Equal(initialVersion + 4, document.Version);
        Assert.Equal(
            [
                DomMutationType.ChildList,
                DomMutationType.Attributes,
                DomMutationType.ChildList,
                DomMutationType.CharacterData,
            ],
            records.Select(static record => record.Type));
        Assert.True(document.TreeVersion > 0);
        Assert.True(body.TreeVersion > 0);
        Assert.True(element.TreeVersion > 0);
    }

    [Fact]
    public void TreeWalker_Respects_Show_Mask_And_Skip_Traversal()
    {
        var document = CreateHtmlDocument(out var body);
        var section = document.CreateElement("section");
        var text = document.CreateTextNode("value");
        var span = document.CreateElement("span");
        body.AppendChild(section);
        section.AppendChild(text);
        section.AppendChild(span);

        var walker = new DomTreeWalker(
            body,
            DomWhatToShow.Element,
            node => node is DomElement { LocalName: "section" }
                ? DomFilterResult.Skip
                : DomFilterResult.Accept);

        Assert.Same(span, walker.NextNode());
        Assert.Same(body, walker.PreviousNode());
    }

    [Fact]
    public void NodeIterator_Adjusts_After_Reference_Subtree_Removal()
    {
        var document = CreateHtmlDocument(out var body);
        var first = document.CreateElement("section");
        var child = document.CreateElement("span");
        var second = document.CreateElement("aside");
        body.AppendChild(first);
        first.AppendChild(child);
        body.AppendChild(second);

        using var iterator = new DomNodeIterator(body, DomWhatToShow.Element);
        Assert.Same(body, iterator.NextNode());
        Assert.Same(first, iterator.NextNode());
        Assert.Same(child, iterator.NextNode());

        body.RemoveChild(first);

        Assert.Same(second, iterator.NextNode());
    }

    [Fact]
    public void Range_Adjusts_Boundaries_After_Subtree_Removal()
    {
        var document = CreateHtmlDocument(out var body);
        var section = document.CreateElement("section");
        var text = document.CreateTextNode("value");
        var aside = document.CreateElement("aside");
        body.AppendChild(section);
        section.AppendChild(text);
        body.AppendChild(aside);

        using var range = new DomRange(body);
        range.SetStart(text, 2);
        range.SetEnd(body, 1);

        body.RemoveChild(section);

        Assert.Same(body, range.StartContainer);
        Assert.Equal(0, range.StartOffset);
        Assert.Same(body, range.EndContainer);
        Assert.Equal(0, range.EndOffset);
        Assert.True(range.Collapsed);
    }

    [Fact]
    public void Descendants_Survives_Child_Mutation_Mid_Enumeration()
    {
        // Descendants() is enumerated lazily; consumers (querySelectorAll,
        // getElementsByTagName, tree walkers) hold the enumerator open while the
        // tree mutates. Iterating the live child list then threw "Collection was
        // modified" and aborted the walk, crashing whole WPT shards (issue #1143).
        var document = CreateHtmlDocument(out var body);
        var section = document.CreateElement("section");
        body.AppendChild(section);
        for (var i = 0; i < 3; i++)
            section.AppendChild(document.CreateElement("span"));

        // Append a new child to the same level being walked, mid-enumeration.
        var visited = 0;
        foreach (var node in body.Descendants())
        {
            visited++;
            if (node is DomElement { LocalName: "section" })
                section.AppendChild(document.CreateElement("span"));
        }

        Assert.True(visited >= 4);
    }

    private static DomDocument CreateHtmlDocument(out DomElement body)
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        body = document.CreateElement("body");
        document.AppendChild(html);
        html.AppendChild(body);
        return document;
    }
}
