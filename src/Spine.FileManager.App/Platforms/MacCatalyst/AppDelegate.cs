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
	public override void BuildMenu(IUIMenuBuilder builder)
	{
		base.BuildMenu(builder);
		builder.RemoveMenu(UIMenuIdentifier.File.GetConstant());
		builder.RemoveMenu(UIMenuIdentifier.Format.GetConstant());
		builder.RemoveMenu(UIMenuIdentifier.View.GetConstant());
	}

	public override bool CanPerform(Selector action, NSObject? withSender) =>
		action.Name == "selectAll:" ? AppMenu.Explorer is not null : base.CanPerform(action, withSender);

	[Export("selectAll:")]
	public void SelectAll(NSObject? sender) => AppMenu.Explorer?.SelectAllCommand.Execute(null);
}
