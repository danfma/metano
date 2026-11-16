using SampleCounterV2.Models;
using SampleCounterV2.Mvu;
using SampleCounterV2.Mvu.Widgets;

App.Mount(containerId: "root", initialState: Counter.Zero, view: (state, setState) =>
    new Column([
        new Text(state.Count.ToString()),
        new Button("Click me", onPressed: () => setState(state.Increment())),
    ])
);
