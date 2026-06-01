using Eruru.HttpZipStream;

namespace Eruru.HttpZipStreamSample {

#pragma warning disable CA1515 // 考虑将公共类型设为内部类型
	public class ZipEntry (
#pragma warning restore CA1515 // 考虑将公共类型设为内部类型
		ICSharpCode.SharpZipLib.Zip.ZipFile zipFile, ICSharpCode.SharpZipLib.Zip.ZipEntry zipEntry
	) : IZipArchiveEntry {

		public long OffsetOfLocalHeader { get; } = zipEntry.Offset;
		public string Comment { get; } = zipEntry.Comment;
		public long CompressedLength { get; } = zipEntry.CompressedSize;
		public uint Crc32 { get; } = (uint)zipEntry.Crc;
		public int ExternalAttributes { get; } = zipEntry.ExternalFileAttributes;
		public string FullName { get; } = zipEntry.Name;
		public bool IsEncrypted { get; } = zipEntry.IsCrypted;
		public DateTimeOffset LastWriteTime { get; } = zipEntry.DateTime;
		public long Length { get; } = zipEntry.Size;
		public string Name { get; } = Path.GetFileName (zipEntry.Name);
		public bool IsDirectory { get; } = zipEntry.IsDirectory;

		readonly ICSharpCode.SharpZipLib.Zip.ZipFile ZipFile = zipFile;
		readonly ICSharpCode.SharpZipLib.Zip.ZipEntry Entry = zipEntry;

		public Stream Open () {
			return ZipFile.GetInputStream (Entry);
		}

	}

}