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
    }
}
