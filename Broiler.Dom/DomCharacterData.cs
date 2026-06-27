using System;

namespace Broiler.Dom;

public abstract class DomCharacterData(DomNodeType nodeType, DomDocument ownerDocument, string data) : 
    DomNode(nodeType, ownerDocument)
{
    private string _data = data ?? throw new ArgumentNullException(nameof(data));

    public string Data
    {
        get => _data;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (string.Equals(_data, value, StringComparison.Ordinal))
                return;

            var oldValue = _data;
            _data = value;
            MarkChanged();
            OwnerDocument.PublishMutation(new DomMutationRecord(
                DomMutationType.CharacterData,
                this,
                OldValue: oldValue,
                NewValue: value));
        }
    }

    public int Length => _data.Length;

    public void AppendData(string data) => Data += data;
}

public sealed class DomText : DomCharacterData
{
    internal DomText(DomDocument ownerDocument, string data)
        : base(DomNodeType.Text, ownerDocument, data)
    {
    }

    internal override DomNode CloneShallow(DomDocument ownerDocument) =>
        new DomText(ownerDocument, Data);
}

public sealed class DomComment : DomCharacterData
{
    internal DomComment(DomDocument ownerDocument, string data)
        : base(DomNodeType.Comment, ownerDocument, data)
    {
    }

    internal override DomNode CloneShallow(DomDocument ownerDocument) =>
        new DomComment(ownerDocument, Data);
}
