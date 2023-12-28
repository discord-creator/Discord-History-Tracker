using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DHT.Utils.Tasks;

public abstract class ThrottledTaskBase<T> : IDisposable {
	private readonly Channel<Func<CancellationToken, T>> taskChannel = Channel.CreateBounded<Func<CancellationToken, T>>(new BoundedChannelOptions(capacity: 1) {
		SingleReader = true,
		SingleWriter = false,
		AllowSynchronousContinuations = false,
		FullMode = BoundedChannelFullMode.DropOldest
	});
	
	private readonly CancellationTokenSource cancellationTokenSource = new ();
	
	internal ThrottledTaskBase() {}

	protected async Task ReaderTask() {
		var cancellationToken = cancellationTokenSource.Token;

		try {
			await foreach (var item in taskChannel.Reader.ReadAllAsync(cancellationToken)) {
				try {
					await Run(item, cancellationToken);
				} catch (OperationCanceledException) {
					throw;
				} catch (Exception) {
					// Ignore.
				}
			}
		} catch (OperationCanceledException) {
			// Ignore.
		} finally {
			cancellationTokenSource.Dispose();
		}
	}
	
	protected abstract Task Run(Func<CancellationToken, T> func, CancellationToken cancellationToken);

	public void Post(Func<CancellationToken, T> resultComputer) {
		taskChannel.Writer.TryWrite(resultComputer);
	}

	public void Dispose() {
		taskChannel.Writer.Complete();
		cancellationTokenSource.Cancel();
	}
}

public sealed class ThrottledTask : ThrottledTaskBase<Task> {
	private readonly Action resultProcessor;
	private readonly TaskScheduler resultScheduler;

	public ThrottledTask(Action resultProcessor, TaskScheduler resultScheduler) {
		this.resultProcessor = resultProcessor;
		this.resultScheduler = resultScheduler;

		Task.Run(ReaderTask);
	}

	protected override async Task Run(Func<CancellationToken, Task> func, CancellationToken cancellationToken) {
		await func(cancellationToken);
		await Task.Factory.StartNew(resultProcessor, cancellationToken, TaskCreationOptions.None, resultScheduler);
	}
}

public sealed class ThrottledTask<T> : ThrottledTaskBase<Task<T>> {
	private readonly Action<T> resultProcessor;
	private readonly TaskScheduler resultScheduler;

	public ThrottledTask(Action<T> resultProcessor, TaskScheduler resultScheduler) {
		this.resultProcessor = resultProcessor;
		this.resultScheduler = resultScheduler;

		Task.Run(ReaderTask);
	}

	protected override async Task Run(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken) {
		T result = await func(cancellationToken);
		await Task.Factory.StartNew(() => resultProcessor(result), cancellationToken, TaskCreationOptions.None, resultScheduler);
	}
}
