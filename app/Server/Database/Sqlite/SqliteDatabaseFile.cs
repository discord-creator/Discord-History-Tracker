using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading.Tasks;
using DHT.Server.Data;
using DHT.Server.Data.Filters;
using DHT.Server.Download;
using DHT.Utils.Collections;
using DHT.Utils.Logging;
using Microsoft.Data.Sqlite;

namespace DHT.Server.Database.Sqlite {
	public sealed class SqliteDatabaseFile : IDatabaseFile {
		public static async Task<SqliteDatabaseFile?> OpenOrCreate(string path, Func<Task<bool>> checkCanUpgradeSchemas) {
			string connectionString = new SqliteConnectionStringBuilder {
				DataSource = path,
				Mode = SqliteOpenMode.ReadWriteCreate
			}.ToString();

			var conn = new SqliteConnection(connectionString);
			conn.Open();

			return await new Schema(conn).Setup(checkCanUpgradeSchemas) ? new SqliteDatabaseFile(path, conn) : null;
		}

		public string Path { get; }
		public DatabaseStatistics Statistics { get; }

		private readonly Log log;
		private readonly SqliteConnection conn;

		private SqliteDatabaseFile(string path, SqliteConnection conn) {
			this.log = Log.ForType(typeof(SqliteDatabaseFile), System.IO.Path.GetFileName(path));
			this.conn = conn;
			this.Path = path;
			this.Statistics = new DatabaseStatistics();
			UpdateServerStatistics();
			UpdateChannelStatistics();
			UpdateUserStatistics();
			UpdateMessageStatistics();
		}

		public void Dispose() {
			conn.Dispose();
		}

		public void AddServer(Data.Server server) {
			using var cmd = conn.Upsert("servers", new[] {
				("id", SqliteType.Integer),
				("name", SqliteType.Text),
				("type", SqliteType.Text)
			});

			cmd.Set(":id", server.Id);
			cmd.Set(":name", server.Name);
			cmd.Set(":type", ServerTypes.ToString(server.Type));
			cmd.ExecuteNonQuery();
			UpdateServerStatistics();
		}

		public List<Data.Server> GetAllServers() {
			var perf = log.Start();
			var list = new List<Data.Server>();

			using var cmd = conn.Command("SELECT id, name, type FROM servers");
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) {
				list.Add(new Data.Server {
					Id = reader.GetUint64(0),
					Name = reader.GetString(1),
					Type = ServerTypes.FromString(reader.GetString(2))
				});
			}

			perf.End();
			return list;
		}

		public void AddChannel(Channel channel) {
			using var cmd = conn.Upsert("channels", new[] {
				("id", SqliteType.Integer),
				("server", SqliteType.Integer),
				("name", SqliteType.Text),
				("parent_id", SqliteType.Integer),
				("position", SqliteType.Integer),
				("topic", SqliteType.Text),
				("nsfw", SqliteType.Integer)
			});

			cmd.Set(":id", channel.Id);
			cmd.Set(":server", channel.Server);
			cmd.Set(":name", channel.Name);
			cmd.Set(":parent_id", channel.ParentId);
			cmd.Set(":position", channel.Position);
			cmd.Set(":topic", channel.Topic);
			cmd.Set(":nsfw", channel.Nsfw);
			cmd.ExecuteNonQuery();
			UpdateChannelStatistics();
		}

		public List<Channel> GetAllChannels() {
			var list = new List<Channel>();

			using var cmd = conn.Command("SELECT id, server, name, parent_id, position, topic, nsfw FROM channels");
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) {
				list.Add(new Channel {
					Id = reader.GetUint64(0),
					Server = reader.GetUint64(1),
					Name = reader.GetString(2),
					ParentId = reader.IsDBNull(3) ? null : reader.GetUint64(3),
					Position = reader.IsDBNull(4) ? null : reader.GetInt32(4),
					Topic = reader.IsDBNull(5) ? null : reader.GetString(5),
					Nsfw = reader.IsDBNull(6) ? null : reader.GetBoolean(6)
				});
			}

