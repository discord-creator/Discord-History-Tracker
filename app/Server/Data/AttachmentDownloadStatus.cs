using System.Net;

namespace DHT.Server.Data {
	public enum AttachmentDownloadStatus {
		NotStarted = 0,
		GenericError = -1,
		Success = HttpStatusCode.OK
	}
}
