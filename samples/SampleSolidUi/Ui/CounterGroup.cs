using Metano.TypeScript.SolidJs;
using Metano.TypeScript.SolidJs.Web;

namespace SampleSolidUi.Ui;

public sealed record CounterGroup : JsxComponent
{
    public override JsxElement Render()
    {
        var (countersCount, setCountersCount) = Solid.CreateSignal(1);

        MouseClickHandler<Html.Button> addCounter = _ =>
            setCountersCount.Invoke(countersCount() + 1);

        return new Html.Div
        {
            Children =
            [
                new Html.Div
                {
                    ClassName = "counters",
                    Children =
                    [
                        Solid.For(
                            Enumerable.Range(0, countersCount()).ToList(),
                            (_, _) => new Counter()
                        ),
                    ],
                },
                new Html.Button
                {
                    ClassName = "add-action",
                    OnClick = addCounter,
                    Children = [Text("Add counter")],
                },
            ],
        };
    }
}
