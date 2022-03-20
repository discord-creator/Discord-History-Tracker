using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using DHT.Utils.Logging;
using Microsoft.Data.Sqlite;

namespace DHT.Server.Database.Sqlite.Utils {
	sealed class SqliteConnectionPool : IDisposable {
		private static string GetConnectionString(SqliteConnectionStringBuilder connectionStringBuilder) {
			connectionStringBuilder.Pooling = false;
			return connectionStringBuilder.ToString();
		}

		private readonly object monitor = new ();
		private volatile bool isDisposed;

		private readonly BlockingCollection<PooledConnection> free = new (new ConcurrentStack<PooledConnection>());
		private readonly List<PooledConnection> used;

		public SqliteConnectionPool(SqliteConnectionStringBuilder connectionStringBuilder, int poolSize) {
			var connectionString = GetConnectionString(connectionStringBuilder);

			for (int i = 0; i < poolSize; i++) {
				var conn = new SqliteConnection(connectionString);
				conn.Open();
				
				var pooledConn = new PooledConnection(this, conn);
				
				using (var cmd = pooledConn.Command("PRAGMA journal_mode=WAL")) {
					cmd.ExecuteNonQuery();
				}
				
				free.Add(pooledConn);
			}

			used = new List<PooledConnection>(poolSize);
		}

		private void ThrowIfDisposed() {
			if (isDisposed) {
				throw new ObjectDisposedException(nameof(SqliteConnectionPool));
			}
		}

		public ISqliteConnection Take() {
			PooledConnection? conn = null;
			
			while (conn == null) {
				ThrowIfDisposed();
				lock (monitor) {
					if (free.TryTake(out conn, TimeSpan.FromMilliseconds(100))) {
						used.Add(conn);
						break;
					}
					else {
						Log.ForType<SqliteConnectionPool>().Warn("Thread " + Thread.CurrentThread.ManagedThreadId + " is starving for connections.");
					}
				}
			}

			return conn;
		}

		private void Return(PooledConnection conn) {
			ThrowIfDisposed();

			lock (monitor) {
				if (used.Remove(conn)) {
					free.Add(conn);
				}
			}
		}

		public void Dispose() {
			if (isDisposed) {
				return;
			}

			isDisposed = true;
			
			lock (monitor) {
				while (free.TryTake(out var conn)) {
					Close(conn.InnerConnection);
				}

				foreach (var conn in used) {
					Close(conn.InnerConnection);
				}

				free.Dispose();
				used.Clear();
			}
		}

		private static void Close(SqliteConnection conn) {
			conn.Close();
			conn.Dispose();
		}

		private sealed class PooledConnection : ISqliteConnection {
			public SqliteConnection InnerConnection { get; }

			private readonly SqliteConnectionPool pool;

			public PooledConnection(SqliteConnectionPool pool, SqliteConnection conn) {
				this.pool = pool;
				this.InnerConnection = conn;
			}

			void IDisposable.Dispose() {
				pool.Return(this);
			}
		}
	}
}
