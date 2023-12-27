using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using DHT.Desktop.Main.Controls;
using DHT.Desktop.Main.Pages;
using DHT.Server;
using DHT.Utils.Logging;

namespace DHT.Desktop.Main.Screens;

sealed class MainContentScreenModel : IDisposable {
	private static readonly Log Log = Log.ForType<MainContentScreenModel>();

	public DatabasePage DatabasePage { get; }
	private DatabasePageModel DatabasePageModel { get; }

	public TrackingPage TrackingPage { get; }
	private TrackingPageModel TrackingPageModel { get; }

	public AttachmentsPage AttachmentsPage { get; }
	private AttachmentsPageModel AttachmentsPageModel { get; }

	public ViewerPage ViewerPage { get; }
	private ViewerPageModel ViewerPageModel { get; }

	public AdvancedPage AdvancedPage { get; }
	private AdvancedPageModel AdvancedPageModel { get; }

	public DebugPage? DebugPage { get; }

	#if DEBUG
	public bool HasDebugPage => true;
	private DebugPageModel DebugPageModel { get; }
	#else
	public bool HasDebugPage => false;
	#endif

	public StatusBarModel StatusBarModel { get; }

	public event EventHandler? DatabaseClosed {
		add {
			DatabasePageModel.DatabaseClosed += value;
		}
		remove {
			DatabasePageModel.DatabaseClosed -= value;
		}
	}

	[Obsolete("Designer")]
	public MainContentScreenModel() : this(null!, State.Dummy) {}

	public MainContentScreenModel(Window window, State state) {
		DatabasePageModel = new DatabasePageModel(window, state);
		DatabasePage = new DatabasePage { DataContext = DatabasePageModel };

		TrackingPageModel = new TrackingPageModel(window);
		TrackingPage = new TrackingPage { DataContext = TrackingPageModel };

		AttachmentsPageModel = new AttachmentsPageModel(state);
		AttachmentsPage = new AttachmentsPage { DataContext = AttachmentsPageModel };

		ViewerPageModel = new ViewerPageModel(window, state);
		ViewerPage = new ViewerPage { DataContext = ViewerPageModel };

		AdvancedPageModel = new AdvancedPageModel(window, state);
		AdvancedPage = new AdvancedPage { DataContext = AdvancedPageModel };

		#if DEBUG
		DebugPageModel = new DebugPageModel(window, state);
		DebugPage = new DebugPage { DataContext = DebugPageModel };
		#else
		DebugPage = null;
		#endif

		StatusBarModel = new StatusBarModel(state);
	}

	public async Task Initialize() {
		await TrackingPageModel.Initialize();
	}

	public void Dispose() {
		AttachmentsPageModel.Dispose();
		ViewerPageModel.Dispose();
		AdvancedPageModel.Dispose();
		StatusBarModel.Dispose();
	}
}
