import 'counter.dart';
import 'i_counter_view.dart';

final class CounterPresenter {
  CounterPresenter(ICounterView view) {
    this._view = view;
    this.initialize();
  }

  late final ICounterView _view;

  Counter _counter = Counter.zero;

  void initialize() => this._view.displayCounter(this._counter);

  void increment() {
    this._counter = this._counter.increment();
    this.displayCounter();
  }

  void decrement() {
    this._counter = this._counter.decrement();
    this.displayCounter();
  }

  void displayCounter() => this._view.displayCounter(this._counter);
}
