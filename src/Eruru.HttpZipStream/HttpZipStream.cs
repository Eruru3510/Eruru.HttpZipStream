using System.Buffers;
using System.Collections.ObjectModel;
using System.Text;

namespace Eruru.HttpZipStream;

public class HttpZipStream : Stream {

	public override bool CanRead { get; } = true;
	public override bool CanSeek { get; } = true;
	public override bool CanWrite { get; }
	public override long Length => _Length;
	public override long Position { get; set; }
	public bool IsZip64 { get; private set; }
	public int HttpRequestCount { get; set; }
	public Uri? Uri { get; private set; }

	const int Zip64EocdLength = 56;
	const int Zip64LocatorLength = 20;
	const int Zip64Length = Zip64EocdLength + Zip64LocatorLength;
	const int EocdLength = 22;
	const int CommentLength = ushort.MaxValue;
	HttpCompletionOption HttpCompletionOption;
	HttpClient? HttpClient;
	Stream? CacheStream;
	Uri? RawUri;
	int BufferSize;
	long FileAreaLength;
	long TargetIndex;
	long TargetLength;
	long TempFileIndex;
	long TempFileLength;
	long _Length;
	int State;

	protected override void Dispose (bool disposing) {
		if (Interlocked.Exchange (ref State, 1) == 1 || !disposing) {
			return;
		}
		base.Dispose (disposing);
		HttpClient?.Dispose ();
		CacheStream?.Dispose ();
	}

	public HttpZipStream ConfigureHttpClient (
		Uri uri, HttpClient httpClient, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseHeadersRead
	) {
		CheckDisposed ();
		RawUri = uri;
		HttpClient = httpClient;
		HttpCompletionOption = httpCompletionOption;
		Uri = RawUri;
		return this;
	}

	public HttpZipStream ConfigureCache (Stream cacheStream, int bufferSize = 1024 * 1024) {
		CheckDisposed ();
		CacheStream = cacheStream;
		BufferSize = bufferSize;
		return this;
	}

	public HttpZipStream Build () {
		CheckDisposed ();
		if (HttpClient == null) {
			throw new ArgumentException (
				$"You need to call the {nameof (ConfigureHttpClient)} method to provide the {nameof (HttpClient)}."
			);
		}
		if (CacheStream == null) {
			throw new ArgumentException (
				$"You need to call the {nameof (ConfigureCache)} method to provide the {nameof (CacheStream)}."
			);
		}
		if (Interlocked.CompareExchange (ref State, 2, 0) != 0) {
			return this;
		}
		return this;
	}

	public override void Flush () {

	}

	public override void SetLength (long value) {
		throw new NotSupportedException ();
	}

	public override void Write (byte[] buffer, int offset, int count) {
		throw new NotSupportedException ();
	}

	public override long Seek (long offset, SeekOrigin origin) {
		CheckDisposed ();
		CheckBuild ();
		CheckOpen ();
		var position = origin switch {
			SeekOrigin.Begin => offset,
			SeekOrigin.Current => Position + offset,
			SeekOrigin.End => Length + offset,
			_ => throw new NotImplementedException ($"{origin}")
		};
		if (position < 0 || position >= Length) {
			throw new ArgumentOutOfRangeException (nameof (offset));
		}
		Position = position;
		return position;
	}

	public override int Read (byte[] buffer, int offset, int count) {
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
		return ReadAsync (buffer, offset, count, CancellationToken.None).GetAwaiter ().GetResult ();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
	}

#if NET
	public override Task<int> ReadAsync (byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
		return ReadAsync (buffer.AsMemory (offset, count), cancellationToken).AsTask ();
	}

