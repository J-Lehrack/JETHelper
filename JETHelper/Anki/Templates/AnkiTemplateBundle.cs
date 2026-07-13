using System.Collections.Generic;

namespace JETHelper.Anki.Templates;

/// <summary>
/// Describes one optional JETHelper note type that can be created through
/// AnkiConnect. The templates are kept in source control so installations are
/// deterministic and do not depend on external files or web resources.
/// </summary>
public sealed record AnkiTemplateBundle(
    string NoteTypeName,
    string CardTemplateName,
    string TemplateMarker,
    IReadOnlyList<string> Fields,
    string FrontTemplate,
    string BackTemplate,
    string Css);
