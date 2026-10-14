using Foundation;
using ObjCRuntime;
using UIKit;

namespace Spine.FileManager;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	// The app's own File and View menus (AppMenu) replace the system's; the system's Edit menu stays for text fields,
	// and its commands that reach the end of the responder chain act on the explorer instead.
	// MAUI lets an app menu titled exactly "File" or "View" take the system menu's place (English), and adds it beside
	// the system one otherwise (Swedish "Arkiv"); only a system menu that is not the app's own is removed.
	public override void BuildMenu(IUIMenuBuilder builder)
	{
		base.BuildMenu(builder);
		builder.RemoveMenu(UIMenuIdentifier.Format.GetConstant());
		// Duplicate, Move, Rename… and Export As… are a document app's; the explorer has its own Rename.
		builder.RemoveMenu(UIMenuIdentifier.Document.GetConstant());
		foreach (var identifier in new[] { UIMenuIdentifier.File, UIMenuIdentifier.View })
			if (builder.GetMenu(identifier.GetConstant()) is { } menu && !AppMenu.Owns(Titles(menu)))
				builder.RemoveMenu(identifier.GetConstant());
	}

	static IEnumerable<string> Titles(UIMenu menu) =>
		menu.Children.SelectMany(child => child is UIMenu inner ? Titles(inner).Prepend(inner.Title) : [child.Title]);

	public override bool CanPerform(Selector action, NSObject? withSender) =>
		action.Name == "selectAll:" ? AppMenu.Explorer is not null : base.CanPerform(action, withSender);

	[Export("selectAll:")]
	public void SelectAll(NSObject? sender) => AppMenu.Explorer?.SelectAllCommand.Execute(null);
}
