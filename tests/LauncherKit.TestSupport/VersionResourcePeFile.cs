using System.Buffers.Binary;
using System.Text;

namespace LauncherKit.TestSupport;

/// <summary>
/// Builds a minimal 64-bit PE image whose only content is a <c>VS_VERSION_INFO</c> resource, so tests can
/// exercise <see cref="System.Diagnostics.FileVersionInfo"/> without committing a real executable.
/// </summary>
public static class VersionResourcePeFile
{
	private const int SectionAlignment = 0x1000;
	private const int FileAlignment = 0x200;
	private const int ResourceRva = 0x1000;
	private const int HeadersSize = FileAlignment;
	private const ushort LanguageId = 0x0409;
	private const ushort CodePageId = 0x04B0;

	/// <summary>
	/// Creates the image bytes. <paramref name="fixedVersion"/> fills <c>VS_FIXEDFILEINFO</c>'s numeric
	/// file/product fields; <paramref name="productVersion"/> is written verbatim as the
	/// <c>ProductVersion</c> string-table entry and may therefore be non-numeric.
	/// </summary>
	public static byte[] Create(Version fixedVersion, string productVersion)
	{
		ArgumentNullException.ThrowIfNull(fixedVersion);
		ArgumentNullException.ThrowIfNull(productVersion);

		var resource = BuildResourceSection(fixedVersion, productVersion);
		var image = new byte[HeadersSize + Align(resource.Length, FileAlignment)];

		WriteHeaders(image, resource.Length);
		resource.CopyTo(image.AsSpan(HeadersSize));

		return image;
	}

	private static void WriteHeaders(Span<byte> image, int resourceSize)
	{
		"MZ"u8.CopyTo(image);
		WriteInt32(image[0x3C..], 0x40);

		"PE\0\0"u8.CopyTo(image[0x40..]);

		var coff = image[0x44..];
		WriteUInt16(coff, 0x8664); // Machine: AMD64.
		WriteUInt16(coff[2..], 1); // NumberOfSections.
		WriteUInt16(coff[16..], 0xF0); // SizeOfOptionalHeader: PE32+.
		WriteUInt16(coff[18..], 0x0022); // EXECUTABLE_IMAGE | LARGE_ADDRESS_AWARE.

		var optional = image[0x58..];
		WriteUInt16(optional, 0x20B); // Magic: PE32+.
		WriteInt32(optional[8..], Align(resourceSize, FileAlignment)); // SizeOfInitializedData.
		WriteInt32(optional[20..], ResourceRva); // BaseOfCode.
		WriteInt64(optional[24..], 0x140000000); // ImageBase.
		WriteInt32(optional[32..], SectionAlignment);
		WriteInt32(optional[36..], FileAlignment);
		WriteUInt16(optional[40..], 6); // MajorOperatingSystemVersion.
		WriteUInt16(optional[48..], 6); // MajorSubsystemVersion.
		WriteInt32(optional[56..], ResourceRva + Align(resourceSize, SectionAlignment)); // SizeOfImage.
		WriteInt32(optional[60..], HeadersSize); // SizeOfHeaders.
		WriteUInt16(optional[68..], 3); // Subsystem: console.
		WriteInt64(optional[72..], 0x100000); // SizeOfStackReserve.
		WriteInt64(optional[80..], 0x1000); // SizeOfStackCommit.
		WriteInt64(optional[88..], 0x100000); // SizeOfHeapReserve.
		WriteInt64(optional[96..], 0x1000); // SizeOfHeapCommit.
		WriteInt32(optional[108..], 16); // NumberOfRvaAndSizes.
		const int resourceDirectoryEntry = 112 + (2 * 8); // Data directory index 2.
		WriteInt32(optional[resourceDirectoryEntry..], ResourceRva);
		WriteInt32(optional[(resourceDirectoryEntry + 4)..], resourceSize);

		var section = image[0x148..];
		".rsrc"u8.CopyTo(section);
		WriteInt32(section[8..], resourceSize); // VirtualSize.
		WriteInt32(section[12..], ResourceRva); // VirtualAddress.
		WriteInt32(section[16..], Align(resourceSize, FileAlignment)); // SizeOfRawData.
		WriteInt32(section[20..], HeadersSize); // PointerToRawData.
		WriteInt32(section[36..], unchecked((int)0x40000040)); // INITIALIZED_DATA | MEM_READ.
	}

	/// <summary>
	/// Emits the three-level resource directory (type -> name -> language) followed by the version block.
	/// </summary>
	private static byte[] BuildResourceSection(Version fixedVersion, string productVersion)
	{
		const int directorySize = 16 + 8;
		const int dataEntryOffset = 3 * directorySize;
		const int versionOffset = dataEntryOffset + 16;

		var version = BuildVersionBlock(fixedVersion, productVersion);
		var section = new byte[versionOffset + version.Length];

		WriteDirectoryEntry(section, 0, id: 16, offset: directorySize, isDirectory: true); // RT_VERSION.
		WriteDirectoryEntry(section, directorySize, id: 1, offset: 2 * directorySize, isDirectory: true);
		WriteDirectoryEntry(section, 2 * directorySize, LanguageId, dataEntryOffset, isDirectory: false);

		var dataEntry = section.AsSpan(dataEntryOffset);
		WriteInt32(dataEntry, ResourceRva + versionOffset); // OffsetToData is an RVA.
		WriteInt32(dataEntry[4..], version.Length);

		version.CopyTo(section.AsSpan(versionOffset));

		return section;
	}

