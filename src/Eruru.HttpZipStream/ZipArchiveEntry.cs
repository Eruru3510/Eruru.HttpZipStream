using System.Globalization;
using System.Reflection;

namespace Eruru.HttpZipStream;

public class ZipArchiveEntry : IZipArchiveEntry {

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

	static readonly FieldInfo OffsetOfLocalHeaderField = typeof (System.IO.Compression.ZipArchiveEntry).GetField (
		"_offsetOfLocalHeader", BindingFlags.NonPublic | BindingFlags.Instance
	) ?? throw new InvalidOperationException (
		$"Failed to retrieve the value of the private field _offsetOfLocalHeader of {nameof (ZipArchiveEntry)}"
	);

	readonly System.IO.Compression.ZipArchiveEntry Entry;

#if NET
	[System.Diagnostics.CodeAnalysis.DynamicDependency ("_offsetOfLocalHeader", typeof (System.IO.Compression.ZipArchiveEntry))]
#endif
	public ZipArchiveEntry (System.IO.Compression.ZipArchiveEntry zipArchiveEntry) {
		Entry = zipArchiveEntry;
		OffsetOfLocalHeader = Convert.ToInt64 (OffsetOfLocalHeaderField.GetValue (zipArchiveEntry), CultureInfo.InvariantCulture);
		Comment =
#if NET
			Entry.Comment
#else
			string.Empty
#endif
		;
#if NET
		Crc32 = Entry.Crc32;
		ExternalAttributes = Entry.ExternalAttributes;
		IsEncrypted = Entry.IsEncrypted;
#endif
		FullName = Entry.FullName;
		LastWriteTime = Entry.LastWriteTime;
		CompressedLength = Entry.CompressedLength;
		Length = Entry.Length;
		Name = Entry.Name;
		IsDirectory = Entry.FullName.Length > 0 && (Entry.FullName.EndsWith (
#if NET
			'/'
#else
			"/", StringComparison.Ordinal
#endif
		) || Entry.FullName.EndsWith (
#if NET
			'\\'
#else
			@"\", StringComparison.Ordinal
#endif
		));
	}

	public Stream Open () {
		return Entry.Open ();
	}

}