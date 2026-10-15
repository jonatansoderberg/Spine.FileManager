using Foundation;
using ObjCRuntime;
using UIKit;

namespace Spine.FileManager;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	// The app restores its own windows and tabs (SessionTracker); macOS keeping and restoring the scenes as well gave
	// windows twice, so the system's keeping is turned off.
	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		NSUserDefaults.StandardUserDefaults.SetBool(false, "NSQuitAlwaysKeepsWindows");
		WindowMaterial.Start();
		WindowAppearance.Start();
		return base.FinishedLaunching(application, launchOptions);
	}

	// The router's port for linked devices goes down with the app; a second is enough for the router to answer.
	public override void WillTerminate(UIApplication application)
	{
		IPlatformApplication.Current?.Services.GetService<Services.DeviceService>()?.UnmapAsync().Wait(TimeSpan.FromSeconds(1));
		base.WillTerminate(application);
	}

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
		// The system's Close (⌘W) closes the window; in the explorer ⌘W closes a tab (File › Close Tab).
		builder.RemoveMenu(UIMenuIdentifier.Close.GetConstant());
		foreach (var identifier in new[] { UIMenuIdentifier.File, UIMenuIdentifier.View })
			if (builder.GetMenu(identifier.GetConstant()) is { } menu && !AppMenu.Owns(Titles(menu)))
				builder.RemoveMenu(identifier.GetConstant());
	}

	static IEnumerable<string> Titles(UIMenu menu) =>
		menu.Children.SelectMany(child => child is UIMenu inner ? Titles(inner).Prepend(inner.Title) : [child.Title]);

	public override bool CanPerform(Selector action, NSObject? withSender) => action.Name switch
	{
		"selectAll:" => AppMenu.Explorer is not null,
		"copy:" or "cut:" => AppMenu.Explorer?.HasSelection == true,
		"paste:" => AppMenu.Explorer is not null && FileClipboard.HasItems,
		// A text field being edited takes undo: itself; the explorer's undo comes after it in the responder chain.
		"undo:" => AppMenu.Explorer is not null && ExplorerPageViewModel.CanUndo,
		// Escape is taken only while the path field is open; otherwise it goes on to whoever wants it (a sheet).
		"closePathField:" => AppMenu.Explorer?.IsEditingPath == true,
		_ => base.CanPerform(action, withSender),
	};

	[Export("selectAll:")]
	public void SelectAll(NSObject? sender) => AppMenu.Explorer?.SelectAllCommand.Execute(null);

	[Export("copy:")]
	public void Copy(NSObject? sender) => AppMenu.Explorer?.CopyCommand.Execute(null);

	[Export("cut:")]
	public void Cut(NSObject? sender) => AppMenu.Explorer?.CutCommand.Execute(null);

	[Export("undo:")]
	public void Undo(NSObject? sender) => AppMenu.Explorer?.UndoLastCommand.Execute(null);

	[Export("paste:")]
	public void Paste(NSObject? sender) => AppMenu.Explorer?.PasteCommand.Execute(null);

	// A text field has no Escape of its own in MAUI; the app's key command closes the path field, last in the responder chain.
	public override UIKit.UIKeyCommand[] KeyCommands =>
		[UIKit.UIKeyCommand.Create(UIKit.UIKeyCommand.Escape, 0, new ObjCRuntime.Selector("closePathField:"))];

	[Export("closePathField:")]
	public void ClosePathField(NSObject? sender) => AppMenu.Explorer?.CancelPathEdit();
}
