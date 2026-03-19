using System.Security.Cryptography;
using System.Text;
using SharpIDE.Application.Features.Git;

namespace SharpIDE.Application.Features.Compare;

public sealed class CompareService
{
	private const string BinaryPreviewPreamble = "Binary or unsupported text file.";
	private readonly GitSelectivePatchBuilder _patchBuilder = new();

	public async Task<GitDiffViewModel> GetFileDiffView(FileCompareRequest request, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var leftPath = NormalizeOptionalPath(request.LeftAbsolutePath);
		var rightPath = NormalizeOptionalPath(request.RightAbsolutePath);

		var leftBytes = await ReadAllBytesIfExists(leftPath, cancellationToken);
		var rightBytes = await ReadAllBytesIfExists(rightPath, cancellationToken);

		var leftDisplayText = await BuildDisplayTextAsync(leftPath, leftBytes, cancellationToken);
		var rightDisplayText = await BuildDisplayTextAsync(rightPath, rightBytes, cancellationToken);

		var absolutePath = rightPath is not null && File.Exists(rightPath)
			? rightPath
			: leftPath ?? rightPath ?? string.Empty;
		var repoRelativePath = Path.GetFileName(absolutePath);

		return _patchBuilder.BuildCanonicalViewModel(
			repoRelativePath,
			absolutePath,
			GitDiffMode.Historical,
			request.LeftDisplayName,
			request.RightDisplayName,
			leftDisplayText,
			rightDisplayText,
			canEditCurrent: false);
	}

	public async Task<DirectoryCompareResult> CompareDirectories(DirectoryCompareRequest request, CancellationToken cancellationToken = default)
	{
		var leftDirectoryPath = Path.GetFullPath(request.LeftDirectoryPath);
		var rightDirectoryPath = Path.GetFullPath(request.RightDirectoryPath);

		var leftFiles = EnumerateFiles(leftDirectoryPath);
		var rightFiles = EnumerateFiles(rightDirectoryPath);
		var allRelativePaths = leftFiles.Keys
			.Concat(rightFiles.Keys)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var entries = new List<DirectoryCompareEntry>();
		foreach (var relativePath in allRelativePaths)
		{
			cancellationToken.ThrowIfCancellationRequested();

			leftFiles.TryGetValue(relativePath, out var leftAbsolutePath);
			rightFiles.TryGetValue(relativePath, out var rightAbsolutePath);

			if (leftAbsolutePath is null)
			{
				entries.Add(new DirectoryCompareEntry
				{
					RelativePath = relativePath,
					EntryStatus = DirectoryCompareEntryStatus.Added,
					LeftAbsolutePath = null,
					RightAbsolutePath = rightAbsolutePath,
					DisplayPath = relativePath
				});
				continue;
			}

			if (rightAbsolutePath is null)
			{
				entries.Add(new DirectoryCompareEntry
				{
					RelativePath = relativePath,
					EntryStatus = DirectoryCompareEntryStatus.Removed,
					LeftAbsolutePath = leftAbsolutePath,
					RightAbsolutePath = null,
					DisplayPath = relativePath
				});
				continue;
			}

			if (await FilesAreEqual(leftAbsolutePath, rightAbsolutePath, cancellationToken))
			{
				continue;
			}

			entries.Add(new DirectoryCompareEntry
			{
				RelativePath = relativePath,
				EntryStatus = DirectoryCompareEntryStatus.Modified,
				LeftAbsolutePath = leftAbsolutePath,
				RightAbsolutePath = rightAbsolutePath,
				DisplayPath = relativePath
			});
		}

		return new DirectoryCompareResult
		{
			Request = request,
			Entries = entries
		};
	}

	public static string BuildStableComparisonKey(string leftAbsolutePath, string rightAbsolutePath)
	{
		var normalizedLeft = Path.GetFullPath(leftAbsolutePath);
		var normalizedRight = Path.GetFullPath(rightAbsolutePath);
		return string.Compare(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase) <= 0
			? $"{normalizedLeft}|{normalizedRight}"
			: $"{normalizedRight}|{normalizedLeft}";
	}

	private static Dictionary<string, string> EnumerateFiles(string rootPath)
	{
		if (!Directory.Exists(rootPath))
		{
			return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}

		return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
			.ToDictionary(
				path => Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/'),
				path => Path.GetFullPath(path),
				StringComparer.OrdinalIgnoreCase);
	}

	private static string? NormalizeOptionalPath(string? absolutePath)
	{
		return string.IsNullOrWhiteSpace(absolutePath) ? null : Path.GetFullPath(absolutePath);
	}

	private static async Task<byte[]?> ReadAllBytesIfExists(string? absolutePath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
		{
			return null;
		}

		return await File.ReadAllBytesAsync(absolutePath, cancellationToken);
	}

	private static async Task<bool> FilesAreEqual(string leftAbsolutePath, string rightAbsolutePath, CancellationToken cancellationToken)
	{
		var leftBytes = await File.ReadAllBytesAsync(leftAbsolutePath, cancellationToken);
		var rightBytes = await File.ReadAllBytesAsync(rightAbsolutePath, cancellationToken);
		return leftBytes.AsSpan().SequenceEqual(rightBytes);
	}

	private static async Task<string> BuildDisplayTextAsync(string? absolutePath, byte[]? bytes, CancellationToken cancellationToken)
	{
		if (bytes is null)
		{
			return string.Empty;
		}

		if (LooksBinary(bytes))
		{
			return BuildBinaryPreview(absolutePath ?? string.Empty, bytes);
		}

		try
		{
			return await File.ReadAllTextAsync(absolutePath!, cancellationToken);
		}
		catch (DecoderFallbackException)
		{
			return BuildBinaryPreview(absolutePath ?? string.Empty, bytes);
		}
		catch (InvalidDataException)
		{
			return BuildBinaryPreview(absolutePath ?? string.Empty, bytes);
		}
	}

	private static bool LooksBinary(byte[] bytes)
	{
		var length = Math.Min(bytes.Length, 4096);
		for (var index = 0; index < length; index++)
		{
			if (bytes[index] == 0)
			{
				return true;
			}
		}

		return false;
	}

	private static string BuildBinaryPreview(string absolutePath, byte[] bytes)
	{
		var hash = Convert.ToHexString(SHA256.HashData(bytes));
		var builder = new StringBuilder();
		builder.AppendLine(BinaryPreviewPreamble);
		builder.AppendLine($"Path: {absolutePath}");
		builder.AppendLine($"Bytes: {bytes.Length}");
		builder.AppendLine($"SHA256: {hash}");
		return builder.ToString();
	}
}
