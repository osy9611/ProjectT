using System;
using System.Collections.Generic;

namespace ProjectT
{
    public static class ErrorCollector
    {
        public static void Run<T>(ref List<Exception> errors, T item, Action<T> action)
        {
            try
            {
                action(item);
            }
            catch (Exception error)
            {
                if (errors == null)
                    errors = new List<Exception>();

                errors.Add(error);
            }
        }

        public static void ThrowIfAny(List<Exception> errors)
        {
            if (errors != null)
                throw new AggregateException(errors);
        }

        // 정리 오류가 없으면 반환하므로 호출자는 이어서 throw;로 primary를 원래 스택 그대로 다시 던진다.
        public static void ThrowIfAny(Exception primary, List<Exception> errors)
        {
            if (errors != null)
            {
                errors.Insert(0, primary);
                throw new AggregateException(errors);
            }
        }
    }
}
