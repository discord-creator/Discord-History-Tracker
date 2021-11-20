using System;
using DHT.Server.Database;
using DHT.Server.Download;
using DHT.Utils.Models;

namespace DHT.Desktop.Main.Pages {
	sealed class AttachmentsPageModel : BaseModel, IDisposable {
		public string ToggleDownloadButtonText => downloadThread == null ? "Start Downloading" : "Stop Downloading";
		
		private readonly IDatabaseFile db;
		private BackgroundDownloadThread? downloadThread;

		public AttachmentsPageModel() : this(DummyDatabaseFile.Instance) {}

		public AttachmentsPageModel(IDatabaseFile db) {
			this.db = db;
		}

		public void OnClickToggleDownload() {
			if (downloadThread == null) {
				downloadThread = new BackgroundDownloadThread(db);
				downloadThread.Enqueue(db.GenerateDownloadItems());
			}
			else {
				Dispose();
			}
			
			OnPropertyChanged(nameof(ToggleDownloadButtonText));
		}

		public void Dispose() {
			downloadThread?.StopThread();
			downloadThread = null;
		}
	}
}
