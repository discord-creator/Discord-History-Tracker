using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DHT.Desktop.Common;
using DHT.Desktop.Dialogs.File;
using DHT.Desktop.Dialogs.Message;
using DHT.Desktop.Main.Controls;
using DHT.Desktop.Server;
using DHT.Server;
using DHT.Server.Data.Filters;
using DHT.Server.Database.Export;
using DHT.Server.Database.Export.Strategy;
using DHT.Utils.Models;
using static DHT.Desktop.Program;

namespace DHT.Desktop.Main.Pages;

sealed class ViewerPageModel : BaseModel, IDisposable {
	public static readonly ConcurrentBag<string> TemporaryFiles = new ();

	public bool DatabaseToolFilterModeKeep { get; set; } = true;
	public bool DatabaseToolFilterModeRemove { get; set; } = false;

	private bool hasFilters = false;

	public bool HasFilters {
		get => hasFilters;
		set => Change(ref hasFilters, value);
	}

	public MessageFilterPanelModel FilterModel { get; }

	private readonly Window window;
	private readonly State state;

	[Obsolete("Designer")]
	public ViewerPageModel() : this(null!, State.Dummy) {}

	public ViewerPageModel(Window window, State state) {
		this.window = window;
		this.state = state;

		FilterModel = new MessageFilterPanelModel(window, state, "Will export");
		FilterModel.FilterPropertyChanged += OnFilterPropertyChanged;
	}

	public void Dispose() {
		FilterModel.Dispose();
	}

	private void OnFilterPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		HasFilters = FilterModel.HasAnyFilters;
	}

	private async Task WriteViewerFile(string path, IViewerExportStrategy strategy) {
		const string ArchiveTag = "/*[ARCHIVE]*/";

		string indexFile = await Resources.ReadTextAsync("Viewer/index.html");
		string viewerTemplate = indexFile.Replace("/*[JS]*/", await Resources.ReadJoinedAsync("Viewer/scripts/", '\n'))
		                                 .Replace("/*[CSS]*/", await Resources.ReadJoinedAsync("Viewer/styles/", '\n'));

		int viewerArchiveTagStart = viewerTemplate.IndexOf(ArchiveTag);
		int viewerArchiveTagEnd = viewerArchiveTagStart + ArchiveTag.Length;

		string jsonTempFile = path + ".tmp";

		await using (var jsonStream = new FileStream(jsonTempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.Read)) {
			await ViewerJsonExport.Generate(jsonStream, strategy, state.Db, FilterModel.CreateFilter());

			char[] jsonBuffer = new char[Math.Min(32768, jsonStream.Position)];
			jsonStream.Position = 0;

			await using (var outputStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
			await using (var outputWriter = new StreamWriter(outputStream, Encoding.UTF8)) {
				await outputWriter.WriteAsync(viewerTemplate[..viewerArchiveTagStart]);

				using (var jsonReader = new StreamReader(jsonStream, Encoding.UTF8)) {
					int readBytes;
					while ((readBytes = await jsonReader.ReadAsync(jsonBuffer, 0, jsonBuffer.Length)) > 0) {
						string jsonChunk = new string(jsonBuffer, 0, readBytes);
						await outputWriter.WriteAsync(HttpUtility.JavaScriptStringEncode(jsonChunk));
					}
				}

				await outputWriter.WriteAsync(viewerTemplate[viewerArchiveTagEnd..]);
			}
		}

		File.Delete(jsonTempFile);
	}

	public async void OnClickOpenViewer() {
		string rootPath = Path.Combine(Path.GetTempPath(), "DiscordHistoryTracker");
		string filenameBase = Path.GetFileNameWithoutExtension(state.Db.Path) + "-" + DateTime.Now.ToString("yyyy-MM-dd");
		string fullPath = Path.Combine(rootPath, filenameBase + ".html");
		int counter = 0;

		while (File.Exists(fullPath)) {
			++counter;
			fullPath = Path.Combine(rootPath, filenameBase + "-" + counter + ".html");
		}

		TemporaryFiles.Add(fullPath);

		Directory.CreateDirectory(rootPath);
		await WriteViewerFile(fullPath, new LiveViewerExportStrategy(ServerConfiguration.Port, ServerConfiguration.Token));

		Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
	}

	private static readonly FilePickerFileType[] ViewerFileTypes = {
		FileDialogs.CreateFilter("Discord History Viewer", new string[] { "html" }),
	};
	
	public async void OnClickSaveViewer() {
		string? path = await window.StorageProvider.SaveFile(new FilePickerSaveOptions {
			Title = "Save Viewer",
			FileTypeChoices = ViewerFileTypes,
			SuggestedFileName = Path.GetFileNameWithoutExtension(state.Db.Path) + ".html",
			SuggestedStartLocation = await FileDialogs.GetSuggestedStartLocation(window, Path.GetDirectoryName(state.Db.Path)),
		});

		if (path != null) {
			await WriteViewerFile(path, StandaloneViewerExportStrategy.Instance);
		}
	}

	public async void OnClickApplyFiltersToDatabase() {
		var filter = FilterModel.CreateFilter();

		if (DatabaseToolFilterModeKeep) {
			if (DialogResult.YesNo.Yes == await Dialog.ShowYesNo(window, "Keep Matching Messages in This Database", state.Db.CountMessages(filter).Pluralize("message") + " will be kept, and the rest will be removed from this database. This action cannot be undone. Proceed?")) {
				state.Db.RemoveMessages(filter, FilterRemovalMode.KeepMatching);
			}
		}
		else if (DatabaseToolFilterModeRemove) {
			if (DialogResult.YesNo.Yes == await Dialog.ShowYesNo(window, "Remove Matching Messages in This Database", state.Db.CountMessages(filter).Pluralize("message") + " will be removed from this database. This action cannot be undone. Proceed?")) {
				state.Db.RemoveMessages(filter, FilterRemovalMode.RemoveMatching);
			}
		}
	}
}
