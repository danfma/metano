using Metano.Annotations;
using Metano.Annotations.TypeScript;

namespace Metano.TypeScript.SolidJs.Web;

public static partial class Html
{
    [External, Name("HTMLElement")]
    public abstract record Element : Node
    {
        [Name("class")]
        public string? ClassName { get; init; }
        public string? Id { get; init; }
        public string? TextContent { get; init; }
    }
}
