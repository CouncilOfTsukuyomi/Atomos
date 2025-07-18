using Avalonia;

namespace Atomos.UI.Interfaces.Tutorial;

public interface IElementHighlightService
{
    void HighlightElement(string elementName, Visual? rootVisual = null);
    void RemoveHighlight();
    Rect? GetElementBounds(string elementName, Visual? rootVisual = null);
}
