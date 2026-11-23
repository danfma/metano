import 'i_view.dart';

abstract interface class ICounterView implements IView {
  void Function()? get onButtonClick;

  void showCounter(int counter);
}
