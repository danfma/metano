using Metano.TypeScript.DOM;
using SampleSolidUi.Ui;
using static Metano.TypeScript.SolidJs.Web.SolidRenderer;

var container = Js.Document.GetElementById("root") ?? Js.Document.Body;

Render(() => new CounterGroup(), container);
