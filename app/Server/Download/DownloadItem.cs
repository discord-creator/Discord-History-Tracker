namespace DHT.Server.Download {
	public readonly struct DownloadItem {
		public string Url { get; }
		
		public DownloadItem(string url) {
			Url = url;
		}
	}
}
