namespace LauncherKit.FeedEditor.Storage;

/// <summary>
/// An upload started by <see cref="FeedSession.StartUploadAsync"/>. The browser sends its parts straight to R2 through
/// presigned URLs; a file up to <see cref="FeedSession.UploadPartSize"/> is a single <c>PUT</c> without
/// <see cref="UploadId"/>, a bigger one is a multipart upload.
/// </summary>
/// <param name="ContentType">Signed into the single <c>PUT</c> URL, so the browser must send it as is.</param>
/// <param name="UploadId">The multipart upload, or <see langword="null"/> for a single <c>PUT</c>.</param>
/// <param name="Parts">Consecutive parts covering the whole file, numbered from 1.</param>
public sealed record ObjectUpload(string Key, long Length, string? ContentType, string? UploadId, IReadOnlyList<UploadPart> Parts);

/// <param name="Number">1-based part number.</param>
/// <param name="Offset">Offset of the part's first byte in the file.</param>
public readonly record struct UploadPart(int Number, long Offset, long Length);
