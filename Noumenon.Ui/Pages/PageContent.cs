using Noumenon.Ui.Widgets;

namespace Noumenon.Ui.Pages;

/// <summary>One tab of the editor: its widgets and the chassis drawing beneath them.</summary>
public sealed record PageContent(IReadOnlyList<Widget> Widgets, Action<Graphics>? DrawStatic);