	private static void WriteDirectoryEntry(Span<byte> section, int directoryOffset, ushort id, int offset, bool isDirectory)
	{
		var directory = section[directoryOffset..];
		WriteUInt16(directory[14..], 1); // NumberOfIdEntries.
		WriteInt32(directory[16..], id);
		WriteInt32(directory[20..], isDirectory ? unchecked((int)(offset | 0x80000000)) : offset);
	}

	private static byte[] BuildVersionBlock(Version fixedVersion, string productVersion)
	{
		using var stream = new MemoryStream();
		var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

		var versionInfo = BeginBlock(writer, "VS_VERSION_INFO", valueLength: 52, isText: false);
		WriteFixedFileInfo(writer, fixedVersion);

		var stringFileInfo = BeginBlock(writer, "StringFileInfo", valueLength: 0, isText: true);
		var stringTable = BeginBlock(writer, $"{LanguageId:X4}{CodePageId:X4}", valueLength: 0, isText: true);

		// FileVersionInfo accepts a code page only when its FileVersion entry is present, and discards
		// everything it read from a rejected code page, so both strings must be written.
		WriteStringEntry(writer, "FileVersion", productVersion);
		WriteStringEntry(writer, "ProductVersion", productVersion);

		EndBlock(writer, stringTable);
		EndBlock(writer, stringFileInfo);

		var varFileInfo = BeginBlock(writer, "VarFileInfo", valueLength: 0, isText: true);
		var translation = BeginBlock(writer, "Translation", valueLength: 4, isText: false);
		writer.Write((uint)((CodePageId << 16) | LanguageId));
		EndBlock(writer, translation);
		EndBlock(writer, varFileInfo);

		EndBlock(writer, versionInfo);
		writer.Flush();

		return stream.ToArray();
	}

	private static void WriteStringEntry(BinaryWriter writer, string key, string value)
	{
		var entry = BeginBlock(writer, key, value.Length + 1, isText: true);
		WriteUnicodeString(writer, value);
		EndBlock(writer, entry);
	}

	private static void WriteFixedFileInfo(BinaryWriter writer, Version version)
	{
		var mostSignificant = (uint)((version.Major << 16) | (ushort)version.Minor);
		var leastSignificant = (uint)((Math.Max(version.Build, 0) << 16) | (ushort)Math.Max(version.Revision, 0));

		writer.Write(0xFEEF04BDu); // dwSignature.
		writer.Write(0x00010000u); // dwStrucVersion.
		writer.Write(mostSignificant); // dwFileVersionMS.
		writer.Write(leastSignificant); // dwFileVersionLS.
		writer.Write(mostSignificant); // dwProductVersionMS.
		writer.Write(leastSignificant); // dwProductVersionLS.
		writer.Write(0x3Fu); // dwFileFlagsMask.
		writer.Write(0u); // dwFileFlags.
		writer.Write(0x00040004u); // dwFileOS: VOS_NT_WINDOWS32.
		writer.Write(1u); // dwFileType: VFT_APP.
		writer.Write(0u); // dwFileSubtype.
		writer.Write(0u); // dwFileDateMS.
		writer.Write(0u); // dwFileDateLS.
	}

	/// <summary>
	/// Writes a version block header with a placeholder length and returns the position to patch in
	/// <see cref="EndBlock"/>, because a block's length covers its children.
	/// </summary>
	private static long BeginBlock(BinaryWriter writer, string key, int valueLength, bool isText)
	{
		AlignTo4(writer);
		var start = writer.BaseStream.Position;

		writer.Write((ushort)0); // wLength placeholder.
		writer.Write((ushort)valueLength);
		writer.Write((ushort)(isText ? 1 : 0));
		WriteUnicodeString(writer, key);
		AlignTo4(writer);

		return start;
	}

	private static void EndBlock(BinaryWriter writer, long start)
	{
		AlignTo4(writer);
		var end = writer.BaseStream.Position;

		writer.Flush();
		writer.BaseStream.Position = start;
		writer.Write((ushort)(end - start));
		writer.Flush();
		writer.BaseStream.Position = end;
	}

	private static void WriteUnicodeString(BinaryWriter writer, string value)
	{
		foreach (var character in value)
		{
			writer.Write((ushort)character);
		}

		writer.Write((ushort)0);
	}

	private static void AlignTo4(BinaryWriter writer)
	{
		writer.Flush();
		while (writer.BaseStream.Position % 4 != 0)
		{
			writer.Write((byte)0);
		}

		writer.Flush();
	}

	private static void WriteUInt16(Span<byte> destination, ushort value) =>
		BinaryPrimitives.WriteUInt16LittleEndian(destination, value);

	private static void WriteInt32(Span<byte> destination, int value) =>
		BinaryPrimitives.WriteInt32LittleEndian(destination, value);

	private static void WriteInt64(Span<byte> destination, long value) =>
		BinaryPrimitives.WriteInt64LittleEndian(destination, value);

	private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
