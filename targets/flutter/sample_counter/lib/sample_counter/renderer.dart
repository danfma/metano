import 'i_view.dart';

final class Renderer {
  Renderer(String containerId) {
    this._container = Js.document.getElementById(containerId) ?? this.createElement(containerId);
  }

  late final HTMLElement _container;

  HTMLElement createElement(String containerId) {
    var element = document.createElement('div') as TElement;
    element.id = containerId;
    Js.document.body.append([element]);
    return element;
  }

  void render(IView view) => view.render(this._container);
}
