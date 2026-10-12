using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace UT
{
    /// <summary>
    /// 決めた回数だけファイルをコピーしたら、それ以降のコピーを「ディスクの空き容量が足りない」で失敗させるファイルシステム。
    /// 容量不足で途中で止まったときに、取り返しのつかない状態にならないかを確かめるテスト用。
    /// （MockFileSystem は空き容量を数えないので、コピーの回数で容量不足を再現する）
    /// 中身は MockFileSystem をそのまま使い、ファイルのコピーだけを差し替える。
    /// </summary>
    public class DiskFullFileSystem : IFileSystem
    {
        private readonly MockFileSystem _inner;
        private readonly CountingFile _file;

        /// <summary>
        /// inner を包む。最初は容量に余裕がある（コピーは失敗しない）。
        /// </summary>
        public DiskFullFileSystem(MockFileSystem inner)
        {
            _inner = inner;
            _file = new CountingFile(inner);
        }

        /// <summary>
        /// これから count 回コピーしたら、その後のコピーを失敗させる（0 なら次のコピーから失敗する）。
        /// </summary>
        public void FailAfterCopies(int count) => _file.RemainingCopies = count;

        /// <summary>容量を空けた状態に戻す（コピーが失敗しなくなる）。</summary>
        public void FreeUp() => _file.RemainingCopies = int.MaxValue;

        public IFile File => _file;
        public IDirectory Directory => _inner.Directory;
        public IFileInfoFactory FileInfo => _inner.FileInfo;
        public IFileStreamFactory FileStream => _inner.FileStream;
        public IPath Path => _inner.Path;
        public IDirectoryInfoFactory DirectoryInfo => _inner.DirectoryInfo;
        public IDriveInfoFactory DriveInfo => _inner.DriveInfo;
        public IFileSystemWatcherFactory FileSystemWatcher => _inner.FileSystemWatcher;
        public IFileVersionInfoFactory FileVersionInfo => _inner.FileVersionInfo;

        /// <summary>
        /// コピーの回数を数え、残りが 0 になったら IOException を投げる MockFile。
        /// </summary>
        private class CountingFile : MockFile
        {
            /// <summary>あと何回コピーできるか</summary>
            public int RemainingCopies { get; set; } = int.MaxValue;

            public CountingFile(IMockFileDataAccessor accessor) : base(accessor) { }

            public override void Copy(string sourceFileName, string destFileName) => Copy(sourceFileName, destFileName, false);

            public override void Copy(string sourceFileName, string destFileName, bool overwrite)
            {
                if (RemainingCopies <= 0) throw new IOException("ディスクの空き容量が不足しています。");
                RemainingCopies--;
                base.Copy(sourceFileName, destFileName, overwrite);
            }
        }
    }
}
