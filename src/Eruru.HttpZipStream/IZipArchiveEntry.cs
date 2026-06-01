namespace Eruru.HttpZipStream {

	public interface IZipArchiveEntry {

		public long OffsetOfLocalHeader { get; }
		public string Comment { get; }
		public long CompressedLength { get; }
		public uint Crc32 { get; }
		public int ExternalAttributes { get; }
		public string FullName { get; }
		public bool IsEncrypted { get; }
		public DateTimeOffset LastWriteTime { get; }
		public long Length { get; }
		public string Name { get; }
		public bool IsDirectory { get; }

		public Stream Open ();

	}

}