using System.Text;
using Eruru.HttpZipStream;
using Eruru.HttpZipStreamSample;

namespace Eruru.HttpZipStreamTests {

	public class HttpZipStreamTest {

		[Fact]
		public async Task ZipArchiveEntry () {
			var url = "https://download.jetbrains.com/idea/idea-2026.1.2.win.zip";
			using var httpZipStream = new HttpZipStream.HttpZipStream ();
			using var httpClient = new HttpClient ();
			using var memoryStream = new MemoryStream ();
			httpZipStream.ConfigureHttpClient (url, httpClient);
			httpZipStream.ConfigureCache (memoryStream);
			httpZipStream.Build ();
			var progress = new HttpZipStream.Progress<object> (static _ => { });
			await httpZipStream.OpenAsync (progress, TestContext.Current.CancellationToken);
			using var zipArchive = new System.IO.Compression.ZipArchive (
				httpZipStream, System.IO.Compression.ZipArchiveMode.Read, false, Encoding.UTF8
			);
			var zipArchiveEntries = new List<IZipArchiveEntry> (
				zipArchive.Entries.Select (x => new ZipArchiveEntry (x)).Where (x => !x.IsDirectory)
			);
			var perloadRanges = httpZipStream.GetPreloadRanges (zipArchiveEntries, [
				zipArchiveEntries[0], zipArchiveEntries[2], zipArchiveEntries[^4], zipArchiveEntries[^2], zipArchiveEntries[^1]
			], 2513);
			var buffer = new byte[1024 * 1024];
			foreach (var perloadRange in perloadRanges) {
				await httpZipStream.PreloadAsync (perloadRange, progress, TestContext.Current.CancellationToken);
				foreach (var zipArchiveEntry in perloadRange.ZipArchiveEntries) {
					using var inputStream = zipArchiveEntry.Open ();
					while (await inputStream.ReadAsync (buffer.AsMemory (), TestContext.Current.CancellationToken) > 0) {

					}
				}
			}
			Assert.Equal (2, perloadRanges.Count);
			Assert.Equal (2, perloadRanges[0].ZipArchiveEntries.Count);
			Assert.Equal (3, perloadRanges[1].ZipArchiveEntries.Count);
			Assert.Equal (5, httpZipStream.HttpRequestCount);
			Assert.Equal (422868, progress.Value);
		}

		[Fact]
		public async Task ZipEntry () {
			var url = "https://download.jetbrains.com/idea/idea-2026.1.2.win.zip";
			using var httpZipStream = new HttpZipStream.HttpZipStream ();
			using var httpClient = new HttpClient ();
			using var memoryStream = new MemoryStream ();
			httpZipStream.ConfigureHttpClient (url, httpClient);
			httpZipStream.ConfigureCache (memoryStream);
			httpZipStream.Build ();
			var progress = new HttpZipStream.Progress<object> (static _ => { });
			await httpZipStream.OpenAsync (progress, TestContext.Current.CancellationToken);
			using var zipFile = new ICSharpCode.SharpZipLib.Zip.ZipFile (
				httpZipStream, false, ICSharpCode.SharpZipLib.Zip.StringCodec.Default
			);
			var zipArchiveEntries = new List<IZipArchiveEntry> ();
			foreach (ICSharpCode.SharpZipLib.Zip.ZipEntry zipEntry in zipFile) {
				if (zipEntry.IsDirectory) {
					continue;
				}
				zipArchiveEntries.Add (new ZipEntry (zipFile, zipEntry));
			}
			var perloadRanges = httpZipStream.GetPreloadRanges (zipArchiveEntries, [
				zipArchiveEntries[0], zipArchiveEntries[2], zipArchiveEntries[3364]
			], 2512);
			var buffer = new byte[1024 * 1024];
			foreach (var perloadRange in perloadRanges) {
				await httpZipStream.PreloadAsync (perloadRange, progress, TestContext.Current.CancellationToken);
				foreach (var zipArchiveEntry in perloadRange.ZipArchiveEntries) {
					using var inputStream = zipArchiveEntry.Open ();
					while (await inputStream.ReadAsync (buffer.AsMemory (), TestContext.Current.CancellationToken) > 0) {

					}
				}
			}
			Assert.Equal (3, perloadRanges.Count);
			Assert.Single (perloadRanges[0].ZipArchiveEntries);
			Assert.Single (perloadRanges[1].ZipArchiveEntries);
			Assert.Single (perloadRanges[2].ZipArchiveEntries);
			Assert.Equal (6, httpZipStream.HttpRequestCount);
			Assert.Equal (387649, progress.Value);
		}

	}

}