	public override async ValueTask<int> ReadAsync (Memory<byte> buffer, CancellationToken cancellationToken = default) {
#else
	public override async Task<int> ReadAsync (byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
#endif
		CheckDisposed ();
		CheckBuild ();
		CheckOpen ();
		var isHitCache = false;
		var remainingLength = (int)Math.Min (
#if NET
			buffer.Length
#else
			count
#endif
		, Length - Position);
		if (Position >= TempFileIndex && Position - TempFileIndex + remainingLength <= TempFileLength) {
			CacheStream!.Position = TargetLength + Position - TempFileIndex;
			isHitCache = true;
		}
		if (!isHitCache && Position >= TargetIndex && Position - TargetIndex + remainingLength <= TargetLength) {
			CacheStream!.Position = Position - TargetIndex;
			isHitCache = true;
		}
		if (!isHitCache) {
			throw new InvalidDataException (
				$"The cache was not hit because the {nameof (PreloadAsync)} method was not called to prepare complete data or parsing failed."
			);
		}
		var length = await CacheStream!.ReadAsync (
#if NET
			buffer
#else
			buffer, offset, count
#endif
		, cancellationToken).ConfigureAwait (false);
		Position += length;
		return length;
	}

	public async Task OpenAsync<TContext> (Progress<TContext>? progress = null, CancellationToken cancellationToken = default) {
		CheckDisposed ();
		CheckBuild ();
		using var cancellationTokenSource = new CancellationTokenSource (HttpClient!.Timeout);
		using var cancellationTokenSource1 = CancellationTokenSource.CreateLinkedTokenSource (
			cancellationTokenSource.Token, cancellationToken
		);
		cancellationToken = cancellationTokenSource1.Token;
		var fileLength = await GetFileLengthAsync (HttpMethod.Get, cancellationToken).ConfigureAwait (false) ?? 0;
		if (fileLength <= 0) {
			throw new HttpRequestException ("Failed to retrieve HTTP response ContentRange");
		}
		var bufferLength = Math.Min (fileLength, Zip64Length + EocdLength + CommentLength);
		if (bufferLength < EocdLength) {
			throw new FileLoadException ("Invalid ZIP file");
		}
		CacheStream!.Position = 0;
		await ReadRangeAsync (
			fileLength - bufferLength, bufferLength, CacheStream, progress, cancellationToken
		).ConfigureAwait (false);
		await CacheStream.FlushAsync (cancellationToken).ConfigureAwait (false);
		var isZip = false;
		var isZip64 = false;
		var centralDirectoryLength = 0L;
		var centralDirectoryIndex = 0L;
		var commentLength = 0;
		using var binaryReader = new BinaryReader (CacheStream, Encoding.UTF8, true);
		for (var i = bufferLength - EocdLength; i >= Zip64Length; i--) {
			binaryReader.BaseStream.Position = i;
			if (binaryReader.ReadUInt32 () != 0x06054b50) {
				continue;
			}
			binaryReader.BaseStream.Position += 8;
			centralDirectoryLength = binaryReader.ReadUInt32 ();
			centralDirectoryIndex = binaryReader.ReadUInt32 ();
			commentLength = binaryReader.ReadUInt16 ();
			if (binaryReader.BaseStream.Position + commentLength != bufferLength) {
				continue;
			}
			isZip = true;
			var eocdIndex = fileLength - commentLength - EocdLength;
			if (centralDirectoryLength == uint.MaxValue || centralDirectoryIndex == uint.MaxValue) {
				isZip64 = true;
				binaryReader.BaseStream.Position = i - Zip64LocatorLength;
				if (binaryReader.ReadUInt32 () != 0x07064b50) {
					throw new NotSupportedException ("The ZIP64 Locator is not adjacent to the ZIP32 EOCD.");
				}
				binaryReader.BaseStream.Position += 4;
				var zip64EocdIndex = (long)binaryReader.ReadUInt64 ();
				eocdIndex -= Zip64LocatorLength;
				binaryReader.BaseStream.Position = i - Zip64LocatorLength - Zip64EocdLength;
				if (zip64EocdIndex + Zip64EocdLength != eocdIndex || binaryReader.ReadUInt32 () != 0x06064b50) {
					throw new NotSupportedException ("The ZIP64 Record is not adjacent to the ZIP64 Locator.");
				}
				binaryReader.BaseStream.Position += 36;
				centralDirectoryLength = (long)binaryReader.ReadUInt64 ();
				centralDirectoryIndex = (long)binaryReader.ReadUInt64 ();
				eocdIndex -= Zip64EocdLength;
			}
			if (centralDirectoryIndex + centralDirectoryLength != eocdIndex) {
				throw new NotSupportedException (
					$"The Central Directory is not adjacent to the ZIP{(isZip64 ? 64 : 32)} EOCD."
				);
			}
			break;
		}
		if (!isZip) {
			throw new FileLoadException ("Invalid ZIP file");
		}
		var targetIndex = centralDirectoryIndex;
		var targetLength = fileLength - centralDirectoryIndex;
		var commentIndex = fileLength - commentLength;
		var fileAreaLength = fileLength - targetLength;
		var bufferTargetIndex = bufferLength - targetLength;
		var bufferFileIndex = bufferLength - fileLength;
		var isSuccess = false;
		try {
			if (bufferTargetIndex >= 0) {
				if (bufferFileIndex >= 0) {
					targetIndex = 0;
					targetLength = 0;
					TempFileIndex = bufferFileIndex;
					TempFileLength = fileLength;
					isSuccess = true;
					return;
				}
				await BlockCopyAsync (
					CacheStream, bufferTargetIndex, CacheStream, 0, targetLength, cancellationToken
				).ConfigureAwait (false);
				isSuccess = true;
				return;
			}
			var remainingCentralDirectoryLength = Math.Abs (bufferTargetIndex);
			await BlockCopyAsync (
				CacheStream, 0, CacheStream, remainingCentralDirectoryLength, bufferLength, cancellationToken
			).ConfigureAwait (false);
			CacheStream.Position = 0;
			await ReadRangeAsync (
				centralDirectoryIndex, remainingCentralDirectoryLength, CacheStream, progress, cancellationToken
			).ConfigureAwait (false);
			await CacheStream.FlushAsync (cancellationToken).ConfigureAwait (false);
			isSuccess = true;
		} finally {
			if (isSuccess) {
				Interlocked.CompareExchange (ref State, 3, 2);
				_Length = fileLength;
				TargetIndex = targetIndex;
				TargetLength = targetLength;
				FileAreaLength = fileAreaLength;
				IsZip64 = isZip64;
			}
		}
	}
	public Task OpenAsync (CancellationToken cancellationToken = default) {
		return OpenAsync<object> (null, cancellationToken);
	}

	public async Task PreloadAsync<TContext> (
		PreloadRange preloadRange, Progress<TContext>? progress = null, CancellationToken cancellationToken = default
	) {
#if NET
		ArgumentNullException.ThrowIfNull (preloadRange, nameof (preloadRange));
#else
		if (preloadRange == null) {
			throw new ArgumentNullException (nameof (preloadRange));
		}
#endif
		CheckDisposed ();
		CheckBuild ();
		CheckOpen ();
		if (preloadRange.Index >= TempFileIndex && preloadRange.Index - TempFileIndex + preloadRange.Length <= TempFileLength) {
			return;
		}
		using var cancellationTokenSource = new CancellationTokenSource (HttpClient!.Timeout);
		using var cancellationTokenSource1 = CancellationTokenSource.CreateLinkedTokenSource (
			cancellationTokenSource.Token, cancellationToken
		);
		cancellationToken = cancellationTokenSource1.Token;
		CacheStream!.Position = TargetLength;
		await ReadRangeAsync (
			preloadRange.Index, preloadRange.Length, CacheStream, progress, cancellationToken
		).ConfigureAwait (false);
		TempFileIndex = preloadRange.Index;
		TempFileLength = preloadRange.Length;
	}
	public Task PreloadAsync (PreloadRange preloadRange, CancellationToken cancellationToken = default) {
		return PreloadAsync<object> (preloadRange, null, cancellationToken);
	}

	public Collection<PreloadRange> GetPreloadRanges (
		IEnumerable<IZipArchiveEntry> zipArchiveEntries, IEnumerable<IZipArchiveEntry> selectedZipArchiveEntries,
		int mergeAdjacentLength = 1024 * 1024
	) {
#if NET
		ArgumentNullException.ThrowIfNull (zipArchiveEntries, nameof (selectedZipArchiveEntries));
		ArgumentNullException.ThrowIfNull (selectedZipArchiveEntries, nameof (selectedZipArchiveEntries));
#else
		if (zipArchiveEntries == null) {
			throw new ArgumentNullException (nameof (zipArchiveEntries));
		}
		if (selectedZipArchiveEntries == null) {
			throw new ArgumentNullException (nameof (selectedZipArchiveEntries));
		}
#endif
		var selectedZipEntriesEnumerator = selectedZipArchiveEntries.GetEnumerator ();
		try {
			if (!selectedZipEntriesEnumerator.MoveNext ()) {
				return [];
			}
			var preloadRanges = new Collection<PreloadRange> ();
			List<IZipArchiveEntry>? currentZipArchiveEntries = null;
			var index = 0L;
			var length = 0L;
			var isEnd = false;
			var invalidIndex = -1L;
			foreach (var zipEntry in zipArchiveEntries) {
			Head:
				if (currentZipArchiveEntries == null) {
					if (zipEntry == selectedZipEntriesEnumerator.Current) {
						currentZipArchiveEntries = [zipEntry];
						index = zipEntry.OffsetOfLocalHeader;
						if (!selectedZipEntriesEnumerator.MoveNext ()) {
							isEnd = true;
						}
					}
					continue;
				}
				if (isEnd) {
					length = zipEntry.OffsetOfLocalHeader - index;
					preloadRanges.Add (new (index, length, currentZipArchiveEntries.AsReadOnly ()));
					isEnd = false;
					currentZipArchiveEntries = null;
					break;
				}
				if (zipEntry == selectedZipEntriesEnumerator.Current) {
					if (invalidIndex > -1 && zipEntry.OffsetOfLocalHeader - invalidIndex > mergeAdjacentLength) {
						length = invalidIndex - index;
						preloadRanges.Add (new (index, length, currentZipArchiveEntries.AsReadOnly ()));
						currentZipArchiveEntries = null;
						isEnd = false;
						invalidIndex = -1;
						goto Head;
					}
					invalidIndex = -1;
					currentZipArchiveEntries.Add (zipEntry);
					if (!selectedZipEntriesEnumerator.MoveNext ()) {
						isEnd = true;
					}
					continue;
				}
				if (invalidIndex > -1) {
					if (zipEntry.OffsetOfLocalHeader - invalidIndex > mergeAdjacentLength) {
						length = invalidIndex - index;
						preloadRanges.Add (new (index, length, currentZipArchiveEntries.AsReadOnly ()));
						currentZipArchiveEntries = null;
						isEnd = false;
						invalidIndex = -1;
					}
					continue;
				}
				invalidIndex = zipEntry.OffsetOfLocalHeader;
			}
			if (isEnd && currentZipArchiveEntries != null) {
				length = FileAreaLength - index;
				preloadRanges.Add (new (index, length, currentZipArchiveEntries.AsReadOnly ()));
			}
			return preloadRanges;
		} finally {
			selectedZipEntriesEnumerator.Dispose ();
		}
	}

	async Task<long?> GetFileLengthAsync (HttpMethod httpMethod, CancellationToken cancellationToken) {
		using var httpRequestMessage = new HttpRequestMessage (httpMethod, RawUri);
		httpRequestMessage.Headers.Range = new (0, 0);
		HttpRequestCount++;
		using var httpResponseMessage = await HttpClient!.SendAsync (
			httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken
		).ConfigureAwait (false);
		Uri = httpResponseMessage.RequestMessage?.RequestUri ?? Uri;
		return httpResponseMessage.Content.Headers.ContentRange?.Length;
	}

	async Task ReadRangeAsync<TContext> (
		long index, long length, Stream stream, Progress<TContext>? progress, CancellationToken cancellationToken
	) {
		using var httpRequestMessage = new HttpRequestMessage (HttpMethod.Get, RawUri);
		httpRequestMessage.Headers.Range = new (index, index + length - 1);
		HttpRequestCount++;
		using var httpResponseMessage = await HttpClient!.SendAsync (
			httpRequestMessage, HttpCompletionOption, cancellationToken
		).ConfigureAwait (false);
		using var inputStream = await httpResponseMessage.Content.ReadAsStreamAsync (
#if NET
			cancellationToken
#endif
		).ConfigureAwait (false);
		if (progress != null) {
			progress.MaxValue += length;
			progress.Resume ();
		}
		try {
			await CopyToAsync (inputStream, stream, progress, cancellationToken).ConfigureAwait (false);
		} finally {
			progress?.Pause ();
		}
	}

	void CheckDisposed () {
		if (Volatile.Read (ref State) != 1) {
			return;
		}
		throw new ObjectDisposedException (nameof (HttpZipStream));
	}

	void CheckBuild () {
		if (Volatile.Read (ref State) > 0) {
			return;
		}
		throw new ArgumentException ($"First, you need to call the {nameof (Build)} method.");
	}

	void CheckOpen () {
		if (Volatile.Read (ref State) == 3) {
			return;
		}
		throw new ArgumentException ($"First, you need to call the {nameof (OpenAsync)} method.");
	}

	async Task CopyToAsync<TContext> (
		Stream stream, Stream destinationStream, Progress<TContext>? progress, CancellationToken cancellationToken
	) {
		var buffer = new ArraySegment<byte> (ArrayPool<byte>.Shared.Rent (BufferSize), 0, BufferSize);
		try {
			while (true) {
				var readLength = await stream.ReadAsync (
#if NET
					buffer.Array.AsMemory (buffer.Offset, buffer.Count)
#else
					buffer.Array, buffer.Offset, buffer.Count
#endif
					, cancellationToken
				).ConfigureAwait (false);
				if (readLength < 1) {
					break;
				}
				await destinationStream.WriteAsync (
#if NET
					buffer.Array.AsMemory (buffer.Offset, readLength)
#else
					buffer.Array, buffer.Offset, readLength
#endif
					, cancellationToken
				).ConfigureAwait (false);
				progress?.Append (readLength);
			}
		} finally {
			ArrayPool<byte>.Shared.Return (buffer.Array!);
		}
	}

	async Task BlockCopyAsync (
		Stream stream, long index, Stream destinationStream, long destinationIndex, long length,
		CancellationToken cancellationToken
	) {
		var buffer = new ArraySegment<byte> (ArrayPool<byte>.Shared.Rent (BufferSize), 0, BufferSize);
		try {
			var remainingLength = length;
			var currentIndex = index;
			var offset = destinationIndex - index;
			while (remainingLength > 0) {
				stream.Position = currentIndex;
				var readLength = (int)Math.Min (buffer.Count, remainingLength);
				readLength = await stream.ReadAsync (
#if NET
					buffer.Array.AsMemory (buffer.Offset, readLength)
#else
					buffer.Array, buffer.Offset, readLength
#endif
					, cancellationToken
				).ConfigureAwait (false);
				if (readLength < 1) {
					break;
				}
				destinationStream.Position = currentIndex + offset;
				await destinationStream.WriteAsync (
#if NET
					buffer.Array.AsMemory (buffer.Offset, readLength)
#else
					buffer.Array, buffer.Offset, readLength
#endif
					, cancellationToken
				).ConfigureAwait (false);
				currentIndex += readLength;
				remainingLength -= readLength;
			}
		} finally {
			ArrayPool<byte>.Shared.Return (buffer.Array!);
		}
	}

}