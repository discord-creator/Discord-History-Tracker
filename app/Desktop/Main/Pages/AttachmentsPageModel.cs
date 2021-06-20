using System;
using DHT.Server.Database;
using DHT.Utils.Models;

namespace DHT.Desktop.Main.Pages {
	sealed class AttachmentsPageModel : BaseModel, IDisposable {
		private readonly IDatabaseFile db;

		public AttachmentsPageModel() : this(DummyDatabaseFile.Instance) {}

		public AttachmentsPageModel(IDatabaseFile db) {
			this.db = db;
		}

		public void Dispose() {
			// TODO
		}
	}
}
