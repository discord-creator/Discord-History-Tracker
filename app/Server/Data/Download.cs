namespace DHT.Server.Data {
	public readonly struct Download {
		public string Url { get; internal init; }
		public int Status { get; internal init; }
		public byte[]? Data { get; internal init; }
		
		public Download(string url, int status, byte[]? data = null) {
			Url = url;
			Status = status;
			Data = data;
		}
	}
}
