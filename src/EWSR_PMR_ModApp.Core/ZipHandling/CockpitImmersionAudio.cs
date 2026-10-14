using System.Text;
using System.Xml;

namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>Native implementation of the reviewed Cockpit Immersion Audio package, never its scripts.</summary>
public static class CockpitImmersionAudio
{
    public const string PatchId = "cockpit-immersion-audio-reviewed-1";
    public const string ArchiveHash = "494C21136D015A04766AE317E9F13967FB59718248FCFAB28371C76414E5FA12";
    private const string Credit = "Cockpit Immersion Audio adjusted by EWSR (Raw + Rowdy - Toned cockpit mix)";
    private static readonly (string Name, string Value)[] Targets =
    [
        ("interiorReverbMix", "0.24"),
        ("rtmEngineVolume", "6.2"),
        ("rtmExhaustVolume", "3.2"),
        ("rtmTransmissionVolume", "4.2"),
        ("rtmInteriorFXWindVolume", "0.95"),
        ("rtmInteriorFXRattlesVolume", "-3.0")
    ];

    public static bool Recognizes(string archiveHash) =>
        string.Equals(archiveHash, ArchiveHash, StringComparison.OrdinalIgnoreCase);

    public static byte[] Transform(byte[] original)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var source = new MemoryStream(original))
        using (var reader = XmlReader.Create(source, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 4 * 1024 * 1024
        }))
            document.Load(reader);

        var root = document.DocumentElement;
        if (root is null || root.Name != "audioConfig" || root.NamespaceURI.Length != 0)
            throw new InvalidDataException("Expected an unnamespaced <audioConfig> root.");
        if (root.ChildNodes.OfType<XmlElement>().Any(e => e.NamespaceURI.Length != 0))
            throw new InvalidDataException("Namespaced audio settings are not supported.");

        foreach (var (name, value) in Targets)
        {
            var matches = root.SelectNodes("./" + name)!;
            if (matches.Count > 1)
                throw new InvalidDataException($"Duplicate audio setting: {name}.");
            var element = matches.Count == 1 ? (XmlElement)matches[0]! : document.CreateElement(name);
            if (element.ChildNodes.OfType<XmlElement>().Any())
                throw new InvalidDataException($"Unexpected nested content in audio setting: {name}.");
            element.SetAttribute("value", value);
            if (matches.Count == 0)
                root.AppendChild(element);
        }

        bool credited = root.ChildNodes.OfType<XmlComment>().Any(c =>
        {
            int title = c.Value!.IndexOf("Cockpit Immersion Audio", StringComparison.OrdinalIgnoreCase);
            return title >= 0 && c.Value.IndexOf("EWSR", title, StringComparison.OrdinalIgnoreCase) >= 0;
        });
        if (!credited)
        {
            var comment = document.CreateComment(" " + Credit + " ");
            var firstElement = root.ChildNodes.OfType<XmlElement>().FirstOrDefault();
            if (firstElement is null) root.AppendChild(comment);
            else root.InsertBefore(comment, firstElement);
        }

        var declaration = document.ChildNodes.OfType<XmlDeclaration>().FirstOrDefault();
        bool littleEndian = original.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })
            || original.AsSpan().StartsWith(new byte[] { 0x3C, 0x00 });
        bool bigEndian = original.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })
            || original.AsSpan().StartsWith(new byte[] { 0x00, 0x3C });
        string? encodingName = declaration?.Encoding;
        if (string.IsNullOrEmpty(encodingName))
            encodingName = bigEndian ? "utf-16be" : littleEndian ? "utf-16le" : "utf-8";
        bool utf8Bom = original.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding encoding = encodingName.ToLowerInvariant() switch
        {
            "" or "utf-8" => new UTF8Encoding(utf8Bom, true),
            "utf-16" or "utf-16le" => new UnicodeEncoding(false, original.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }), true),
            "utf-16be" => new UnicodeEncoding(true, original.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }), true),
            _ => throw new InvalidDataException($"Unsupported audio XML encoding: {encodingName}.")
        };
        if (bigEndian)
            encoding = new UnicodeEncoding(true, original.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }), true);
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = encoding,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            OmitXmlDeclaration = declaration is null
        }))
            document.Save(writer);
        return output.ToArray();
    }
}
