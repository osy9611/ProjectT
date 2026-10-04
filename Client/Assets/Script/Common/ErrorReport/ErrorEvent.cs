using System;
using System.Text;

namespace ProjectT
{
    public readonly struct ErrorEvent
    {
        public const string LogKind = "Log";

        public readonly Guid Id;
        // 예외 타입 전체 이름. 예외 객체가 없는 Unity 로그는 LogKind다.
        public readonly string Kind;
        // 큐에 넣을 때는 null이고 메인 스레드의 Flush에서 정해진다.
        public readonly string Fingerprint;
        public readonly string Message;
        public readonly string StackTrace;
        public readonly Exception Exception;
        public readonly DateTime TimeUtc;
        // 같은 지문의 세션 내 발생 순번. Flush에서 정해진다.
        public readonly int Occurrence;

        internal ErrorEvent(Guid id, string kind, string message, string stackTrace, Exception exception)
            : this(id, kind, null, message ?? string.Empty, stackTrace ?? string.Empty, exception, DateTime.UtcNow, 0)
        {
        }

        private ErrorEvent(Guid id, string kind, string fingerprint, string message, string stackTrace, Exception exception, DateTime timeUtc, int occurrence)
        {
            Id = id;
            Kind = kind;
            Fingerprint = fingerprint;
            Message = message;
            StackTrace = stackTrace;
            Exception = exception;
            TimeUtc = timeUtc;
            Occurrence = occurrence;
        }

        internal ErrorEvent WithFingerprint(string fingerprint, int occurrence)
        {
            return new ErrorEvent(Id, Kind, fingerprint, Message, StackTrace, Exception, TimeUtc, occurrence);
        }

        internal string MakeFingerprint()
        {
            string frame = FirstProjectFrame(StackTrace);
            var builder = new StringBuilder(Kind.Length + 64);
            builder.Append(Kind).Append('|');
            // 예외는 같은 위치에서 메시지만 다른 경우를 묶는다. 로그는 같은 호출 위치에서 서로 다른 오류를 남기므로 메시지를 포함한다.
            if (frame == null || string.Equals(Kind, LogKind, StringComparison.Ordinal))
            {
                AppendMessageInternal(builder, Message);
                builder.Append('|');
            }

            if (frame != null)
                builder.Append(frame);

            return builder.ToString();
        }

        // 메시지의 숫자(타임스탬프·id·개수)가 지문을 쪼개지 않도록 첫 줄의 연속된 숫자를 하나의 문자로 바꾼다.
        private static void AppendMessageInternal(StringBuilder builder, string message)
        {
            int end = message.IndexOf('\n');
            string line = (end < 0 ? message : message.Substring(0, end)).Trim();
            bool digit = false;
            foreach (char c in line)
            {
                if (!char.IsDigit(c))
                    builder.Append(c);
                else if (!digit)
                    builder.Append('#');

                digit = char.IsDigit(c);
            }
        }

        private static string FirstProjectFrame(string stackTrace)
        {
            int start = 0;
            while (start < stackTrace.Length)
            {
                int end = stackTrace.IndexOf('\n', start);
                if (end < 0)
                    end = stackTrace.Length;

                string line = stackTrace.Substring(start, end - start).Trim();
                start = end + 1;
                if (line.StartsWith("at ", StringComparison.Ordinal))
                    line = line.Substring(3);

                if (line.Length == 0 || IsSkippedFrameInternal(line))
                    continue;

                return RemoveILOffsetInternal(line);
            }

            return null;
        }

        // 엔진·라이브러리 프레임과 보고 경로 자체의 프레임은 오류 위치가 아니다.
        private static bool IsSkippedFrameInternal(string frame)
        {
            return frame.StartsWith("System.", StringComparison.Ordinal)
                || frame.StartsWith("Cysharp.", StringComparison.Ordinal)
                || frame.StartsWith("UnityEngine.", StringComparison.Ordinal)
                || frame.StartsWith("ProjectT.Global:", StringComparison.Ordinal)
                || frame.StartsWith("ProjectT.Global.", StringComparison.Ordinal)
                || frame.StartsWith("ProjectT.ErrorReporter", StringComparison.Ordinal);
        }

        // Mono 스택의 IL 오프셋은 빌드마다 바뀌므로 지우고 소스 줄 번호는 유지한다.
        private static string RemoveILOffsetInternal(string frame)
        {
            int start = frame.IndexOf("[0x", StringComparison.Ordinal);
            if (start < 0)
                return frame;

            int end = frame.IndexOf(']', start);
            if (end < 0)
                return frame;

            return frame.Remove(start, end - start + 1);
        }
    }
}
