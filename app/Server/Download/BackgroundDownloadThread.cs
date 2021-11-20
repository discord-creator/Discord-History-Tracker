using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using DHT.Server.Data;
using DHT.Server.Database;
using DHT.Utils.Logging;
using DHT.Utils.Models;

namespace DHT.Server.Download {
	public sealed class BackgroundDownloadThread : BaseModel {
		private static readonly Log Log = Log.ForType<BackgroundDownloadThread>();

		public int EnqueuedCount => items.Count;

		private readonly CancellationTokenSource cancellationTokenSource = new();
		private readonly CancellationToken cancellationToken;

		private readonly BlockingCollection<DownloadItem> items = new (new ConcurrentQueue<DownloadItem>());

		public BackgroundDownloadThread(IDatabaseFile db) {
			this.cancellationToken = cancellationTokenSource.Token;

			var thread = new Thread(new ThreadInstance().Work) {
				Name = "DHT download thread"
			};

			thread.Start(new ThreadInstance.Parameters(db, items, cancellationTokenSource));
		}

		public void Enqueue(IEnumerable<DownloadItem> items) {
			try {
				foreach (var item in items) {
					this.items.Add(item, cancellationToken);
				}

				OnPropertyChanged(nameof(EnqueuedCount));
			} catch (ObjectDisposedException) {
				Log.Warn("Attempted to enqueue items to background download thread after the cancellation token has been disposed.");
			}
		}

		public void StopThread() {
			try {
				cancellationTokenSource.Cancel();
			} catch (ObjectDisposedException) {
				Log.Warn("Attempted to stop background download thread after the cancellation token has been disposed.");
			}
		}

		private sealed class ThreadInstance {
			public sealed class Parameters {
				public IDatabaseFile Db { get; }
				public BlockingCollection<DownloadItem> Items { get; }
				public CancellationTokenSource CancellationTokenSource { get; }

				public Parameters(IDatabaseFile db, BlockingCollection<DownloadItem> items, CancellationTokenSource cancellationTokenSource) {
					Db = db;
					Items = items;
					CancellationTokenSource = cancellationTokenSource;
				}
			}

			private readonly WebClient client = new ();

			public ThreadInstance() {
				client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/99.0.4844.51 Safari/537.36";
			}

			public void Work(object? obj) {
				var parameters = (Parameters) obj!;

				var cancellationTokenSource = parameters.CancellationTokenSource;
				var cancellationToken = cancellationTokenSource.Token;

				var db = parameters.Db;
				var items = parameters.Items;

				cancellationToken.Register(client.CancelAsync);

				try {
					foreach (var item in items.GetConsumingEnumerable(cancellationToken)) {
						var url = item.Url;
						Log.Info("Downloading " + url + " (" + items.Count + " item(s) in queue)...");

						try {
							db.AddDownload(new Data.Download(url, (int) AttachmentDownloadStatus.Success, client.DownloadData(url)));
						} catch (WebException e) {
							db.AddDownload(new Data.Download(url, e.Response is HttpWebResponse response ? (int) response.StatusCode : (int) AttachmentDownloadStatus.GenericError));
							Log.Error(e);
						}
					}
				} catch (OperationCanceledException) {
					//
				} finally {
					items.Dispose();
					cancellationTokenSource.Dispose();
				}
			}
		}
	}
}
