using System;
using System.Linq;

namespace Broiler.Dom;

public sealed class DomRange : IDisposable
{
    private bool _disposed;
    private DomNode _startContainer;
    private int _startOffset;
    private DomNode _endContainer;
    private int _endOffset;

    public DomRange(DomNode root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        _startContainer = root;
        _endContainer = root;
        root.OwnerDocument.Mutated += OnMutation;
    }

    public DomNode Root { get; }
    public DomNode StartContainer => _startContainer;
    public int StartOffset => _startOffset;
    public DomNode EndContainer => _endContainer;
    public int EndOffset => _endOffset;
    public bool Collapsed => ReferenceEquals(StartContainer, EndContainer) && StartOffset == EndOffset;

    public DomNode CommonAncestorContainer
    {
        get
        {
            var ancestors = StartContainer.InclusiveAncestors().ToHashSet();
            return EndContainer.InclusiveAncestors().First(ancestors.Contains);
        }
    }

    public void SetStart(DomNode container, int offset)
    {
        ValidateBoundary(container, offset);
        _startContainer = container;
        _startOffset = offset;
        if (CompareBoundaryPoints(StartContainer, StartOffset, EndContainer, EndOffset) > 0)
        {
            _endContainer = container;
            _endOffset = offset;
        }
    }

    public void SetEnd(DomNode container, int offset)
    {
        ValidateBoundary(container, offset);
        _endContainer = container;
        _endOffset = offset;
        if (CompareBoundaryPoints(StartContainer, StartOffset, EndContainer, EndOffset) > 0)
        {
            _startContainer = container;
            _startOffset = offset;
        }
    }

    public void SelectNode(DomNode node)
    {
        var parent = node.ParentNode ?? throw DomException.InvalidState("The node has no parent.");
        var index = parent.ChildNodes.IndexOfReference(node);
        SetStart(parent, index);
        SetEnd(parent, index + 1);
    }

    public void SelectNodeContents(DomNode node)
    {
        SetStart(node, 0);
        SetEnd(node, BoundaryLength(node));
    }

    public void Collapse(bool toStart)
    {
        if (toStart)
            SetEnd(StartContainer, StartOffset);
        else
            SetStart(EndContainer, EndOffset);
    }

    public int CompareBoundaryPoints(
        DomNode containerA,
        int offsetA,
        DomNode containerB,
        int offsetB)
    {
        if (!ReferenceEquals(containerA.GetRootNode(), containerB.GetRootNode()))
            throw DomException.WrongDocument("Boundary points belong to different trees.");
        if (ReferenceEquals(containerA, containerB))
            return offsetA.CompareTo(offsetB);

        if (containerB.IsDescendantOf(containerA))
        {
            var child = containerB;
            while (!ReferenceEquals(child.ParentNode, containerA))
                child = child.ParentNode!;
            return offsetA <= containerA.ChildNodes.IndexOfReference(child) ? -1 : 1;
        }

        if (containerA.IsDescendantOf(containerB))
            return -CompareBoundaryPoints(containerB, offsetB, containerA, offsetA);

        var common = FindCommonAncestor(containerA, containerB);
        var childA = containerA;
        var childB = containerB;
        while (!ReferenceEquals(childA.ParentNode, common))
            childA = childA.ParentNode!;
        while (!ReferenceEquals(childB.ParentNode, common))
            childB = childB.ParentNode!;
        return common.ChildNodes.IndexOfReference(childA)
            .CompareTo(common.ChildNodes.IndexOfReference(childB));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Root.OwnerDocument.Mutated -= OnMutation;
    }

    private void OnMutation(DomMutationRecord mutation)
    {
        if (mutation.Type != DomMutationType.ChildList ||
            mutation.RemovedNodes is not { Count: > 0 })
        {
            return;
        }

        var index = mutation.PreviousSibling is null
            ? 0
            : mutation.Target.ChildNodes.IndexOfReference(mutation.PreviousSibling) + 1;
        foreach (var removed in mutation.RemovedNodes)
            AdjustForRemoval(mutation.Target, removed, index);
    }

    private void AdjustForRemoval(DomNode parent, DomNode removed, int index)
    {
        AdjustBoundary(ref _startContainer, ref _startOffset, parent, removed, index);
        AdjustBoundary(ref _endContainer, ref _endOffset, parent, removed, index);
    }

    private static void AdjustBoundary(ref DomNode container, ref int offset, DomNode parent, DomNode removed, int index)
    {
        if (ReferenceEquals(container, removed) || container.IsDescendantOf(removed))
        {
            container = parent;
            offset = index;
        }
        else if (ReferenceEquals(container, parent) && offset > index)
        {
            offset--;
        }
    }

    private static DomNode FindCommonAncestor(DomNode first, DomNode second)
    {
        var ancestors = first.InclusiveAncestors().ToHashSet();
        return second.InclusiveAncestors().First(ancestors.Contains);
    }

    private static int BoundaryLength(DomNode node) => node switch
    {
        DomCharacterData data => data.Data.Length,
        _ => node.ChildNodes.Count
    };

    private static void ValidateBoundary(DomNode container, int offset)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (offset < 0 || offset > BoundaryLength(container))
            throw DomException.IndexSize("The boundary offset is outside the node.");
    }
}
