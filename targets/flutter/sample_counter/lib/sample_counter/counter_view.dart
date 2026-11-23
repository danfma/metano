import 'i_counter_view.dart';

final class CounterView implements ICounterView {
  HTMLSpanElement? _text;

  void Function()? onButtonClick;

  void render(HTMLElement container) {
    var root = document.createElement('div') as TElement;
    container.append([root]);
    var text = document.createElement('span') as TElement;
    text.innerHTML = '0';
    root.append([text]);
    this._text = text;
    var button = document.createElement('button') as TElement;
    button.innerHTML = 'Click me';
    button.onclick = (MouseEvent _) => this.onButtonClick?.call();
    root.append([button]);
  }

  void showCounter(int counter) => (throw UnsupportedError('Unsupported expression: ConditionalAccess(SimpleAssignmentExpression)'));
}
