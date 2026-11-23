import 'counter.dart';
import 'i_counter_view.dart';
import 'renderer.dart';

final class CounterPresenter {
  CounterPresenter(ICounterView view, Counter initialState) {
    this._view = view;
    this._state = initialState;
  }

  late Counter _state;

  void startApplication(String containerName) {
    var renderer = Renderer(containerName);
    renderer.render(this._view);
    this._view.onButtonClick = this.onButtonClicked;
    this._view.showCounter(this._state.count);
  }

  void onButtonClicked() {
    this._state = this._state.increment();
    this._view.showCounter(this._state.count);
  }

  late final ICounterView _view;
}
