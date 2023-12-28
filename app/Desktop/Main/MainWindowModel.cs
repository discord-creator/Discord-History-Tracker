using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using DHT.Desktop.Dialogs.Message;
using DHT.Desktop.Main.Screens;
using DHT.Desktop.Server;
using DHT.Server;
using DHT.Server.Database;
using DHT.Utils.Logging;
using DHT.Utils.Models;

namespace DHT.Desktop.Main;

sealed class MainWindowModel : BaseModel, IAsyncDisposable {
	private const string DefaultTitle = "Discord History Tracker";

	private static readonly Log Log = Log.ForType<MainWindowModel>();

	public string Title { get; private set; } = DefaultTitle;

	public UserControl CurrentScreen { get; private set; }

	private readonly WelcomeScreen welcomeScreen;
	private readonly WelcomeScreenModel welcomeScreenModel;

	private MainContentScreenModel? mainContentScreenModel;

	private readonly Window window;

	private State? state;

	[Obsolete("Designer")]
	public MainWindowModel() : this(null!, Arguments.Empty) {}

	public MainWindowModel(Window window, Arguments args) {
		this.window = window;

		welcomeScreenModel = new WelcomeScreenModel(window);
		welcomeScreenModel.DatabaseSelected += OnDatabaseSelected;

		welcomeScreen = new WelcomeScreen { DataContext = welcomeScreenModel };
		CurrentScreen = welcomeScreen;

		var dbFile = args.DatabaseFile;
		if (!string.IsNullOrWhiteSpace(dbFile)) {
			async void OnWindowOpened(object? o, EventArgs eventArgs) {
				window.Opened -= OnWindowOpened;

				// https://github.com/AvaloniaUI/Avalonia/issues/3071
				if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					await Task.Delay(500);
				}

				if (File.Exists(dbFile)) {
					await welcomeScreenModel.OpenOrCreateDatabaseFromPath(dbFile);
				}
				else {
					await Dialog.ShowOk(window, "Database Error", "Database file not found:\n" + dbFile);
				}
			}

			window.Opened += OnWindowOpened;
		}

		if (args.ServerPort != null) {
			ServerConfiguration.Port = args.ServerPort.Value;
		}

		if (args.ServerToken != null) {
			ServerConfiguration.Token = args.ServerToken;
		}
	}

	private async void OnDatabaseSelected(object? sender, IDatabaseFile db) {
		welcomeScreenModel.DatabaseSelected -= OnDatabaseSelected;
		
		await DisposeState();
		
		state = new State(db);

		try {
			await state.Server.Start(ServerConfiguration.Port, ServerConfiguration.Token);
		} catch (Exception ex) {
			Log.Error(ex);
			await Dialog.ShowOk(window, "Internal Server Error", ex.Message);
		}

		mainContentScreenModel = new MainContentScreenModel(window, state);
		mainContentScreenModel.DatabaseClosed += MainContentScreenModelOnDatabaseClosed;
		
		Title = Path.GetFileName(state.Db.Path) + " - " + DefaultTitle;
		CurrentScreen = new MainContentScreen { DataContext = mainContentScreenModel };

		OnPropertyChanged(nameof(Title));
		OnPropertyChanged(nameof(CurrentScreen));

		window.Focus();
	}

	private async void MainContentScreenModelOnDatabaseClosed(object? sender, EventArgs e) {
		if (mainContentScreenModel != null) {
			mainContentScreenModel.DatabaseClosed -= MainContentScreenModelOnDatabaseClosed;
			mainContentScreenModel.Dispose();
			mainContentScreenModel = null;
		}

		await DisposeState();

		Title = DefaultTitle;
		CurrentScreen = welcomeScreen;

		welcomeScreenModel.DatabaseSelected += OnDatabaseSelected;
		
		OnPropertyChanged(nameof(Title));
		OnPropertyChanged(nameof(CurrentScreen));
	}

	private async Task DisposeState() {
		if (state != null) {
			await state.DisposeAsync();
			state = null;
		}
	}

	public async ValueTask DisposeAsync() {
		mainContentScreenModel?.Dispose();
		await DisposeState();
	}
}
