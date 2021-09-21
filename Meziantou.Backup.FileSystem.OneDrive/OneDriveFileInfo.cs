using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meziantou.Backup.FileSystem.Abstractions;
using Meziantou.OneDrive;

namespace Meziantou.Backup.FileSystem.OneDrive
{
    [DebuggerDisplay("{FullName}")]
    public sealed class OneDriveFileInfo : IDirectoryInfo, IFileInfo, IFullName, IHashProvider
    {
        private readonly OneDriveItem _item;

        internal OneDriveFileInfo(OneDriveFileSystem fileSystem, OneDriveItem item)
        {
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            if (item == null) throw new ArgumentNullException(nameof(item));

            FileSystem = fileSystem;
            _item = item;
        }

        public OneDriveFileSystem FileSystem { get; }
        public string Name => _item.Name;

        public bool IsDirectory => _item.Folder != null;
        public DateTime CreationTimeUtc => _item.CreatedDateTime;
        public DateTime LastWriteTimeUtc => _item.LastModifiedDateTime;
        public long Length => _item.Size;

        public string FullName
        {
            get
            {
                if (_item.ParentReference == null)
                    return _item.Name;

                return _item.ParentReference.Path + "/" + _item.Name;
            }
        }

        public Task DeleteAsync(CancellationToken ct)
        {
            return _item.DeleteAsync(ct);
        }

        public async Task<IReadOnlyCollection<IFileSystemInfo>> GetItemsAsync(CancellationToken ct)
        {
            var oneDriveItems = await _item.GetChildrenAsync(ct).ConfigureAwait(false);
            return oneDriveItems.Select(item => new OneDriveFileInfo(FileSystem, item)).ToList();
        }

        public async Task<IFileInfo> CreateFileAsync(string name, Stream stream, long length, CancellationToken ct)
        {
            var oneDriveItem = await _item.CreateFileAsync(name, stream, length, FileSystem.UploadChunkSize, OnChunkErrorHandler, ct).ConfigureAwait(false);
            return new OneDriveFileInfo(FileSystem, oneDriveItem);
        }

        private static bool OnChunkErrorHandler(ChunkUploadErrorEventArgs chunkUploadErrorEventArgs)
        {
            return chunkUploadErrorEventArgs.AttemptCount < 3; // Retry 3 times
        }

        public async Task<IDirectoryInfo> CreateDirectoryAsync(string name, CancellationToken ct)
        {
            var item = await _item.CreateDirectoryAsync(name, ct).ConfigureAwait(false);
            return new OneDriveFileInfo(FileSystem, item);
        }

        public async Task<Stream> OpenReadAsync(CancellationToken ct)
        {
            try
            {
                return await _item.DownloadAsync(ct);
            }
            catch (OneDriveException ex) when (ex.Message.Contains("The specified item does not have content", StringComparison.Ordinal))
            {
                return Stream.Null;
            }
        }

        public byte[] GetHash(string algorithmName)
        {
            if (string.Equals(WellKnownHashAlgorithms.Sha1, algorithmName, StringComparison.OrdinalIgnoreCase))
            {
                if (_item?.File?.Hashes.Sha1Hash != null)
                    return Convert.FromBase64String(_item.File.Hashes.Sha1Hash);
            }

            if (string.Equals(WellKnownHashAlgorithms.Crc32, algorithmName, StringComparison.OrdinalIgnoreCase))
            {
                if (_item?.File?.Hashes.Crc32Hash != null)
                    return Convert.FromBase64String(_item.File.Hashes.Crc32Hash);
            }

            return null;
        }
    }
}