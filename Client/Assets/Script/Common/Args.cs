namespace ProjectT
{
    public interface IArgs
    {

    }
    public struct Args<T1> : IArgs
    {
        public T1 Arg1 { get; set; }

        public Args(T1 arg1)
        {
            Arg1 = arg1;
        }
    }

    public struct Args<T1, T2> : IArgs
    {
        public T1 Arg1 { get; set; }
        public T2 Arg2 { get; set; }

        public Args(T1 arg1, T2 arg2)
        {
            Arg1 = arg1;
            Arg2 = arg2;
        }
    }

    public struct Args<T1, T2, T3> : IArgs
    {
        public T1 Arg1 { get; set; }
        public T2 Arg2 { get; set; }
        public T3 Arg3 { get; set; }

        public Args(T1 arg1, T2 arg2, T3 arg3)
        {
            Arg1 = arg1;
            Arg2 = arg2;
            Arg3 = arg3;
        }
    }

    public struct Args<T1, T2, T3, T4> : IArgs
    {
        public T1 Arg1 { get; set; }
        public T2 Arg2 { get; set; }
        public T3 Arg3 { get; set; }
        public T4 Arg4 { get; set; }

        public Args(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            Arg1 = arg1;
            Arg2 = arg2;
            Arg3 = arg3;
            Arg4 = arg4;
        }
    }

    public struct Args<T1,T2,T3,T4,T5> : IArgs
    {
        public T1 Arg1 { get; set; }
        public T2 Arg2 { get; set; }
        public T3 Arg3 { get; set; }
        public T4 Arg4 { get; set; }
        public T5 Arg5 { get; set; }

        public Args(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            Arg1 = arg1;
            Arg2 = arg2;
            Arg3 = arg3;
            Arg4 = arg4;
            Arg5 = arg5;
        }
    }    
}