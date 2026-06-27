namespace Broiler.Dom;

public sealed class DomDocumentType(DomDocument ownerDocument, string name, string publicId, string systemId) :
    DomNode(DomNodeType.DocumentType, ownerDocument)
{
    public string Name { get; } = name;

    public string PublicId { get; } = publicId;

    public string SystemId { get; } = systemId;

    internal override DomNode CloneShallow(DomDocument ownerDocument) =>
        new DomDocumentType(ownerDocument, Name, PublicId, SystemId);
}
