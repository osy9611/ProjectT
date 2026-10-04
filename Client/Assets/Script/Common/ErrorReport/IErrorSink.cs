using System.Collections.Generic;

namespace ProjectT
{
    // 메인 스레드에서 Flush 한 번당 한 묶음으로 호출된다. 묶음은 호출이 끝나면 재사용되므로 보관하지 않는다.
    public interface IErrorSink
    {
        void Write(IReadOnlyList<ErrorEvent> errors);
    }
}
