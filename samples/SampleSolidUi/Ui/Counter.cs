using Metano.TypeScript.SolidJs;
using Metano.TypeScript.SolidJs.Web;

namespace SampleSolidUi.Ui;

public sealed record Counter : JsxComponent
{
    public int Count { get; init; }

    public override JsxElement Render()
    {
        var count = Solid.CreateSignal(Count);

        MouseClickHandler<Html.Button> decrement = _ => count.Set(count.Value - 1);
        MouseClickHandler<Html.Button> increment = _ => count.Set(count.Value + 1);

        return new Html.Div
        {
            ClassName = "counter",
            Children =
            [
                new Html.Button
                {
                    ClassName = "action",
                    OnClick = decrement,
                    Children = [Text("-")],
                },
                new Html.Span { ClassName = "display", Children = [Text(count.Value)] },
                new Html.Button
                {
                    ClassName = "action",
                    OnClick = increment,
                    Children = [Text("+")],
                },
            ],
        };
    }
}
