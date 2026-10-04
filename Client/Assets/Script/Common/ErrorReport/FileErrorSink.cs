using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ProjectT
{
    public sealed class FileErrorSink : IErrorSink
    {
        private readonly string directory;
        private readonly string path;
        private readonly string rotatedPath;
        private readonly long maxBytes;
        private readonly StringBuilder builder = new StringBuilder();
        private int droppedBatches;

        public FileErrorSink(string directory, long maxBytes = 1024 * 1024)
        {
            this.directory = directory;
            this.maxBytes = maxBytes;
            path = Path.Combine(directory, "errors.log");
            rotatedPath = Path.Combine(directory, "errors.1.log");
        }

        public void Write(IReadOnlyList<ErrorEvent> errors)
        {
            builder.Clear();
            if (droppedBatches > 0)
                builder.Append(DateTime.UtcNow.ToString("o")).Append(' ').Append(droppedBatches).Append(" error batches dropped\n");

            foreach (var error in errors)
            {
                builder.Append(error.TimeUtc.ToString("o")).Append(" [").Append(error.Id.ToString("N")).Append("] #").Append(error.Occurrence)
                    .Append(' ').Append(error.Fingerprint).Append('\n');
                builder.Append(error.Exception != null ? error.Exception.ToString() : error.Message).Append('\n');
                if (error.Exception == null && error.StackTrace.Length > 0)
                    builder.Append(error.StackTrace).Append('\n');
            }

            string text = builder.ToString();
            // 다른 프로세스의 잠금·디스크 부족 같은 일시적 파일 오류는 이 sink 안에서 복구한다.
            // 회전에 실패해도 추가는 시도하고, 추가에 실패한 묶음은 버린 뒤 다음 기록에 개수를 남긴다.
            try
            {
                Directory.CreateDirectory(directory);
                var file = new FileInfo(path);
                if (file.Exists && file.Length + Encoding.UTF8.GetByteCount(text) > maxBytes)
                {
                    File.Delete(rotatedPath);
                    File.Move(path, rotatedPath);
                }
            }
            catch (Exception error) when (IsTransientInternal(error))
            {
            }

            try
            {
                File.AppendAllText(path, text);
                droppedBatches = 0;
            }
            catch (Exception error) when (IsTransientInternal(error))
            {
                droppedBatches++;
            }
        }

        private static bool IsTransientInternal(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException;
        }
    }
}
