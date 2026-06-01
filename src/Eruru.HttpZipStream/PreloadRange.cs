using System.Collections.ObjectModel;

namespace Eruru.HttpZipStream {

	public class PreloadRange (long index, long length, ReadOnlyCollection<IZipArchiveEntry> zipArchiveEntries) {

		public long Index { get; } = index;
		public long Length { get; } = length;
		public ReadOnlyCollection<IZipArchiveEntry> ZipArchiveEntries { get; } = zipArchiveEntries;

	}

}