using System.Text;
using Eruru.HttpZipStream;

namespace Eruru.HttpZipStreamSample;

sealed internal class Program {

	static async Task Main () {
		Encoding.RegisterProvider (CodePagesEncodingProvider.Instance);
		var url = "https://download.jetbrains.com/idea/idea-2026.1.2.win.zip";
		using var httpClient = new HttpClient ();
		using var memoryStream = new MemoryStream ();
		using var httpZipStream = new HttpZipStream.HttpZipStream ();
		httpZipStream.ConfigureHttpClient (new (url), httpClient);
		httpZipStream.ConfigureCache (memoryStream);
		httpZipStream.Build ();
		var progress = new HttpZipStream.Progress<Context> (static progress => {
			Console.WriteLine ($"Downloading {progress.Value:#,0.##}/{progress.MaxValue:#,0.##}\t{progress.Speed:#,0.##}/S");
			progress.Context?.TotalDownloadedLength = progress.Value;
		}, context: new Context ());
		await httpZipStream.OpenAsync (progress).ConfigureAwait (false);
		using var zipFile = new ICSharpCode.SharpZipLib.Zip.ZipFile (
			httpZipStream, false, ICSharpCode.SharpZipLib.Zip.StringCodec.Default
		);
		var zipArchiveEntries = new List<IZipArchiveEntry> ();
		for (var i = 0; i < zipFile.Count; i++) {
			var zipEntry = new ZipEntry (zipFile, zipFile[i]);
			if (zipEntry.IsDirectory) {
				continue;
			}
			Console.WriteLine ($"{i}\t{zipEntry.OffsetOfLocalHeader:#,0.##}\t{zipEntry.CompressedLength:#,0.##}\t{zipEntry.FullName}");
			zipArchiveEntries.Add (zipEntry);
		}
		// 提供文件列表和选择的文件列表，注意两者都要过滤掉目录并且按照原始顺序排列，选择的文件列表不能有重复项，也不能有非文件列表内含有的文件
		// Provide the full file list and the selected file list.
		// Both lists should exclude directories and preserve the original order.
		// The selected file list must not contain duplicates,
		// and every selected file must be included in the full file list.
		var perloadRanges = httpZipStream.GetPreloadRanges (zipArchiveEntries, [zipArchiveEntries[0]]);
		var buffer = new byte[1024 * 1024];
		foreach (var perloadRange in perloadRanges) {
			Console.WriteLine (
				$"PreloadRange Index: {perloadRange.Index:#,0.##} Length: {perloadRange.Length:#,0.##} FileCount: {perloadRange.ZipArchiveEntries.Count}"
			);
			progress.Reset ();
			await httpZipStream.PreloadAsync (perloadRange, progress).ConfigureAwait (false);
			foreach (var zipArchiveEntry in perloadRange.ZipArchiveEntries) {
				using var inputStream = zipArchiveEntry.Open ();
				while (await inputStream.ReadAsync (buffer.AsMemory ()).ConfigureAwait (false) > 0) {

				}
				Console.WriteLine ($"Decompressed {zipArchiveEntry.FullName}");
			}
		}
	}

	internal sealed class Context {

		public long TotalDownloadedLength { get; set; }

	}

}