			return list;
		}

		public void AddUsers(User[] users) {
			using var tx = conn.BeginTransaction();
			using var cmd = conn.Upsert("users", new[] {
				("id", SqliteType.Integer),
				("name", SqliteType.Text),
				("avatar_url", SqliteType.Text),
				("discriminator", SqliteType.Text)
			});

			foreach (var user in users) {
				cmd.Set(":id", user.Id);
				cmd.Set(":name", user.Name);
				cmd.Set(":avatar_url", user.AvatarUrl);
				cmd.Set(":discriminator", user.Discriminator);
				cmd.ExecuteNonQuery();
			}

			tx.Commit();
			UpdateUserStatistics();
		}

		public List<User> GetAllUsers() {
			var perf = log.Start();
			var list = new List<User>();

			using var cmd = conn.Command("SELECT id, name, avatar_url, discriminator FROM users");
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) {
				list.Add(new User {
					Id = reader.GetUint64(0),
					Name = reader.GetString(1),
					AvatarUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
					Discriminator = reader.IsDBNull(3) ? null : reader.GetString(3)
				});
			}

			perf.End();
			return list;
		}

		public void AddMessages(Message[] messages) {
			static SqliteCommand DeleteByMessageId(SqliteConnection conn, string tableName) {
				return conn.Delete(tableName, ("message_id", SqliteType.Integer));
			}

			static void ExecuteDeleteByMessageId(SqliteCommand cmd, object id) {
				cmd.Set(":message_id", id);
				cmd.ExecuteNonQuery();
			}

			using var tx = conn.BeginTransaction();

			using var messageCmd = conn.Upsert("messages", new[] {
				("message_id", SqliteType.Integer),
				("sender_id", SqliteType.Integer),
				("channel_id", SqliteType.Integer),
				("text", SqliteType.Text),
				("timestamp", SqliteType.Integer)
			});

			using var deleteEditTimestampCmd = DeleteByMessageId(conn, "edit_timestamps");
			using var deleteRepliedToCmd = DeleteByMessageId(conn, "replied_to");

			using var deleteAttachmentsCmd = DeleteByMessageId(conn, "attachments");
			using var deleteEmbedsCmd = DeleteByMessageId(conn, "embeds");
			using var deleteReactionsCmd = DeleteByMessageId(conn, "reactions");

			using var editTimestampCmd = conn.Insert("edit_timestamps", new [] {
				("message_id", SqliteType.Integer),
				("edit_timestamp", SqliteType.Integer)
			});

			using var repliedToCmd = conn.Insert("replied_to", new [] {
				("message_id", SqliteType.Integer),
				("replied_to_id", SqliteType.Integer)
			});

			using var attachmentCmd = conn.Insert("attachments", new[] {
				("message_id", SqliteType.Integer),
				("attachment_id", SqliteType.Integer),
				("name", SqliteType.Text),
				("type", SqliteType.Text),
				("url", SqliteType.Text),
				("size", SqliteType.Integer)
			});

			using var embedCmd = conn.Insert("embeds", new[] {
				("message_id", SqliteType.Integer),
				("json", SqliteType.Text)
			});

			using var reactionCmd = conn.Insert("reactions", new[] {
				("message_id", SqliteType.Integer),
				("emoji_id", SqliteType.Integer),
				("emoji_name", SqliteType.Text),
				("emoji_flags", SqliteType.Integer),
				("count", SqliteType.Integer)
			});

			foreach (var message in messages) {
				object messageId = message.Id;

				messageCmd.Set(":message_id", messageId);
				messageCmd.Set(":sender_id", message.Sender);
				messageCmd.Set(":channel_id", message.Channel);
				messageCmd.Set(":text", message.Text);
				messageCmd.Set(":timestamp", message.Timestamp);
				messageCmd.ExecuteNonQuery();

				ExecuteDeleteByMessageId(deleteEditTimestampCmd, messageId);
				ExecuteDeleteByMessageId(deleteRepliedToCmd, messageId);

				ExecuteDeleteByMessageId(deleteAttachmentsCmd, messageId);
				ExecuteDeleteByMessageId(deleteEmbedsCmd, messageId);
				ExecuteDeleteByMessageId(deleteReactionsCmd, messageId);

				if (message.EditTimestamp is {} timestamp) {
					editTimestampCmd.Set(":message_id", messageId);
					editTimestampCmd.Set(":edit_timestamp", timestamp);
					editTimestampCmd.ExecuteNonQuery();
				}

				if (message.RepliedToId is {} repliedToId) {
					repliedToCmd.Set(":message_id", messageId);
					repliedToCmd.Set(":replied_to_id", repliedToId);
					repliedToCmd.ExecuteNonQuery();
				}

				if (!message.Attachments.IsEmpty) {
					foreach (var attachment in message.Attachments) {
						attachmentCmd.Set(":message_id", messageId);
						attachmentCmd.Set(":attachment_id", attachment.Id);
						attachmentCmd.Set(":name", attachment.Name);
						attachmentCmd.Set(":type", attachment.Type);
						attachmentCmd.Set(":url", attachment.Url);
						attachmentCmd.Set(":size", attachment.Size);
						attachmentCmd.ExecuteNonQuery();
					}
				}

				if (!message.Embeds.IsEmpty) {
					foreach (var embed in message.Embeds) {
						embedCmd.Set(":message_id", messageId);
						embedCmd.Set(":json", embed.Json);
						embedCmd.ExecuteNonQuery();
					}
				}

				if (!message.Reactions.IsEmpty) {
					foreach (var reaction in message.Reactions) {
						reactionCmd.Set(":message_id", messageId);
						reactionCmd.Set(":emoji_id", reaction.EmojiId);
						reactionCmd.Set(":emoji_name", reaction.EmojiName);
						reactionCmd.Set(":emoji_flags", (int) reaction.EmojiFlags);
						reactionCmd.Set(":count", reaction.Count);
						reactionCmd.ExecuteNonQuery();
					}
				}
			}

			tx.Commit();
			UpdateMessageStatistics();
		}

		public int CountMessages(MessageFilter? filter = null) {
			using var cmd = conn.Command("SELECT COUNT(*) FROM messages" + filter.GenerateWhereClause());
			using var reader = cmd.ExecuteReader();

			return reader.Read() ? reader.GetInt32(0) : 0;
		}

		public List<Message> GetMessages(MessageFilter? filter = null) {
			var perf = log.Start();
			var list = new List<Message>();

			var attachments = GetAllAttachments();
			var embeds = GetAllEmbeds();
			var reactions = GetAllReactions();

			using var cmd = conn.Command(@"
SELECT m.message_id, m.sender_id, m.channel_id, m.text, m.timestamp, et.edit_timestamp, rt.replied_to_id
FROM messages m
LEFT JOIN edit_timestamps et ON m.message_id = et.message_id
LEFT JOIN replied_to rt ON m.message_id = rt.message_id" + filter.GenerateWhereClause("m"));
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) {
				ulong id = reader.GetUint64(0);

				list.Add(new Message {
					Id = id,
					Sender = reader.GetUint64(1),
					Channel = reader.GetUint64(2),
					Text = reader.GetString(3),
					Timestamp = reader.GetInt64(4),
					EditTimestamp = reader.IsDBNull(5) ? null : reader.GetInt64(5),
					RepliedToId = reader.IsDBNull(6) ? null : reader.GetUint64(6),
					Attachments = attachments.GetListOrNull(id)?.ToImmutableArray() ?? ImmutableArray<Attachment>.Empty,
					Embeds = embeds.GetListOrNull(id)?.ToImmutableArray() ?? ImmutableArray<Embed>.Empty,
					Reactions = reactions.GetListOrNull(id)?.ToImmutableArray() ?? ImmutableArray<Reaction>.Empty
				});
			}

			perf.End();
			return list;
		}

		public void RemoveMessages(MessageFilter filter, MessageFilterRemovalMode mode) {
			var whereClause = filter.GenerateWhereClause(invert: mode == MessageFilterRemovalMode.KeepMatching);
			if (string.IsNullOrEmpty(whereClause)) {
				return;
			}

			var perf = log.Start();

			// Rider is being stupid...
			StringBuilder build = new StringBuilder()
			                      .Append("DELETE ")
			                      .Append("FROM messages")
			                      .Append(whereClause);

			using var cmd = conn.Command(build.ToString());
			cmd.ExecuteNonQuery();

			UpdateMessageStatistics();
			perf.End();
		}

		public void AddDownload(Data.Download download) {
			using var cmd = conn.Upsert("downloads", new[] {
				("url", SqliteType.Text),
				("status", SqliteType.Integer),
				("blob", SqliteType.Blob)
			});
			
			cmd.Set(":url", download.Url);
			cmd.Set(":status", download.Status);
			cmd.Set(":blob", download.Data);
			cmd.ExecuteNonQuery();
		}

		public List<DownloadItem> GenerateDownloadItems() {
			var list = new List<DownloadItem>();
			
			using var cmd = conn.Command("SELECT DISTINCT a.url FROM attachments a WHERE a.url NOT IN (SELECT d.url FROM downloads d WHERE d.status = 200)");
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) {
				string url = reader.GetString(0);
				list.Add(new DownloadItem(url));
			}
			
			return list;
		}

		private MultiDictionary<ulong, Attachment> GetAllAttachments() {
			var dict = new MultiDictionary<ulong, Attachment>();

			using var cmd = conn.Command("SELECT message_id, attachment_id, name, type, url, size FROM attachments");
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) {
				ulong messageId = reader.GetUint64(0);

				dict.Add(messageId, new Attachment {
					Id = reader.GetUint64(1),
					Name = reader.GetString(2),
					Type = reader.IsDBNull(3) ? null : reader.GetString(3),
					Url = reader.GetString(4),
					Size = reader.GetUint64(5)
				});
			}

			return dict;
		}

		private MultiDictionary<ulong, Embed> GetAllEmbeds() {
			var dict = new MultiDictionary<ulong, Embed>();

			using var cmd = conn.Command("SELECT message_id, json FROM embeds");
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) {
				ulong messageId = reader.GetUint64(0);

				dict.Add(messageId, new Embed {
					Json = reader.GetString(1)
				});
			}

			return dict;
		}

		private MultiDictionary<ulong, Reaction> GetAllReactions() {
			var dict = new MultiDictionary<ulong, Reaction>();

			using var cmd = conn.Command("SELECT message_id, emoji_id, emoji_name, emoji_flags, count FROM reactions");
			using var reader = cmd.ExecuteReader();

			while (reader.Read()) {
				ulong messageId = reader.GetUint64(0);

				dict.Add(messageId, new Reaction {
					EmojiId = reader.IsDBNull(1) ? null : reader.GetUint64(1),
					EmojiName = reader.IsDBNull(2) ? null : reader.GetString(2),
					EmojiFlags = (EmojiFlags) reader.GetInt16(3),
					Count = reader.GetInt32(4)
				});
			}

			return dict;
		}

		private void UpdateServerStatistics() {
			Statistics.TotalServers = conn.SelectScalar("SELECT COUNT(*) FROM servers") as long? ?? 0;
		}

		private void UpdateChannelStatistics() {
			Statistics.TotalChannels = conn.SelectScalar("SELECT COUNT(*) FROM channels") as long? ?? 0;
		}

		private void UpdateUserStatistics() {
			Statistics.TotalUsers = conn.SelectScalar("SELECT COUNT(*) FROM users") as long? ?? 0;
		}

		private void UpdateMessageStatistics() {
			Statistics.TotalMessages = conn.SelectScalar("SELECT COUNT(*) FROM messages") as long? ?? 0L;
		}
	}
}
